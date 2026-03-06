using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

/// <summary>
/// Reusable utilities for Stage-based RFC 9113 testing.
/// Provides frame decoding, building, response aggregation, and RFC validation helpers.
/// Phase 40 — see IMPLEMENTATION_PLAN.md for migration guide.
/// </summary>
public static class Http2StageTestHelper
{
    // ── Frame Decoding ────────────────────────────────────────────────────────

    /// <summary>
    /// Decode raw HTTP/2 bytes into frame objects.
    /// Does not aggregate or interpret frames (pure structural parsing).
    /// RFC 9113 §4.1: Frame Format
    /// </summary>
    public static IReadOnlyList<Http2Frame> DecodeFrames(ReadOnlyMemory<byte> data)
    {
        var decoder = new Http2FrameDecoder();
        return decoder.Decode(data);
    }

    // ── Frame Building ────────────────────────────────────────────────────────

    /// <summary>
    /// Build a HEADERS frame with HPACK-encoded headers.
    /// Uses literal encoding (no Huffman) for deterministic test output.
    /// RFC 9113 §6.2
    /// </summary>
    public static HeadersFrame BuildHeadersFrame(
        int streamId,
        Dictionary<string, string> headers,
        bool endStream = true,
        bool endHeaders = true)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerList = headers
            .Select(kv => (kv.Key.ToLowerInvariant(), kv.Value))
            .ToList();
        var block = hpack.Encode(headerList);
        return new HeadersFrame(streamId, block, endStream, endHeaders);
    }

    /// <summary>
    /// Build a DATA frame.
    /// RFC 9113 §6.1
    /// </summary>
    public static DataFrame BuildDataFrame(int streamId, byte[] payload, bool endStream = false)
    {
        return new DataFrame(streamId, payload, endStream);
    }

    // ── Response Aggregation ──────────────────────────────────────────────────

    /// <summary>
    /// Simulate Http2StreamStage: convert a frame sequence into an HttpResponseMessage.
    /// Returns null if no HEADERS frame for the expected stream is found.
    /// Throws Http2Exception if the HEADERS frame lacks a :status pseudo-header.
    /// </summary>
    public static HttpResponseMessage? TryBuildResponseFromFrames(
        IReadOnlyList<Http2Frame> frames,
        int expectedStreamId = 1)
    {
        var headersFrame = frames.OfType<HeadersFrame>()
            .FirstOrDefault(f => f.StreamId == expectedStreamId);

        if (headersFrame == null)
        {
            return null;
        }

        var hpack = new HpackDecoder();
        var headers = hpack.Decode(headersFrame.HeaderBlockFragment.Span);

        var statusHeader = headers.FirstOrDefault(h => h.Name == ":status");
        if (statusHeader == default)
        {
            throw new Http2Exception(
                "Missing :status pseudo-header",
                Http2ErrorCode.ProtocolError,
                Http2ErrorScope.Connection);
        }

        var response = new HttpResponseMessage((HttpStatusCode)int.Parse(statusHeader.Value));

        foreach (var h in headers.Where(h => !h.Name.StartsWith(':')))
        {
            response.Headers.TryAddWithoutValidation(h.Name, h.Value);
        }

        var bodyStream = new MemoryStream();
        foreach (var dataFrame in frames.OfType<DataFrame>()
                     .Where(f => f.StreamId == expectedStreamId))
        {
            bodyStream.Write(dataFrame.Data.Span);
        }

        if (bodyStream.Length > 0)
        {
            response.Content = new ByteArrayContent(bodyStream.ToArray());
        }

        return response;
    }

    // ── RFC Validation ────────────────────────────────────────────────────────

    /// <summary>
    /// Validate frame compliance with RFC 9113 rules.
    /// Throws Http2Exception if a frame violates protocol constraints.
    /// </summary>
    public static void ValidateFrame(Http2Frame frame)
    {
        // RFC 9113 §6.2: HEADERS must be on stream > 0
        if (frame is HeadersFrame && frame.StreamId == 0)
        {
            throw new Http2Exception(
                "HEADERS on stream 0 is connection error",
                Http2ErrorCode.ProtocolError,
                Http2ErrorScope.Connection);
        }

        // RFC 9113 §6.1: DATA must be on stream > 0
        if (frame is DataFrame && frame.StreamId == 0)
        {
            throw new Http2Exception(
                "DATA on stream 0 is connection error",
                Http2ErrorCode.ProtocolError,
                Http2ErrorScope.Connection);
        }

        // RFC 9113 §6.5: SETTINGS must be on stream 0
        if (frame is SettingsFrame && frame.StreamId != 0)
        {
            throw new Http2Exception(
                "SETTINGS on non-zero stream is connection error",
                Http2ErrorCode.ProtocolError,
                Http2ErrorScope.Connection);
        }

        // RFC 9113 §6.7: PING must be on stream 0
        if (frame is PingFrame && frame.StreamId != 0)
        {
            throw new Http2Exception(
                "PING on non-zero stream is connection error",
                Http2ErrorCode.ProtocolError,
                Http2ErrorScope.Connection);
        }

        // RFC 9113 §6.8: GOAWAY must be on stream 0
        if (frame is GoAwayFrame && frame.StreamId != 0)
        {
            throw new Http2Exception(
                "GOAWAY on non-zero stream is connection error",
                Http2ErrorCode.ProtocolError,
                Http2ErrorScope.Connection);
        }
    }
}
