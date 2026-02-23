using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http2DecoderTests
{
    [Fact]
    public void Decode_SettingsFrame_ExtractsParameters()
    {
        var settings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 100u),
            (SettingsParameter.InitialWindowSize, 65535u),
        }).Serialize();

        var decoder = new Http2Decoder();
        var decoded = decoder.TryDecode(settings, out var result);

        Assert.True(decoded);
        Assert.True(result.HasNewSettings);
        Assert.Single(result.ReceivedSettings);

        var s = result.ReceivedSettings[0];
        Assert.Equal(2, s.Count);
        Assert.Contains(s, p => p.Item1 == SettingsParameter.MaxConcurrentStreams && p.Item2 == 100u);
    }

    [Fact]
    public void Decode_SettingsAck_DoesNotAddToSettings()
    {
        var ack = SettingsFrame.SettingsAck();
        var decoder = new Http2Decoder();
        decoder.TryDecode(ack, out var result);

        Assert.False(result.HasNewSettings);
    }

    [Fact]
    public void Decode_PingRequest_ReturnsInPingRequests()
    {
        var data = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        var ping = new PingFrame(data, isAck: false).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(ping, out var result);

        Assert.Single(result.PingRequests);
        Assert.Equal(data, result.PingRequests[0]);
    }

    [Fact]
    public void Decode_PingAck_ReturnsInPingAcks()
    {
        var data = new byte[] { 7, 6, 5, 4, 3, 2, 1, 0 };
        var ping = new PingFrame(data, isAck: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(ping, out var result);

        Assert.Single(result.PingAcks);
        Assert.Equal(data, result.PingAcks[0]);
    }

    [Fact]
    public void Decode_WindowUpdate_ReturnsIncrement()
    {
        var frame = new WindowUpdateFrame(1, 32768).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.WindowUpdates);
        Assert.Equal((1, 32768), result.WindowUpdates[0]);
    }

    [Fact]
    public void Decode_RstStream_ReturnsErrorCode()
    {
        var frame = new RstStreamFrame(3, Http2ErrorCode.Cancel).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.RstStreams);
        Assert.Equal((3, Http2ErrorCode.Cancel), result.RstStreams[0]);
    }

    [Fact]
    public void Decode_GoAway_ParsedCorrectly()
    {
        var frame = new GoAwayFrame(5, Http2ErrorCode.NoError,
            "server shutdown"u8.ToArray()).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.True(result.HasGoAway);
        Assert.Equal(5, result.GoAway!.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, result.GoAway.ErrorCode);
    }

    [Fact]
    public void Decode_FrameFragmented_ReassembledCorrectly()
    {
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();
        const int cut = 5;
        var chunk1 = ping[..cut];
        var chunk2 = ping[cut..];

        var decoder = new Http2Decoder();
        var d1 = decoder.TryDecode(chunk1, out _);
        var d2 = decoder.TryDecode(chunk2, out var result);

        Assert.False(d1);
        Assert.True(d2);
        Assert.Single(result.PingRequests);
    }

    [Fact]
    public void Decode_MultipleFrames_AllProcessed()
    {
        var ping1 = new PingFrame([1, 1, 1, 1, 1, 1, 1, 1]).Serialize();
        var ping2 = new PingFrame([2, 2, 2, 2, 2, 2, 2, 2]).Serialize();
        var settings = SettingsFrame.SettingsAck();

        var combined = new byte[ping1.Length + ping2.Length + settings.Length];
        ping1.CopyTo(combined, 0);
        ping2.CopyTo(combined, ping1.Length);
        settings.CopyTo(combined, ping1.Length + ping2.Length);

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        Assert.Equal(2, result.PingRequests.Count);
    }

    [Fact]
    public async Task Decode_HeadersAndData_ReturnsCompleteResponse()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var responseHeaders = new List<(string, string)>
        {
            (":status", "200"),
            ("content-type", "text/plain"),
        };
        var headerBlock = hpackEncoder.Encode(responseHeaders);
        var headersFrame = new HeadersFrame(1, headerBlock,
            endStream: false, endHeaders: true).Serialize();

        var bodyData = "Hello, HTTP/2!"u8.ToArray();
        var dataFrame = new DataFrame(1, bodyData, endStream: true).Serialize();

        var combined = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(combined, 0);
        dataFrame.CopyTo(combined, headersFrame.Length);

        var decoder = new Http2Decoder();
        var decoded = decoder.TryDecode(combined, out var result);

        Assert.True(decoded);
        Assert.True(result.HasResponses);
        Assert.Single(result.Responses);

        var (streamId, response) = result.Responses[0];
        Assert.Equal(1, streamId);
        Assert.Equal(200, (int)response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello, HTTP/2!", content);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void Decode_HeadersWithEndStream_NoBodyResponse()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode(new List<(string, string)>
            { (":status", "204") });
        var headersFrame = new HeadersFrame(3, headerBlock,
            endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out var result);

        Assert.True(result.HasResponses);
        Assert.Equal(204, (int)result.Responses[0].Response.StatusCode);
        Assert.Equal(0, result.Responses[0].Response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task Decode_ContinuationFrames_Reassembled()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode(new List<(string, string)>
        {
            (":status", "200"),
            ("content-type", "application/json"),
            ("x-request-id", "abc-123"),
        });

        var split1 = headerBlock[..(headerBlock.Length / 2)];
        var split2 = headerBlock[(headerBlock.Length / 2)..];

        var headersFrame = new HeadersFrame(5, split1,
            endStream: false, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(5, split2,
            endHeaders: true).Serialize();

        var bodyData = "{\"ok\":true}"u8.ToArray();
        var dataFrame = new DataFrame(5, bodyData, endStream: true).Serialize();

        var combined = new byte[headersFrame.Length + contFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(combined, 0);
        contFrame.CopyTo(combined, headersFrame.Length);
        dataFrame.CopyTo(combined, headersFrame.Length + contFrame.Length);

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        Assert.True(result.HasResponses);
        var response = result.Responses[0].Response;
        Assert.Equal(200, (int)response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("{\"ok\":true}", content);
    }
}