#nullable enable
using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Stress;

/// <summary>
/// Phase 17 — HTTP/1.1 Decoder Robustness tests.
/// Tests the decoder in isolation against fragmentation, EOF scenarios, state leakage,
/// and edge-case response formats. No network connection is used — all tests feed
/// raw bytes directly to <see cref="Http11Decoder"/>.
/// </summary>
public sealed class Http11DecoderRobustnessTests
{
    private static readonly byte[] SimpleOkResponse =
        "HTTP/1.1 200 OK\r\nContent-Length: 4\r\nContent-Type: text/plain\r\n\r\npong"u8.ToArray();

    // ── 1-byte fragmentation ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-201: Decoder receives response 1 byte at a time — eventually succeeds")]
    [Trait("Category", "Stress")]
    public async Task Should_DecodeFully_When_ResponseFedOneByteAtATime()
    {
        using var decoder = new Http11Decoder();
        var total = SimpleOkResponse;
        HttpResponseMessage? response = null;

        for (var i = 0; i < total.Length; i++)
        {
            var chunk = total.AsMemory(i, 1);
            if (decoder.TryDecode(chunk, out var responses) && responses.Count > 0)
            {
                response = responses[0];
                break;
            }
        }

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("pong", Encoding.UTF8.GetString(body));
    }

    // ── 2-byte fragmentation ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-202: Decoder receives response in 2-byte chunks — eventually succeeds")]
    [Trait("Category", "Stress")]
    public void Should_DecodeFully_When_ResponseFedIn2ByteChunks()
    {
        using var decoder = new Http11Decoder();
        var total = SimpleOkResponse;
        HttpResponseMessage? response = null;

        for (var i = 0; i < total.Length; i += 2)
        {
            var len = Math.Min(2, total.Length - i);
            var chunk = total.AsMemory(i, len);
            if (decoder.TryDecode(chunk, out var responses) && responses.Count > 0)
            {
                response = responses[0];
                break;
            }
        }

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Headers in one chunk, body in tiny chunks ─────────────────────────────

    [Fact(DisplayName = "IT-STRESS-203: Decoder receives headers in 1 chunk, body in 1000 tiny chunks")]
    [Trait("Category", "Stress")]
    public async Task Should_DecodeFully_When_HeadersInOneChunkBodyInTinyChunks()
    {
        using var decoder = new Http11Decoder();

        // Build a response with 1000-byte body
        const int bodyLen = 1000;
        var body = new byte[bodyLen];
        Array.Fill(body, (byte)'X');
        var headers = $"HTTP/1.1 200 OK\r\nContent-Length: {bodyLen}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(headers);
        var fullResponse = headerBytes.Concat(body).ToArray();

        HttpResponseMessage? response = null;

        // Send all headers in one chunk
        if (decoder.TryDecode(fullResponse.AsMemory(0, headerBytes.Length), out var r1) && r1.Count > 0)
        {
            response = r1[0];
        }

        // Send body 1 byte at a time
        if (response is null)
        {
            for (var i = headerBytes.Length; i < fullResponse.Length; i++)
            {
                if (decoder.TryDecode(fullResponse.AsMemory(i, 1), out var r2) && r2.Count > 0)
                {
                    response = r2[0];
                    break;
                }
            }
        }

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var decoded = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bodyLen, decoded.Length);
        Assert.True(decoded.All(b => b == (byte)'X'));
    }

    // ── Fuzz-style fragmentation ──────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-204: Decoder on 10 000 fragmentation patterns (fuzz-style)")]
    [Trait("Category", "Stress")]
    public void Should_AlwaysDecodeSuccessfully_When_10000FragmentationPatternsTried()
    {
        var total = SimpleOkResponse;
        var rng = new Random(42);

        for (var trial = 0; trial < 10_000; trial++)
        {
            using var decoder = new Http11Decoder();
            HttpResponseMessage? response = null;
            var pos = 0;

            while (pos < total.Length && response is null)
            {
                // Random chunk size 1..remaining
                var remaining = total.Length - pos;
                var size = rng.Next(1, Math.Min(remaining + 1, 32));
                var chunk = total.AsMemory(pos, size);
                pos += size;

                if (decoder.TryDecode(chunk, out var responses) && responses.Count > 0)
                {
                    response = responses[0];
                }
            }

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        }
    }

