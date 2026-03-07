using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Stateful HTTP/2 protocol session for integration tests.
/// Mirrors Http2ProtocolSession from TurboHttp.Tests — wraps Http2FrameDecoder with
/// stream-state tracking, flow-control accounting, and SETTINGS parsing.
/// NOT for production use.
/// </summary>
public sealed class Http2IntegrationSession
{
    private readonly Http2FrameDecoder _frameDecoder = new();
    private readonly Dictionary<int, Http2StreamLifecycleState> _streamStates = new();
    private readonly HashSet<int> _closedStreamIds = [];
    private readonly List<(int StreamId, HttpResponseMessage Response)> _responses = new();
    private readonly List<IReadOnlyList<(SettingsParameter, uint)>> _settings = new();
    private readonly List<byte[]> _pingRequests = new();
    private readonly List<(int StreamId, Http2ErrorCode Error)> _rstStreams = new();
    private readonly List<(int StreamId, int Increment)> _windowUpdates = new();
    private readonly HashSet<int> _promisedStreamIds = [];
    private readonly Dictionary<int, (HttpResponseMessage Response, List<byte> Body)> _pendingResponses = new();
    private int _connectionReceiveWindow = 65535;
    private long _connectionSendWindow = 65535;
    private readonly Dictionary<int, long> _streamSendWindows = new();
    private readonly Dictionary<int, int> _streamReceiveWindows = new();
    private int _initialWindowSize = 65535;
    private int _maxConcurrentStreams = int.MaxValue;
    private int _maxFrameSize = 16384;
    private GoAwayFrame? _goAwayFrame;
    private int _pingCount;
    private int _activeStreamCount;
    private readonly HpackDecoder _hpack = new();
    private int _continuationStreamId;
    private List<byte>? _continuationBuffer;
    private byte _continuationEndStreamFlags;
    private int _continuationCount;
    private int _rstStreamCount;
    private int _settingsCount;
    private int _emptyDataFrameCount;
    private bool _hasNewSettings;
    private readonly List<byte[]> _settingsAcksToSend = new();

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
    public IReadOnlyCollection<int> PromisedStreamIds => _promisedStreamIds;
    public bool HasNewSettings => _hasNewSettings;
    public IReadOnlyList<byte[]> SettingsAcksToSend => _settingsAcksToSend;

    public Http2StreamLifecycleState GetStreamState(int streamId) =>
        _streamStates.GetValueOrDefault(streamId, Http2StreamLifecycleState.Idle);

    public int GetStreamReceiveWindow(int streamId) =>
        _streamReceiveWindows.GetValueOrDefault(streamId, _initialWindowSize);

    public long GetStreamSendWindow(int streamId) =>
        _streamSendWindows.GetValueOrDefault(streamId, _initialWindowSize);

    public void SetConnectionReceiveWindow(int value) => _connectionReceiveWindow = value;

    public void SetStreamReceiveWindow(int streamId, int value) =>
        _streamReceiveWindows[streamId] = value;

    public IReadOnlyList<Http2Frame> Process(ReadOnlyMemory<byte> data) =>
        throw new NotImplementedException();

    public void Reset() => throw new NotImplementedException();

    private void Dispatch(Http2Frame frame) => throw new NotImplementedException();
    private void HandleHeaders(HeadersFrame frame) => throw new NotImplementedException();
    private void HandleContinuation(ContinuationFrame frame) => throw new NotImplementedException();
    private void HandleData(DataFrame frame) => throw new NotImplementedException();
    private void HandleSettings(SettingsFrame frame) => throw new NotImplementedException();
    private void ApplySettingsParameters(IReadOnlyList<(SettingsParameter, uint)> parameters) =>
        throw new NotImplementedException();
    private void HandlePing(PingFrame frame) => throw new NotImplementedException();
    private void HandleWindowUpdate(WindowUpdateFrame frame) => throw new NotImplementedException();
    private void HandleRst(RstStreamFrame frame) => throw new NotImplementedException();
    private void HandleGoAway(GoAwayFrame frame) => throw new NotImplementedException();
    private void HandlePushPromise(PushPromiseFrame frame) => throw new NotImplementedException();
    private void MarkClosed(int streamId) => throw new NotImplementedException();
    private static void ValidateHeaders(IReadOnlyList<HpackHeader> headers) =>
        throw new NotImplementedException();
    private static bool IsRequestPseudoHeader(string name) =>
        throw new NotImplementedException();
    private static bool IsForbiddenConnectionHeader(string name) =>
        throw new NotImplementedException();
    private static HttpResponseMessage? BuildResponse(
        IReadOnlyList<HpackHeader> headers, int streamId) =>
        throw new NotImplementedException();
    private static bool IsContentHeader(string name) =>
        throw new NotImplementedException();
}
