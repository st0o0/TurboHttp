using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http11DecoderTests
{
    private readonly Http11Decoder _decoder = new();

    [Fact]
    public async Task SimpleOk_WithContentLength_Decodes()
    {
        const string body = "Hello, World!";
        var raw = BuildResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal((int)HttpStatusCode.OK, (int)responses[0].StatusCode);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void Response204_NoContent_NoBody()
    {
        var raw = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact]
    public void ResponseWithCustomHeaders_PreservesHeaders()
    {
        var raw = BuildResponse(200, "OK", "data",
            ("Content-Length", "4"),
            ("X-Custom", "my-value"),
            ("Cache-Control", "no-store"));

        _decoder.TryDecode(raw, out var responses);

        Assert.True(responses[0].Headers.TryGetValues("X-Custom", out var custom));
        Assert.Equal("my-value", custom.Single());
        Assert.True(responses[0].Headers.TryGetValues("Cache-Control", out var cache));
        Assert.Equal("no-store", cache.Single());
    }

    [Theory]
    [InlineData(200, "OK", HttpStatusCode.OK)]
    [InlineData(201, "Created", HttpStatusCode.Created)]
    [InlineData(301, "Moved Permanently", HttpStatusCode.MovedPermanently)]
    [InlineData(400, "Bad Request", HttpStatusCode.BadRequest)]
    [InlineData(404, "Not Found", HttpStatusCode.NotFound)]
    [InlineData(500, "Internal Server Error", HttpStatusCode.InternalServerError)]
    public void KnownStatusCodes_ParseCorrectly(int code, string reason, HttpStatusCode expected)
    {
        var raw = BuildResponse(code, reason, "", ("Content-Length", "0"));
        _decoder.TryDecode(raw, out var responses);

        Assert.Equal(expected, responses[0].StatusCode);
        Assert.Equal(reason, responses[0].ReasonPhrase);
    }

    [Fact]
    public async Task IncompleteHeader_NeedMoreData_ReturnsFalse_OnSecondChunk()
    {
        const string body = "complete body";
        var full = BuildResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));

        var chunk1 = full[..10];
        var chunk2 = full[10..];

        var decoded1 = _decoder.TryDecode(chunk1, out _);
        var decoded2 = _decoder.TryDecode(chunk2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        Assert.Single(responses);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(body, result);
    }

    [Fact]
    public async Task IncompleteBody_NeedMoreData_ReturnsTrue_AfterBodyArrives()
    {
        const string body = "complete";
        var full = BuildResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));

        var headerEnd = IndexOfDoubleCrlf(full) + 4;
        var chunk1 = full[..headerEnd];
        var chunk2 = full[headerEnd..];

        var decoded1 = _decoder.TryDecode(chunk1, out _);
        var decoded2 = _decoder.TryDecode(chunk2, out var responses);

        Assert.False(decoded1);
        Assert.True(decoded2);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal(body, result);
    }

    [Fact]
    public void TwoResponses_InSameBuffer_BothDecoded()
    {
        var resp1 = BuildResponse(200, "OK", "first", ("Content-Length", "5"));
        var resp2 = BuildResponse(201, "Created", "second", ("Content-Length", "6"));

        var combined = new byte[resp1.Length + resp2.Length];
        resp1.Span.CopyTo(combined);
        resp2.Span.CopyTo(combined.AsSpan(resp1.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Equal(2, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);
    }

    [Fact]
    public async Task ChunkedBody_Decodes_Correctly()
    {
        const string chunkedBody = "5\r\nHello\r\n6\r\n World\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);
        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public async Task ChunkedBody_WithExtensions_Ignored()
    {
        const string chunkedBody = "5;ext=value\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);
        Assert.True(decoded);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void ChunkedBody_Incomplete_NeedMoreData()
    {
        const string partial = "5\r\nHel";
        var raw = BuildRaw(200, "OK", partial, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out _);
        Assert.False(decoded);
    }

    [Fact]
    public void Informational_100Continue_IsSkipped()
    {
        var raw100 = "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray();
        var raw200 = BuildResponse(200, "OK", "body", ("Content-Length", "4"));

        var combined = new byte[raw100.Length + raw200.Length];
        raw100.CopyTo(combined);
        raw200.Span.CopyTo(combined.AsSpan(raw100.Length));

        var decoded = _decoder.TryDecode(combined, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    [Fact]
    public void Response204_NoBody_ParsedCorrectly()
    {
        var raw = "HTTP/1.1 204 No Content\r\n\r\n"u8.ToArray();
        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact]
    public void Response304_NoBody_ParsedCorrectly()
    {
        var raw = "HTTP/1.1 304 Not Modified\r\nETag: \"abc\"\r\n\r\n"u8.ToArray();
        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Equal(HttpStatusCode.NotModified, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact]
    public void Decode_HeaderWithoutColon_ThrowsHttpDecoderException()
    {
        // RFC 9112 §5.1 / RFC 7230 §3.2: every header field MUST contain a colon separator.
        // A header line with no colon is a protocol violation and MUST be rejected.
        var raw = "HTTP/1.1 200 OK\r\nThisHeaderHasNoColon\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidHeader, ex.DecodeError);
    }

    // ── US-101: RFC 7230 §3.2 — Header field edge cases ────────────────────────

    [Fact]
    public void Decode_Header_OWS_Trimmed()
    {
        // RFC 7230 §3.2: OWS (optional whitespace) around header field value MUST be trimmed.
        var raw = "HTTP/1.1 200 OK\r\nX-Foo:   bar   \r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Foo", out var values));
        Assert.Equal("bar", values.Single());
    }

    [Fact]
    public void Decode_Header_EmptyValue_Accepted()
    {
        // RFC 7230 §3.2: A header field with an empty value is valid.
        var raw = "HTTP/1.1 200 OK\r\nX-Empty:\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("X-Empty", out var values));
        Assert.Equal("", values.Single());
    }

    [Fact]
    public void Decode_Header_CaseInsensitiveName()
    {
        // RFC 7230 §3.2: Header field names are case-insensitive.
        var raw = "HTTP/1.1 200 OK\r\nHOST: example.com\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        // Accessible via any casing — .NET HttpResponseMessage headers are case-insensitive
        Assert.True(responses[0].Headers.TryGetValues("Host", out var values));
        Assert.Equal("example.com", values.Single());
    }

    [Fact]
    public void Decode_Header_MultipleValuesForSameName_Preserved()
    {
        // RFC 7230 §3.2.2: Multiple header fields with the same name are valid;
        // the recipient MUST preserve all values.
        var raw = "HTTP/1.1 200 OK\r\nAccept: text/html\r\nAccept: application/json\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.True(responses[0].Headers.TryGetValues("Accept", out var values));
        var list = values.ToList();
        Assert.Contains("text/html", list);
        Assert.Contains("application/json", list);
    }

    // ── US-102: RFC 7230 §3.3 — Message Body edge cases ──────────────────────

    [Fact]
    public async Task Decode_ConflictingHeaders_ChunkedTakesPrecedence()
    {
        // RFC 9112 §6.3: If Transfer-Encoding and Content-Length are both present,
        // Transfer-Encoding takes precedence and Content-Length MUST be ignored.
        const string chunkedBody = "5\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody,
            ("Transfer-Encoding", "chunked"),
            ("Content-Length", "999"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        var result = await responses[0].Content.ReadAsStringAsync();
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void Decode_MultipleContentLength_DifferentValues_Throws()
    {
        // RFC 9112 §6.3: Multiple Content-Length headers with differing values
        // indicate a message framing error and MUST be treated as an error.
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nContent-Length: 6\r\n\r\nHello"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact]
    public void Decode_NegativeContentLength_HandledGracefully()
    {
        // RFC 7230 §3.3: A negative Content-Length is invalid. The decoder should
        // not throw; instead it treats the value as unparseable and falls through
        // to the no-body path (empty body returned).
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: -1\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact]
    public void Decode_NoBodyIndicator_EmptyBody()
    {
        // RFC 7230 §3.3-007: Response with neither Content-Length nor
        // Transfer-Encoding and non-1xx/204/304 status → empty body.
        var raw = "HTTP/1.1 200 OK\r\nX-Custom: test\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    [Fact]
    public void Decode_Header_ObsFold_Http11_IsError()
    {
        // RFC 9112 §5.2: A server MUST NOT send obs-fold in HTTP/1.1 responses.
        // A recipient that receives obs-fold SHOULD reject the message.
        // Obs-fold is a line that starts with SP or HT (continuation of previous header).
        var raw = "HTTP/1.1 200 OK\r\nX-Foo: bar\r\n baz\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
    }

    // ── US-104: RFC 7231 §6.1 — Status code edge cases ────────────────────────

    [Fact]
    public void Decode_Status599_ParsedAsCustom()
    {
        // RFC 7231 §6.1: Status codes are three-digit integers from 100 to 599.
        // 599 is at the upper boundary and MUST be accepted.
        var raw = "HTTP/1.1 599 Custom\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(599, (int)responses[0].StatusCode);
        Assert.Equal("Custom", responses[0].ReasonPhrase);
    }

    [Fact]
    public void Decode_Status600_ReturnsError()
    {
        // RFC 7231 §6.1: Status codes ≥ 600 are outside the defined range.
        // TryParseStatusLine rejects them → InvalidStatusLine error.
        var raw = "HTTP/1.1 600 Invalid\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidStatusLine, ex.DecodeError);
    }

    [Fact]
    public void Decode_EmptyReasonPhrase_Accepted()
    {
        // RFC 7230 §3.1.2: The reason-phrase is optional; a response may have
        // an empty reason after the status code (e.g., "HTTP/1.1 200 \r\n").
        var raw = "HTTP/1.1 200 \r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        // Reason phrase should be empty (or whitespace-only, implementation-defined)
        Assert.True(string.IsNullOrEmpty(responses[0].ReasonPhrase));
    }

    // ── US-103: RFC 7230 §4.1 — Chunked encoding error cases ──────────────────

    [Fact]
    public void Decode_InvalidChunkSize_ReturnsError()
    {
        // RFC 7230 §4.1: chunk-size is a hex string. Non-hex characters MUST cause a parse error.
        const string chunkedBody = "xyz\r\nHello\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact]
    public void Decode_ChunkSizeTooLarge_ReturnsError()
    {
        // RFC 7230 §4.1: A chunk size that overflows the parser's integer type MUST be rejected.
        const string chunkedBody = "999999999999\r\ndata\r\n0\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var ex = Assert.Throws<HttpDecoderException>(() => _decoder.TryDecode(raw, out _));
        Assert.Equal(HttpDecodeError.InvalidChunkSize, ex.DecodeError);
    }

    [Fact]
    public void Decode_ChunkedWithTrailer_TrailerHeadersPresent()
    {
        // RFC 7230 §4.1.2: A chunked message may include trailer fields after the last chunk.
        // Trailer headers appear between the final "0\r\n" chunk and the terminating "\r\n".
        const string chunkedBody = "5\r\nHello\r\n0\r\nX-Trailer: value\r\n\r\n";
        var raw = BuildRaw(200, "OK", chunkedBody, ("Transfer-Encoding", "chunked"));

        var decoded = _decoder.TryDecode(raw, out var responses);

        Assert.True(decoded);
        Assert.Single(responses);
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Trailer", out var values));
        Assert.Equal("value", values.Single());
    }

    private static ReadOnlyMemory<byte> BuildResponse(
        int code, string reason, string body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static ReadOnlyMemory<byte> BuildRaw(
        int code, string reason, string rawBody,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(rawBody);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static int IndexOfDoubleCrlf(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        for (var i = 0; i <= span.Length - 4; i++)
        {
            if (span[i] == '\r' && span[i + 1] == '\n' && span[i + 2] == '\r' && span[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }
}