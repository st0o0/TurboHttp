using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 7540 §6.8 — GOAWAY Frame  |  RFC 7540 §6.4 — RST_STREAM Frame
/// Phase 7: GOAWAY &amp; RST_STREAM Handling.
///
/// Covers:
///   - Stop new streams after GOAWAY (RFC 7540 §6.8)
///   - Process streams ≤ last-stream-id (RFC 7540 §6.8)
///   - Clean up stream resources for streams &gt; last-stream-id
///   - GetGoAwayLastStreamId() and IsGoingAway accessors
///   - Reset() clears GOAWAY state
///   - RST_STREAM stream termination
/// </summary>
public sealed class Http2GoAwayRstStreamTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static byte[] MakeHeadersFrame(int streamId, bool endStream = false, bool endHeaders = true)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        return new HeadersFrame(streamId, headerBlock, endStream, endHeaders).Serialize();
    }

    private static byte[] MakeDataFrame(int streamId, bool endStream, byte[]? body = null)
    {
        return new DataFrame(streamId, body ?? "hello"u8.ToArray(), endStream).Serialize();
    }

    private static byte[] MakeGoAway(int lastStreamId, Http2ErrorCode error = Http2ErrorCode.NoError)
    {
        return new GoAwayFrame(lastStreamId, error).Serialize();
    }

    // =========================================================================
    // GA-001..005: GetGoAwayLastStreamId and IsGoingAway
    // =========================================================================

    /// RFC 7540 §6.8 — GetGoAwayLastStreamId returns int.MaxValue before GOAWAY
    [Fact(DisplayName = "RFC7540-6.8-GA-001: GetGoAwayLastStreamId returns int.MaxValue before GOAWAY")]
    public void GetGoAwayLastStreamId_BeforeGoAway_ReturnsIntMaxValue()
    {
        var decoder = new Http2Decoder();
        Assert.Equal(int.MaxValue, decoder.GetGoAwayLastStreamId());
    }

    /// RFC 7540 §6.8 — IsGoingAway returns false before GOAWAY
    [Fact(DisplayName = "RFC7540-6.8-GA-002: IsGoingAway returns false before GOAWAY")]
    public void IsGoingAway_BeforeGoAway_ReturnsFalse()
    {
        var decoder = new Http2Decoder();
        Assert.False(decoder.IsGoingAway);
    }

    /// RFC 7540 §6.8 — After GOAWAY received, GetGoAwayLastStreamId returns lastStreamId
    [Fact(DisplayName = "RFC7540-6.8-GA-003: After GOAWAY received, GetGoAwayLastStreamId returns lastStreamId")]
    public void GetGoAwayLastStreamId_AfterGoAway_ReturnsLastStreamId()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeGoAway(lastStreamId: 7), out _);
        Assert.Equal(7, decoder.GetGoAwayLastStreamId());
    }

    /// RFC 7540 §6.8 — After GOAWAY received, IsGoingAway returns true
    [Fact(DisplayName = "RFC7540-6.8-GA-004: After GOAWAY received, IsGoingAway returns true")]
    public void IsGoingAway_AfterGoAway_ReturnsTrue()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeGoAway(lastStreamId: 5), out _);
        Assert.True(decoder.IsGoingAway);
    }

    /// RFC 7540 §6.8 — GOAWAY with lastStreamId=0 recorded correctly
    [Fact(DisplayName = "RFC7540-6.8-GA-005: GOAWAY with lastStreamId=0 recorded correctly")]
    public void GetGoAwayLastStreamId_LastStreamIdZero_RecordedCorrectly()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeGoAway(lastStreamId: 0), out _);
        Assert.Equal(0, decoder.GetGoAwayLastStreamId());
    }

    // =========================================================================
    // GA-006..008: Stop new streams after GOAWAY
    // =========================================================================

    /// RFC 7540 §6.8 — New stream HEADERS after GOAWAY throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC7540-6.8-GA-006: New stream HEADERS after GOAWAY throws PROTOCOL_ERROR")]
    public void NewStream_AfterGoAway_ThrowsProtocolError()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeGoAway(lastStreamId: 1), out _);

        var headers = MakeHeadersFrame(streamId: 3);
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(headers, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §6.8 — GOAWAY with lastStreamId=0 blocks all new streams
    [Fact(DisplayName = "RFC7540-6.8-GA-007: GOAWAY with lastStreamId=0 blocks all new streams")]
    public void NewStream_AfterGoAwayLastStreamIdZero_ThrowsProtocolError()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeGoAway(lastStreamId: 0), out _);

        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(MakeHeadersFrame(streamId: 1), out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §6.8 — Second GOAWAY updates lastStreamId
    [Fact(DisplayName = "RFC7540-6.8-GA-008: Second GOAWAY updates lastStreamId")]
    public void SecondGoAway_UpdatesLastStreamId()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeGoAway(lastStreamId: 9), out _);
        decoder.TryDecode(MakeGoAway(lastStreamId: 3), out _);
        Assert.Equal(3, decoder.GetGoAwayLastStreamId());
    }

    // =========================================================================
    // GA-009..013: Clean up streams > lastStreamId
    // =========================================================================

    /// RFC 7540 §6.8 — Streams with ID > lastStreamId are moved to Closed after GOAWAY
    [Fact(DisplayName = "RFC7540-6.8-GA-009: Streams with ID > lastStreamId are moved to Closed after GOAWAY")]
    public void StreamsAboveLastStreamId_AfterGoAway_AreClosed()
    {
        var decoder = new Http2Decoder();

        // Open streams 1, 3, 5 (no END_STREAM so they stay open waiting for DATA)
        decoder.TryDecode(MakeHeadersFrame(streamId: 1), out _);
        decoder.TryDecode(MakeHeadersFrame(streamId: 3), out _);
        decoder.TryDecode(MakeHeadersFrame(streamId: 5), out _);

        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(3));
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(5));

        // GOAWAY: server only processed up to stream 3
        decoder.TryDecode(MakeGoAway(lastStreamId: 3), out _);

        // Stream 5 was NOT processed — must be Closed
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(5));
        // Streams 1 and 3 remain Open (server may still send their responses)
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(3));
    }

    /// RFC 7540 §6.8 — Active stream count decremented for cleaned-up streams
    [Fact(DisplayName = "RFC7540-6.8-GA-010: Active stream count decremented for cleaned-up streams")]
    public void ActiveStreamCount_AfterGoAway_DecrementedForCancelledStreams()
    {
        var decoder = new Http2Decoder();

        decoder.TryDecode(MakeHeadersFrame(streamId: 1), out _); // open
        decoder.TryDecode(MakeHeadersFrame(streamId: 3), out _); // open
        decoder.TryDecode(MakeHeadersFrame(streamId: 5), out _); // open

        Assert.Equal(3, decoder.GetActiveStreamCount());

        // GOAWAY lastStreamId=1 → streams 3 and 5 cleaned up
        decoder.TryDecode(MakeGoAway(lastStreamId: 1), out _);

        Assert.Equal(1, decoder.GetActiveStreamCount());
    }

    /// RFC 7540 §6.8 — DATA for stream > lastStreamId after GOAWAY is rejected
    [Fact(DisplayName = "RFC7540-6.8-GA-011: DATA for stream > lastStreamId after GOAWAY is rejected")]
    public void Data_ForCleanedUpStream_AfterGoAway_IsRejected()
    {
        var decoder = new Http2Decoder();

        // Open stream 5, then receive GOAWAY with lastStreamId=3
        decoder.TryDecode(MakeHeadersFrame(streamId: 5), out _);
        decoder.TryDecode(MakeGoAway(lastStreamId: 3), out _);

        // Stream 5 was cleaned up; DATA on it should be rejected (StreamClosed)
        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(MakeDataFrame(streamId: 5, endStream: true), out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(5, ex.StreamId);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(5));
    }

    /// RFC 7540 §6.8 — DATA for stream ≤ lastStreamId after GOAWAY is still processed
    [Fact(DisplayName = "RFC7540-6.8-GA-012: DATA for stream ≤ lastStreamId after GOAWAY is still processed")]
    public void Data_ForStreamAtOrBelowLastStreamId_AfterGoAway_IsProcessed()
    {
        var decoder = new Http2Decoder();

        // Open stream 1, then GOAWAY with lastStreamId=5
        decoder.TryDecode(MakeHeadersFrame(streamId: 1), out _);
        decoder.TryDecode(MakeGoAway(lastStreamId: 5), out _);

        // Stream 1 ≤ 5, so DATA is still accepted and produces a response
        decoder.TryDecode(MakeDataFrame(streamId: 1, endStream: true), out var result);

        Assert.Single(result.Responses);
        Assert.Equal(1, result.Responses[0].StreamId);
    }

    /// RFC 7540 §6.8 — GOAWAY with lastStreamId=int.MaxValue cleans up nothing
    [Fact(DisplayName = "RFC7540-6.8-GA-013: GOAWAY with lastStreamId=int.MaxValue cleans up nothing")]
    public void GoAway_LastStreamIdMaxValue_NothingCleaned()
    {
        var decoder = new Http2Decoder();

        decoder.TryDecode(MakeHeadersFrame(streamId: 1), out _);
        decoder.TryDecode(MakeHeadersFrame(streamId: 3), out _);

        Assert.Equal(2, decoder.GetActiveStreamCount());

        // GOAWAY with very high lastStreamId — no streams cancelled
        decoder.TryDecode(MakeGoAway(lastStreamId: int.MaxValue & 0x7FFFFFFF), out _);

        Assert.Equal(2, decoder.GetActiveStreamCount());
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(3));
    }

    // =========================================================================
    // GA-014..015: Reset() clears GOAWAY state
    // =========================================================================

    /// RFC 7540 §6.8 — Reset() clears IsGoingAway flag
    [Fact(DisplayName = "RFC7540-6.8-GA-014: Reset() clears IsGoingAway flag")]
    public void Reset_ClearsIsGoingAway()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeGoAway(lastStreamId: 1), out _);
        Assert.True(decoder.IsGoingAway);

        decoder.Reset();
        Assert.False(decoder.IsGoingAway);
    }

    /// RFC 7540 §6.8 — Reset() restores GetGoAwayLastStreamId to int.MaxValue
    [Fact(DisplayName = "RFC7540-6.8-GA-015: Reset() restores GetGoAwayLastStreamId to int.MaxValue")]
    public void Reset_RestoresGetGoAwayLastStreamId()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeGoAway(lastStreamId: 3), out _);
        Assert.Equal(3, decoder.GetGoAwayLastStreamId());

        decoder.Reset();
        Assert.Equal(int.MaxValue, decoder.GetGoAwayLastStreamId());
    }

    /// RFC 7540 §6.8 — After Reset(), new streams can be opened again
    [Fact(DisplayName = "RFC7540-6.8-GA-016: After Reset(), new streams can be opened again")]
    public void AfterReset_NewStreamsCanBeOpened()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeGoAway(lastStreamId: 1), out _);

        decoder.Reset();

        // Should not throw after reset
        decoder.TryDecode(MakeHeadersFrame(streamId: 1), out _);
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));
    }

    // =========================================================================
    // GA-017..019: Edge cases
    // =========================================================================

    /// RFC 7540 §6.8 — GOAWAY on non-zero stream throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC7540-6.8-GA-017: GOAWAY on non-zero stream throws PROTOCOL_ERROR")]
    public void GoAway_OnNonZeroStream_ThrowsProtocolError()
    {
        var decoder = new Http2Decoder();

        // Manually craft a GOAWAY frame header with stream ID = 1
        var frame = new byte[9 + 8];
        frame[0] = 0; frame[1] = 0; frame[2] = 8; // payload length = 8
        frame[3] = 0x07;                            // GOAWAY type
        frame[4] = 0x00;                            // no flags
        frame[5] = 0x00; frame[6] = 0x00; frame[7] = 0x00; frame[8] = 0x01; // stream ID = 1
        // lastStreamId = 0, errorCode = 0
        frame[9] = 0; frame[10] = 0; frame[11] = 0; frame[12] = 0;
        frame[13] = 0; frame[14] = 0; frame[15] = 0; frame[16] = 0;

        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §6.8 — GOAWAY with debug data does not affect stream cleanup
    [Fact(DisplayName = "RFC7540-6.8-GA-018: GOAWAY with debug data does not affect stream cleanup")]
    public void GoAway_WithDebugData_StreamCleanupStillWorks()
    {
        var decoder = new Http2Decoder();

        decoder.TryDecode(MakeHeadersFrame(streamId: 7), out _);

        var goaway = new GoAwayFrame(3, Http2ErrorCode.NoError, "graceful shutdown"u8.ToArray()).Serialize();
        decoder.TryDecode(goaway, out _);

        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(7));
        Assert.Equal(3, decoder.GetGoAwayLastStreamId());
    }

    /// RFC 7540 §6.8 — GOAWAY result contains GoAway frame details
    [Fact(DisplayName = "RFC7540-6.8-GA-019: GOAWAY result contains GoAway frame details")]
    public void GoAway_DecodeResult_ContainsGoAwayDetails()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeGoAway(lastStreamId: 5, error: Http2ErrorCode.ProtocolError), out var result);

        Assert.True(result.HasGoAway);
        Assert.Equal(5, result.GoAway!.LastStreamId);
        Assert.Equal(Http2ErrorCode.ProtocolError, result.GoAway.ErrorCode);
    }

    // =========================================================================
    // GA-020: Multiple streams cleanup
    // =========================================================================

    /// RFC 7540 §6.8 — GOAWAY cleans up multiple streams above lastStreamId
    [Fact(DisplayName = "RFC7540-6.8-GA-020: GOAWAY cleans up multiple streams above lastStreamId")]
    public void GoAway_MultiplePendingStreams_AllAboveLastStreamIdCleaned()
    {
        var decoder = new Http2Decoder();

        // Open 5 streams: 1, 3, 5, 7, 9
        for (var sid = 1; sid <= 9; sid += 2)
        {
            decoder.TryDecode(MakeHeadersFrame(streamId: sid), out _);
        }

        Assert.Equal(5, decoder.GetActiveStreamCount());

        // GOAWAY: only processed up to stream 3
        decoder.TryDecode(MakeGoAway(lastStreamId: 3), out _);

        // Streams 5, 7, 9 cancelled
        Assert.Equal(2, decoder.GetActiveStreamCount());
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(3));
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(5));
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(7));
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(9));
    }
}
