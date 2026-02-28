using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http11RoundTripTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static (byte[] Buffer, int Written) EncodeRequest(HttpRequestMessage request)
    {
        var buffer = new byte[65536];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);
        return (buffer, written);
    }

    private static ReadOnlyMemory<byte> BuildResponse(int status, string reason, string body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        sb.Append(body);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static ReadOnlyMemory<byte> BuildBinaryResponse(int status, string reason, byte[] body,
        params (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {reason}\r\n");
        foreach (var (name, value) in headers)
        {
            sb.Append($"{name}: {value}\r\n");
        }

        sb.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    private static ReadOnlyMemory<byte> BuildChunkedResponse(int status, string reason,
        string[] chunks, (string Name, string Value)[]? trailers = null)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {reason}\r\n");
        sb.Append("Transfer-Encoding: chunked\r\n");
        sb.Append("\r\n");
        foreach (var chunk in chunks)
        {
            var chunkLen = Encoding.ASCII.GetByteCount(chunk);
            sb.Append($"{chunkLen:x}\r\n{chunk}\r\n");
        }

        sb.Append("0\r\n");
        if (trailers != null)
        {
            foreach (var (name, value) in trailers)
            {
                sb.Append($"{name}: {value}\r\n");
            }
        }

        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    // ── RT-11-001 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-001: HTTP/1.1 GET → 200 OK round-trip")]
    public async Task Should_Return200_When_GetRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.StartsWith("GET /api HTTP/1.1\r\n", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "hello", ("Content-Length", "5"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("hello", await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-002 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-002: HTTP/1.1 POST JSON → 201 Created round-trip")]
    public void Should_Return201Created_When_PostJsonRoundTrip()
    {
        const string json = "{\"name\":\"Alice\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/users")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("POST /users HTTP/1.1", encoded);
        Assert.Contains("Content-Type: application/json", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(201, "Created", "",
            ("Content-Length", "0"), ("Location", "/users/42"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
        Assert.True(responses[0].Headers.TryGetValues("Location", out var loc));
        Assert.Equal("/users/42", loc.Single());
    }

    // ── RT-11-003 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-003: HTTP/1.1 PUT → 204 No Content round-trip")]
    public void Should_Return204NoContent_When_PutRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/resource/1")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("PUT /resource/1 HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(204, "No Content", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
    }

    // ── RT-11-004 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-004: HTTP/1.1 DELETE → 200 OK round-trip")]
    public void Should_Return200_When_DeleteRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://example.com/resource/5");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("DELETE /resource/5 HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
    }

    // ── RT-11-005 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-005: HTTP/1.1 PATCH → 200 OK round-trip")]
    public async Task Should_Return200_When_PatchRoundTrip()
    {
        const string patch = "{\"op\":\"replace\",\"path\":\"/name\",\"value\":\"Bob\"}";
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), "http://example.com/item/3")
        {
            Content = new StringContent(patch, Encoding.UTF8, "application/json-patch+json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("PATCH /item/3 HTTP/1.1", encoded);

        const string responseBody = "{\"id\":3}";
        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", responseBody,
            ("Content-Length", responseBody.Length.ToString()),
            ("Content-Type", "application/json"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(responseBody, await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-006 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-006: HTTP/1.1 HEAD → Content-Length but no body")]
    public void Should_ReturnContentLengthHeader_When_HeadRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.StartsWith("HEAD /resource HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        // HEAD response: Content-Length=0; no body bytes follow
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Content-Type", "application/octet-stream"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(0, responses[0].Content.Headers.ContentLength);
    }

    // ── RT-11-007 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-007: HTTP/1.1 OPTIONS → 200 with Allow header")]
    public void Should_ReturnAllowHeader_When_OptionsRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "http://example.com/resource");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("OPTIONS /resource HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", "",
            ("Content-Length", "0"),
            ("Allow", "GET, POST, PUT, DELETE, OPTIONS"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.True(responses[0].Content.Headers.TryGetValues("Allow", out var allowVals));
        Assert.Contains("GET", string.Join(",", allowVals));
    }

    // ── RT-11-008 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-008: HTTP/1.1 GET → 200 chunked response round-trip")]
    public async Task Should_AssembleChunkedBody_When_ChunkedRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/stream");
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["Hello, ", "World!"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("Hello, World!", await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-009 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-009: HTTP/1.1 GET → response with 5 chunks round-trip")]
    public async Task Should_ConcatenateChunks_When_FiveChunksRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/multi");
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK", ["one", "two", "three", "four", "five"]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("onetwothreefourfive", await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-010 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-010: HTTP/1.1 chunked response with trailer round-trip")]
    public async Task Should_AccessTrailer_When_ChunkedWithTrailerRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/trailer");
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildChunkedResponse(200, "OK",
            ["chunk1", "chunk2"],
            [("X-Checksum", "abc123")]);
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("chunk1chunk2", await responses[0].Content.ReadAsStringAsync());
        Assert.True(responses[0].TrailingHeaders.TryGetValues("X-Checksum", out var trailerVals));
        Assert.Equal("abc123", trailerVals.Single());
    }

    // ── RT-11-011 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-011: HTTP/1.1 GET → 301 with Location round-trip")]
    public void Should_Return301WithLocation_When_GetRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/old-path");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("GET /old-path HTTP/1.1", encoded);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(301, "Moved Permanently", "",
            ("Content-Length", "0"),
            ("Location", "http://example.com/new-path"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.MovedPermanently, responses[0].StatusCode);
        Assert.True(responses[0].Headers.TryGetValues("Location", out var loc));
        Assert.Contains("new-path", loc.Single());
    }

    // ── RT-11-012 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-012: HTTP/1.1 POST binary → 200 binary response round-trip")]
    public async Task Should_PreserveBinaryBody_When_PostBinaryRoundTrip()
    {
        var binary = new byte[256];
        for (var i = 0; i < 256; i++) { binary[i] = (byte)i; }

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Content = new ByteArrayContent(binary)
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var (buffer, written) = EncodeRequest(request);
        Assert.Contains("POST", Encoding.ASCII.GetString(buffer, 0, 20));

        var decoder = new Http11Decoder();
        var raw = BuildBinaryResponse(200, "OK", binary, ("Content-Length", "256"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(binary, await responses[0].Content.ReadAsByteArrayAsync());
    }

    // ── RT-11-013 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-013: HTTP/1.1 GET → 404 Not Found round-trip")]
    public async Task Should_Return404_When_ResourceMissingRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing");
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("GET /missing HTTP/1.1", encoded);

        const string body = "Not Found";
        var decoder = new Http11Decoder();
        var raw = BuildResponse(404, "Not Found", body, ("Content-Length", body.Length.ToString()));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.NotFound, responses[0].StatusCode);
        Assert.Equal("Not Found", await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-014 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-014: HTTP/1.1 GET → 500 Internal Server Error round-trip")]
    public void Should_Return500_When_ServerErrorRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/error");
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildResponse(500, "Internal Server Error", "", ("Content-Length", "0"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.InternalServerError, responses[0].StatusCode);
    }

    // ── RT-11-015 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-015: Two pipelined requests and responses round-trip")]
    public async Task Should_DecodeBothResponses_When_TwoPipelinedRequestsRoundTrip()
    {
        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a");
        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/b");
        var (_, w1) = EncodeRequest(req1);
        var (_, w2) = EncodeRequest(req2);
        Assert.True(w1 > 0);
        Assert.True(w2 > 0);

        var resp1 = BuildResponse(200, "OK", "alpha", ("Content-Length", "5"));
        var resp2 = BuildResponse(200, "OK", "beta", ("Content-Length", "4"));
        var combined = new byte[resp1.Length + resp2.Length];
        resp1.Span.CopyTo(combined);
        resp2.Span.CopyTo(combined.AsSpan(resp1.Length));

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        Assert.Equal(2, responses.Count);
        Assert.Equal("alpha", await responses[0].Content.ReadAsStringAsync());
        Assert.Equal("beta", await responses[1].Content.ReadAsStringAsync());
    }

    // ── RT-11-016 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-016: 100 Continue before 200 OK round-trip")]
    public async Task Should_SkipContinue_And_Return200_When_100ContinueRoundTrip()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/data")
        {
            Content = new StringContent("Hello", Encoding.ASCII, "text/plain")
        };
        request.Headers.ExpectContinue = true;
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var continue100 = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
        var ok200 = BuildResponse(200, "OK", "done", ("Content-Length", "4"));
        var combined = new byte[continue100.Length + ok200.Length];
        continue100.CopyTo(combined, 0);
        ok200.Span.CopyTo(combined.AsSpan(continue100.Length));

        var decoder = new Http11Decoder();
        decoder.TryDecode(combined, out var responses);

        // 100 Continue is skipped; only the 200 OK is returned
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal("done", await responses[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-017 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-017: HTTP/1.1 1 MB body round-trip")]
    public async Task Should_Preserve1MbBody_When_LargeBodyRoundTrip()
    {
        const int oneMb = 1024 * 1024;
        var body = new byte[oneMb];
        for (var i = 0; i < oneMb; i++) { body[i] = (byte)(i % 256); }

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Content = new ByteArrayContent(body)
        };
        var encBuf = new byte[oneMb + 4096];
        var span = encBuf.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);
        Assert.True(written > oneMb);

        var decoder = new Http11Decoder(maxBodySize: oneMb + 1024);
        var raw = BuildBinaryResponse(200, "OK", body, ("Content-Length", oneMb.ToString()));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(body, await responses[0].Content.ReadAsByteArrayAsync());
    }

    // ── RT-11-018 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-018: HTTP/1.1 binary body with null bytes round-trip")]
    public async Task Should_PreserveNullBytes_When_BinaryBodyRoundTrip()
    {
        var body = new byte[] { 0x00, 0x01, 0x00, 0xFF, 0x00, 0x7F, 0x00 };
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/binary")
        {
            Content = new ByteArrayContent(body)
        };
        var (_, written) = EncodeRequest(request);
        Assert.True(written > 0);

        var decoder = new Http11Decoder();
        var raw = BuildBinaryResponse(200, "OK", body, ("Content-Length", body.Length.ToString()));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal(body, await responses[0].Content.ReadAsByteArrayAsync());
    }

    // ── RT-11-019 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-019: Two responses on keep-alive connection round-trip")]
    public async Task Should_DecodeSecondResponse_When_KeepAliveRoundTrip()
    {
        var decoder = new Http11Decoder();

        var req1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/first");
        var (_, w1) = EncodeRequest(req1);
        Assert.True(w1 > 0);

        var raw1 = BuildResponse(200, "OK", "first",
            ("Content-Length", "5"), ("Connection", "keep-alive"));
        decoder.TryDecode(raw1, out var responses1);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/second");
        var (_, w2) = EncodeRequest(req2);
        Assert.True(w2 > 0);

        var raw2 = BuildResponse(200, "OK", "second",
            ("Content-Length", "6"), ("Connection", "keep-alive"));
        decoder.TryDecode(raw2, out var responses2);

        Assert.Single(responses1);
        Assert.Equal("first", await responses1[0].Content.ReadAsStringAsync());
        Assert.Single(responses2);
        Assert.Equal("second", await responses2[0].Content.ReadAsStringAsync());
    }

    // ── RT-11-020 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-11-020: Content-Type: application/json; charset=utf-8 preserved")]
    public void Should_PreserveContentType_When_JsonCharsetRoundTrip()
    {
        const string json = "{\"key\":\"value\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var (buffer, written) = EncodeRequest(request);
        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Content-Type: application/json", encoded);

        var byteCount = Encoding.UTF8.GetByteCount(json);
        var decoder = new Http11Decoder();
        var raw = BuildResponse(200, "OK", json,
            ("Content-Length", byteCount.ToString()),
            ("Content-Type", "application/json; charset=utf-8"));
        decoder.TryDecode(raw, out var responses);

        Assert.Single(responses);
        Assert.Equal("application/json", responses[0].Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", responses[0].Content.Headers.ContentType!.CharSet);
    }
}
