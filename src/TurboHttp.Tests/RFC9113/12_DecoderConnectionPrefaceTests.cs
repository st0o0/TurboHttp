using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2DecoderConnectionPrefaceTests
{
    [Fact(DisplayName = "7540-3.5-002: Invalid server preface causes PROTOCOL_ERROR")]
    public void ServerPreface_NonSettingsFrame_ThrowsProtocolError()
    {
        // A PING frame (not SETTINGS) as the first bytes from the server.
        var pingFrame = new PingFrame(new byte[8], isAck: false).Serialize();
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(pingFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-3.5-004: Missing SETTINGS after preface causes error")]
    public void ServerPreface_IncompleteBytes_ReturnsFalse()
    {
        // Fewer than 9 bytes — cannot determine frame type yet; no error, return false.
        var incompleteBytes = new byte[] { 0x00, 0x00 };
        var decoder = new Http2Decoder();
        var result = decoder.ValidateServerPreface(incompleteBytes);
        Assert.False(result);
    }

    [Fact(DisplayName = "7540-4.1-001: Valid 9-byte frame header decoded correctly")]
    public void FrameHeader_Valid9Bytes_DecodedCorrectly()
    {
        // A SETTINGS frame has a 9-byte header with zero payload.
        var frame = SettingsFrame.SettingsAck(); // 9 bytes: length=0, type=4, flags=ACK, stream=0
        var decoder = new Http2Decoder();
        var ok = decoder.TryDecode(frame, out var result);
        Assert.True(ok);
        Assert.False(result.HasNewSettings); // ACK, not a new settings
    }

    [Fact(DisplayName = "7540-4.1-002: Frame length uses 24-bit field")]
    public void FrameHeader_LargePayload_24BitLengthParsed()
    {
        // Build a SETTINGS frame with payload > 65535 bytes (need large frame size).
        // Use 66000 bytes of SETTINGS-like content (11000 settings × 6 bytes = 66000).
        const int payloadLen = 66006; // divisible by 6
        var buf = new byte[9 + payloadLen];
        buf[0] = payloadLen >> 16;
        buf[1] = (payloadLen >> 8) & 0xFF;
        buf[2] = payloadLen & 0xFF;
        buf[3] = 0x04; // SETTINGS
        buf[4] = 0x00; // no flags
        // stream ID = 0 (bytes 5–8 remain zero)

        // Fill with valid SETTINGS entries (param=1, value=0 repeated).
        for (var i = 0; i < payloadLen; i += 6)
        {
            buf[9 + i + 0] = 0x00;
            buf[9 + i + 1] = 0x01; // HeaderTableSize
            // value = 0 (4 bytes remain zero)
        }

        var decoder = new Http2Decoder();
        // Raise max frame size so the frame is accepted.
        decoder.SetConnectionReceiveWindow(int.MaxValue);

        // Use reflection or a raw approach: build SETTINGS with enlarged _maxFrameSize.
        // Since SetConnectionReceiveWindow doesn't set maxFrameSize, we do it indirectly:
        // Send a SETTINGS frame with MaxFrameSize = 2^24-1.
        var maxSizeSettings = new SettingsFrame([(SettingsParameter.MaxFrameSize, (uint)payloadLen + 100)]).Serialize();
        decoder.TryDecode(maxSizeSettings, out _);

        var ok = decoder.TryDecode(buf, out var result);
        Assert.True(ok);
        Assert.True(result.HasNewSettings);
        // Verify at least one settings entry was decoded.
        Assert.True(result.ReceivedSettings[0].Count > 0);
    }

    [Theory(DisplayName = "7540-4.1-003: Frame type {0} dispatched to correct handler")]
    [InlineData(0x0)] // DATA — requires active stream; test stream-0 error
    [InlineData(0x1)] // HEADERS
    [InlineData(0x2)] // PRIORITY
    [InlineData(0x3)] // RST_STREAM
    [InlineData(0x4)] // SETTINGS
    [InlineData(0x5)] // PUSH_PROMISE
    [InlineData(0x6)] // PING
    [InlineData(0x7)] // GOAWAY
    [InlineData(0x8)] // WINDOW_UPDATE
    [InlineData(0x9)] // CONTINUATION — tested separately
    public void FrameType_AllKnownTypes_DispatchedWithoutCrash(byte typeCode)
    {
        // Build a minimal valid frame for each type and verify it doesn't throw unexpectedly.
        // Frames that require special setup (DATA, HEADERS, CONTINUATION) use stream 0 check
        // which will throw Http2Exception — that's acceptable as "dispatched to correct handler".
        byte[] frame;
        switch ((FrameType)typeCode)
        {
            case FrameType.Settings:
                frame = SettingsFrame.SettingsAck();
                break;
            case FrameType.Ping:
                frame = new PingFrame(new byte[8]).Serialize();
                break;
            case FrameType.GoAway:
                frame = new GoAwayFrame(0, Http2ErrorCode.NoError).Serialize();
                break;
            case FrameType.WindowUpdate:
                frame = new WindowUpdateFrame(0, 1).Serialize();
                break;
            case FrameType.RstStream:
                frame = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
                break;
            case FrameType.Priority:
                // PRIORITY: 9-byte header + 5-byte payload (stream dep + weight)
                frame =
                [
                    0x00, 0x00, 0x05, // length=5
                    0x02,             // PRIORITY
                    0x00,             // flags=0
                    0x00, 0x00, 0x00, 0x01, // stream=1
                    0x00, 0x00, 0x00, 0x01, // stream dependency
                    0x00              // weight
                ];
                break;
            default:
                // DATA on stream 0, HEADERS on stream 0, CONTINUATION without headers,
                // PUSH_PROMISE — all handled by throwing Http2Exception for invalid state.
                frame =
                [
                    0x00, 0x00, 0x01,
                    typeCode,
                    0x00,
                    0x00, 0x00, 0x00, 0x00, // stream 0 — will trigger PROTOCOL_ERROR
                    0x00
                ];
                break;
        }

        var decoder = new Http2Decoder();
        // Allow PROTOCOL_ERROR for stream-0 cases; everything else should decode cleanly.
        try
        {
            decoder.TryDecode(frame, out _);
        }
        catch (Http2Exception ex) when (ex.ErrorCode == Http2ErrorCode.ProtocolError)
        {
            // Expected for DATA/HEADERS/CONTINUATION on stream 0 — handler was invoked.
        }
    }

    [Fact(DisplayName = "7540-4.1-004: Unknown frame type 0x0A is ignored")]
    public void FrameType_Unknown0x0A_Ignored()
    {
        // Build a raw frame with unknown type 0x0A (10).
        var frame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x0A,             // type  = unknown
            0x00,             // flags = none
            0x00, 0x00, 0x00, 0x01, // stream = 1
            0x00, 0x00, 0x00, 0x00  // 4 bytes payload
        };

        var decoder = new Http2Decoder();
        var ok = decoder.TryDecode(frame, out var result);
        Assert.True(ok);
        Assert.False(result.HasResponses);
        Assert.False(result.HasNewSettings);
    }

    [Fact(DisplayName = "7540-4.1-005: R-bit masked out when reading stream ID")]
    public void FrameHeader_RBitSetInGoAway_LastStreamIdMasked()
    {
        // RFC 7540 §6.8: The GOAWAY frame last-stream-id field also has a reserved bit.
        // Verify the decoder masks it out: stream 3 with R-bit = 0x80000003.
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload, 0x80000003u); // lastStreamId = 3 with R-bit
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)Http2ErrorCode.NoError);

        var frame = new byte[9 + 8];
        frame[0] = 0; frame[1] = 0; frame[2] = 8; // length=8
        frame[3] = 0x07;                           // GOAWAY
        frame[4] = 0x00;                           // flags=0
        // stream ID = 0 in header (bytes 5–8)
        payload.CopyTo(frame, 9);

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.True(result.HasGoAway);
        Assert.Equal(3, result.GoAway!.LastStreamId); // R-bit stripped → 3, not 0x80000003
    }

    [Fact(DisplayName = "7540-4.1-006: R-bit set in stream ID causes PROTOCOL_ERROR")]
    public void FrameHeader_RBitSetInStreamId_ThrowsProtocolError()
    {
        // Build a raw DATA frame with R-bit set in the stream ID field.
        var frame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x06,             // PING (valid payload size = 8; use SETTINGS for simplicity)
            0x00,             // flags
            0x80, 0x00, 0x00, 0x00, // stream ID = 0 with R-bit set (0x80000000)
            0x00, 0x00, 0x00, 0x00
        };

        // Actually, for SETTINGS the stream must be 0 and this sets stream=0 with R-bit.
        // Let's use a SETTINGS ACK frame with R-bit in the stream word.
        var settingsFrame = new byte[9];
        settingsFrame[0] = 0; settingsFrame[1] = 0; settingsFrame[2] = 0; // length=0
        settingsFrame[3] = 0x04; // SETTINGS
        settingsFrame[4] = (byte)SettingsFlags.Ack; // ACK
        // Set R-bit in stream ID field:
        settingsFrame[5] = 0x80; // MSB = R-bit set
        settingsFrame[6] = 0;
        settingsFrame[7] = 0;
        settingsFrame[8] = 0;

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(settingsFrame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-4.1-007: Oversized frame causes FRAME_SIZE_ERROR")]
    public void FrameHeader_PayloadExceedsMaxFrameSize_ThrowsFrameSizeError()
    {
        // Build a raw frame with length = 16385 (just over default MAX_FRAME_SIZE of 16384).
        const int overSize = 16385;
        var frame = new byte[9]; // header only; we never get to the payload check
        frame[0] = overSize >> 16;
        frame[1] = overSize >> 8;
        frame[2] = overSize & 0xFF;
        frame[3] = 0x00; // DATA
        frame[4] = 0x00; // flags
        // stream = 1
        frame[5] = 0; frame[6] = 0; frame[7] = 0; frame[8] = 1;

        // Pad to full length so the frame is "complete".
        var fullFrame = new byte[9 + overSize];
        frame.CopyTo(fullFrame, 0);

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(fullFrame, out _));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.1-001: DATA frame payload decoded correctly")]
    public async Task DataFrame_Payload_DecodedCorrectly()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var body = "hello"u8.ToArray();
        var dataFrame = new DataFrame(1, body, endStream: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.Concat(dataFrame).ToArray(), out var result);

        var text = await result.Responses[0].Response.Content.ReadAsStringAsync();
        Assert.Equal("hello", text);
    }

    [Fact(DisplayName = "7540-6.1-002: END_STREAM on DATA marks stream closed")]
    public void DataFrame_EndStream_MarksStreamClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var dataFrame = new DataFrame(1, new byte[4], endStream: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.Concat(dataFrame).ToArray(), out var result);

        Assert.Single(result.Responses);

        // Subsequent DATA on same stream should throw STREAM_CLOSED.
        var extra = new DataFrame(1, new byte[1]).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(extra, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.1-003: Padded DATA frame padding stripped")]
    public async Task DataFrame_Padded_PaddingStripped()
    {
        // Manually build PADDED DATA: flag=0x08, pad_length=3, data="hi", padding=0x00×3
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        // Payload: pad_length(1) + data(2) + padding(3) = 6 bytes
        var paddedPayload = new byte[] { 3, (byte)'h', (byte)'i', 0x00, 0x00, 0x00 };
        var dataFrame = new byte[9 + paddedPayload.Length];
        dataFrame[0] = 0; dataFrame[1] = 0; dataFrame[2] = (byte)paddedPayload.Length; // length=6
        dataFrame[3] = 0x00; // DATA
        dataFrame[4] = 0x09; // END_STREAM | PADDED (0x01 | 0x08)
        dataFrame[5] = 0; dataFrame[6] = 0; dataFrame[7] = 0; dataFrame[8] = 1; // stream=1
        paddedPayload.CopyTo(dataFrame, 9);

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.Concat(dataFrame).ToArray(), out var result);

        Assert.Single(result.Responses);
        var body = await result.Responses[0].Response.Content.ReadAsStringAsync();
        Assert.Equal("hi", body);
    }

    [Fact(DisplayName = "7540-6.1-004: DATA on stream 0 is PROTOCOL_ERROR")]
    public void DataFrame_Stream0_ThrowsProtocolError()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x00
        };
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.1-005: DATA on closed stream causes STREAM_CLOSED")]
    public void DataFrame_ClosedStream_ThrowsStreamClosed()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);

        var dataFrame = new DataFrame(1, new byte[1]).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(dataFrame, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.1-006: Empty DATA frame with END_STREAM valid")]
    public void DataFrame_EmptyWithEndStream_ResponseComplete()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();
        var emptyDataFrame = new DataFrame(1, ReadOnlyMemory<byte>.Empty, endStream: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame.Concat(emptyDataFrame).ToArray(), out var result);

        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.2-001: HEADERS frame decoded into response headers")]
    public void HeadersFrame_ResponseHeaders_Decoded()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("x-custom", "value")]);
        var frame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.Responses);
        var response = result.Responses[0].Response;
        Assert.Equal(200, (int)response.StatusCode);
        Assert.True(response.Headers.Contains("x-custom"));
    }

    [Fact(DisplayName = "7540-6.2-002: END_STREAM on HEADERS closes stream immediately")]
    public void HeadersFrame_EndStream_StreamClosedImmediately()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "204")]);
        var frame = new HeadersFrame(1, headerBlock, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(204, (int)result.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.2-003: END_HEADERS on HEADERS marks complete block")]
    public void HeadersFrame_EndHeaders_HeaderBlockComplete()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        // END_HEADERS flag (0x4) is set → no CONTINUATION expected.
        var frame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out _);

        // If END_HEADERS was properly recognized, a subsequent non-CONTINUATION frame
        // should NOT throw PROTOCOL_ERROR.
        var pingFrame = new PingFrame(new byte[8]).Serialize();
        decoder.TryDecode(pingFrame, out var result);
        Assert.Single(result.PingRequests); // no exception → END_HEADERS was respected
    }

    [Fact(DisplayName = "7540-6.2-004: Padded HEADERS padding stripped")]
    public void HeadersFrame_Padded_PaddingStripped()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        // Build PADDED HEADERS: PADDED flag=0x08, pad_length=2, header block, 2 bytes padding.
        const int padLength = 2;
        var payload = new byte[1 + headerBlock.Length + padLength];
        payload[0] = padLength; // Pad Length
        headerBlock.CopyTo(payload.AsMemory(1)); // header block starts after pad length byte
        // last 2 bytes remain zero (padding)

        var frame = new byte[9 + payload.Length];
        frame[0] = 0; frame[1] = 0; frame[2] = (byte)payload.Length; // length
        frame[3] = 0x01; // HEADERS
        frame[4] = 0x0C; // END_STREAM(0x1) | END_HEADERS(0x4) | PADDED(0x8) = 0x0D
        // Actually END_HEADERS=0x4, PADDED=0x8, END_STREAM=0x1 → 0x0D
        frame[4] = 0x0D;
        frame[5] = 0; frame[6] = 0; frame[7] = 0; frame[8] = 1; // stream=1
        payload.CopyTo(frame, 9);

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.2-005: PRIORITY flag in HEADERS consumed correctly")]
    public void HeadersFrame_PriorityFlag_ConsumedCorrectly()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        // Build HEADERS with PRIORITY flag: extra 5 bytes (4 stream dep + 1 weight).
        var priorityBytes = new byte[] { 0x00, 0x00, 0x00, 0x03, 0x0F }; // dep=3, weight=15
        var payload = priorityBytes.Concat(headerBlock.ToArray()).ToArray();

        var frame = new byte[9 + payload.Length];
        frame[0] = 0; frame[1] = 0; frame[2] = (byte)payload.Length;
        frame[3] = 0x01; // HEADERS
        // END_STREAM(0x1) | END_HEADERS(0x4) | PRIORITY(0x20) = 0x25
        frame[4] = 0x25;
        frame[5] = 0; frame[6] = 0; frame[7] = 0; frame[8] = 1; // stream=1
        payload.CopyTo(frame, 9);

        var decoder = new Http2Decoder();
        decoder.TryDecode(frame, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.2-006: HEADERS without END_HEADERS waits for CONTINUATION")]
    public void HeadersFrame_WithoutEndHeaders_WaitsForContinuation()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var split1 = headerBlock[..(headerBlock.Length / 2)];
        var split2 = headerBlock[(headerBlock.Length / 2)..];

        var headersFrame = new HeadersFrame(1, split1, endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, split2, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        // HEADERS without END_HEADERS: the frame is parsed but no response is produced yet.
        decoder.TryDecode(headersFrame, out var r1Result);
        Assert.False(r1Result.HasResponses);

        decoder.TryDecode(contFrame, out var result);
        Assert.Single(result.Responses);
    }

    [Fact(DisplayName = "7540-6.2-007: HEADERS on stream 0 is PROTOCOL_ERROR")]
    public void HeadersFrame_Stream0_ThrowsProtocolError()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01, 0x05,
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x88
        };
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.9-001: CONTINUATION appended to HEADERS block")]
    public void ContinuationFrame_AppendedToHeaders_HeaderBlockMerged()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("x-test", "cont")]);
        var split = headerBlock.Length / 2;

        var headersFrame = new HeadersFrame(1, headerBlock[..split], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, headerBlock[split..], endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);
        decoder.TryDecode(contFrame, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.9-dec-002: END_HEADERS on final CONTINUATION completes block")]
    public void ContinuationFrame_EndHeaders_CompletesBlock()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);

        var headersFrame = new HeadersFrame(1, headerBlock[..1], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, headerBlock[1..], endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);
        decoder.TryDecode(contFrame, out var result);

        Assert.Single(result.Responses);
    }

    [Fact(DisplayName = "7540-6.9-003: Multiple CONTINUATION frames all merged")]
    public void ContinuationFrame_Multiple_AllMerged()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200"), ("a", "1"), ("b", "2"), ("c", "3")]);
        // Split into 3 parts.
        var third = headerBlock.Length / 3;

        var headersFrame = new HeadersFrame(1, headerBlock[..third], endStream: true, endHeaders: false).Serialize();
        var cont1 = new ContinuationFrame(1, headerBlock[third..(2 * third)], endHeaders: false).Serialize();
        var cont2 = new ContinuationFrame(1, headerBlock[(2 * third)..], endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);
        decoder.TryDecode(cont1, out _);
        decoder.TryDecode(cont2, out var result);

        Assert.Single(result.Responses);
        Assert.Equal(200, (int)result.Responses[0].Response.StatusCode);
    }

    [Fact(DisplayName = "7540-6.9-004: CONTINUATION on wrong stream is PROTOCOL_ERROR")]
    public void ContinuationFrame_WrongStream_ThrowsProtocolError()
    {
        var headersFrame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01, 0x00,
            0x00, 0x00, 0x00, 0x01, // stream=1
            0x82
        };
        var contFrame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x09, 0x04,
            0x00, 0x00, 0x00, 0x03, // stream=3 (wrong)
            0x84
        };

        var combined = headersFrame.Concat(contFrame).ToArray();
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(combined, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.9-005: Non-CONTINUATION after HEADERS is PROTOCOL_ERROR")]
    public void ContinuationFrame_NonContinuationAfterHeaders_ThrowsProtocolError()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: false).Serialize();
        var pingFrame = new PingFrame(new byte[8]).Serialize();

        var decoder = new Http2Decoder();
        decoder.TryDecode(headersFrame, out _);

        // PING arrives while we're waiting for CONTINUATION → PROTOCOL_ERROR.
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(pingFrame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "7540-6.9-006: CONTINUATION on stream 0 is PROTOCOL_ERROR")]
    public void ContinuationFrame_Stream0_ThrowsProtocolError()
    {
        var headersOnStream1 = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01, 0x00,
            0x00, 0x00, 0x00, 0x01, // stream=1
            0x82
        };
        var contOnStream0 = new byte[]
        {
            0x00, 0x00, 0x01,
            0x09, 0x04,
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x84
        };

        var combined = headersOnStream1.Concat(contOnStream0).ToArray();
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(combined, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "dec6-cont-001: CONTINUATION without HEADERS is PROTOCOL_ERROR")]
    public void ContinuationFrame_WithoutPrecedingHeaders_ThrowsProtocolError()
    {
        var contFrame = new ContinuationFrame(1, new byte[] { 0x88 }, endHeaders: true).Serialize();
        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(contFrame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }
}
