using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace TurboHttp.Protocol;

public sealed class Http2Encoder(bool useHuffman = true)
{
    private readonly HpackEncoder _hpack = new(useHuffman);
    private int _nextStreamId = 1;
    private int _maxFrameSize = 16384;

    private static readonly (SettingsParameter, uint)[] DefaultSettings =
    [
        (SettingsParameter.HeaderTableSize, 4096),
        (SettingsParameter.EnablePush, 0),
        (SettingsParameter.InitialWindowSize, 65535),
        (SettingsParameter.MaxFrameSize, 16384),
    ];

    // ========================================================================
    // CONNECTION PREFACE
    // ========================================================================
    public static byte[] BuildConnectionPreface()
    {
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

        var settingsSize = 9 + DefaultSettings.Length * 6;
        var result = new byte[magic.Length + settingsSize];

        magic.CopyTo(result, 0);

        var settingsSpan = result.AsSpan(magic.Length);
        Http2FrameWriter.WriteSettingsFrame(settingsSpan, DefaultSettings);

        return result;
    }

    // ========================================================================
    // ENCODE REQUEST
    // ========================================================================
    public (int StreamId, int BytesWritten) Encode(HttpRequestMessage request, ref Memory<byte> buffer)
    {
        var streamId = AllocStreamId();
        var hasBody = request.Content is not null;

        var headers = new List<(string, string)>
        {
            (":method", request.Method.Method),
            (":path", GetFullPath(request.RequestUri!)),
            (":scheme", request.RequestUri!.Scheme),
            (":authority", request.RequestUri!.Authority),
        };

        // Request Headers
        foreach (var header in request.Headers)
        {
            var lower = header.Key.ToLowerInvariant();
            if (lower is "connection" or "keep-alive" or "transfer-encoding" or "upgrade" or "proxy-connection" or "te")
                continue;

            headers.AddRange(header.Value.Select(v => (lower, v)));
        }

        // Content Headers
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                var lower = header.Key.ToLowerInvariant();
                if (lower is "connection" or "keep-alive" or "transfer-encoding" or "upgrade")
                    continue;

                headers.AddRange(header.Value.Select(v => (lower, v)));
            }
        }

        var headerBlock = _hpack.Encode(headers);
        var span = buffer.Span;
        var bytesWritten = 0;

        bytesWritten += WriteHeadersWithContinuation(ref span, streamId, headerBlock, endStream: !hasBody);

        if (!hasBody) return (streamId, bytesWritten);
        span = buffer[bytesWritten..].Span;
        bytesWritten += WriteData(ref span, streamId, request.Content!, endStream: true);
        return (streamId, bytesWritten);
    }

    // ========================================================================
    // STATIC FRAME METHODS
    // ========================================================================
    public static byte[] EncodeSettingsAck()
    {
        var buf = new byte[9];
        Http2FrameWriter.WriteSettingsAck(buf);
        return buf;
    }

    public static byte[] EncodeSettings(ReadOnlySpan<(SettingsParameter Key, uint Value)> parameters)
    {
        var size = 9 + parameters.Length * 6;
        var buf = new byte[size];
        Http2FrameWriter.WriteSettingsFrame(buf, parameters);
        return buf;
    }

    public static byte[] EncodePing(ReadOnlySpan<byte> data)
    {
        var buf = new byte[17]; // 9 + 8
        Http2FrameWriter.WritePingFrame(buf, data, isAck: false);
        return buf;
    }

    public static byte[] EncodePingAck(ReadOnlySpan<byte> data)
    {
        var buf = new byte[17];
        Http2FrameWriter.WritePingFrame(buf, data, isAck: true);
        return buf;
    }

    public static byte[] EncodeWindowUpdate(int streamId, int increment)
    {
        var buf = new byte[13]; // 9 + 4
        Http2FrameWriter.WriteWindowUpdateFrame(buf, streamId, increment);
        return buf;
    }

    public static byte[] EncodeRstStream(int streamId, Http2ErrorCode errorCode)
    {
        var buf = new byte[13]; // 9 + 4
        Http2FrameWriter.WriteRstStreamFrame(buf, streamId, errorCode);
        return buf;
    }

    public static byte[] EncodeGoAway(int lastStreamId, Http2ErrorCode errorCode, string? debugMessage = null)
    {
        var debugData = debugMessage is not null ? Encoding.UTF8.GetBytes(debugMessage) : ReadOnlySpan<byte>.Empty;
        var buf = new byte[17 + debugData.Length]; // 9 + 8 + debug
        Http2FrameWriter.WriteGoAwayFrame(buf, lastStreamId, errorCode, debugData);
        return buf;
    }

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

    private int AllocStreamId()
    {
        var id = _nextStreamId;
        _nextStreamId += 2;
        return id;
    }

    private static string GetFullPath(Uri uri)
    {
        var path = uri.AbsolutePath;
        var query = uri.Query;
        return string.IsNullOrEmpty(query) ? path : path + query;
    }

    // ========================================================================
    // HEADERS WITH CONTINUATION
    // ========================================================================
    private int WriteHeadersWithContinuation(
        ref Span<byte> span,
        int streamId,
        ReadOnlyMemory<byte> headerBlock,
        bool endStream)
    {
        var headerSpan = headerBlock.Span;
        var bytesWritten = 0;

        var firstChunkSize = Math.Min(headerBlock.Length, _maxFrameSize);
        var firstChunk = headerSpan[..firstChunkSize];
        var endHeaders = headerBlock.Length <= _maxFrameSize;


        bytesWritten += Http2FrameWriter.WriteHeadersFrame(
            span,
            streamId,
            firstChunk,
            endStream,
            endHeaders);

        if (endHeaders) return bytesWritten;

        // CONTINUATION Frames
        var pos = firstChunkSize;
        while (pos < headerBlock.Length)
        {
            var chunkSize = Math.Min(headerBlock.Length - pos, _maxFrameSize);
            var chunk = headerSpan.Slice(pos, chunkSize);
            var isLast = pos + chunkSize >= headerBlock.Length;
            pos += chunkSize;

            span = span[bytesWritten..];
            bytesWritten += Http2FrameWriter.WriteContinuationFrame(span, streamId, chunk, isLast);
        }

        return bytesWritten;
    }

    // ========================================================================
    // DATA FRAME
    // ========================================================================
    private int WriteData(ref Span<byte> span, int streamId, HttpContent content, bool endStream)
    {
        var contentLength = content.Headers.ContentLength;

        if (contentLength == 0)
        {
            // Leerer DATA Frame
            return Http2FrameWriter.WriteDataFrame(span, streamId, ReadOnlySpan<byte>.Empty, endStream);
        }

        var stream = content.ReadAsStreamAsync().GetAwaiter().GetResult();
        var bytesWritten = 0;

        try
        {
            while (true)
            {
                var availableSpace = span.Length - bytesWritten - 9;
                if (availableSpace <= 0)
                {
                    break;
                }

                var payloadSize = Math.Min(_maxFrameSize, availableSpace);

                var payloadDestination = span.Slice(bytesWritten + 9, payloadSize);
                var read = stream.Read(payloadDestination);

                if (read == 0)
                {
                    break;
                }

                var isLast = stream.Position >= stream.Length ||
                             (contentLength.HasValue && stream.Position >= contentLength.Value);

                Http2FrameWriter.WriteDataFrameHeader(
                    span[bytesWritten..],
                    streamId,
                    read,
                    endStream && isLast);

                bytesWritten += 9 + read;
            }
        }
        finally
        {
            stream.Dispose();
        }

        return bytesWritten;
    }
}