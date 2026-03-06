using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

/// <summary>
/// Stateful HTTP/2 protocol session for RFC 9113 unit tests.
/// Replaces Http2Decoder in tests — wraps Http2FrameDecoder with
/// stream-state tracking, flow-control accounting, and SETTINGS parsing.
/// NOT for production use.
/// </summary>
public sealed class Http2ProtocolSession
{
    private readonly Http2FrameDecoder _frameDecoder = new();
    private readonly Dictionary<int, Http2StreamLifecycleState> _streamStates = new();
    private readonly HashSet<int> _closedStreamIds = [];
    private readonly List<(int StreamId, HttpResponseMessage Response)> _responses = new();
    private readonly List<IReadOnlyList<(SettingsParameter, uint)>> _settings = new();
    private readonly List<byte[]> _pingRequests = new();
    private readonly List<(int StreamId, Http2ErrorCode Error)> _rstStreams = new();
    private readonly List<(int StreamId, int Increment)> _windowUpdates = new();
    private int _connectionReceiveWindow = 65535;
    private long _connectionSendWindow = 65535;
    private readonly Dictionary<int, long> _streamSendWindows = new();
    private readonly Dictionary<int, int> _streamReceiveWindows = new();
    private int _initialWindowSize = 65535;
    private int _maxConcurrentStreams = int.MaxValue;
    private GoAwayFrame? _goAwayFrame;
    private int _pingCount;
    private int _activeStreamCount;
    private readonly HpackDecoder _hpack = new();
    private int _continuationStreamId;
    private List<byte>? _continuationBuffer;
    private byte _continuationEndStreamFlags;

    public bool IsGoingAway => _goAwayFrame is not null;
    public GoAwayFrame? GoAwayFrame => _goAwayFrame;
    public int GoAwayLastStreamId => _goAwayFrame?.LastStreamId ?? int.MaxValue;
    public int ActiveStreamCount => _activeStreamCount;
    public int MaxConcurrentStreams => _maxConcurrentStreams;
    public int ConnectionReceiveWindow => _connectionReceiveWindow;
    public long ConnectionSendWindow => _connectionSendWindow;
    public int PingCount => _pingCount;
    public int ClosedStreamCount => _closedStreamIds.Count;

    public IReadOnlyList<(int StreamId, HttpResponseMessage Response)> Responses => _responses;
    public IReadOnlyList<IReadOnlyList<(SettingsParameter, uint)>> ReceivedSettings => _settings;
    public IReadOnlyList<byte[]> PingRequests => _pingRequests;
    public IReadOnlyList<(int StreamId, Http2ErrorCode Error)> RstStreams => _rstStreams;
    public IReadOnlyList<(int StreamId, int Increment)> WindowUpdates => _windowUpdates;

    public Http2StreamLifecycleState GetStreamState(int streamId) =>
        _streamStates.TryGetValue(streamId, out var s) ? s : Http2StreamLifecycleState.Idle;

    public int GetStreamReceiveWindow(int streamId) =>
        _streamReceiveWindows.TryGetValue(streamId, out var w) ? w : _initialWindowSize;

    public long GetStreamSendWindow(int streamId) =>
        _streamSendWindows.TryGetValue(streamId, out var w) ? w : _initialWindowSize;

    public void SetConnectionReceiveWindow(int value) => _connectionReceiveWindow = value;
    public void SetStreamReceiveWindow(int streamId, int value) =>
        _streamReceiveWindows[streamId] = value;

    public IReadOnlyList<Http2Frame> Process(ReadOnlyMemory<byte> data)
    {
        var frames = _frameDecoder.Decode(data);
        foreach (var frame in frames)
        {
            Dispatch(frame);
        }
        return frames;
    }