    // ── EOF mid-header ────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-205: EOF mid-header — decoder returns NeedMoreData (no exception)")]
    [Trait("Category", "Stress")]
    public void Should_ReturnFalse_When_ConnectionClosedMidHeader()
    {
        using var decoder = new Http11Decoder();

        // Partial header — missing the blank line that terminates headers
        var partial = "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n"u8.ToArray();

        var result = decoder.TryDecode(partial.AsMemory(), out var responses);

        // Decoder should buffer the partial data and return false (needs more data)
        Assert.False(result, "Decoder should not decode a partial header.");
        Assert.Empty(responses);
    }

    // ── EOF mid-body ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-206: EOF mid-body (Content-Length not fulfilled) — decoder returns false")]
    [Trait("Category", "Stress")]
    public void Should_ReturnFalse_When_ConnectionClosedMidBody()
    {
        using var decoder = new Http11Decoder();

        // Headers declare Content-Length: 100 but only 10 bytes of body follow
        var partial = "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\npartial!!!"u8.ToArray();

        var result = decoder.TryDecode(partial.AsMemory(), out var responses);

        Assert.False(result, "Decoder should not decode when Content-Length body is incomplete.");
        Assert.Empty(responses);
    }

    // ── EOF mid-chunk ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-207: EOF mid-chunk — partial chunk data → decoder returns false")]
    [Trait("Category", "Stress")]
    public void Should_ReturnFalse_When_ConnectionClosedMidChunk()
    {
        using var decoder = new Http11Decoder();

        // Chunked response where the chunk data is incomplete
        // Chunk size says 10 bytes but only 3 bytes of chunk data follow
        var partial = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\na\r\npar"u8.ToArray();

        var result = decoder.TryDecode(partial.AsMemory(), out var responses);

        Assert.False(result, "Decoder should not decode when chunked body is incomplete.");
        Assert.Empty(responses);
    }

    // ── Decoder Reset ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-208: Decoder Reset() clears all state — next response decoded fresh")]
    [Trait("Category", "Stress")]
    public async Task Should_DecodeFreshResponse_When_DecoderResetAfterPartialFeed()
    {
        using var decoder = new Http11Decoder();

        // Feed partial data from response A
        var partialA = "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\npartial"u8.ToArray();
        decoder.TryDecode(partialA.AsMemory(), out _);

        // Reset decoder — all buffered state should be cleared
        decoder.Reset();

        // Now feed a complete response B — must decode correctly without interference from A
        var result = decoder.TryDecode(SimpleOkResponse.AsMemory(), out var responses);

        Assert.True(result, "Decoder should decode response B after Reset().");
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal("pong", Encoding.UTF8.GetString(body));
    }

