using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace TurboHttp.Protocol;

public sealed class Http2Decoder
{
    private readonly HpackDecoder _hpack = new();
    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;
    private readonly Dictionary<int, StreamState> _streams = new();

    private int _continuationStreamId;
    private byte[]? _continuationBuffer;
    private int _continuationBufferLength;
    private bool _continuationEndStream;

    public bool TryDecode(in ReadOnlyMemory<byte> incoming, out Http2DecodeResult result)
    {
        result = Http2DecodeResult.Empty;
        var responses = ImmutableList.CreateBuilder<(int StreamId, HttpResponseMessage Response)>();
        var controlFrames = ImmutableList.CreateBuilder<Http2Frame>();
        var settingsList = ImmutableList.CreateBuilder<IReadOnlyList<(SettingsParameter, uint)>>();
        var pingAcks = ImmutableList.CreateBuilder<byte[]>();
        var windowUpdates = ImmutableList.CreateBuilder<(int StreamId, int Increment)>();
        var rstStreams = ImmutableList.CreateBuilder<(int StreamId, Http2ErrorCode Error)>();
        GoAwayFrame? goAway = null;

        var working = Combine(_remainder, incoming);
        _remainder = ReadOnlyMemory<byte>.Empty;

        if (working.Length < 9)
        {
            _remainder = working;
            return false;
        }

        var decoded = false;

        while (working.Length >= 9)
        {
            var span = working.Span;
            var payloadLength = (span[0] << 16) | (span[1] << 8) | span[2];

            if (working.Length < 9 + payloadLength)
            {
                _remainder = working;
                break;
            }

            var frameType = (FrameType)span[3];
            var flags = span[4];
            var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(span[5..]) & 0x7FFFFFFFu);

            var payload = working.Slice(9, payloadLength);
            working = working[(9 + payloadLength)..];
            decoded = true;

            switch (frameType)
            {
                case FrameType.Data:
                    HandleData(payload, flags, streamId, responses);
                    break;

                case FrameType.Headers:
                    HandleHeaders(payload, flags, streamId, responses);
                    break;

                case FrameType.Continuation:
                    HandleContinuation(payload, flags, streamId, responses);
                    break;

                case FrameType.Settings:
                    if ((flags & (byte)SettingsFlags.Ack) == 0)
                    {
                        var settings = ParseSettings(payload.Span);
                        settingsList.Add(settings);
                        controlFrames.Add(new SettingsFrame(settings));
                    }

                    break;

                case FrameType.Ping:
                    if ((flags & (byte)PingFlags.Ack) != 0)
                    {
                        pingAcks.Add(payload.ToArray());
                    }
                    else
                    {
                        controlFrames.Add(new PingFrame(payload.Span.ToArray(), isAck: false));
                    }

                    break;

                case FrameType.WindowUpdate:
                    if (payload.Length >= 4)
                    {
                        var increment = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.Span) & 0x7FFFFFFFu);
                        windowUpdates.Add((streamId, increment));
                    }

                    break;

