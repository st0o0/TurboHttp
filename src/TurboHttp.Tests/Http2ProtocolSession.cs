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

    public IReadOnlyList<Http2Frame> Process(ReadOnlyMemory<byte> data) =>
        throw new NotImplementedException();

    private void Dispatch(Http2Frame frame) => throw new NotImplementedException();
    private void HandleHeaders(HeadersFrame frame) => throw new NotImplementedException();
    private void HandleContinuation(ContinuationFrame frame) => throw new NotImplementedException();
    private void HandleData(DataFrame frame) => throw new NotImplementedException();
    private void HandleSettings(SettingsFrame frame) => throw new NotImplementedException();
    private void HandlePing(PingFrame frame) => throw new NotImplementedException();
    private void HandleWindowUpdate(WindowUpdateFrame frame) => throw new NotImplementedException();
    private void HandleRst(RstStreamFrame frame) => throw new NotImplementedException();
    private void MarkClosed(int streamId) => throw new NotImplementedException();
    private static HttpResponseMessage? BuildResponse(
        IReadOnlyList<HpackHeader> headers, int streamId) => throw new NotImplementedException();
}
