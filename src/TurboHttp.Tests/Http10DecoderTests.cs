using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http10DecoderTests
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        string body = "")
    {
        var raw = $"{statusLine}\r\n{headers}\r\n\r\n{body}";
        return Bytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildRawResponse(
        string statusLine,
        string headers,
        byte[] body)
    {
        var headerPart = Encoding.ASCII.GetBytes($"{statusLine}\r\n{headers}\r\n\r\n");
        var result = new byte[headerPart.Length + body.Length];
        headerPart.CopyTo(result, 0);
        body.CopyTo(result, headerPart.Length);
        return result;
    }

    [Fact]
    public void StatusLine_200Ok_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.ReasonPhrase);
    }

    [Fact]
    public void StatusLine_404NotFound_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 404 Not Found", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
        Assert.Equal("Not Found", response.ReasonPhrase);
    }

    [Fact]
    public void StatusLine_500InternalServerError_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 500 Internal Server Error", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.InternalServerError, response!.StatusCode);
        Assert.Equal("Internal Server Error", response.ReasonPhrase);
    }

    [Fact]
    public void StatusLine_301MovedPermanently_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 301 Moved Permanently",
            "Location: http://example.com/new\r\nContent-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.MovedPermanently, response!.StatusCode);
    }

    [Fact]
    public void StatusLine_ReasonPhraseWithMultipleWords_PreservedCompletely()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 Very Long Reason Phrase Here", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal("Very Long Reason Phrase Here", response!.ReasonPhrase);
    }

    [Fact]
    public void StatusLine_Version_IsSetToHttp10()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(new Version(1, 0), response!.Version);
    }

    [Fact]
    public void StatusLine_InvalidStatusCode_FallsBackTo500()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 ABC BadCode", "Content-Length: 0");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.Equal((HttpStatusCode)500, response!.StatusCode);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public void StatusLine_CommonStatusCodes_AllParsedCorrectly(int code)
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse($"HTTP/1.0 {code} Reason", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal((HttpStatusCode)code, response!.StatusCode);
    }

    [Fact]
    public void Headers_SingleHeader_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Type: text/plain\r\nContent-Length: 5", "Hello");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(result);
        Assert.NotNull(response);
        Assert.NotNull(response.Content);

        Assert.True(response.Content.Headers.TryGetValues("Content-Type", out var values));
        Assert.Contains("text/plain", values!);
    }

    [Fact]
    public void Headers_CustomHeader_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Custom-Header: my-value\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Custom-Header", out var values));
        Assert.Contains("my-value", values);
    }

    [Fact]
    public void Headers_MultipleCustomHeaders_AllParsed()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Header-A: value-a\r\nX-Header-B: value-b\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Header-A", out var a));
        Assert.True(response.Headers.TryGetValues("X-Header-B", out var b));
        Assert.Contains("value-a", a);
        Assert.Contains("value-b", b);
    }

    [Fact]
    public void Headers_NamesAreCaseInsensitive()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "x-custom-header: lower-case\r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Custom-Header", out var values)
                    || response.Headers.TryGetValues("x-custom-header", out values));
        Assert.Contains("lower-case", values);
    }

    [Fact]
    public void Headers_FoldedHeader_IsContinuedCorrectly()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Folded: first part\r\n continued\r\nContent-Length: 0\r\n\r\n";

        decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(response!.Headers.TryGetValues("X-Folded", out var values));

        var combined = string.Join(" ", values);
        Assert.Contains("first part", combined);
        Assert.Contains("continued", combined);
    }

    [Fact]
    public void Headers_HeaderWithLeadingTrailingSpaces_AreTrimmed()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Spaced:   trimmed-value   \r\nContent-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Spaced", out var values));
        Assert.Contains("trimmed-value", values);
    }

    [Fact]
    public void Headers_LfOnlyLineEnding_ParsedCorrectly()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\nX-Lf-Header: lf-value\nContent-Length: 0\n\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task Body_WithContentLength_BodyReadCorrectly()
    {
        var decoder = new Http10Decoder();
        const string body = "Hello, World!";
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {body.Length}\r\nContent-Type: text/plain", body);

        decoder.TryDecode(data, out var response);

        var actualBody = await response!.Content.ReadAsStringAsync();
        Assert.Equal(body, actualBody);
    }

    [Fact]
    public async Task Body_WithContentLength_ExactBytesRead()
    {
        var decoder = new Http10Decoder();
        const string body = "ABCDE";
        const string raw = $"HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\n{body}";

        decoder.TryDecode(Bytes(raw), out var response);

        var bytes = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(3, bytes.Length);
        Assert.Equal("ABC", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void Body_WithZeroContentLength_EmptyBody()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(0, response!.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task Body_WithoutContentLength_ReadsUntilEndOfData()
    {
        var decoder = new Http10Decoder();
        const string body = "body without content-length";
        const string raw = $"HTTP/1.0 200 OK\r\n\r\n{body}";

        decoder.TryDecode(Bytes(raw), out var response);

        var actualBody = await response!.Content.ReadAsStringAsync();
        Assert.Equal(body, actualBody);
    }

    [Fact]
    public async Task Body_BinaryContent_PreservedExactly()
    {
        var bodyBytes = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFE, 0xFF };
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {bodyBytes.Length}", bodyBytes);

        decoder.TryDecode(data, out var response);

        var actualBody = await response!.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact]
    public void Body_ContentLengthHeader_SetOnContent()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 5", "Hello");

        decoder.TryDecode(data, out var response);

        Assert.Equal(5, response!.Content.Headers.ContentLength);
    }

    [Fact]
    public void Body_NoBody_ResponseContentIsNull()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 204 No Content", "Content-Length: 0");

        decoder.TryDecode(data, out var response);

        Assert.Equal(0, response!.Content.Headers.ContentLength);
    }

    [Fact]
    public void Fragmentation_HeadersSplitAcrossTwoChunks_ReassembledCorrectly()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nHello");

        var chunk1 = full[..15];
        var chunk2 = full[15..];

        var result1 = decoder.TryDecode(chunk1, out var r1);
        Assert.False(result1);
        Assert.Null(r1);

        var result2 = decoder.TryDecode(chunk2, out var r2);
        Assert.True(result2);
        Assert.NotNull(r2);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Fact]
    public async Task Fragmentation_BodySplitAcrossTwoChunks_ReassembledCorrectly()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 10\r\n\r\n1234567890");

        var separatorIdx = FindSequence(full.Span, "\r\n\r\n"u8) + 4;
        var chunk1 = full[..(separatorIdx + 5)];
        var chunk2 = full[(separatorIdx + 5)..];

        var result1 = decoder.TryDecode(chunk1, out _);
        Assert.False(result1);

        var result2 = decoder.TryDecode(chunk2, out var response);
        Assert.True(result2);
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("1234567890", body);
    }

    [Fact]
    public void Fragmentation_SingleByteChunks_EventuallyDecodes()
    {
        var decoder = new Http10Decoder();
        var full = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nABC").ToArray();

        HttpResponseMessage? response = null;
        var decoded = false;

        for (var i = 0; i < full.Length; i++)
        {
            var chunk = new ReadOnlyMemory<byte>(full, i, 1);
            if (decoder.TryDecode(chunk, out response))
            {
                decoded = true;
                break;
            }
        }

        Assert.True(decoded);
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Fragmentation_MultipleResponses_DecodedIndependently()
    {
        var decoder = new Http10Decoder();

        var resp1 = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 3\r\n\r\nONE");
        var resp2 = Bytes("HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n");

        decoder.TryDecode(resp1, out var r1);
        decoder.TryDecode(resp2, out var r2);

        Assert.Equal(HttpStatusCode.OK, r1!.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, r2!.StatusCode);
    }

    [Fact]
    public void Fragmentation_IncompleteHeader_ReturnsFalseAndBuffers()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200 OK\r\nContent-Le");

        var result = decoder.TryDecode(incomplete, out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void Fragmentation_IncompleteBody_ReturnsFalseAndBuffers()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nonly10bytes");

        var result = decoder.TryDecode(incomplete, out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public async Task Fragmentation_ThreeChunks_DecodesCorrectly()
    {
        var decoder = new Http10Decoder();
        const string full = "HTTP/1.0 200 OK\r\nContent-Length: 9\r\n\r\nABCDEFGHI";
        var bytes = Bytes(full).ToArray();

        var third = bytes.Length / 3;
        var c1 = new ReadOnlyMemory<byte>(bytes, 0, third);
        var c2 = new ReadOnlyMemory<byte>(bytes, third, third);
        var c3 = new ReadOnlyMemory<byte>(bytes, third * 2, bytes.Length - third * 2);

        Assert.False(decoder.TryDecode(c1, out _));
        Assert.False(decoder.TryDecode(c2, out _));
        var result = decoder.TryDecode(c3, out var response);

        Assert.True(result);
        var body = await response!.Content.ReadAsStringAsync();
        Assert.Equal("ABCDEFGHI", body);
    }

    [Fact]
    public void TryDecodeEof_WithBufferedData_ReturnsTrue()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200 OK\r\n\r\nsome body data");
        decoder.TryDecode(incomplete, out _);

        var decoder2 = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nshort");
        decoder2.TryDecode(partial, out _);

        var result = decoder2.TryDecodeEof(out var response);

        Assert.True(result);
        Assert.NotNull(response);
    }

    [Fact]
    public void TryDecodeEof_WithEmptyBuffer_ReturnsFalse()
    {
        var decoder = new Http10Decoder();

        var result = decoder.TryDecodeEof(out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void TryDecodeEof_WithIncompleteHeader_ReturnsFalse()
    {
        var decoder = new Http10Decoder();
        var incomplete = Bytes("HTTP/1.0 200");
        decoder.TryDecode(incomplete, out _);

        var result = decoder.TryDecodeEof(out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void TryDecodeEof_ClearsRemainder()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nshort");
        decoder.TryDecode(partial, out _);

        decoder.TryDecodeEof(out _);

        var result = decoder.TryDecodeEof(out var response);
        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void Reset_ClearsBufferedData()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nincomplete");
        decoder.TryDecode(partial, out _);

        decoder.Reset();

        var result = decoder.TryDecodeEof(out var response);
        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void Reset_AfterReset_DecodesNewResponseCorrectly()
    {
        var decoder = new Http10Decoder();
        var partial = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 100\r\n\r\nincomplete");
        decoder.TryDecode(partial, out _);

        decoder.Reset();

        var fresh = BuildRawResponse("HTTP/1.0 201 Created", "Content-Length: 0");
        var result = decoder.TryDecode(fresh, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.Created, response!.StatusCode);
    }

    [Fact]
    public void Reset_CalledMultipleTimes_DoesNotThrow()
    {
        var decoder = new Http10Decoder();

        var ex = Record.Exception(() =>
        {
            decoder.Reset();
            decoder.Reset();
            decoder.Reset();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void EdgeCase_EmptyInput_ReturnsFalse()
    {
        var decoder = new Http10Decoder();

        var result = decoder.TryDecode(ReadOnlyMemory<byte>.Empty, out var response);

        Assert.False(result);
        Assert.Null(response);
    }

    [Fact]
    public void EdgeCase_OnlyHeaderSeparator_ReturnsFalse()
    {
        var decoder = new Http10Decoder();
        var data = Bytes("\r\n\r\n");

        var result = decoder.TryDecode(data, out var response);

        Assert.True(!result || response == null || response.StatusCode == (HttpStatusCode)500);
    }

    [Fact]
    public void EdgeCase_ContentLengthNegative_HandledGracefully()
    {
        var decoder = new Http10Decoder();
        var data = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: -1");

        var ex = Record.Exception(() => decoder.TryDecode(data, out _));
        Assert.Null(ex);
    }

    [Fact]
    public void EdgeCase_DuplicateContentLength_LastValueWins()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nContent-Length: 3\r\nContent-Length: 5\r\n\r\nHello";

        var ex = Record.Exception(() => decoder.TryDecode(Bytes(raw), out _));
        Assert.Null(ex);
    }

    [Fact]
    public void EdgeCase_HeaderWithoutValue_SkippedSafely()
    {
        var decoder = new Http10Decoder();
        const string raw = "HTTP/1.0 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact]
    public void EdgeCase_VeryLargeHeader_HandledCorrectly()
    {
        var decoder = new Http10Decoder();
        var longValue = new string('A', 8000);
        var raw = $"HTTP/1.0 200 OK\r\nX-Big: {longValue}\r\nContent-Length: 0\r\n\r\n";

        var result = decoder.TryDecode(Bytes(raw), out var response);

        Assert.True(result);
        Assert.True(response!.Headers.TryGetValues("X-Big", out var values));
        Assert.Equal(longValue, values.First());
    }

    private static int FindSequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        }

        return -1;
    }
}