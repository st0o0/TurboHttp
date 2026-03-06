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
    private int _maxFrameSize;
    private int _nextStreamId = 1;
    private long _connectionSendWindow = 65535; // Initial window size per RFC 7540
    private readonly Dictionary<int, long> _streamSendWindows = new();

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
        ValidatePseudoHeaders(headers);

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

    /// <summary>
    /// TEST ONLY: Encodes a request and extracts the raw HPACK header block.
    /// Used by RFC compliance tests to verify header encoding details.
    /// </summary>
    internal byte[] EncodeToHpackBlock(HttpRequestMessage request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.RequestUri is null)
            throw new ArgumentNullException(nameof(request.RequestUri));

        var headers = BuildHeaderList(request);
        ValidatePseudoHeaders(headers);
        return _hpack.Encode(headers).ToArray();
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

    // ── Pseudo-Header Validation (RFC 7540 §8.1.2.1) ──────────────────────────

    /// <summary>
    /// Validates pseudo-headers per RFC 7540 §8.1.2.1:
    /// - All four required: :method, :path, :scheme, :authority
    /// - Must appear before regular headers
    /// - Must have exactly one of each (no duplicates)
    /// - No other pseudo-headers allowed
    /// </summary>
    internal static void ValidatePseudoHeaders(List<(string Name, string Value)> headers)
    {
        var hasMethod = false;
        var hasPath = false;
        var hasScheme = false;
        var hasAuthority = false;
        var lastPseudoIndex = -1;
        var firstRegularIndex = int.MaxValue;

        for (var i = 0; i < headers.Count; i++)
        {
            var (name, _) = headers[i];

            if (name.StartsWith(':'))
            {
                lastPseudoIndex = i;

                switch (name)
                {
                    case ":method":
                        if (hasMethod)
                            throw new Http2Exception("RFC 7540 §8.1.2.1: Duplicate :method pseudo-header");
                        hasMethod = true;
                        break;
                    case ":path":
                        if (hasPath)
                            throw new Http2Exception("RFC 7540 §8.1.2.1: Duplicate :path pseudo-header");
                        hasPath = true;
                        break;
                    case ":scheme":
                        if (hasScheme)
                            throw new Http2Exception("RFC 7540 §8.1.2.1: Duplicate :scheme pseudo-header");
                        hasScheme = true;
                        break;
                    case ":authority":
                        if (hasAuthority)
                            throw new Http2Exception("RFC 7540 §8.1.2.1: Duplicate :authority pseudo-header");
                        hasAuthority = true;
                        break;
                    default:
                        throw new Http2Exception($"RFC 7540 §8.1.2.1: Unknown request pseudo-header '{name}'");
                }
            }
            else
            {
                if (firstRegularIndex == int.MaxValue)
                    firstRegularIndex = i;
            }
        }

        if (lastPseudoIndex > firstRegularIndex)
        {
            throw new Http2Exception(
                $"RFC 7540 §8.1.2.1: Pseudo-header at index {lastPseudoIndex} appears after regular header at index {firstRegularIndex}");
        }

        var missing = new System.Text.StringBuilder();
        if (!hasMethod)
            missing.Append(missing.Length > 0 ? ", :method" : ":method");
        if (!hasPath)
            missing.Append(missing.Length > 0 ? ", :path" : ":path");
        if (!hasScheme)
            missing.Append(missing.Length > 0 ? ", :scheme" : ":scheme");
        if (!hasAuthority)
            missing.Append(missing.Length > 0 ? ", :authority" : ":authority");

        if (missing.Length > 0)
            throw new Http2Exception($"RFC 7540 §8.1.2.1: Missing required pseudo-headers: {missing}");
    }

    // ── Flow Control Window Management ────────────────────────────────────────

    /// <summary>
    /// Updates the connection-level send window when server sends WINDOW_UPDATE on stream 0.
    /// RFC 7540 §6.9: Sender increases window size via WINDOW_UPDATE.
    /// </summary>
    public void UpdateConnectionWindow(int increment)
    {
        if (increment is < 1 or > 0x7FFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(increment));
        }

        _connectionSendWindow += increment;
    }

    /// <summary>
    /// Updates the stream-level send window when server sends WINDOW_UPDATE on a stream.
    /// RFC 7540 §6.9: Sender increases stream window size via WINDOW_UPDATE.
    /// </summary>
    public void UpdateStreamWindow(int streamId, int increment)
    {
        if (increment is < 1 or > 0x7FFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(increment));
        }

        _streamSendWindows.TryGetValue(streamId, out var current);
        _streamSendWindows[streamId] = current + increment;
    }

    /// <summary>
    /// Applies server settings to the encoder (e.g., MAX_FRAME_SIZE).
    /// RFC 7540 §6.5: Received SETTINGS ACK updates encoder state.
    /// </summary>
    public void ApplyServerSettings(IEnumerable<(SettingsParameter Key, uint Value)> settings)
    {
        foreach (var (key, val) in settings)
        {
            if (key == SettingsParameter.MaxFrameSize)
            {
                _maxFrameSize = (int)val;
            }
        }
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
