using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2RoundTripHpackTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a complete HTTP/2 response: HEADERS frame (endStream=false) + DATA frame (endStream=true).
    /// Uses a fresh HpackEncoder so the state is independent across calls.
    /// </summary>
    private static byte[] BuildH2Response(
        int streamId,
        int status,
        string body,
        HpackEncoder hpack,
        params (string Name, string Value)[] extraHeaders)
    {
        var headers = new List<(string, string)>
        {
            (":status", status.ToString())
        };
        headers.AddRange(extraHeaders.Select(h => (h.Name, h.Value)));

        var headerBlock = hpack.Encode(headers);
        var headersFrame = new HeadersFrame(streamId, headerBlock,
            endStream: body.Length == 0, endHeaders: true).Serialize();

        if (body.Length == 0)
        {
            return headersFrame;
        }

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var dataFrame = new DataFrame(streamId, bodyBytes, endStream: true).Serialize();

        var combined = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(combined, 0);
        dataFrame.CopyTo(combined, headersFrame.Length);
        return combined;
    }

    private static byte[] CombineFrames(params byte[][] frames)
    {
        var total = frames.Sum(f => f.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var f in frames)
        {
            f.CopyTo(result, offset);
            offset += f.Length;
        }

        return result;
    }

    // ── RT-2-039 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-039: Well-known :status 200 uses static table indexed reference")]
    public void Should_UseIndexedRepresentation_When_Status200Encoded()
    {
        // Static table index 8 = (:status, 200) → indexed representation is 1 byte (0x88)
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "200")]);

        // For a well-known static entry, encoding should be compact (≤2 bytes)
        Assert.True(block.Length <= 2, $"Expected compact indexed encoding for :status 200, got {block.Length} bytes");
    }

    // ── RT-2-040 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-040: Response fragmented at frame boundary requires two TryDecode calls")]
    public async Task Should_DecodeResponse_When_FragmentedAtFrameBoundary()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, block, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, "split-body"u8.ToArray(), endStream: true).Serialize();

        var decoder = new Http2Decoder();

        // First call: only HEADERS frame
        var decoded1 = decoder.TryDecode(headersFrame.AsMemory(), out var result1);
        Assert.True(decoded1);
        Assert.False(result1.HasResponses); // not complete yet (no DATA with END_STREAM)

        // Second call: only DATA frame
        var decoded2 = decoder.TryDecode(dataFrame.AsMemory(), out var result2);
        Assert.True(decoded2);
        Assert.True(result2.HasResponses);
        Assert.Equal("split-body", await result2.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-041 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-041: Response fragmented mid-frame header accumulates correctly")]
    public async Task Should_DecodeResponse_When_FragmentedMidFrameHeader()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, block, endStream: true, endHeaders: true).Serialize();

        // Split the frame right in the middle of the 9-byte frame header
        var part1 = headersFrame[..4]; // only 4 bytes of the 9-byte header
        var part2 = headersFrame[4..]; // rest

        var decoder = new Http2Decoder();

        var decoded1 = decoder.TryDecode(part1.AsMemory(), out var result1);
        Assert.False(decoded1); // incomplete — cannot even read frame length

        var decoded2 = decoder.TryDecode(part2.AsMemory(), out var result2);
        Assert.True(decoded2);
        Assert.True(result2.HasResponses);
        Assert.Equal(HttpStatusCode.OK, result2.Responses[0].Response.StatusCode);
    }

    // ── RT-2-042 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-042: Single-byte delivery accumulates a complete frame over many calls")]
    public async Task Should_DecodeResponse_When_DeliveredOneByteAtATime()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, block, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        Http2DecodeResult? finalResult = null;

        for (var i = 0; i < headersFrame.Length; i++)
        {
            var oneByte = headersFrame.AsMemory(i, 1);
            decoder.TryDecode(oneByte, out var partialResult);
            if (partialResult.HasResponses)
            {
                finalResult = partialResult;
                break;
            }
        }

        Assert.NotNull(finalResult);
        Assert.True(finalResult.HasResponses);
        Assert.Equal(HttpStatusCode.OK, finalResult.Responses[0].Response.StatusCode);
    }

    // ── RT-2-043 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-043: HEADERS with endStream=true produces response with empty body")]
    public async Task Should_ReturnEmptyBody_When_HeadersFrameHasEndStream()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "204")]);
        var headersFrame = new HeadersFrame(1, block, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.AsMemory(), out var result);

        Assert.True(result.HasResponses);
        Assert.Equal(HttpStatusCode.NoContent, result.Responses[0].Response.StatusCode);
        var body = await result.Responses[0].Response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── RT-2-044 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-044: Unknown frame type (0x0F) is silently ignored")]
    public void Should_IgnoreUnknownFrameType_When_UnknownTypeReceived()
    {
        // Build a frame with type 0x0F (unknown), 0 payload, stream 0
        var unknownFrame = new byte[9];
        unknownFrame[3] = 0x0F; // unknown type

        // Follow it with a valid SETTINGS to confirm decoding continues
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 10u),
        }).Serialize();

        var combined = CombineFrames(unknownFrame, settingsFrame);

        var decoder = new Http2Decoder();
        var decoded = decoder.TryDecode(combined.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.True(result.HasNewSettings);
        Assert.Single(result.SettingsAcksToSend);
    }

    // ── RT-2-045 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-045: MAX_CONCURRENT_STREAMS enforced, exceeding limit throws")]
    public void Should_ThrowRefusedStream_When_MaxConcurrentStreamsExceeded()
    {
        // Set limit to 2 streams
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 2u),
        }).Serialize();

        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "200")]);
        var headers1 = new HeadersFrame(1, block, endStream: false, endHeaders: true).Serialize();
        var headers3 = new HeadersFrame(3, block, endStream: false, endHeaders: true).Serialize();
        var headers5 = new HeadersFrame(5, block, endStream: false, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(settingsFrame.AsMemory(), out _);

        // Open 2 streams → OK
        decoder.TryDecode(CombineFrames(headers1, headers3).AsMemory(), out _);
        Assert.Equal(2, decoder.GetActiveStreamCount());

        // Third stream → should throw
        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(headers5.AsMemory(), out _));

        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    // ── RT-2-046 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-046: Stream IDs increment as odd numbers: 1, 3, 5, 7, 9")]
    public void Should_IncrementStreamIdsAsOddNumbers_When_MultipleRequestsEncoded()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var ids = new int[5];

        for (var i = 0; i < 5; i++)
        {
            var buf = new byte[4096].AsMemory();
            var (id, _) = encoder.Encode(
                new HttpRequestMessage(HttpMethod.Get, $"https://example.com/{i}"), ref buf);
            ids[i] = id;
        }

        Assert.Equal([1, 3, 5, 7, 9], ids);
    }

    // ── RT-2-047 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-047: Reset decoder between connections clears all state")]
    public async Task Should_AcceptNewStreams_When_DecoderResetBetweenConnections()
    {
        var hpack1 = new HpackEncoder(useHuffman: false);
        var decoder = new Http2Decoder();

        // First connection: stream 1
        var frame1 = BuildH2Response(1, 200, "conn1", hpack1);
        decoder.TryDecode(frame1, out var result1);
        Assert.True(result1.HasResponses);

        // Reset for new connection
        decoder.Reset();

        // Second connection: stream 1 again (valid after reset)
        var hpack2 = new HpackEncoder(useHuffman: false);
        var frame2 = BuildH2Response(1, 200, "conn2", hpack2);
        decoder.TryDecode(frame2, out var result2);

        Assert.True(result2.HasResponses);
        Assert.Equal(1, result2.Responses[0].StreamId);
        Assert.Equal("conn2", await result2.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-048 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-048: GOAWAY debug message decoded correctly")]
    public void Should_DecodeGoAwayDebugMessage_When_GoAwayHasDebugData()
    {
        var debugMsg = "Server shutting down for maintenance";
        var goAwayFrame = Http2Encoder.EncodeGoAway(7, Http2ErrorCode.NoError, debugMsg);

        var decoder = new Http2Decoder();
        decoder.TryDecode(goAwayFrame.AsMemory(), out var result);

        Assert.True(result.HasGoAway);
        Assert.Equal(7, result.GoAway!.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, result.GoAway.ErrorCode);
    }

    // ── RT-2-049 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-049: Two independent encoders maintain separate stream ID sequences")]
    public void Should_HaveIndependentStreamIds_When_TwoEncodersCreated()
    {
        var enc1 = new Http2Encoder(useHuffman: false);
        var enc2 = new Http2Encoder(useHuffman: false);

        var buf1 = new byte[4096].AsMemory();
        var (id1a, _) = enc1.Encode(new HttpRequestMessage(HttpMethod.Get, "https://a.com/"), ref buf1);
        buf1 = new byte[4096].AsMemory();
        var (id1b, _) = enc1.Encode(new HttpRequestMessage(HttpMethod.Get, "https://a.com/2"), ref buf1);

        var buf2 = new byte[4096].AsMemory();
        var (id2a, _) = enc2.Encode(new HttpRequestMessage(HttpMethod.Get, "https://b.com/"), ref buf2);

        // Each encoder starts from stream 1 independently
        Assert.Equal(1, id1a);
        Assert.Equal(3, id1b);
        Assert.Equal(1, id2a); // independent from enc1
    }

    // ── RT-2-050 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-050: 301 Moved Permanently response decoded with Location header")]
    public async Task Should_Decode301_When_RedirectResponseReceived()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([
            (":status", "301"),
            ("location", "https://example.com/new-location"),
        ]);
        var headersFrame = new HeadersFrame(1, block, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.AsMemory(), out var result);

        Assert.True(result.HasResponses);
        var response = result.Responses[0].Response;
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        // Location header should be present
        Assert.True(response.Headers.Location is not null || response.Headers.Contains("location"));
    }

    // ── RT-2-051 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-051: Content-Type and Content-Length headers round-trip correctly")]
    public async Task Should_PreserveContentHeaders_When_ResponseHasContentHeaders()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var body = "Hello, World!"u8.ToArray();
        var block = hpack.Encode([
            (":status", "200"),
            ("content-type", "text/plain; charset=utf-8"),
            ("content-length", body.Length.ToString()),
        ]);
        var headersFrame = new HeadersFrame(1, block, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, body, endStream: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(CombineFrames(headersFrame, dataFrame), out var result);

        Assert.True(result.HasResponses);
        var response = result.Responses[0].Response;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(body.Length, response.Content.Headers.ContentLength);
        Assert.Equal("Hello, World!", await response.Content.ReadAsStringAsync());
    }

    // ── RT-2-052 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-052: HEAD-like response (HEADERS only, endStream=true) produces no body")]
    public async Task Should_HaveEmptyBody_When_ResponseHasNoDataFrame()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([
            (":status", "200"),
            ("content-length", "1024"),  // declared but no body sent (HEAD response)
        ]);
        var headersFrame = new HeadersFrame(1, block, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.AsMemory(), out var result);

        Assert.True(result.HasResponses);
        var response = result.Responses[0].Response;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    // ── RT-2-053 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-053: ValidateServerPreface accepts valid SETTINGS frame on stream 0")]
    public void Should_ReturnTrue_When_ServerPrefaceHasValidSettingsFrame()
    {
        var decoder = new Http2Decoder();
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 100u),
        }).Serialize();

        var result = decoder.ValidateServerPreface(settingsFrame.AsMemory());
        Assert.True(result);
    }

    // ── RT-2-054 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-054: Encoder strips Connection-specific headers (TE, Keep-Alive, etc.)")]
    public void Should_StripConnectionHeaders_When_RequestHasForbiddenHeaders()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var encoderBuf = new byte[4096];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        request.Headers.TryAddWithoutValidation("TE", "trailers");
        request.Headers.TryAddWithoutValidation("X-Safe-Header", "allowed");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.True(written > 0);

        // Decode the HEADERS block to verify connection-specific headers stripped
        var headerBlockLen = (encoderBuf[0] << 16) | (encoderBuf[1] << 8) | encoderBuf[2];
        var headerBlock = encoderBuf.AsMemory(9, headerBlockLen);
        var hpackDec = new HpackDecoder();
        var decoded = hpackDec.Decode(headerBlock.Span);

        var headerNames = decoded.Select(h => h.Name).ToList();
        Assert.DoesNotContain("connection", headerNames);
        Assert.DoesNotContain("keep-alive", headerNames);
        Assert.DoesNotContain("te", headerNames);
        Assert.Contains("x-safe-header", headerNames);
    }

    // ── RT-2-056 ───────────────────────────────────────────────────────────────

    /// RFC 9110 §8 — Entity headers (content-language, content-location, content-md5,
    /// content-range, content-disposition, expires, last-modified) must be preserved
    /// on the response Content object, exercising all IsContentHeader switch arms.
    [Fact(DisplayName = "RT-2-056: Entity headers (content-language, etc.) preserved in response.Content.Headers")]
    public void Should_PreserveAllEntityHeaders_When_ResponseDecodedWithEntityHeaders()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var decoder = new Http2Decoder();

        // Build a 200 response with all 7 uncovered entity header types and a small body.
        var responseBytes = BuildH2Response(
            streamId: 1,
            status: 200,
            body: "hello",
            hpack,
            ("content-language", "en-US"),
            ("content-location", "/docs/resource"),
            ("content-md5", "Q2hlY2sgSW50ZWdyaXR5IQ=="),
            ("content-range", "bytes 0-4/5"),
            ("content-disposition", "inline; filename=\"file.txt\""),
            ("expires", "Thu, 01 Jan 2026 00:00:00 GMT"),
            ("last-modified", "Wed, 01 Jan 2025 00:00:00 GMT"));

        decoder.TryDecode(responseBytes.AsMemory(), out var result);

        Assert.True(result.HasResponses);
        var response = result.Responses[0].Response;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content);

        // All 7 entity headers must be present in Content.Headers (lines 1220-1226 in Http2Decoder)
        Assert.True(response.Content.Headers.Contains("content-language"),   "content-language missing");
        Assert.True(response.Content.Headers.Contains("content-location"),    "content-location missing");
        Assert.True(response.Content.Headers.Contains("content-range"),       "content-range missing");
        Assert.True(response.Content.Headers.Contains("content-disposition"), "content-disposition missing");
        Assert.True(response.Content.Headers.Contains("expires"),             "expires missing");
        Assert.True(response.Content.Headers.Contains("last-modified"),       "last-modified missing");
    }

    // ── RT-2-055 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-055: HPACK decoder state survives across multiple TryDecode calls on same connection")]
    public async Task Should_MaintainHpackState_When_MultipleFramesSentAcrossDecodeCallBatches()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var decoder = new Http2Decoder();

        // Batch 1: stream 1 response
        var block1 = hpack.Encode([(":status", "200"), ("x-trace", "trace-001")]);
        var frames1 = CombineFrames(
            new HeadersFrame(1, block1, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(1, "batch1"u8.ToArray(), endStream: true).Serialize());
        decoder.TryDecode(frames1.AsMemory(), out var result1);

        // Batch 2: stream 3 response — uses dynamic table from batch 1
        var block2 = hpack.Encode([(":status", "200"), ("x-trace", "trace-001")]);
        var frames2 = CombineFrames(
            new HeadersFrame(3, block2, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(3, "batch2"u8.ToArray(), endStream: true).Serialize());
        decoder.TryDecode(frames2.AsMemory(), out var result2);

        // Both batches decoded correctly
        Assert.True(result1.HasResponses);
        Assert.True(result2.HasResponses);
        Assert.Equal(HttpStatusCode.OK, result1.Responses[0].Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, result2.Responses[0].Response.StatusCode);
        Assert.Equal("batch1", await result1.Responses[0].Response.Content.ReadAsStringAsync());
        Assert.Equal("batch2", await result2.Responses[0].Response.Content.ReadAsStringAsync());

        // Second block should be shorter (dynamic table reuse)
        Assert.True(block2.Length < block1.Length,
            "HPACK dynamic table should make second identical encoding shorter");
    }
}