    private void Dispatch(Http2Frame frame)
    {
        // RFC 7540 §6.10: After HEADERS without END_HEADERS, only CONTINUATION is allowed.
        if (_continuationBuffer != null && frame is not ContinuationFrame)
        {
            throw new Http2Exception(
                $"Expected CONTINUATION frame but received {frame.GetType().Name}",
                Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
        }

        switch (frame)
        {
            case HeadersFrame h:      HandleHeaders(h);       break;
            case DataFrame d:         HandleData(d);          break;
            case SettingsFrame s:     HandleSettings(s);      break;
            case PingFrame p:         HandlePing(p);          break;
            case WindowUpdateFrame w: HandleWindowUpdate(w);  break;
            case RstStreamFrame r:    HandleRst(r);           break;
            case GoAwayFrame g:       _goAwayFrame = g;       break;
            case ContinuationFrame c: HandleContinuation(c);  break;
        }
    }

    private void HandleHeaders(HeadersFrame frame)
    {
        var streamId = frame.StreamId;
        var currentState = GetStreamState(streamId);

        if (currentState == Http2StreamLifecycleState.Idle)
        {
            if (_maxConcurrentStreams != int.MaxValue &&
                _activeStreamCount >= _maxConcurrentStreams)
            {
                throw new Http2Exception(
                    $"MAX_CONCURRENT_STREAMS ({_maxConcurrentStreams}) exceeded",
                    Http2ErrorCode.RefusedStream, Http2ErrorScope.Stream);
            }
            _activeStreamCount++;
            _streamSendWindows[streamId] = _initialWindowSize;
            _streamReceiveWindows[streamId] = _initialWindowSize;
        }
        else if (currentState == Http2StreamLifecycleState.Closed)
        {
            throw new Http2Exception(
                $"HEADERS on closed stream {streamId}",
                Http2ErrorCode.StreamClosed, Http2ErrorScope.Stream);
        }

        if (frame.EndHeaders)
        {
            var headers = _hpack.Decode(frame.HeaderBlockFragment.Span);
            var response = BuildResponse(headers, streamId);
            if (response != null)
            {
                _responses.Add((streamId, response));
            }

            var newState = frame.EndStream
                ? Http2StreamLifecycleState.Closed
                : Http2StreamLifecycleState.Open;

            if (newState == Http2StreamLifecycleState.Closed)
            {
                MarkClosed(streamId);
            }
            else
            {
                _streamStates[streamId] = newState;
            }
        }
        else
        {
            _continuationStreamId = streamId;
            _continuationBuffer = new List<byte>(frame.HeaderBlockFragment.ToArray());
            _continuationEndStreamFlags = frame.EndStream ? (byte)1 : (byte)0;
            _streamStates[streamId] = Http2StreamLifecycleState.Open;
        }
    }

    private void HandleContinuation(ContinuationFrame frame)
    {
        if (_continuationBuffer == null || frame.StreamId != _continuationStreamId)
        {
            throw new Http2Exception("Unexpected CONTINUATION frame",
                Http2ErrorCode.ProtocolError, Http2ErrorScope.Connection);
        }

        _continuationBuffer.AddRange(frame.HeaderBlockFragment.ToArray());

        if (frame.EndHeaders)
        {
            var block = _continuationBuffer.ToArray().AsMemory();
            var headers = _hpack.Decode(block.Span);
            var response = BuildResponse(headers, _continuationStreamId);
            if (response != null)
            {
                _responses.Add((_continuationStreamId, response));
            }

            var endStream = _continuationEndStreamFlags != 0;
            if (endStream)
            {
                MarkClosed(_continuationStreamId);
            }

            _continuationBuffer = null;
            _continuationStreamId = 0;
        }
    }

    private void HandleData(DataFrame frame)
    {
        var streamId = frame.StreamId;
        var state = GetStreamState(streamId);

        if (state == Http2StreamLifecycleState.Idle)
        {
            throw new Http2Exception($"DATA on idle stream {streamId}",
                Http2ErrorCode.StreamClosed, Http2ErrorScope.Connection);
        }

        _connectionReceiveWindow -= frame.Data.Length;
        if (_streamReceiveWindows.TryGetValue(streamId, out var sw))
        {
            _streamReceiveWindows[streamId] = sw - frame.Data.Length;
        }

        if (frame.EndStream && state == Http2StreamLifecycleState.Open)
        {
            MarkClosed(streamId);
        }
    }

    private void HandleSettings(SettingsFrame frame)
    {
        if (frame.IsAck)
        {
            return;
        }

        var parameters = frame.Parameters;
        _settings.Add(parameters);

        foreach (var (param, value) in parameters)
        {
            switch (param)
            {
                case SettingsParameter.MaxConcurrentStreams:
                    _maxConcurrentStreams = (int)value;
                    break;
                case SettingsParameter.InitialWindowSize:
                    _initialWindowSize = (int)value;
                    foreach (var sid in _streamSendWindows.Keys.ToList())
                    {
                        _streamSendWindows[sid] = value;
                    }
                    break;
                case SettingsParameter.MaxFrameSize:
                    break;
            }
        }
    }

    private void HandlePing(PingFrame frame)
    {
        if (!frame.IsAck)
        {
            _pingCount++;
            _pingRequests.Add(frame.Data);
        }
    }

    private void HandleWindowUpdate(WindowUpdateFrame frame)
    {
        var streamId = frame.StreamId;
        if (streamId == 0)
        {
            _connectionSendWindow += frame.Increment;
        }
        else
        {
            _windowUpdates.Add((streamId, frame.Increment));
            if (_streamSendWindows.TryGetValue(streamId, out var w))
            {
                _streamSendWindows[streamId] = w + frame.Increment;
            }
        }
    }

    private void HandleRst(RstStreamFrame frame)
    {
        _rstStreams.Add((frame.StreamId, frame.ErrorCode));
        MarkClosed(frame.StreamId);
    }

    private void MarkClosed(int streamId)
    {
        _streamStates[streamId] = Http2StreamLifecycleState.Closed;
        _closedStreamIds.Add(streamId);
        _activeStreamCount = Math.Max(0, _activeStreamCount - 1);
    }

    private static HttpResponseMessage? BuildResponse(
        IReadOnlyList<HpackHeader> headers, int streamId)
    {
        var status = headers.FirstOrDefault(h => h.Name == ":status");
        if (status == default)
        {
            return null;
        }

        var response = new HttpResponseMessage((HttpStatusCode)int.Parse(status.Value));
        foreach (var h in headers.Where(h => !h.Name.StartsWith(':')))
        {
            response.Headers.TryAddWithoutValidation(h.Name, h.Value);
        }
        return response;
    }
}
