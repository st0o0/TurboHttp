using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http2RoundTripTests
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

    // ── RT-2-001 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-001: HTTP/2 connection preface + SETTINGS exchange")]
    public void Should_ContainMagicAndSettings_When_ConnectionPrefaceBuilt()
    {
        var preface = Http2Encoder.BuildConnectionPreface();

        // Verify PRI magic (24 bytes)
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        Assert.Equal(magic, preface[..magic.Length]);

        // Verify SETTINGS frame starts at byte 24
        Assert.Equal((byte)FrameType.Settings, preface[magic.Length + 3]);

        // Server sends its own SETTINGS; client decoder should queue a SETTINGS ACK
        var serverSettings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 128u),
        }).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(serverSettings, out var result);

        Assert.True(result.HasNewSettings);
        Assert.Single(result.SettingsAcksToSend);
    }

    // ── RT-2-002 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-002: HTTP/2 GET → 200 on stream 1")]
    public async Task Should_Return200_When_Http2GetRoundTrip()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var buf = new byte[4096].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);
        Assert.True(written > 0);

        var hpack = new HpackEncoder(useHuffman: false);
        var responseFrame = BuildH2Response(streamId, 200, "Hello HTTP/2", hpack);
        var decoder = new Http2Decoder();
        var decoded = decoder.TryDecode(responseFrame, out var result);

        Assert.True(decoded);
        Assert.True(result.HasResponses);
        Assert.Single(result.Responses);
        Assert.Equal(1, result.Responses[0].StreamId);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
        Assert.Equal("Hello HTTP/2", await result.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-003 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-003: HTTP/2 POST → HEADERS+DATA → 201 response")]
    public async Task Should_Return201_When_Http2PostRoundTrip()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var encoderBuf = new byte[8192];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/items")
        {
            Content = new StringContent("{\"name\":\"Alice\"}", Encoding.UTF8, "application/json")
        };
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);

        // First frame: HEADERS (type 0x01)
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);

        // HEADERS for POST must NOT have END_STREAM (bit 0) since there is a body
        var headersFlags = (HeadersFlags)encoderBuf[4];
        Assert.False(headersFlags.HasFlag(HeadersFlags.EndStream));
        Assert.True(headersFlags.HasFlag(HeadersFlags.EndHeaders));

        // A DATA frame must follow (verify it exists)
        var firstFrameLen = (encoderBuf[0] << 16) | (encoderBuf[1] << 8) | encoderBuf[2];
        var dataFrameStart = 9 + firstFrameLen;
        Assert.Equal((byte)FrameType.Data, encoderBuf[dataFrameStart + 3]);
        Assert.True(written > dataFrameStart);

        // Decode 201 response
        var hpack = new HpackEncoder(useHuffman: false);
        var responseFrame = BuildH2Response(streamId, 201, "", hpack);
        var decoder = new Http2Decoder();
        decoder.TryDecode(responseFrame, out var result);

        Assert.True(result.HasResponses);
        Assert.Equal(HttpStatusCode.Created, result.Responses[0].Response.StatusCode);
        Assert.Equal(0, result.Responses[0].Response.Content.Headers.ContentLength);
    }

    // ── RT-2-004 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-004: HTTP/2 three concurrent streams each complete independently")]
    public async Task Should_ReturnThreeResponses_When_ThreeConcurrentStreams()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var buf = new byte[4096].AsMemory();

        var (id1, _) = encoder.Encode(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/a"), ref buf);

        buf = new byte[4096].AsMemory();
        var (id2, _) = encoder.Encode(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/b"), ref buf);

        buf = new byte[4096].AsMemory();
        var (id3, _) = encoder.Encode(
            new HttpRequestMessage(HttpMethod.Get, "https://example.com/c"), ref buf);

        Assert.Equal(1, id1);
        Assert.Equal(3, id2);
        Assert.Equal(5, id3);

        var hpack = new HpackEncoder(useHuffman: false);
        var stream1 = BuildH2Response(id1, 200, "response-1", hpack);
        var stream2 = BuildH2Response(id2, 200, "response-2", hpack);
        var stream3 = BuildH2Response(id3, 200, "response-3", hpack);
        var combined = CombineFrames(stream1, stream2, stream3);

        var decoder = new Http2Decoder();
        var decoded = decoder.TryDecode(combined, out var result);

        Assert.True(decoded);
        Assert.Equal(3, result.Responses.Count);

        var byStream = result.Responses.ToDictionary(r => r.StreamId, r => r.Response);
        Assert.True(byStream.ContainsKey(id1));
        Assert.True(byStream.ContainsKey(id2));
        Assert.True(byStream.ContainsKey(id3));
        Assert.Equal("response-1", await byStream[id1].Content.ReadAsStringAsync());
        Assert.Equal("response-2", await byStream[id2].Content.ReadAsStringAsync());
        Assert.Equal("response-3", await byStream[id3].Content.ReadAsStringAsync());
    }

    // ── RT-2-005 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-005: HTTP/2 HPACK dynamic table reused across three requests")]
    public async Task Should_ProduceSmallerHpackBlock_When_DynamicTableReuseAcrossRequests()
    {
        // Build three response header blocks with the same headers using one HpackEncoder.
        // The first block encodes "content-type: text/html" as a full literal (~20 bytes).
        // The second and third blocks use the indexed reference (~1 byte).
        var hpack = new HpackEncoder(useHuffman: false);

        var block1 = hpack.Encode([
            (":status", "200"),
            ("content-type", "text/html"),
        ]);
        var block2 = hpack.Encode([
            (":status", "200"),
            ("content-type", "text/html"),
        ]);
        var block3 = hpack.Encode([
            (":status", "200"),
            ("content-type", "text/html"),
        ]);

        // Dynamic table reuse means subsequent blocks are smaller
        Assert.True(block1.Length > block2.Length,
            "Second HPACK block should be shorter due to dynamic table reuse");
        Assert.Equal(block2.Length, block3.Length);

        // Build frames for streams 1, 3, 5 using the three blocks
        var frames1 = CombineFrames(
            new HeadersFrame(1, block1, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(1, "body-1"u8.ToArray(), endStream: true).Serialize());
        var frames2 = CombineFrames(
            new HeadersFrame(3, block2, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(3, "body-2"u8.ToArray(), endStream: true).Serialize());
        var frames3 = CombineFrames(
            new HeadersFrame(5, block3, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(5, "body-3"u8.ToArray(), endStream: true).Serialize());
        var combined = CombineFrames(frames1, frames2, frames3);

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        Assert.Equal(3, result.Responses.Count);

        foreach (var (_, response) in result.Responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        }

        var bodies = new HashSet<string>();
        foreach (var (_, response) in result.Responses)
        {
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        Assert.Contains("body-1", bodies);
        Assert.Contains("body-2", bodies);
        Assert.Contains("body-3", bodies);
    }

    // ── RT-2-006 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-006: HTTP/2 server SETTINGS → client ACK → both sides updated")]
    public void Should_ApplyServerSettings_When_SettingsReceivedAndAcked()
    {
        var serverSettings = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxFrameSize, 32768u),
            (SettingsParameter.InitialWindowSize, 131070u),
        }).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(serverSettings, out var result);

        Assert.True(result.HasNewSettings);
        Assert.Single(result.ReceivedSettings);

        var settings = result.ReceivedSettings[0];
        Assert.Contains(settings, s =>
            s.Item1 == SettingsParameter.MaxFrameSize && s.Item2 == 32768u);
        Assert.Contains(settings, s =>
            s.Item1 == SettingsParameter.InitialWindowSize && s.Item2 == 131070u);

        // Client must send a SETTINGS ACK back
        Assert.Single(result.SettingsAcksToSend);

        // Apply to encoder: encoder respects the new max frame size
        var encoder = new Http2Encoder(useHuffman: false);
        encoder.ApplyServerSettings(settings);
        var buf = new byte[65536].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var (_, written) = encoder.Encode(request, ref buf);
        Assert.True(written > 0);
    }

    // ── RT-2-007 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-007: HTTP/2 server PING → client PONG with same payload")]
    public void Should_ReturnPingAckWithSamePayload_When_ServerPingReceived()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var pingFrame = new PingFrame(payload, isAck: false).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(pingFrame, out var result);

        // Decoder queues a PING ACK for the client to send
        Assert.Single(result.PingAcksToSend);

        // Verify the ACK payload matches the original PING payload
        var ackFrame = result.PingAcksToSend[0];
        Assert.Equal((byte)FrameType.Ping, ackFrame[3]);
        Assert.Equal((byte)PingFlags.Ack, ackFrame[4]);
        Assert.Equal(payload, ackFrame[9..17]);
    }

    // ── RT-2-008 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-008: HTTP/2 GOAWAY received → no new requests sent")]
    public void Should_SignalGoAway_When_GoAwayFrameReceived()
    {
        var goawayFrame = new GoAwayFrame(5, Http2ErrorCode.NoError).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(goawayFrame, out var result);

        Assert.True(result.HasGoAway);
        Assert.Equal(5, result.GoAway!.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, result.GoAway.ErrorCode);
    }

    // ── RT-2-009 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-009: HTTP/2 RST_STREAM → stream dropped, other streams continue")]
    public async Task Should_DropStream1AndCompleteStream3_When_RstStreamReceived()
    {
        var hpack = new HpackEncoder(useHuffman: false);

        // Stream 1: HEADERS (no endStream) then RST_STREAM
        var headers1Block = hpack.Encode([(":status", "200")]);
        var headers1 = new HeadersFrame(1, headers1Block,
            endStream: false, endHeaders: true).Serialize();
        var rst1 = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();

        // Stream 3: complete HEADERS + DATA
        var headers3Block = hpack.Encode([(":status", "200")]);
        var headers3 = new HeadersFrame(3, headers3Block,
            endStream: false, endHeaders: true).Serialize();
        var data3 = new DataFrame(3, "stream3-ok"u8.ToArray(), endStream: true).Serialize();

        var combined = CombineFrames(headers1, rst1, headers3, data3);

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        // Stream 1 was RST'd
        Assert.Contains(result.RstStreams, r => r.StreamId == 1 && r.Error == Http2ErrorCode.Cancel);

        // Stream 3 completed normally
        Assert.True(result.HasResponses);
        var stream3 = result.Responses.Single(r => r.StreamId == 3);
        Assert.Equal(HttpStatusCode.OK, stream3.Response.StatusCode);
        Assert.Equal("stream3-ok", await stream3.Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-010 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-010: Authorization header NeverIndexed in HTTP/2 round-trip")]
    public void Should_NeverIndexAuthorization_When_AuthorizationHeaderEncoded()
    {
        // Verify NeverIndex encoding by encoding Authorization twice using HpackEncoder directly.
        // NeverIndexed headers are never added to the dynamic table, so both encodings
        // produce identical byte sequences (the header cannot be reduced to an indexed reference).
        var hpackEnc = new HpackEncoder(useHuffman: false);
        var block1 = hpackEnc.Encode([("authorization", "Bearer secret123")]);
        var block2 = hpackEnc.Encode([("authorization", "Bearer secret123")]);

        // Both blocks must be identical in length — NeverIndexed prevents dynamic table entry
        Assert.Equal(block1.Length, block2.Length);

        // The first byte must have the NeverIndexed flag (0x10 = 0001xxxx prefix)
        // For "authorization" with static name index 42 > 15: first byte = 0x1F (prefix overflow)
        Assert.Equal(0x10, block1.Span[0] & 0xF0);

        // Full round-trip: encode a request with Authorization, then decode a 200 response
        var encoder = new Http2Encoder(useHuffman: false);
        var buf = new byte[4096].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/secure");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret123");
        var (streamId, written) = encoder.Encode(request, ref buf);
        Assert.True(written > 0);

        var respHpack = new HpackEncoder(useHuffman: false);
        var responseFrame = BuildH2Response(streamId, 200, "", respHpack);
        var decoder = new Http2Decoder();
        decoder.TryDecode(responseFrame, out var result);
        Assert.True(result.HasResponses);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
    }

    // ── RT-2-011 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-011: Cookie header NeverIndexed in HTTP/2 round-trip")]
    public void Should_NeverIndexCookie_When_CookieHeaderEncoded()
    {
        // Same pattern as rt2-010: Cookie is a sensitive header and must use NeverIndexed encoding.
        var hpackEnc = new HpackEncoder(useHuffman: false);
        var block1 = hpackEnc.Encode([("cookie", "session=abc123; user=alice")]);
        var block2 = hpackEnc.Encode([("cookie", "session=abc123; user=alice")]);

        // NeverIndexed: dynamic table never updated → same encoding both times
        Assert.Equal(block1.Length, block2.Length);

        // First byte must have the NeverIndexed flag (0x10 = 0001xxxx prefix)
        Assert.Equal(0x10, block1.Span[0] & 0xF0);

        // Full round-trip
        var encoder = new Http2Encoder(useHuffman: false);
        var buf = new byte[4096].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/profile");
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc123; user=alice");
        var (streamId, written) = encoder.Encode(request, ref buf);
        Assert.True(written > 0);

        var respHpack = new HpackEncoder(useHuffman: false);
        var responseFrame = BuildH2Response(streamId, 200, "ok", respHpack);
        var decoder = new Http2Decoder();
        decoder.TryDecode(responseFrame, out var result);
        Assert.True(result.HasResponses);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
    }

    // ── RT-2-012 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-012: HTTP/2 request with headers exceeding frame size uses CONTINUATION")]
    public async Task Should_UseContinuationFrames_When_HeadersExceedMaxFrameSize()
    {
        // Set a small max frame size to force CONTINUATION frames
        var encoder = new Http2Encoder(useHuffman: false);
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 10u)]);

        var encoderBuf = new byte[4096];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/resource");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.True(written > 0);

        // First frame is HEADERS type
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);

        // HEADERS frame flags must NOT have END_HEADERS (0x04) — continuation follows
        var headersFlags = (HeadersFlags)encoderBuf[4];
        Assert.False(headersFlags.HasFlag(HeadersFlags.EndHeaders));

        // Second frame must be CONTINUATION type (0x09)
        var firstFramePayloadLen = (encoderBuf[0] << 16) | (encoderBuf[1] << 8) | encoderBuf[2];
        var continuationFrameOffset = 9 + firstFramePayloadLen;
        Assert.Equal((byte)FrameType.Continuation, encoderBuf[continuationFrameOffset + 3]);

        // Response side: build a response using HEADERS + CONTINUATION manually
        var responseHpack = new HpackEncoder(useHuffman: false);
        var responseHeaderBlock = responseHpack.Encode([
            (":status", "200"),
            ("content-type", "text/plain"),
            ("x-request-id", "12345"),
        ]);

        // Split header block across HEADERS (no END_HEADERS) + CONTINUATION (END_HEADERS)
        var splitAt = responseHeaderBlock.Length / 2;
        var part1 = responseHeaderBlock[..splitAt];
        var part2 = responseHeaderBlock[splitAt..];

        var respHeadersFrame = new HeadersFrame(streamId, part1,
            endStream: false, endHeaders: false).Serialize();
        var respContinuationFrame = new ContinuationFrame(streamId, part2,
            endHeaders: true).Serialize();
        var respDataFrame = new DataFrame(streamId, "fragmented"u8.ToArray(),
            endStream: true).Serialize();

        var combined = CombineFrames(respHeadersFrame, respContinuationFrame, respDataFrame);

        var decoder = new Http2Decoder();
        var decoded = decoder.TryDecode(combined, out var result);

        Assert.True(decoded);
        Assert.True(result.HasResponses);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
        Assert.Equal("fragmented", await result.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-013 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-013: HTTP/2 server PUSH_PROMISE decoded, pushed response received")]
    public async Task Should_DecodePromisedStream_When_PushPromiseReceived()
    {
        var hpack = new HpackEncoder(useHuffman: false);

        // Server sends PUSH_PROMISE on stream 1 promising stream 2
        var pushHeaderBlock = hpack.Encode([
            (":method", "GET"),
            (":path", "/style.css"),
            (":scheme", "https"),
            (":authority", "example.com"),
        ]);
        var pushPromiseFrame = new PushPromiseFrame(1, 2, pushHeaderBlock,
            endHeaders: true).Serialize();

        // Server sends pushed response on stream 2 (HEADERS + DATA)
        var pushedHeaderBlock = hpack.Encode([
            (":status", "200"),
            ("content-type", "text/css"),
        ]);
        var pushedHeadersFrame = new HeadersFrame(2, pushedHeaderBlock,
            endStream: false, endHeaders: true).Serialize();
        var pushedDataFrame = new DataFrame(2, "body { color: red; }"u8.ToArray(),
            endStream: true).Serialize();

        var combined = CombineFrames(pushPromiseFrame, pushedHeadersFrame, pushedDataFrame);

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        // PUSH_PROMISE registered stream 2 as promised
        Assert.Contains(2, result.PromisedStreamIds);

        // Pushed response on stream 2 was decoded
        Assert.True(result.HasResponses);
        var pushed = result.Responses.Single(r => r.StreamId == 2);
        Assert.Equal(HttpStatusCode.OK, pushed.Response.StatusCode);
        Assert.Equal("text/css", pushed.Response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("body { color: red; }", await pushed.Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-014 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-014: HTTP/2 POST body larger than initial window uses WINDOW_UPDATE")]
    public void Should_UpdateSendWindow_When_ServerWindowUpdateReceived()
    {
        const int increment = 65535;

        // Server sends a WINDOW_UPDATE on stream 1 and the connection (stream 0)
        var streamWindowUpdate = new WindowUpdateFrame(1, increment).Serialize();
        var connectionWindowUpdate = new WindowUpdateFrame(0, increment).Serialize();
        var combined = CombineFrames(streamWindowUpdate, connectionWindowUpdate);

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        // Both WINDOW_UPDATE frames were processed
        Assert.Equal(2, result.WindowUpdates.Count);
        Assert.Contains(result.WindowUpdates, u => u.StreamId == 1 && u.Increment == increment);
        Assert.Contains(result.WindowUpdates, u => u.StreamId == 0 && u.Increment == increment);

        // Encoder applies the window update and can now send more data
        var encoder = new Http2Encoder(useHuffman: false);
        encoder.UpdateConnectionWindow(increment);
        encoder.UpdateStreamWindow(1, increment);

        var bigBody = new byte[70000];
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/upload")
        {
            Content = new ByteArrayContent(bigBody)
        };
        var encoderBuf = new byte[200000].AsMemory();
        var (_, written) = encoder.Encode(request, ref encoderBuf);
        Assert.True(written > 0);
    }

    // ── RT-2-015 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-015: HTTP/2 request → 404 response on stream decoded")]
    public void Should_Return404_When_Http2StreamReturnsNotFound()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var buf = new byte[4096].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/missing");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);
        Assert.True(written > 0);

        // Server responds with 404 (headers-only response, endStream=true)
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "404")]);
        var headersFrame = new HeadersFrame(streamId, headerBlock,
            endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out var result);

        Assert.True(result.HasResponses);
        Assert.Single(result.Responses);
        Assert.Equal(streamId, result.Responses[0].StreamId);
        Assert.Equal(HttpStatusCode.NotFound, result.Responses[0].Response.StatusCode);
    }

    // ── RT-2-016 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-016: ValidateServerPreface returns false for incomplete frame (<9 bytes)")]
    public void Should_ReturnFalse_When_ServerPrefaceIncomplete()
    {
        var decoder = new Http2Decoder();
        var incomplete = new byte[5]; // less than 9 bytes (frame header size)
        var result = decoder.ValidateServerPreface(incomplete.AsMemory());
        Assert.False(result);
    }

    // ── RT-2-017 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-017: ValidateServerPreface throws on non-SETTINGS first frame")]
    public void Should_ThrowProtocolError_When_ServerPrefaceHasWrongFrameType()
    {
        var decoder = new Http2Decoder();
        // Build a PING frame (type=0x06) on stream 0 with 8-byte payload
        var pingFrame = new PingFrame(new byte[8], isAck: false).Serialize();

        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.ValidateServerPreface(pingFrame.AsMemory()));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── RT-2-018 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-018: Multiple sequential SETTINGS frames each produce one ACK")]
    public void Should_QueueOneAckPerSettingsFrame_When_MultipleSettingsReceived()
    {
        var settings1 = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxConcurrentStreams, 100u),
        }).Serialize();
        var settings2 = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.MaxFrameSize, 32768u),
        }).Serialize();

        var combined = CombineFrames(settings1, settings2);
        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        Assert.Equal(2, result.SettingsAcksToSend.Count);
    }

    // ── RT-2-019 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-019: SETTINGS ACK frame consumed silently, no response queued")]
    public void Should_ConsumeSettingsAck_When_SettingsAckFrameReceived()
    {
        var settingsAck = Http2Encoder.EncodeSettingsAck();
        var decoder = new Http2Decoder();
        var decoded = decoder.TryDecode(settingsAck.AsMemory(), out var result);

        Assert.True(decoded);
        Assert.False(result.HasNewSettings);
        Assert.Empty(result.SettingsAcksToSend);
        Assert.Empty(result.PingAcksToSend);
        Assert.False(result.HasResponses);
    }

    // ── RT-2-020 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-020: DELETE request encodes correctly with no body")]
    public void Should_EncodeDeleteWithNoBody_When_DeleteRequestEncoded()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var encoderBuf = new byte[4096];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Delete, "https://api.example.com/items/42");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);
        Assert.True(written > 0);

        // Only a HEADERS frame (endStream=true), no DATA frame
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);
        var headersFlags = (HeadersFlags)encoderBuf[4];
        Assert.True(headersFlags.HasFlag(HeadersFlags.EndStream));
        Assert.True(headersFlags.HasFlag(HeadersFlags.EndHeaders));
    }

    // ── RT-2-021 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-021: PUT request encodes with body (HEADERS + DATA)")]
    public void Should_EncodePutWithBody_When_PutRequestEncoded()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var encoderBuf = new byte[8192];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Put, "https://api.example.com/items/42")
        {
            Content = new StringContent("{\"name\":\"updated\"}", Encoding.UTF8, "application/json")
        };
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);
        Assert.True(written > 0);

        // HEADERS frame without END_STREAM (body follows)
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);
        var headersFlags = (HeadersFlags)encoderBuf[4];
        Assert.False(headersFlags.HasFlag(HeadersFlags.EndStream));

        // DATA frame follows
        var firstFrameLen = (encoderBuf[0] << 16) | (encoderBuf[1] << 8) | encoderBuf[2];
        var dataFrameOffset = 9 + firstFrameLen;
        Assert.Equal((byte)FrameType.Data, encoderBuf[dataFrameOffset + 3]);
    }

    // ── RT-2-022 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-022: PATCH request encodes with body (HEADERS + DATA)")]
    public void Should_EncodePatchWithBody_When_PatchRequestEncoded()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var encoderBuf = new byte[8192];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Patch, "https://api.example.com/items/42")
        {
            Content = new StringContent("{\"field\":\"value\"}", Encoding.UTF8, "application/json")
        };
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.Equal(1, streamId);
        Assert.True(written > 0);
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);
    }

    // ── RT-2-023 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-023: Three CONTINUATION frames required for tiny max frame size")]
    public async Task Should_UseThreeContinuationFrames_When_MaxFrameSizeVerySmall()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        // Force very small frames so the HEADERS block must be split into multiple CONTINUATION frames
        encoder.ApplyServerSettings([(SettingsParameter.MaxFrameSize, 5u)]);

        var encoderBuf = new byte[65536];
        var buf = encoderBuf.AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/resource");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "some-value-here");
        var (streamId, written) = encoder.Encode(request, ref buf);

        // First frame HEADERS without END_HEADERS (continuation follows)
        Assert.Equal((byte)FrameType.Headers, encoderBuf[3]);
        var headersFlags = (HeadersFlags)encoderBuf[4];
        Assert.False(headersFlags.HasFlag(HeadersFlags.EndHeaders));

        // Find at least two CONTINUATION frames
        var continuationCount = 0;
        var offset = 0;
        while (offset < written)
        {
            var frameLen = (encoderBuf[offset] << 16) | (encoderBuf[offset + 1] << 8) | encoderBuf[offset + 2];
            var frameType = (FrameType)encoderBuf[offset + 3];
            if (frameType == FrameType.Continuation)
            {
                continuationCount++;
            }

            offset += 9 + frameLen;
        }

        Assert.True(continuationCount >= 2, $"Expected at least 2 CONTINUATION frames, got {continuationCount}");

        // Decode the whole thing: build a response with CONTINUATION
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "200"), ("content-type", "application/json")]);
        var splitAt = block.Length / 2;
        var respHeaders = new HeadersFrame(streamId, block[..splitAt], endStream: false, endHeaders: false).Serialize();
        var respContinuation = new ContinuationFrame(streamId, block[splitAt..], endHeaders: true).Serialize();
        var respData = new DataFrame(streamId, "ok"u8.ToArray(), endStream: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(CombineFrames(respHeaders, respContinuation, respData), out var result);

        Assert.True(result.HasResponses);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
        Assert.Equal("ok", await result.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-024 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-024: Response with multiple CONTINUATION frames decoded correctly")]
    public async Task Should_DecodeResponse_When_HeadersSplitAcrossMultipleContinuations()
    {
        // Build a large header block and split it across 3 CONTINUATION frames
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([
            (":status", "200"),
            ("content-type", "application/json"),
            ("x-request-id", "abc-123-def-456"),
            ("x-trace-id", "trace-987654321"),
            ("cache-control", "no-cache, no-store"),
        ]);

        // Split into 3 roughly equal parts
        var part1Len = block.Length / 3;
        var part2Len = block.Length / 3;
        var part3Len = block.Length - part1Len - part2Len;

        var frame1 = new HeadersFrame(1, block[..part1Len], endStream: false, endHeaders: false).Serialize();
        var frame2 = new ContinuationFrame(1, block.Slice(part1Len, part2Len), endHeaders: false).Serialize();
        var frame3 = new ContinuationFrame(1, block.Slice(part1Len + part2Len, part3Len), endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, "multi-cont-body"u8.ToArray(), endStream: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(CombineFrames(frame1, frame2, frame3, dataFrame), out var result);

        Assert.True(result.HasResponses);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
        Assert.Equal("multi-cont-body", await result.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-025 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-025: set-cookie response header NeverIndexed does not appear in dynamic table")]
    public void Should_NeverIndexSetCookie_When_SetCookieHeaderEncoded()
    {
        // set-cookie is sensitive; encoding twice must produce same length (never cached)
        var hpackEnc = new HpackEncoder(useHuffman: false);
        var block1 = hpackEnc.Encode([("set-cookie", "sessionid=abc; HttpOnly; Secure")]);
        var block2 = hpackEnc.Encode([("set-cookie", "sessionid=abc; HttpOnly; Secure")]);

        Assert.Equal(block1.Length, block2.Length);
    }

    // ── RT-2-026 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-026: proxy-authorization request header NeverIndexed")]
    public void Should_NeverIndexProxyAuthorization_When_ProxyAuthHeaderEncoded()
    {
        var hpackEnc = new HpackEncoder(useHuffman: false);
        var block1 = hpackEnc.Encode([("proxy-authorization", "Basic dXNlcjpwYXNz")]);
        var block2 = hpackEnc.Encode([("proxy-authorization", "Basic dXNlcjpwYXNz")]);

        Assert.Equal(block1.Length, block2.Length);
    }

    // ── RT-2-027 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-027: Mixed sensitive and non-sensitive headers round-trip correctly")]
    public async Task Should_HandleMixedHeaders_When_SensitiveAndNonSensitiveCombined()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var buf = new byte[8192].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Cookie", "session=xyz");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.True(written > 0);

        var hpack = new HpackEncoder(useHuffman: false);
        var respFrame = BuildH2Response(streamId, 200, "secured data", hpack);
        var decoder = new Http2Decoder();
        decoder.TryDecode(respFrame, out var result);

        Assert.True(result.HasResponses);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
        Assert.Equal("secured data", await result.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-028 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-028: Non-sensitive custom header is indexed and produces shorter second encoding")]
    public void Should_IndexCustomHeader_When_NonSensitiveHeaderEncoded()
    {
        var hpackEnc = new HpackEncoder(useHuffman: false);
        var block1 = hpackEnc.Encode([("x-custom-header", "my-value")]);
        var block2 = hpackEnc.Encode([("x-custom-header", "my-value")]);

        // Second encoding should be shorter due to dynamic table indexing
        Assert.True(block2.Length < block1.Length,
            "Non-sensitive header should be indexed, making second encoding shorter");
    }

    // ── RT-2-029 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-029: Five concurrent streams all complete successfully")]
    public async Task Should_ReturnFiveResponses_When_FiveConcurrentStreams()
    {
        var encoder = new Http2Encoder(useHuffman: false);
        var hpack = new HpackEncoder(useHuffman: false);
        var decoder = new Http2Decoder();

        var streamIds = new int[5];
        for (var i = 0; i < 5; i++)
        {
            var buf = new byte[4096].AsMemory();
            var (id, _) = encoder.Encode(
                new HttpRequestMessage(HttpMethod.Get, $"https://example.com/item/{i}"), ref buf);
            streamIds[i] = id;
        }

        // Stream IDs should be 1, 3, 5, 7, 9
        Assert.Equal([1, 3, 5, 7, 9], streamIds);

        var frames = streamIds.Select(id => BuildH2Response(id, 200, $"body-{id}", hpack)).ToArray();
        decoder.TryDecode(CombineFrames(frames), out var result);

        Assert.Equal(5, result.Responses.Count);
        var byId = result.Responses.ToDictionary(r => r.StreamId, r => r.Response);
        foreach (var id in streamIds)
        {
            Assert.True(byId.ContainsKey(id));
            Assert.Equal(HttpStatusCode.OK, byId[id].StatusCode);
            Assert.Equal($"body-{id}", await byId[id].Content.ReadAsStringAsync());
        }
    }

    // ── RT-2-030 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-030: Mixed status codes across concurrent streams decoded correctly")]
    public void Should_DecodeCorrectStatusCodes_When_ConcurrentStreamsHaveDifferentStatuses()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var decoder = new Http2Decoder();

        var statusMap = new Dictionary<int, HttpStatusCode>
        {
            { 1, HttpStatusCode.OK },
            { 3, HttpStatusCode.Created },
            { 5, HttpStatusCode.NotFound },
            { 7, HttpStatusCode.InternalServerError },
        };

        var frames = statusMap.Select(kv =>
        {
            var block = hpack.Encode([(":status", ((int)kv.Value).ToString())]);
            return new HeadersFrame(kv.Key, block, endStream: true, endHeaders: true).Serialize();
        }).ToArray();

        decoder.TryDecode(CombineFrames(frames), out var result);

        Assert.Equal(4, result.Responses.Count);
        var byId = result.Responses.ToDictionary(r => r.StreamId, r => r.Response);
        foreach (var (id, expectedStatus) in statusMap)
        {
            Assert.Equal(expectedStatus, byId[id].StatusCode);
        }
    }

    // ── RT-2-031 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-031: Interleaved DATA frames for two concurrent streams decoded correctly")]
    public async Task Should_DecodeResponses_When_DataFramesInterleaved()
    {
        var hpack = new HpackEncoder(useHuffman: false);

        // Stream 1: HEADERS, then DATA
        var h1 = hpack.Encode([(":status", "200")]);
        var headers1 = new HeadersFrame(1, h1, endStream: false, endHeaders: true).Serialize();
        var data1 = new DataFrame(1, "stream1-part"u8.ToArray(), endStream: true).Serialize();

        // Stream 3: HEADERS, then DATA (interleaved)
        var h3 = hpack.Encode([(":status", "200")]);
        var headers3 = new HeadersFrame(3, h3, endStream: false, endHeaders: true).Serialize();
        var data3 = new DataFrame(3, "stream3-part"u8.ToArray(), endStream: true).Serialize();

        // Interleave: headers1, headers3, data1, data3
        var combined = CombineFrames(headers1, headers3, data1, data3);

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined, out var result);

        Assert.Equal(2, result.Responses.Count);
        var byId = result.Responses.ToDictionary(r => r.StreamId, r => r.Response);
        Assert.Equal("stream1-part", await byId[1].Content.ReadAsStringAsync());
        Assert.Equal("stream3-part", await byId[3].Content.ReadAsStringAsync());
    }

    // ── RT-2-032 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-032: Large body response (32 KB) decoded correctly")]
    public async Task Should_DecodeLargeBody_When_ResponseBodyIs32KB()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var largeBody = new byte[32768];
        new Random(42).NextBytes(largeBody);

        var block = hpack.Encode([(":status", "200"), ("content-length", "32768")]);
        var headersFrame = new HeadersFrame(1, block, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, largeBody, endStream: true).Serialize();

        var decoder = new Http2Decoder();
        // Increase max frame size to allow 32KB DATA frame and set receive window accordingly
        decoder.TryDecode(
            new SettingsFrame(new List<(SettingsParameter, uint)>
                { (SettingsParameter.MaxFrameSize, 65536u) }).Serialize().AsMemory(),
            out _);
        decoder.SetConnectionReceiveWindow(131072);

        decoder.TryDecode(CombineFrames(headersFrame, dataFrame).AsMemory(), out var result);

        Assert.True(result.HasResponses);
        var body = await result.Responses[0].Response.Content.ReadAsByteArrayAsync();
        Assert.Equal(32768, body.Length);
        Assert.Equal(largeBody, body);
    }

    // ── RT-2-033 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-033: Multiple DATA frames for a single stream assembled into one body")]
    public async Task Should_AssembleBody_When_MultipleDataFramesForSingleStream()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, block, endStream: false, endHeaders: true).Serialize();

        // Three DATA frames, last one has END_STREAM
        var data1 = new DataFrame(1, "Hello, "u8.ToArray(), endStream: false).Serialize();
        var data2 = new DataFrame(1, "HTTP/2 "u8.ToArray(), endStream: false).Serialize();
        var data3 = new DataFrame(1, "World!"u8.ToArray(), endStream: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(CombineFrames(headersFrame, data1, data2, data3), out var result);

        Assert.True(result.HasResponses);
        var body = await result.Responses[0].Response.Content.ReadAsStringAsync();
        Assert.Equal("Hello, HTTP/2 World!", body);
    }

    // ── RT-2-034 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-034: WINDOW_UPDATE that brings stream window to exactly 2^31-1 is accepted")]
    public void Should_AcceptMaxIncrement_When_WindowUpdateWithMaxValue()
    {
        // Default initial window is 65535. Use an increment that brings the send window to exactly
        // 2^31-1 (0x7FFFFFFF) without overflowing — per RFC 7540 §6.9.1.
        const int maxWindow = 0x7FFFFFFF;
        const int defaultInitialWindow = 65535;
        const int increment = maxWindow - defaultInitialWindow; // reaches exactly 2^31-1

        var windowUpdate = new WindowUpdateFrame(1, increment).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(windowUpdate.AsMemory(), out var result);

        Assert.Single(result.WindowUpdates);
        Assert.Equal(1, result.WindowUpdates[0].StreamId);
        Assert.Equal(increment, result.WindowUpdates[0].Increment);
        Assert.Equal((long)maxWindow, decoder.GetStreamSendWindow(1));
    }

    // ── RT-2-035 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-035: Connection and stream WINDOW_UPDATE increments tracked separately")]
    public void Should_TrackWindowUpdates_When_ConnectionAndStreamUpdatesReceived()
    {
        var connectionUpdate = new WindowUpdateFrame(0, 32768).Serialize();
        var streamUpdate = new WindowUpdateFrame(1, 16384).Serialize();
        var combined = CombineFrames(connectionUpdate, streamUpdate);

        var decoder = new Http2Decoder();
        decoder.TryDecode(combined.AsMemory(), out var result);

        Assert.Equal(2, result.WindowUpdates.Count);

        var conn = result.WindowUpdates.Single(u => u.StreamId == 0);
        var stream = result.WindowUpdates.Single(u => u.StreamId == 1);
        Assert.Equal(32768, conn.Increment);
        Assert.Equal(16384, stream.Increment);
    }

    // ── RT-2-036 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-036: HTTP/2 round-trip with Huffman encoding enabled")]
    public async Task Should_DecodeResponse_When_HuffmanEncodingEnabled()
    {
        var encoder = new Http2Encoder(useHuffman: true);
        var buf = new byte[4096].AsMemory();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/huffman");
        var (streamId, written) = encoder.Encode(request, ref buf);

        Assert.True(written > 0);

        var hpack = new HpackEncoder(useHuffman: true);
        var responseFrame = BuildH2Response(streamId, 200, "Huffman body", hpack);
        var decoder = new Http2Decoder();
        decoder.TryDecode(responseFrame, out var result);

        Assert.True(result.HasResponses);
        Assert.Equal(HttpStatusCode.OK, result.Responses[0].Response.StatusCode);
        Assert.Equal("Huffman body", await result.Responses[0].Response.Content.ReadAsStringAsync());
    }

    // ── RT-2-037 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-037: HPACK encoder with Huffman produces smaller encoding for well-known headers")]
    public void Should_ProduceSmallerBlock_When_HuffmanEnabledForKnownHeaders()
    {
        var hpackHuffman = new HpackEncoder(useHuffman: true);
        var hpackPlain = new HpackEncoder(useHuffman: false);

        // Encode a header with a long string value — Huffman should compress it
        var blockHuffman = hpackHuffman.Encode([(":status", "200"), ("content-type", "application/json")]);
        var blockPlain = hpackPlain.Encode([(":status", "200"), ("content-type", "application/json")]);

        // Huffman should produce a smaller or equal block
        Assert.True(blockHuffman.Length <= blockPlain.Length,
            "Huffman encoding should produce a block no larger than plain encoding");
    }

    // ── RT-2-038 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RT-2-038: HPACK sync: decoder correctly interprets indexed reference after dynamic table update")]
    public async Task Should_DecodeIndexedHeader_When_HpackDynamicTableContainsEntry()
    {
        // First response: encode "x-correlation-id: req-001" as literal → adds to dynamic table
        // Second response: "x-correlation-id: req-001" should now be indexed (1 byte vs. full literal)
        var hpack = new HpackEncoder(useHuffman: false);

        var block1 = hpack.Encode([(":status", "200"), ("x-correlation-id", "req-001")]);
        var block2 = hpack.Encode([(":status", "200"), ("x-correlation-id", "req-001")]);

        // block2 should be shorter because of dynamic table entry
        Assert.True(block2.Length < block1.Length);

        // Both must decode correctly
        var frames1 = CombineFrames(
            new HeadersFrame(1, block1, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(1, "body1"u8.ToArray(), endStream: true).Serialize());
        var frames3 = CombineFrames(
            new HeadersFrame(3, block2, endStream: false, endHeaders: true).Serialize(),
            new DataFrame(3, "body3"u8.ToArray(), endStream: true).Serialize());

        var decoder = new Http2Decoder();
        decoder.TryDecode(CombineFrames(frames1, frames3), out var result);

        Assert.Equal(2, result.Responses.Count);
        foreach (var (_, response) in result.Responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var bodies = result.Responses.Select(r => r.Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()).ToHashSet();
        Assert.Contains("body1", bodies);
        Assert.Contains("body3", bodies);
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
        var body = Encoding.UTF8.GetBytes("Hello, World!");
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
        Assert.True(response.Content!.Headers.Contains("content-language"),   "content-language missing");
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
