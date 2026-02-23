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