                case FrameType.RstStream:
                    if (payload.Length >= 4)
                    {
                        var error = (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(payload.Span);
                        rstStreams.Add((streamId, error));
                        _streams.Remove(streamId);
                    }

                    break;

                case FrameType.GoAway:
                    goAway = ParseGoAway(payload);
                    break;

                case FrameType.PushPromise:
                case FrameType.Priority:
                    break;
            }
        }

        if (!decoded) return false;

        result = new Http2DecodeResult(
            responses.ToImmutable(),
            controlFrames.ToImmutable(),
            settingsList.ToImmutable(),
            pingAcks.ToImmutable(),
            windowUpdates.ToImmutable(),
            rstStreams.ToImmutable(),
            goAway);
        return true;
    }

    public void Reset()
    {
        _remainder = ReadOnlyMemory<byte>.Empty;
        _streams.Clear();
        _continuationStreamId = 0;
        _continuationBuffer = null;
        _continuationBufferLength = 0;
    }

    // ========================================================================
    // FRAME HANDLERS
    // ========================================================================
    private void HandleData(
        ReadOnlyMemory<byte> payload,
        byte flags,
        int streamId,
        ImmutableList<(int, HttpResponseMessage)>.Builder responses)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            return;
        }

        var data = StripPadding(payload, flags, padded: (flags & 0x8) != 0);
        state.AppendBody(data.Span);

        if ((flags & (byte)DataFlags.EndStream) != 0)
        {
            var response = state.BuildResponse();
            _streams.Remove(streamId);
            responses.Add((streamId, response));
        }
    }

    private void HandleHeaders(
        ReadOnlyMemory<byte> payload,
        byte flags,
        int streamId,
        ImmutableList<(int, HttpResponseMessage)>.Builder responses)
    {
        var data = payload;

        if ((flags & 0x8) != 0)
        {
            data = StripPadding(data, flags, padded: true);
        }

        if ((flags & 0x20) != 0)
        {
            if (data.Length < 5) return;
            data = data[5..]; // 4B Stream Dependency + 1B Weight
        }

        if ((flags & (byte)HeadersFlags.EndHeaders) != 0)
        {
            ProcessCompleteHeaders(data.Span, flags, streamId, responses);
        }
        else
        {
            _continuationStreamId = streamId;
            _continuationBufferLength = data.Length;

            if (_continuationBuffer == null || _continuationBuffer.Length < data.Length)
            {
                if (_continuationBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(_continuationBuffer);
                }

                _continuationBuffer = ArrayPool<byte>.Shared.Rent(data.Length);
            }

            data.Span.CopyTo(_continuationBuffer);
            _continuationEndStream = (flags & (byte)HeadersFlags.EndStream) != 0;
        }
    }

    private void HandleContinuation(
        ReadOnlyMemory<byte> payload,
        byte flags,
        int streamId,
        ImmutableList<(int, HttpResponseMessage)>.Builder responses)
    {
        if (streamId != _continuationStreamId || _continuationBuffer == null)
        {
            return;
        }

        var newSize = _continuationBufferLength + payload.Length;

        if (newSize > _continuationBuffer.Length)
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            _continuationBuffer.AsSpan(0, _continuationBufferLength).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_continuationBuffer);
            _continuationBuffer = newBuffer;
        }

        payload.Span.CopyTo(_continuationBuffer.AsSpan(_continuationBufferLength));
        _continuationBufferLength = newSize;

        if ((flags & (byte)ContinuationFlags.EndHeaders) != 0)
        {
            var endStream = _continuationEndStream;
            var headerData = _continuationBuffer.AsSpan(0, _continuationBufferLength);

            ProcessCompleteHeaders(headerData, endStream ? (byte)0x1 : (byte)0x0, _continuationStreamId, responses);

            ArrayPool<byte>.Shared.Return(_continuationBuffer);
            _continuationBuffer = null;
            _continuationBufferLength = 0;
            _continuationStreamId = 0;
        }
    }

    private void ProcessCompleteHeaders(
        ReadOnlySpan<byte> headerBlock,
        byte flags,
        int streamId,
        ImmutableList<(int, HttpResponseMessage)>.Builder responses)
    {
        var decodedHeaders = _hpack.Decode(headerBlock);
        var state = new StreamState(decodedHeaders);
        var endStream = (flags & (byte)HeadersFlags.EndStream) != 0;

        if (endStream)
        {
            responses.Add((streamId, state.BuildResponse()));
        }
        else
        {
            _streams[streamId] = state;
        }
    }

    private static ReadOnlyMemory<byte> StripPadding(
        ReadOnlyMemory<byte> data,
        byte flags,
        bool padded)
    {
        if (!padded || data.IsEmpty) return data;
        var padLength = data.Span[0];
        if (1 + padLength > data.Length) return data;
        return data.Slice(1, data.Length - 1 - padLength);
    }

    private static IReadOnlyList<(SettingsParameter, uint)> ParseSettings(ReadOnlySpan<byte> payload)
    {
        var result = new List<(SettingsParameter, uint)>(payload.Length / 6);
        for (var i = 0; i + 6 <= payload.Length; i += 6)
        {
            var param = (SettingsParameter)BinaryPrimitives.ReadUInt16BigEndian(payload[i..]);
            var value = BinaryPrimitives.ReadUInt32BigEndian(payload[(i + 2)..]);
            result.Add((param, value));
        }

        return result;
    }

    private static GoAwayFrame ParseGoAway(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < 8) return new GoAwayFrame(0, Http2ErrorCode.ProtocolError);
        var lastStreamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.Span) & 0x7FFFFFFFu);
        var errorCode = (Http2ErrorCode)BinaryPrimitives.ReadUInt32BigEndian(payload.Span[4..]);
        var debugData = payload.Length > 8 ? payload[8..].ToArray() : null;
        return new GoAwayFrame(lastStreamId, errorCode, debugData);
    }

    private static ReadOnlyMemory<byte> Combine(ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;

        var merged = new byte[a.Length + b.Length];
        a.Span.CopyTo(merged);
        b.Span.CopyTo(merged.AsSpan(a.Length));
        return merged;
    }

    private sealed class StreamState(List<(string Name, string Value)> headers)
    {
        private byte[]? _bodyBuffer;
        private int _bodyLength;

        public void AppendBody(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return;

            var newSize = _bodyLength + data.Length;

            if (_bodyBuffer == null || _bodyBuffer.Length < newSize)
            {
                var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
                if (_bodyBuffer != null)
                {
                    _bodyBuffer.AsSpan(0, _bodyLength).CopyTo(newBuffer);
                    ArrayPool<byte>.Shared.Return(_bodyBuffer);
                }

                _bodyBuffer = newBuffer;
            }

            data.CopyTo(_bodyBuffer.AsSpan(_bodyLength));
            _bodyLength = newSize;
        }

        public HttpResponseMessage BuildResponse()
        {
            var statusCode = HttpStatusCode.OK;
            var response = new HttpResponseMessage();

            foreach (var (name, value) in headers)
            {
                if (name == ":status")
                {
                    if (int.TryParse(value, out var s))
                    {
                        statusCode = (HttpStatusCode)s;
                    }

                    continue;
                }

                if (name.StartsWith(':'))
                {
                    continue;
                }

                if (IsContentHeader(name))
                {
                    continue;
                }

                response.Headers.TryAddWithoutValidation(name, value);
            }

            response.StatusCode = statusCode;

            if (_bodyLength > 0 || HasContentHeaders())
            {
                var bodyBytes = new byte[_bodyLength];
                if (_bodyBuffer != null && _bodyLength > 0)
                {
                    _bodyBuffer.AsSpan(0, _bodyLength).CopyTo(bodyBytes);
                }

                response.Content = new ByteArrayContent(bodyBytes);

                foreach (var (name, value) in headers)
                {
                    if (!name.StartsWith(':') && IsContentHeader(name))
                    {
                        response.Content.Headers.TryAddWithoutValidation(name, value);
                    }
                }
            }

            if (_bodyBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_bodyBuffer);
                _bodyBuffer = null;
            }

            return response;
        }

        private bool HasContentHeaders()
        {
            return headers.Any(h => !h.Name.StartsWith(':') && IsContentHeader(h.Name));
        }

        private static bool IsContentHeader(string headerName)
        {
            return headerName.ToLowerInvariant() switch
            {
                "content-type" => true,
                "content-length" => true,
                "content-encoding" => true,
                "content-language" => true,
                "content-location" => true,
                "content-md5" => true,
                "content-range" => true,
                "content-disposition" => true,
                "expires" => true,
                "last-modified" => true,
                _ => false
            };
        }
    }
}