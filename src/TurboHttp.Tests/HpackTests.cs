using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class HpackTests
{
    [Fact]
    public void Encode_IndexedStaticEntry_SingleByte()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<(string, string)> { (":method", "GET") };
        var encoded = encoder.Encode(headers);

        Assert.Single(encoded);
        Assert.Equal(0x82, encoded[0]);
    }

    [Fact]
    public void Encode_Decode_RoundTrip_PseudoHeaders()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/index.html"),
            (":scheme", "https"),
            (":authority", "example.com"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i], decoded[i]);
        }
    }

    [Fact]
    public void Encode_Decode_RoundTrip_WithHuffman()
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/api/search?q=hello"),
            (":scheme", "https"),
            (":authority", "api.example.com"),
            ("content-type", "application/json"),
            ("authorization", "Bearer token123"),
            ("accept", "application/json, text/plain"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i], decoded[i]);
        }
    }

    [Fact]
    public void DynamicTable_SecondRequest_UsesDynamicIndex()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var h1 = new List<(string, string)> { ("authorization", "Bearer token") };
        var e1 = encoder.Encode(h1);
        decoder.Decode(e1);

        var h2 = new List<(string, string)> { ("authorization", "Bearer token") };
        var e2 = encoder.Encode(h2);
        decoder.Decode(e2);


        Assert.True(e2.Length < e1.Length, $"e2={e2.Length} sollte < e1={e1.Length} sein (dynamischer Index)");
    }

    [Fact]
    public void Decode_LiteralNewName_CorrectOrder()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var headers = new List<(string, string)>
        {
            ("x-custom-header", "my-value"),
            ("x-another", "data"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded);

        Assert.Equal(2, decoded.Count);
        Assert.Equal("x-custom-header", decoded[0].Name);
        Assert.Equal("my-value", decoded[0].Value);
        Assert.Equal("x-another", decoded[1].Name);
        Assert.Equal("data", decoded[1].Value);
    }

    [Fact]
    public void Decode_DynamicTableSizeUpdate_Respected()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var decoder = new HpackDecoder();

        var h1 = new List<(string, string)> { ("x-test", "value") };
        var e1 = encoder.Encode(h1);
        decoder.Decode(e1);

        var sizeUpdate = new byte[] { 0x20 };

        using var ms = new MemoryStream();
        ms.WriteByte(0x40);
        var nameBytes = "x-fresh"u8.ToArray();
        HpackEncoder.WriteInteger(ms, nameBytes.Length, 7, 0x00);
        ms.Write(nameBytes);
        // value "new"
        var valueBytes = "new"u8.ToArray();
        HpackEncoder.WriteInteger(ms, valueBytes.Length, 7, 0x00);
        ms.Write(valueBytes);

        var combined = new byte[sizeUpdate.Length + ms.Length];
        sizeUpdate.CopyTo(combined, 0);
        ms.ToArray().CopyTo(combined, sizeUpdate.Length);

        var decoded = decoder.Decode(combined);
        Assert.Single(decoded);
        Assert.Equal(("x-fresh", "new"), decoded[0]);
    }

    [Fact]
    public void Decode_Rfc7541_AppendixC3_FirstRequest()
    {
        var encoded = new byte[]
        {
            0x82, 0x86, 0x84, 0x41, 0x0f,
            (byte)'w', (byte)'w', (byte)'w', (byte)'.', (byte)'e', (byte)'x', (byte)'a', (byte)'m',
            (byte)'p', (byte)'l', (byte)'e', (byte)'.', (byte)'c', (byte)'o', (byte)'m',
        };
        var decoder = new HpackDecoder();
        var decoded = decoder.Decode(encoded);

        Assert.Equal(4, decoded.Count);
        Assert.Equal((":method", "GET"), decoded[0]);
        Assert.Equal((":scheme", "http"), decoded[1]);
        Assert.Equal((":path", "/"), decoded[2]);
        Assert.Equal((":authority", "www.example.com"), decoded[3]);
    }
}