using System;
using System.Collections.Generic;
using System.Net.Http;

namespace TurboHttp.Protocol;

/// <summary>
/// Encodes HTTP request messages as HTTP/2 frame sequences.
/// Stateful: maintains HPACK encoder and stream ID counter.
/// One instance per connection.
/// </summary>
public sealed class Http2RequestEncoder
{
    private readonly HpackEncoder _hpack;
    private readonly int _maxFrameSize;
    private int _nextStreamId = 1;

    public Http2RequestEncoder(bool useHuffman = false, int maxFrameSize = 16384)
    {
        _hpack = new HpackEncoder(useHuffman);
        _maxFrameSize = maxFrameSize;
    }

    /// <summary>
    /// Encodes a request to HTTP/2 frames. Returns the stream ID and frame list.
    /// Thread-safety: not thread-safe (one stream at a time per connection).
    /// </summary>
    public (int StreamId, IReadOnlyList<Http2Frame> Frames) Encode(HttpRequestMessage request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.RequestUri is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var streamId = _nextStreamId;
        _nextStreamId += 2;

        var headers = BuildHeaderList(request);
        var headerBlock = _hpack.Encode(headers).ToArray();
        var hasBody = request.Content != null;

        var frames = new List<Http2Frame>();
        EncodeHeaders(frames, streamId, headerBlock, hasBody);

        if (hasBody)
        {
            var body = request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (body.Length > 0)
            {
                frames.Add(new DataFrame(streamId, body, endStream: true));
            }
            else
            {
                // Empty body — set END_STREAM on the last HEADERS/CONTINUATION frame
                AppendEndStream(frames, streamId);
            }
        }

        return (streamId, frames);
    }

    private void EncodeHeaders(
        List<Http2Frame> frames, int streamId, byte[] headerBlock, bool hasBody)
    {
        if (headerBlock.Length <= _maxFrameSize)
        {
            frames.Add(new HeadersFrame(streamId, headerBlock,
                endStream: !hasBody, endHeaders: true));
            return;
        }

        // Fragmented header block — first chunk goes in HEADERS frame
        frames.Add(new HeadersFrame(streamId, headerBlock[.._maxFrameSize],
            endStream: false, endHeaders: false));

        var pos = _maxFrameSize;
        while (pos < headerBlock.Length)
        {
            var chunkSize = Math.Min(headerBlock.Length - pos, _maxFrameSize);
            var isLast = pos + chunkSize >= headerBlock.Length;
            frames.Add(new ContinuationFrame(streamId,
                headerBlock[pos..(pos + chunkSize)],
                endHeaders: isLast));
            pos += chunkSize;
        }
    }

    private static void AppendEndStream(List<Http2Frame> frames, int streamId)
    {
        // RFC 9113 §6.1 — zero-length DATA with END_STREAM is the correct approach
        // when the last header frame is a CONTINUATION (which has no END_STREAM flag)
        if (frames.Count == 0)
        {
            return;
        }

        if (frames[^1] is HeadersFrame hf)
        {
            frames[^1] = new HeadersFrame(
                hf.StreamId,
                hf.HeaderBlockFragment,
                endStream: true,
                endHeaders: hf.EndHeaders);
        }
        else
        {
            // Last frame is ContinuationFrame — append zero-length DATA with END_STREAM
            frames.Add(new DataFrame(streamId, Array.Empty<byte>(), endStream: true));
        }
    }

    // ── Header building (mirrors Http2Encoder pseudo-header logic) ─────────────

    private static List<(string, string)> BuildHeaderList(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        var pathAndQuery = string.IsNullOrEmpty(uri.Query)
            ? uri.AbsolutePath
            : uri.AbsolutePath + uri.Query;

        var headers = new List<(string, string)>
        {
            (":method",    request.Method.Method),
            (":path",      pathAndQuery),
            (":scheme",    uri.Scheme),
            (":authority", uri.Authority),
        };

        foreach (var h in request.Headers)
        {
            if (!IsForbidden(h.Key))
            {
                headers.Add((h.Key.ToLowerInvariant(), string.Join(", ", h.Value)));
            }
        }

        if (request.Content != null)
        {
            foreach (var h in request.Content.Headers)
            {
                headers.Add((h.Key.ToLowerInvariant(), string.Join(", ", h.Value)));
            }
        }

        return headers;
    }

    // Forbidden connection-specific headers per RFC 9113 §8.2.2
    private static bool IsForbidden(string name) =>
        string.Equals(name, "connection",        StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "transfer-encoding", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "upgrade",           StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "proxy-connection",  StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "keep-alive",        StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "te",                StringComparison.OrdinalIgnoreCase);
}
