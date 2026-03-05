#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TurboHttp.Protocol;
using Xunit;

namespace TurboHttp.Tests;

/// <summary>
/// RFC 9113 §5.1 — HTTP/2 Stream States (Phase 5-6: Full Stream Lifecycle).
///
/// Covers:
///   - Lifecycle state transitions: idle → open → closed (RFC 9113 §5.1)
///   - AUTO-CLOSE on END_STREAM in HEADERS and DATA frames
///   - Rejection of frames that violate stream state machine transitions
///   - DATA before HEADERS on idle stream → PROTOCOL_ERROR (RFC 9113 §5.1)
///   - DATA on closed stream → STREAM_CLOSED error (RFC 9113 §6.1)
///   - HEADERS on closed stream → PROTOCOL_ERROR (RFC 9113 §5.1)
///   - RST_STREAM closes stream (RFC 9113 §6.4)
///   - GetStreamLifecycleState() accessor for state machine inspection
/// </summary>
public sealed class Http2StreamLifecycleTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static byte[] MakeResponseHeadersFrame(int streamId, bool endStream = false, bool endHeaders = true)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        return new HeadersFrame(streamId, headerBlock, endStream, endHeaders).Serialize();
    }

    private static byte[] MakeResponseHeadersFrameStatus(int streamId, int status, bool endStream = false)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", status.ToString())]);
        return new HeadersFrame(streamId, headerBlock, endStream, endHeaders: true).Serialize();
    }

    private static byte[] MakeDataFrame(int streamId, bool endStream, byte[]? body = null)
    {
        return new DataFrame(streamId, body ?? "data"u8.ToArray(), endStream).Serialize();
    }

    private static byte[] Concat(params byte[][] arrays)
    {
        var result = new byte[arrays.Sum(a => a.Length)];
        var offset = 0;
        foreach (var arr in arrays)
        {
            arr.CopyTo(result, offset);
            offset += arr.Length;
        }
        return result;
    }

    // =========================================================================
    // SS-001..005: Initial / basic lifecycle state transitions
    // =========================================================================

    /// RFC 9113 §5.1 — Unknown stream ID reports Idle state
    [Fact(DisplayName = "RFC9113-5.1-SS-001: Unknown stream ID reports Idle state")]
    public void GetLifecycleState_UnknownStream_ReturnsIdle()
    {
        var decoder = new Http2Decoder();
        Assert.Equal(Http2StreamLifecycleState.Idle, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Idle, decoder.GetStreamLifecycleState(99));
    }

    /// RFC 9113 §5.1 — HEADERS frame (no END_STREAM) moves stream from Idle to Open
    [Fact(DisplayName = "RFC9113-5.1-SS-002: HEADERS frame (no END_STREAM) moves stream from Idle to Open")]
    public void Headers_NoEndStream_StreamBecomesOpen()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));
    }

    /// RFC 9113 §5.1 — HEADERS+END_STREAM moves stream directly to Closed
    [Fact(DisplayName = "RFC9113-5.1-SS-003: HEADERS+END_STREAM moves stream directly to Closed")]
    public void Headers_WithEndStream_StreamBecomesClosed()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: true), out _);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));
    }

    /// RFC 9113 §5.1 — DATA+END_STREAM after HEADERS closes the stream
    [Fact(DisplayName = "RFC9113-5.1-SS-004: DATA+END_STREAM after HEADERS closes the stream")]
    public void Data_WithEndStream_AfterHeaders_StreamBecomesClosed()
    {
        var decoder = new Http2Decoder();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var data = MakeDataFrame(streamId: 1, endStream: true);

        decoder.TryDecode(Concat(headers, data), out _);

        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));
    }

    /// RFC 9113 §5.1 — RST_STREAM closes the stream
    [Fact(DisplayName = "RFC9113-5.1-SS-005: RST_STREAM closes the stream")]
    public void RstStream_MovesStream_ToClosed()
    {
        var decoder = new Http2Decoder();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        decoder.TryDecode(headers, out _);
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        decoder.TryDecode(rst, out _);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));
    }

    // =========================================================================
    // SS-006..008: Reject invalid frame per state
    // =========================================================================

    /// RFC 9113 §5.1 — DATA on idle stream (no HEADERS) is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-5.1-SS-006: DATA on idle stream (no HEADERS) is PROTOCOL_ERROR")]
    public void Data_OnIdleStream_ThrowsProtocolError()
    {
        var decoder = new Http2Decoder();
        // Send DATA on stream 1 without any preceding HEADERS.
        var data = MakeDataFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(data, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    /// RFC 9113 §5.1 — DATA on closed stream is STREAM_CLOSED error
    [Fact(DisplayName = "RFC9113-5.1-SS-007: DATA on closed stream is STREAM_CLOSED error")]
    public void Data_OnClosedStream_ThrowsStreamClosed()
    {
        var decoder = new Http2Decoder();
        // Close stream via HEADERS+END_STREAM.
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: true), out _);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));

        // Now send DATA on the closed stream.
        var data = MakeDataFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(data, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    /// RFC 9113 §5.1 — HEADERS on closed stream is connection error STREAM_CLOSED (RFC 7540 §6.2)
    [Fact(DisplayName = "RFC9113-5.1-SS-008: HEADERS on closed stream is connection error STREAM_CLOSED (RFC 7540 §6.2)")]
    public void Headers_OnClosedStream_ThrowsStreamClosed()
    {
        var decoder = new Http2Decoder();
        // Close stream via HEADERS+END_STREAM.
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: true), out _);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));

        // RFC 7540 §6.2: HEADERS on a closed stream is a connection error of type STREAM_CLOSED.
        var headers2 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(headers2, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // =========================================================================
    // SS-009..012: Auto-close on END_STREAM (MUST)
    // =========================================================================

    /// RFC 9113 §5.1 — Auto-close: HEADERS+END_STREAM produces response immediately
    [Fact(DisplayName = "RFC9113-5.1-SS-009: Auto-close: HEADERS+END_STREAM produces response immediately")]
    public void AutoClose_HeadersEndStream_ProducesResponse()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrameStatus(streamId: 1, status: 204, endStream: true), out var result);

        Assert.Single(result.Responses);
        Assert.Equal(204, (int)result.Responses[0].Response.StatusCode);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));
    }

    /// RFC 9113 §5.1 — Auto-close: DATA+END_STREAM produces response
    [Fact(DisplayName = "RFC9113-5.1-SS-010: Auto-close: DATA+END_STREAM produces response")]
    public async Task AutoClose_DataEndStream_ProducesResponse()
    {
        var decoder = new Http2Decoder();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var data = MakeDataFrame(streamId: 1, endStream: true, body: "hello"u8.ToArray());

        decoder.TryDecode(Concat(headers, data), out var result);

        Assert.Single(result.Responses);
        var body = await result.Responses[0].Response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));
    }

    /// RFC 9113 §5.1 — Auto-close: multiple streams closed independently via END_STREAM
    [Fact(DisplayName = "RFC9113-5.1-SS-011: Auto-close: multiple streams closed independently via END_STREAM")]
    public void AutoClose_MultipleStreams_EachClosedIndependently()
    {
        var decoder = new Http2Decoder();

        // Stream 1: HEADERS + DATA+END_STREAM.
        var h1 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var d1 = MakeDataFrame(streamId: 1, endStream: true);

        // Stream 3: HEADERS+END_STREAM.
        var h3 = MakeResponseHeadersFrame(streamId: 3, endStream: true);

        decoder.TryDecode(Concat(h1, d1, h3), out var result);

        Assert.Equal(2, result.Responses.Count);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(3));
    }

    /// RFC 9113 §5.1 — Stream open while DATA accumulates, then closed on END_STREAM
    [Fact(DisplayName = "RFC9113-5.1-SS-012: Stream open while DATA accumulates, then closed on END_STREAM")]
    public void StreamIsOpen_WhileDataAccumulates_ClosedOnEndStream()
    {
        var decoder = new Http2Decoder();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        decoder.TryDecode(headers, out _);
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));

        var data1 = MakeDataFrame(streamId: 1, endStream: false, body: "chunk1"u8.ToArray());
        decoder.TryDecode(data1, out _);
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));

        var data2 = MakeDataFrame(streamId: 1, endStream: true, body: "chunk2"u8.ToArray());
        decoder.TryDecode(data2, out _);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));
    }

    // =========================================================================
    // SS-013..015: Multiple independent streams
    // =========================================================================

    /// RFC 9113 §5.1 — Different streams have independent lifecycle states
    [Fact(DisplayName = "RFC9113-5.1-SS-013: Different streams have independent lifecycle states")]
    public void MultipleStreams_IndependentLifecycleStates()
    {
        var decoder = new Http2Decoder();

        var h1 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var h3 = MakeResponseHeadersFrame(streamId: 3, endStream: true);

        decoder.TryDecode(Concat(h1, h3), out _);

        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(3));
        Assert.Equal(Http2StreamLifecycleState.Idle, decoder.GetStreamLifecycleState(5)); // never seen
    }

    /// RFC 9113 §5.1 — Closing one stream does not affect other open streams
    [Fact(DisplayName = "RFC9113-5.1-SS-014: Closing one stream does not affect other open streams")]
    public void CloseOneStream_OtherRemainsOpen()
    {
        var decoder = new Http2Decoder();

        var h1 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var h3 = MakeResponseHeadersFrame(streamId: 3, endStream: false);
        decoder.TryDecode(Concat(h1, h3), out _);

        // Close stream 1 via DATA+END_STREAM.
        var d1 = MakeDataFrame(streamId: 1, endStream: true);
        decoder.TryDecode(d1, out _);

        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(3));
    }

    /// RFC 9113 §5.1 — RST_STREAM on open stream does not affect other streams
    [Fact(DisplayName = "RFC9113-5.1-SS-015: RST_STREAM on open stream does not affect other streams")]
    public void RstStream_OnOneStream_DoesNotAffectOthers()
    {
        var decoder = new Http2Decoder();

        var h1 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var h3 = MakeResponseHeadersFrame(streamId: 3, endStream: false);
        decoder.TryDecode(Concat(h1, h3), out _);

        var rst1 = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        decoder.TryDecode(rst1, out _);

        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(3));
    }

    // =========================================================================
    // SS-016..018: Post-RST_STREAM frame rejection
    // =========================================================================

    /// RFC 9113 §5.1 — DATA after RST_STREAM on same stream throws STREAM_CLOSED
    [Fact(DisplayName = "RFC9113-5.1-SS-016: DATA after RST_STREAM on same stream throws STREAM_CLOSED")]
    public void Data_AfterRstStream_ThrowsStreamClosed()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        decoder.TryDecode(rst, out _);

        var data = MakeDataFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(data, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    /// RFC 9113 §5.1 — HEADERS after RST_STREAM on same stream is connection error STREAM_CLOSED (RFC 7540 §6.2)
    [Fact(DisplayName = "RFC9113-5.1-SS-017: HEADERS after RST_STREAM on same stream is connection error STREAM_CLOSED (RFC 7540 §6.2)")]
    public void Headers_AfterRstStream_ThrowsStreamClosed()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        decoder.TryDecode(rst, out _);

        // RFC 7540 §6.2: HEADERS on a closed stream is a connection error of type STREAM_CLOSED.
        var headers2 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(headers2, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §5.1 — DATA on stream with only DATA+END_STREAM received (half-closed-remote) throws STREAM_CLOSED
    [Fact(DisplayName = "RFC9113-5.1-SS-018: DATA on stream with only DATA+END_STREAM received (half-closed-remote) throws STREAM_CLOSED")]
    public void Data_OnHalfClosedRemoteStream_ViaData_ThrowsStreamClosed()
    {
        var decoder = new Http2Decoder();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var dataEnd = MakeDataFrame(streamId: 1, endStream: true);
        decoder.TryDecode(Concat(headers, dataEnd), out _);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));

        // Stream is now closed; any subsequent DATA frame must be rejected.
        var extra = MakeDataFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(extra, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }

    // =========================================================================
    // SS-019..022: Reset behaviour
    // =========================================================================

    /// RFC 9113 §5.1 — Reset() clears all lifecycle states back to Idle
    [Fact(DisplayName = "RFC9113-5.1-SS-019: Reset() clears all lifecycle states back to Idle")]
    public void Reset_ClearsAllLifecycleStates()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 3, endStream: true), out _);

        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(3));

        decoder.Reset();

        Assert.Equal(Http2StreamLifecycleState.Idle, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Idle, decoder.GetStreamLifecycleState(3));
    }

    /// RFC 9113 §5.1 — After Reset(), stream IDs can be reused (back to Idle)
    [Fact(DisplayName = "RFC9113-5.1-SS-020: After Reset(), stream IDs can be reused (back to Idle)")]
    public void AfterReset_StreamIdsCanBeReused()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: true), out _);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));

        decoder.Reset();
        // Now stream 1 is idle again; HEADERS should work without PROTOCOL_ERROR.
        var ex = Record.Exception(() => decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _));
        Assert.Null(ex);
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));
    }

    // =========================================================================
    // SS-021..025: Edge cases
    // =========================================================================

    /// RFC 9113 §5.1 — DATA on stream 0 is PROTOCOL_ERROR (stream 0 is for control only)
    [Fact(DisplayName = "RFC9113-5.1-SS-021: DATA on stream 0 is PROTOCOL_ERROR (stream 0 is for control only)")]
    public void Data_OnStream0_ThrowsProtocolError()
    {
        var decoder = new Http2Decoder();
        var frame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x00,             // DATA
            0x00,             // no flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x01, 0x02, 0x03, 0x04
        };
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    /// RFC 9113 §5.1 — HEADERS on stream 0 is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-5.1-SS-022: HEADERS on stream 0 is PROTOCOL_ERROR")]
    public void Headers_OnStream0_ThrowsProtocolError()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var frame = new HeadersFrame(0, headerBlock, endStream: false, endHeaders: true).Serialize();

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(frame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    /// RFC 9113 §5.1 — DATA on a different idle stream than already-open streams throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-5.1-SS-023: DATA on a different idle stream than already-open streams throws PROTOCOL_ERROR")]
    public void Data_OnDifferentIdleStream_ThrowsProtocolError()
    {
        var decoder = new Http2Decoder();
        // Open stream 1.
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(1));

        // Send DATA on stream 3 which was never opened.
        var data = MakeDataFrame(streamId: 3, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(data, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    /// RFC 9113 §5.1 — Five streams each with distinct lifecycle states are tracked correctly
    [Fact(DisplayName = "RFC9113-5.1-SS-024: Five streams each with distinct lifecycle states are tracked correctly")]
    public void FiveStreams_DistinctLifecycles_AllTrackedCorrectly()
    {
        var decoder = new Http2Decoder();

        // Stream 1: HEADERS+END_STREAM → Closed
        decoder.TryDecode(MakeResponseHeadersFrame(1, endStream: true), out _);
        // Stream 3: HEADERS only → Open
        decoder.TryDecode(MakeResponseHeadersFrame(3, endStream: false), out _);
        // Stream 5: HEADERS then DATA+END_STREAM → Closed
        var h5 = MakeResponseHeadersFrame(5, endStream: false);
        var d5 = MakeDataFrame(5, endStream: true);
        decoder.TryDecode(Concat(h5, d5), out _);
        // Stream 7: HEADERS then RST_STREAM → Closed
        decoder.TryDecode(MakeResponseHeadersFrame(7, endStream: false), out _);
        decoder.TryDecode(new RstStreamFrame(7, Http2ErrorCode.Cancel).Serialize(), out _);
        // Stream 9: never seen → Idle

        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));
        Assert.Equal(Http2StreamLifecycleState.Open, decoder.GetStreamLifecycleState(3));
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(5));
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(7));
        Assert.Equal(Http2StreamLifecycleState.Idle, decoder.GetStreamLifecycleState(9));
    }

    /// RFC 9113 §5.1 — DATA on a known closed stream reports STREAM_CLOSED after RST_STREAM
    [Fact(DisplayName = "RFC9113-5.1-SS-025: DATA on a known closed stream reports STREAM_CLOSED after RST_STREAM")]
    public void Data_OnRstStreamClosedStream_ThrowsStreamClosed()
    {
        var decoder = new Http2Decoder();
        decoder.TryDecode(MakeResponseHeadersFrame(streamId: 1, endStream: false), out _);

        // Server sends RST_STREAM to cancel stream 1.
        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        decoder.TryDecode(rst, out _);
        Assert.Equal(Http2StreamLifecycleState.Closed, decoder.GetStreamLifecycleState(1));

        // Any further DATA must throw STREAM_CLOSED.
        var data = MakeDataFrame(1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(data, out _));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
    }
}