    // ── Response with minimal headers ─────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-209: Decoder handles response with no headers except status-line")]
    [Trait("Category", "Stress")]
    public async Task Should_Decode_When_ResponseHasNoHeadersExceptStatusLine()
    {
        using var decoder = new Http11Decoder();

        // RFC 9112 allows a response with only the status-line and CRLF CRLF
        var minimal = "HTTP/1.1 200 OK\r\n\r\n"u8.ToArray();

        var result = decoder.TryDecode(minimal.AsMemory(), out var responses);

        Assert.True(result);
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── HTTP/1.0-style response on HTTP/1.1 decoder ───────────────────────────

    [Fact(DisplayName = "IT-STRESS-210: Decoder handles HTTP/1.0 response on HTTP/1.1 connection")]
    [Trait("Category", "Stress")]
    public async Task Should_Decode_When_Http10StyleResponseReceivedOnHttp11Decoder()
    {
        using var decoder = new Http11Decoder();

        // HTTP/1.0 response with Content-Length (decoder should handle this gracefully)
        var http10Response =
            "HTTP/1.0 200 OK\r\nContent-Length: 5\r\nContent-Type: text/plain\r\n\r\nhello"u8.ToArray();

        var result = decoder.TryDecode(http10Response.AsMemory(), out var responses);

        Assert.True(result, "HTTP/1.1 decoder should handle HTTP/1.0 version responses.");
        Assert.Single(responses);
        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal("hello", Encoding.UTF8.GetString(body));
    }

    // ── Content-Length mismatch ───────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-211: Content-Length mismatch — server sends fewer bytes → incomplete")]
    [Trait("Category", "Stress")]
    public void Should_ReturnFalse_When_ServerSendsFewerBytesThanContentLength()
    {
        using var decoder = new Http11Decoder();

        // Content-Length declares 50 bytes but only 10 are supplied
        var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 50\r\n\r\n");
        var partial10 = Encoding.ASCII.GetBytes("0123456789"); // only 10 bytes
        var truncated = header.Concat(partial10).ToArray();

        var result = decoder.TryDecode(truncated.AsMemory(), out var responses);

        Assert.False(result, "Decoder must not return a response when Content-Length is not fulfilled.");
        Assert.Empty(responses);
    }

    // ── Two responses in one TCP segment ─────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-212: Two responses in one TCP segment — both decoded")]
    [Trait("Category", "Stress")]
    public async Task Should_DecodeBothResponses_When_TwoResponsesInOneTcpSegment()
    {
        using var decoder = new Http11Decoder();

        // Two complete HTTP/1.1 responses concatenated (as if sent in one TCP segment)
        var responseA = "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\npong"u8.ToArray();
        var responseB = "HTTP/1.1 404 Not Found\r\nContent-Length: 9\r\n\r\nnot-found"u8.ToArray();
        var combined = responseA.Concat(responseB).ToArray();

        var result = decoder.TryDecode(combined.AsMemory(), out var responses);

        Assert.True(result, "Decoder should return true when at least one response is decoded.");
        Assert.Equal(2, responses.Count);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        var body0 = await responses[0].Content.ReadAsByteArrayAsync();
        Assert.Equal("pong", Encoding.UTF8.GetString(body0));

        Assert.Equal(HttpStatusCode.NotFound, responses[1].StatusCode);
        var body1 = await responses[1].Content.ReadAsByteArrayAsync();
        Assert.Equal("not-found", Encoding.UTF8.GetString(body1));
    }

    // ── Interleaved partial responses from two connections ────────────────────

    [Fact(DisplayName = "IT-STRESS-213: Interleaved partial responses from two connections — both decode independently")]
    [Trait("Category", "Stress")]
    public async Task Should_DecodeIndependently_When_TwoDecodersFedInterleavedPartialData()
    {
        using var decoder1 = new Http11Decoder();
        using var decoder2 = new Http11Decoder();

        var responseA = "HTTP/1.1 200 OK\r\nContent-Length: 7\r\n\r\nrespA-1"u8.ToArray();
        var responseB = "HTTP/1.1 201 Created\r\nContent-Length: 7\r\n\r\nrespB-2"u8.ToArray();

        // Split each response at the midpoint
        var midA = responseA.Length / 2;
        var midB = responseB.Length / 2;

        // Feed first half of A to decoder1, first half of B to decoder2
        decoder1.TryDecode(responseA.AsMemory(0, midA), out _);
        decoder2.TryDecode(responseB.AsMemory(0, midB), out _);

        // Feed second half of A to decoder1, second half of B to decoder2
        var result1 = decoder1.TryDecode(responseA.AsMemory(midA), out var responses1);
        var result2 = decoder2.TryDecode(responseB.AsMemory(midB), out var responses2);

        Assert.True(result1, "decoder1 should decode response A.");
        Assert.Single(responses1);
        Assert.Equal(HttpStatusCode.OK, responses1[0].StatusCode);
        var bodyA = await responses1[0].Content.ReadAsByteArrayAsync();
        Assert.Equal("respA-1", Encoding.UTF8.GetString(bodyA));

        Assert.True(result2, "decoder2 should decode response B.");
        Assert.Single(responses2);
        Assert.Equal(HttpStatusCode.Created, responses2[0].StatusCode);
        var bodyB = await responses2[0].Content.ReadAsByteArrayAsync();
        Assert.Equal("respB-2", Encoding.UTF8.GetString(bodyB));
    }
}
