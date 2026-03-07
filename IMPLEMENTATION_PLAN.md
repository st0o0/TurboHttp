# Implementation Plan — Http2Decoder Removal + Stream Stage Tests

## Goal

Remove `Http2Decoder` (obsolete since Phase 39) and its supporting types
(`Http2DecodeResult`, `Http2StreamLifecycleState`) from the production library.
Replace every test reference with stage-based testing using
`Http2StageTestHelper` / the new `Http2ProtocolSession` helper.
Then extend stream-stage test coverage for `Http20Engine`, top-level `Engine` routing,
and `HostConnectionPool` lifecycle.

**RFC compliance must not regress.** All RFC 9113 tests must remain green.

---

## Migration Architecture

### Why a new helper is needed

`Http2Decoder` serves two roles in tests:

| Role | Replaced by |
|------|-------------|
| Parse raw bytes → frame objects | `Http2StageTestHelper.DecodeFrames()` (exists) |
| Stateful stream-state tracking across multiple `TryDecode` calls | **`Http2ProtocolSession`** (new, Phase 1–2) |

`Http2ProtocolSession` is a **test-only** lightweight state machine built on top
of the production `Http2FrameDecoder`. It provides the same accessor API as
`Http2Decoder` without duplicating the monolithic implementation.

### Replacement mapping

| Old (`Http2Decoder`) | New |
|----------------------|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `TryDecode(bytes, out result)` | `session.Process(bytes)` → `IReadOnlyList<Http2Frame>` |
| `result.Responses[0].Response` | `session.Responses[0].Response` |
| `result.GoAway` / `IsGoingAway` | `session.IsGoingAway` / `session.GoAwayFrame` |
| `result.ReceivedSettings` | `session.ReceivedSettings` |
| `result.RstStreams` | `session.RstStreams` |
| `result.WindowUpdates` | `session.WindowUpdates` |
| `result.PingRequests` | `session.PingRequests` |
| `result.HasNewSettings` | `frames.OfType<SettingsFrame>().Any(f => !f.IsAck)` |
| `result.HasResponses` | `session.Responses.Count > 0` |
| `GetStreamLifecycleState(id)` | `session.GetStreamState(id)` |
| `GetActiveStreamCount()` | `session.ActiveStreamCount` |
| `GetMaxConcurrentStreams()` | `session.MaxConcurrentStreams` |
| `GetGoAwayLastStreamId()` | `session.GoAwayLastStreamId` |
| `GetConnectionReceiveWindow()` | `session.ConnectionReceiveWindow` |
| `GetConnectionSendWindow()` | `session.ConnectionSendWindow` |
| `GetStreamReceiveWindow(id)` | `session.GetStreamReceiveWindow(id)` |
| `GetStreamSendWindow(id)` | `session.GetStreamSendWindow(id)` |
| `GetPingCount()` | `session.PingCount` |
| `GetClosedStreamIdCount()` | `session.ClosedStreamCount` |
| `SetConnectionReceiveWindow(v)` | `session.SetConnectionReceiveWindow(v)` |
| `Reset()` | `new Http2ProtocolSession()` (create fresh) |
| `ValidateServerPreface(bytes)` | `Http2StageTestHelper.ValidateServerPreface(bytes)` (exists) |
| `Assert.True(decoder.TryDecode(...))` | `Assert.NotEmpty(session.Process(...))` |
| `Assert.False(decoder.TryDecode(...))` | `Assert.Empty(session.Process(...))` |

### `Http2IntegrationSession` (mirror for IntegrationTests project)

`Http2ProtocolSession` lives in `TurboHttp.Tests` and is not accessible from
`TurboHttp.IntegrationTests`. A verbatim copy named `Http2IntegrationSession` is
created in the integration project (Phase 35–36). Its public API is identical.

---

## Files needing migration

| File | Project | Decoder refs | Phase |
|------|---------|-------------|-------|
| `RFC9113/01_ConnectionPrefaceTests.cs` | Tests | 1 | 3 |
| `RFC9113/02_FrameParsingTests.cs` | Tests | 2 | 4 |
| `RFC9113/12_DecoderConnectionPrefaceTests.cs` | Tests | 30 | 5–6 |
| `RFC9113/03_StreamStateMachineTests.cs` | Tests | 25 | 7 |
| `RFC9113/11_DecoderStreamValidationTests.cs` | Tests | 9 | 8 |
| `RFC9113/04_SettingsTests.cs` | Tests | 23 | 9 |
| `RFC9113/05_FlowControlTests.cs` | Tests | 23 | 10 |
| `RFC9113/13_DecoderStreamFlowControlTests.cs` | Tests | 6 | 11 |
| `RFC9113/06_HeadersTests.cs` | Tests | 29 | 12 |
| `RFC9113/09_ContinuationFrameTests.cs` | Tests | 25 | 13 |
| `RFC9113/07_ErrorHandlingTests.cs` | Tests | 20 | 14 |
| `RFC9113/14_DecoderErrorCodeTests.cs` | Tests | 15 | 15 |
| `RFC9113/08_GoAwayTests.cs` | Tests | 20 | 16 |
| `RFC9113/15_RoundTripHandshakeTests.cs` | Tests | 19 | 17 |
| `RFC9113/16_RoundTripMethodTests.cs` | Tests | 12 | 18 |
| `RFC9113/17_RoundTripHpackTests.cs` | Tests | 15 | 19 |
| `RFC9113/Http2SecurityTests.cs` | Tests | 6 | 20 |
| `RFC9113/Http2CrossComponentValidationTests.cs` | Tests | 21 | 21 |
| `RFC9113/Http2HighConcurrencyTests.cs` | Tests | 22 | 22 |
| `RFC9113/Http2MaxConcurrentStreamsTests.cs` | Tests | 46 | 23–24 |
| `RFC9113/Http2ResourceExhaustionTests.cs` | Tests | 29 | 25 |
| `RFC9113/Http2FuzzHarnessTests.cs` | Tests | 30 | 26 |
| `RFC9110/01_ContentEncodingGzipTests.cs` | Tests | 4 | 27 |
| `RFC9110/02_ContentEncodingDeflateTests.cs` | Tests | 2 | 28 |
| `Integration/TcpFragmentationTests.cs` | Tests | 9 | 29 |
| `Shared/Http2Connection.cs` | IntegrationTests | 3 | 31–34 |
| `Http2/Http2EdgeCaseTests.cs` | IntegrationTests | 6 | 37 |
| `Http2/Http2ErrorTests.cs` | IntegrationTests | 14 | 38–39 |
| `Http2/Http2FlowControlTests.cs` | IntegrationTests | 3 | 40 |
| `Http2/Http2PushPromiseTests.cs` | IntegrationTests | 9 | 41 |

### Files already clean (0 `Http2Decoder` references)
- `RFC9113/10_DecoderBasicFrameTests.cs`
- `RFC9113/18_EncoderBaselineTests.cs`
- `RFC9113/19_EncoderRfcTaggedTests.cs`
- `RFC9113/20_EncoderStreamSettingsTests.cs`
- `RFC9113/21_RequestEncoderFrameTests.cs`
- `RFC9113/Http2FrameTests.cs`
- `RFC9113/Http2EncoderSensitiveHeaderTests.cs`
- `RFC9113/Http2EncoderPseudoHeaderValidationTests.cs`

---

## Phases

---

### Phase 0 — Baseline audit
- [x] **Status**: complete (2026-03-06, iter-01)

**Baseline recorded**:
- StreamTests:  Passed: 24,  Failed: 0  (Total: 24)
- Tests:        Passed: 2020, Failed: 44 (Total: 2064) — 44 pre-existing failures
- IntegTests:   Passed: 405,  Failed: 2  (Total: 407)  — 2 pre-existing failures
- Combined:     Passed: 2449, Failed: 46, Total: 2495
- `Http2Decoder|Http2DecodeResult|Http2StreamLifecycleState` refs: **555 occurrences across 33 files**
- `Http2ProtocolSession`: **absent** (confirmed)

**Acceptance criteria**:
- [x] Tests run; counts recorded
- [x] Decoder ref count recorded (555)
- [x] `Http2ProtocolSession` confirmed absent

---

### Phase 1 — Create `Http2ProtocolSession` skeleton
- [x] **Status**: complete (2026-03-06, iter-02)

**File to create**: `src/TurboHttp.Tests/Http2ProtocolSession.cs`

Create the class with all fields, properties, and method signatures — but
**no logic in method bodies** (throw `NotImplementedException` for now).
This lets the compiler validate that all types referenced are accessible.

```csharp
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
```

**Acceptance criteria**:
- File compiles: `dotnet build src/TurboHttp.Tests/TurboHttp.Tests.csproj` → 0 errors
- No production code modified

---

### Phase 2 — Implement `Http2ProtocolSession` full logic
- [x] **Status**: complete (2026-03-06, iter-03)

**Prerequisite**: Phase 1 complete (skeleton compiles).

Replace all `throw new NotImplementedException()` method bodies with the full
implementation shown below. This is the only phase with significant new logic.

**Full implementation**:
```csharp
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
```

**Acceptance criteria**:
- `dotnet build src/TurboHttp.Tests/TurboHttp.Tests.csproj` → 0 errors
- No `NotImplementedException` remaining in `Http2ProtocolSession.cs`
- All previously passing tests still pass

---

### Phase 3 — Migrate `01_ConnectionPrefaceTests.cs` (1 ref)
- [x] **Status**: complete (2026-03-06, iter-04)

**Prerequisite**: Phase 2 complete.

**File**: `src/TurboHttp.Tests/RFC9113/01_ConnectionPrefaceTests.cs`

**What to do**:
- Find the one `new Http2Decoder()` usage — it calls only `ValidateServerPreface(bytes)`.
- Replace with `Http2StageTestHelper.ValidateServerPreface(bytes)` directly.
- Remove the `var decoder = new Http2Decoder();` local variable.

**Acceptance criteria**:
- `grep -c Http2Decoder 01_ConnectionPrefaceTests.cs` → `0`
- `dotnet test --filter "FullyQualifiedName~RFC9113"` passes

---

### Phase 4 — Migrate `02_FrameParsingTests.cs` (2 refs)
- [x] **Status**: complete (2026-03-06, iter-05)

**File**: `src/TurboHttp.Tests/RFC9113/02_FrameParsingTests.cs`

**What to do**:
- Both refs call `TryDecode(bytes, out _)` for frame decoding with no result inspection.
- Replace with `Http2StageTestHelper.DecodeFrames(bytes)`.

**Acceptance criteria**:
- `grep -c Http2Decoder 02_FrameParsingTests.cs` → `0`
- `dotnet test --filter "FullyQualifiedName~RFC9113"` passes

---

### Phase 5 — Audit `12_DecoderConnectionPrefaceTests.cs` for unique tests
- [x] **Status**: complete (2026-03-06, iter-01)

**File**: `src/TurboHttp.Tests/RFC9113/12_DecoderConnectionPrefaceTests.cs`

**What to do**:
1. Read the file fully.
2. Compare each test case against `01_ConnectionPrefaceTests.cs`.
3. List which tests in `12_` are **unique** (not covered by `01_`) and which are **duplicates**.
4. Create a short note (comment in the plan or a scratch file) listing unique test names.

No code changes in this phase — this is a read-and-analyse step.

**Acceptance criteria**:
- List of unique tests documented (in a comment or scratch file)
- No code changed; all tests still pass

**Findings** (2026-03-06, iter-01):

Overlap with `01_` (2 tests — map to commented-out SP-002/SP-005 stubs):
- `ServerPreface_NonSettingsFrame_ThrowsProtocolError` ↔ SP-005
- `ServerPreface_IncompleteBytes_ReturnsFalse` ↔ SP-002

Unique tests (27) — must be migrated in Phase 6:

Frame-header (7540-4.1-xxx):
- `FrameHeader_Valid9Bytes_DecodedCorrectly`
- `FrameHeader_LargePayload_24BitLengthParsed`
- `FrameType_AllKnownTypes_DispatchedWithoutCrash` [Theory]
- `FrameType_Unknown0x0A_Ignored`
- `FrameHeader_RBitSetInGoAway_LastStreamIdMasked`
- `FrameHeader_RBitSetInStreamId_ThrowsProtocolError`
- `FrameHeader_PayloadExceedsMaxFrameSize_ThrowsFrameSizeError`

DATA frame (7540-6.1-xxx):
- `DataFrame_Payload_DecodedCorrectly`
- `DataFrame_EndStream_MarksStreamClosed`
- `DataFrame_Padded_PaddingStripped`
- `DataFrame_Stream0_ThrowsProtocolError`
- `DataFrame_ClosedStream_ThrowsStreamClosed`
- `DataFrame_EmptyWithEndStream_ResponseComplete`

HEADERS frame (7540-6.2-xxx):
- `HeadersFrame_ResponseHeaders_Decoded`
- `HeadersFrame_EndStream_StreamClosedImmediately`
- `HeadersFrame_EndHeaders_HeaderBlockComplete`
- `HeadersFrame_Padded_PaddingStripped`
- `HeadersFrame_PriorityFlag_ConsumedCorrectly`
- `HeadersFrame_WithoutEndHeaders_WaitsForContinuation`
- `HeadersFrame_Stream0_ThrowsProtocolError`

CONTINUATION frame (7540-6.9-xxx, dec6-cont-001):
- `ContinuationFrame_AppendedToHeaders_HeaderBlockMerged`
- `ContinuationFrame_EndHeaders_CompletesBlock`
- `ContinuationFrame_Multiple_AllMerged`
- `ContinuationFrame_WrongStream_ThrowsProtocolError`
- `ContinuationFrame_NonContinuationAfterHeaders_ThrowsProtocolError`
- `ContinuationFrame_Stream0_ThrowsProtocolError`
- `ContinuationFrame_WithoutPrecedingHeaders_ThrowsProtocolError`

---

### Phase 6 — Migrate + consolidate `12_DecoderConnectionPrefaceTests.cs` (30 refs)
- [x] **Status**: complete (2026-03-06, iter-02)

**Prerequisite**: Phase 5 (unique tests identified).

**File**: `src/TurboHttp.Tests/RFC9113/12_DecoderConnectionPrefaceTests.cs`

**What to do**:
1. Move any unique test methods (identified in Phase 5) into `01_ConnectionPrefaceTests.cs`.
2. For each moved/remaining test, replace:
   - `new Http2Decoder()` + `ValidateServerPreface(bytes)` → `Http2StageTestHelper.ValidateServerPreface(bytes)`
   - `TryDecode(bytes, out _)` → `Http2StageTestHelper.DecodeFrames(bytes)`
   - `new Http2ProtocolSession()` where stateful tracking is needed
3. Delete `12_DecoderConnectionPrefaceTests.cs`.

**Acceptance criteria**:
- `12_DecoderConnectionPrefaceTests.cs` deleted
- `01_ConnectionPrefaceTests.cs` has all unique tests
- `dotnet test --filter "FullyQualifiedName~RFC9113"` passes

---

### Phase 7 — Migrate `03_StreamStateMachineTests.cs` (25 refs)
- [x] **Status**: complete (2026-03-06, iter-03)

**File**: `src/TurboHttp.Tests/RFC9113/03_StreamStateMachineTests.cs`

**Context**: Heavy use of `GetStreamLifecycleState()`, `GetActiveStreamCount()`,
`TryDecode()` in sequence to verify Idle→Open→Closed transitions.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `decoder.TryDecode(bytes, out _)` | `session.Process(bytes)` |
| `decoder.GetStreamLifecycleState(id)` | `session.GetStreamState(id)` |
| `decoder.GetActiveStreamCount()` | `session.ActiveStreamCount` |
| `decoder.Reset()` | `session = new Http2ProtocolSession()` |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All stream state machine tests pass

---

### Phase 8 — Migrate `11_DecoderStreamValidationTests.cs` (9 refs)
- [x] **Status**: complete (2026-03-06, iter-04)

**File**: `src/TurboHttp.Tests/RFC9113/11_DecoderStreamValidationTests.cs`

**What to do**:
- Same mechanical swap as Phase 7 (smaller file).
- `session.Process(bytes)` + `session.GetStreamState(id)` + `session.ActiveStreamCount`

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All tests pass

---

### Phase 9 — Migrate `04_SettingsTests.cs` (23 refs)
- [x] **Status**: complete (2026-03-06, iter-05)

**File**: `src/TurboHttp.Tests/RFC9113/04_SettingsTests.cs`

**Context**: Tests verify SETTINGS parameter application —
`GetMaxConcurrentStreams()`, `GetConnectionReceiveWindow()`,
`GetConnectionSendWindow()`, initial window size propagation.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `decoder.TryDecode(bytes, out var result)` | `session.Process(bytes)` |
| `decoder.GetMaxConcurrentStreams()` | `session.MaxConcurrentStreams` |
| `decoder.GetConnectionReceiveWindow()` | `session.ConnectionReceiveWindow` |
| `decoder.GetConnectionSendWindow()` | `session.ConnectionSendWindow` |
| `result.ReceivedSettings` | `session.ReceivedSettings` |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All SETTINGS tests pass

---

### Phase 10 — Migrate `05_FlowControlTests.cs` (23 refs)
- [x] **Status**: complete (2026-03-06, iter-01)

**File**: `src/TurboHttp.Tests/RFC9113/05_FlowControlTests.cs`

**Context**: Flow control windows — connection + per-stream send/receive.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `decoder.TryDecode(bytes, out _)` | `session.Process(bytes)` |
| `decoder.GetStreamSendWindow(id)` | `session.GetStreamSendWindow(id)` |
| `decoder.GetStreamReceiveWindow(id)` | `session.GetStreamReceiveWindow(id)` |
| `decoder.SetConnectionReceiveWindow(v)` | `session.SetConnectionReceiveWindow(v)` |
| `decoder.GetConnectionSendWindow()` | `session.ConnectionSendWindow` |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All flow control tests pass

---

### Phase 11 — Migrate `13_DecoderStreamFlowControlTests.cs` (6 refs)
- [x] **Status**: complete (2026-03-06, iter-02)

**File**: `src/TurboHttp.Tests/RFC9113/13_DecoderStreamFlowControlTests.cs`

**What to do**:
- Same mechanical swap as Phase 10 (smaller file, same accessors).

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All tests pass

---

### Phase 12 — Migrate `06_HeadersTests.cs` (29 refs)
- [x] **Status**: complete (2026-03-06, iter-03)

**File**: `src/TurboHttp.Tests/RFC9113/06_HeadersTests.cs`

**Context**: Header block parsing. Tests verify pseudo-header ordering, HPACK encoding.
Uses `TryDecode` + `result.Responses` + stream lifecycle state.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `decoder.TryDecode(bytes, out var result)` | `session.Process(bytes)` |
| `result.Responses.First(r => r.StreamId == id).Response` | `session.Responses.Last(r => r.StreamId == id).Response` |
| `decoder.GetStreamLifecycleState(id)` | `session.GetStreamState(id)` |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All header tests pass

---

### Phase 13 — Migrate `09_ContinuationFrameTests.cs` (25 refs)
- [x] **Status**: complete (2026-03-06, iter-04)

**File**: `src/TurboHttp.Tests/RFC9113/09_ContinuationFrameTests.cs`

**Context**: Multi-frame header blocks via CONTINUATION frames.
Tests verify that HPACK decompression occurs only when `END_HEADERS` is set.

**What to do**:
- Same swap as Phase 12.
- `session.Responses` accumulates across all `Process()` calls — verify index access
  reflects the correct response for the correct stream.

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All continuation frame tests pass

---

### Phase 14 — Migrate `07_ErrorHandlingTests.cs` (20 refs)
- [x] **Status**: complete (2026-03-06, iter-05)

**File**: `src/TurboHttp.Tests/RFC9113/07_ErrorHandlingTests.cs`

**Context**: Error code propagation — `Http2Exception` with correct
`ErrorCode` and `IsConnectionError`/`IsStreamError`.
Many tests use `Assert.Throws<Http2Exception>(() => decoder.TryDecode(...))`.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `Assert.Throws<Http2Exception>(() => decoder.TryDecode(bytes, out _))` | `Assert.Throws<Http2Exception>(() => session.Process(bytes))` |

**Note**: If `Http2ProtocolSession` does not throw for a specific error
(because `Http2FrameDecoder` handles it upstream), verify the exception still
propagates through `Process()` — it should, since `Http2FrameDecoder.Decode()`
throws and `Process()` does not catch.

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All error-handling tests pass with correct `Http2ErrorCode` assertions

---

### Phase 15 — Migrate `14_DecoderErrorCodeTests.cs` (15 refs)
- [x] **Status**: complete (2026-03-06, iter-06)

**File**: `src/TurboHttp.Tests/RFC9113/14_DecoderErrorCodeTests.cs`

**What to do**:
- Same pattern as Phase 14.
- `Assert.Throws<Http2Exception>(() => session.Process(bytes))` for each error case.

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All error code tests pass

---

### Phase 16 — Migrate `08_GoAwayTests.cs` (20 refs)
- [x] **Status**: complete (2026-03-06, iter-07)

**File**: `src/TurboHttp.Tests/RFC9113/08_GoAwayTests.cs`

**Replacement mapping**:
| Old | New |
|-----|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `decoder.TryDecode(bytes, out var result)` | `session.Process(bytes)` |
| `decoder.IsGoingAway` | `session.IsGoingAway` |
| `decoder.GetGoAwayLastStreamId()` | `session.GoAwayLastStreamId` |
| `result.GoAway` | `session.GoAwayFrame` |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All GoAway tests pass

---

### Phase 17 — Migrate `15_RoundTripHandshakeTests.cs` (19 refs)
- [x] **Status**: complete (2026-03-06, iter-08)

**File**: `src/TurboHttp.Tests/RFC9113/15_RoundTripHandshakeTests.cs`

**Context**: Round-trip tests encode a request with `Http2RequestEncoder`
then decode the response bytes with `Http2Decoder`. Pattern:
`encoder.Encode(request)` → bytes → `decoder.TryDecode(bytes, out result)`.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `decoder.TryDecode(serverBytes, out var result)` | `session.Process(serverBytes)` |
| `result.Responses` | `session.Responses` |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All handshake round-trip tests pass

---

### Phase 18 — Migrate `16_RoundTripMethodTests.cs` (12 refs)
- [x] **Status**: complete (2026-03-06, iter-09)

**File**: `src/TurboHttp.Tests/RFC9113/16_RoundTripMethodTests.cs`

**What to do**:
- Same mechanical swap as Phase 17.

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All method round-trip tests pass

---

### Phase 19 — Migrate `17_RoundTripHpackTests.cs` (15 refs)
- [x] **Status**: complete (2026-03-06, iter-10)

**File**: `src/TurboHttp.Tests/RFC9113/17_RoundTripHpackTests.cs`

**Context**: Also checks HPACK dynamic table state via decoded header values.

**What to do**:
- Same mechanical swap as Phase 17.
- For HPACK assertions: compare decoded header values from `session.Responses`.

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All HPACK round-trip tests pass

---

### Phase 20 — Migrate `Http2SecurityTests.cs` (6 refs)
- [x] **Status**: complete (2026-03-06, iter-01)

**File**: `src/TurboHttp.Tests/RFC9113/Http2SecurityTests.cs`

**Context**: Security-focused tests — sensitive header NeverIndex,
stream ID exhaustion, malformed frame injection.

**What to do**:
- `new Http2Decoder()` → `new Http2ProtocolSession()`
- `TryDecode` → `session.Process` (6 occurrences)

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All security tests pass

---

### Phase 21 — Migrate `Http2CrossComponentValidationTests.cs` (21 refs)
- [x] **Status**: complete (2026-03-06, iter-02)

**File**: `src/TurboHttp.Tests/RFC9113/Http2CrossComponentValidationTests.cs`

**Context**: Cross-component tests validate encoder output is parseable
by the decoder. Mix of frame inspection + response extraction.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `decoder.TryDecode(bytes, out var result)` | `session.Process(bytes)` |
| `result.Responses` | `session.Responses` |
| `result.ReceivedSettings` | `session.ReceivedSettings` |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All cross-component tests pass

---

### Phase 22 — Migrate `Http2HighConcurrencyTests.cs` (22 refs)
- [x] **Status**: complete (2026-03-06, iter-03)

**File**: `src/TurboHttp.Tests/RFC9113/Http2HighConcurrencyTests.cs`

**Context**: Multi-stream concurrency. Creates many streams, verifies
`GetActiveStreamCount()` and stream state for each. May use `Reset()` between runs.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `decoder.GetActiveStreamCount()` | `session.ActiveStreamCount` |
| `decoder.GetStreamLifecycleState(id)` | `session.GetStreamState(id)` |
| `decoder.Reset()` | `session = new Http2ProtocolSession()` |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All high-concurrency tests pass

---

### Phase 23 — Migrate `Http2MaxConcurrentStreamsTests.cs` — first half (~23 refs)
- [x] **Status**: complete

**File**: `src/TurboHttp.Tests/RFC9113/Http2MaxConcurrentStreamsTests.cs`

**Context**: 46 references, ~665 lines. Heavy use of `GetMaxConcurrentStreams()`,
`GetActiveStreamCount()`, `GetClosedStreamIdCount()`.
Split into two phases — first half covers tests up to ~line 330.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `decoder.GetMaxConcurrentStreams()` | `session.MaxConcurrentStreams` |
| `decoder.GetActiveStreamCount()` | `session.ActiveStreamCount` |
| `decoder.GetClosedStreamIdCount()` | `session.ClosedStreamCount` |
| `decoder.TryDecode(bytes, out _)` | `session.Process(bytes)` |

**Acceptance criteria**:
- First half of file migrated (~23 refs)
- Tests in first half still pass

---

### Phase 24 — Migrate `Http2MaxConcurrentStreamsTests.cs` — second half (~23 refs)
- [x] **Status**: complete

**Prerequisite**: Phase 23 complete.

**File**: `src/TurboHttp.Tests/RFC9113/Http2MaxConcurrentStreamsTests.cs`

**What to do**:
- Migrate remaining refs (line ~330 to end).
- Verify `Http2ProtocolSession` throws `RefusedStream` when
  `MAX_CONCURRENT_STREAMS` is exceeded (implemented in Phase 2).

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All max-concurrent-streams tests pass, including exception assertions

---

### Phase 25 — Migrate `Http2ResourceExhaustionTests.cs` (29 refs)
- [x] **Status**: complete (2026-03-07, iter-01)

**File**: `src/TurboHttp.Tests/RFC9113/Http2ResourceExhaustionTests.cs`

**Context**: Large DATA payloads, rapid stream creation, memory exhaustion scenarios.
Uses flow control + stream count accessors.

**What to do**:
- Same mechanical swap as Phases 22–24.
- `session.Process(bytes)` replaces all `TryDecode` calls.

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All resource exhaustion tests pass

---

### Phase 26 — Migrate `Http2FuzzHarnessTests.cs` (30 refs)
- [x] **Status**: complete (2026-03-07, iter-02)

**File**: `src/TurboHttp.Tests/RFC9113/Http2FuzzHarnessTests.cs`

**Context**: Random/malformed input sequences. Most tests call `TryDecode` expecting
either a result or a thrown `Http2Exception`.

**What to do**:
- `Assert.Throws<Http2Exception>(() => session.Process(bytes))` where exception expected
- `session.Process(bytes)` (no assertion on return) where no exception expected

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All fuzz harness tests pass

---

### Phase 27 — Migrate `RFC9110/01_ContentEncodingGzipTests.cs` (4 refs)
- [x] **Status**: complete (2026-03-07, iter-03)

**File**: `src/TurboHttp.Tests/RFC9110/01_ContentEncodingGzipTests.cs`

**Context**: RFC9110 content-encoding tests include H2-specific sub-tests
that decode a hand-built HTTP/2 response frame to verify decompression.
`TurboHttp.Tests` can access `Http2ProtocolSession`.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `var decoder = new Http2Decoder()` | `var session = new Http2ProtocolSession()` |
| `decoder.TryDecode(responseBytes, out var result)` | `session.Process(responseBytes.AsMemory())` |
| `result.Responses[0].Response` | `session.Responses[0].Response` |
| `Assert.True(decoder.TryDecode(...))` | `Assert.NotEmpty(session.Process(...))` |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- `dotnet test --filter "RFC9110"` passes

---

### Phase 28 — Migrate `RFC9110/02_ContentEncodingDeflateTests.cs` (2 refs)
- [x] **Status**: complete (2026-03-07, iter-04)

**File**: `src/TurboHttp.Tests/RFC9110/02_ContentEncodingDeflateTests.cs`

**What to do**:
- Same swap as Phase 27 (2 refs only).

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- `dotnet test --filter "RFC9110"` passes

---

### Phase 29 — Migrate `Integration/TcpFragmentationTests.cs` (9 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/Integration/TcpFragmentationTests.cs`

**Context**: Tests incremental partial-byte decoding by feeding partial frames
in two calls. Key pattern:
```csharp
// Old
var decoder = new Http2Decoder();
Assert.False(decoder.TryDecode(partial1, out _));        // NeedMoreData
Assert.True(decoder.TryDecode(remainder, out var result));
Assert.True(result.HasNewSettings);
```

`Http2ProtocolSession.Process()` returns `IReadOnlyList<Http2Frame>`.
Empty list = partial (need more data); non-empty = at least one frame decoded.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `var decoder = new Http2Decoder()` | `var session = new Http2ProtocolSession()` |
| `Assert.False(decoder.TryDecode(buf, out _))` | `Assert.Empty(session.Process(buf))` |
| `Assert.True(decoder.TryDecode(buf, out var r))` | `var frames = session.Process(buf); Assert.NotEmpty(frames)` |
| `result.HasNewSettings` | `frames.OfType<SettingsFrame>().Any(f => !f.IsAck)` |
| `result.HasResponses` | `session.Responses.Count > 0` |

**Note**: Each test must use a **fresh** `new Http2ProtocolSession()` because
state accumulates across `Process()` calls.

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All fragmentation tests pass (all HTTP/2 fragmentation test methods)

---

### Phase 30 — Grep verification: zero `Http2Decoder` refs in `TurboHttp.Tests`
- [ ] **Status**: pending

**Prerequisite**: Phases 3–29 all complete.

**What to do**:
```bash
grep -r "Http2Decoder\|Http2DecodeResult" \
  src/TurboHttp.Tests/ --include="*.cs" | grep -v "/bin/" | grep -v "/obj/"
# Must return zero lines
```

If any refs remain, identify and fix them before proceeding.

**Acceptance criteria**:
- Zero `Http2Decoder` or `Http2DecodeResult` references in `TurboHttp.Tests`
- `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj` → 0 failures

---

### Phase 31 — Replace `Http2Decoder` field in `Http2Connection.cs`
- [ ] **Status**: pending

**File**: `src/TurboHttp.IntegrationTests/Shared/Http2Connection.cs`

**Context**: `Http2Connection` wraps a raw TCP socket for H2c integration testing.
It holds `private Http2Decoder _decoder` and uses `Http2DecodeResult` throughout.
This phase handles only the **field swap** — no logic changes yet.

**What to do**:
1. Remove `private readonly Http2Decoder _decoder = new();`
2. Add `private readonly Http2FrameDecoder _frameDecoder = new();`
3. Add `private readonly HpackDecoder _hpack = new();` (needed for Phase 33)
4. Add `private readonly Dictionary<int, int> _streamReceiveWindows = new();` (needed for Phase 34)
5. Remove the `public Http2Decoder Decoder { get; }` property (to be replaced in Phase 34)
6. Update `using` directives — remove `Http2Decoder`-specific imports, add `Http2FrameDecoder`

**Note**: After this phase the project will NOT compile (methods still reference old types).
That is expected — the remaining phases complete the refactor.

**Acceptance criteria**:
- Field replaced; `public Http2Decoder Decoder` property removed
- `using TurboHttp.Protocol;` still present (needed for `Http2FrameDecoder`)

---

### Phase 32 — Implement SETTINGS + PING dispatch in `Http2Connection.cs`
- [ ] **Status**: pending

**Prerequisite**: Phase 31.

**Context**: The old `Http2DecodeResult` provided pre-built `SettingsAcksToSend`
and `PingAcksToSend` byte arrays. The new approach decodes frames inline and
sends ACKs directly.

**What to do**:
Add a private `DispatchControlFramesAsync` method and update read loops to call it:

```csharp
private async Task DispatchControlFrameAsync(Http2Frame frame, CancellationToken ct)
{
    switch (frame)
    {
        case SettingsFrame { IsAck: false }:
            await _stream.WriteAsync(Http2FrameUtils.EncodeSettingsAck(), ct);
            break;
        case PingFrame { IsAck: false } ping:
            await _stream.WriteAsync(Http2FrameUtils.EncodePingAck(ping.Data), ct);
            break;
        case PingFrame { IsAck: true } ackPing:
            _pendingPingAcks.Enqueue(ackPing.Data);
            break;
        case WindowUpdateFrame wu:
            if (wu.StreamId == 0)
            {
                _encoder?.UpdateConnectionWindow(wu.Increment);
            }
            else
            {
                _encoder?.UpdateStreamWindow(wu.StreamId, wu.Increment);
            }
            break;
        case RstStreamFrame rst:
            throw new Http2Exception(
                $"RST_STREAM on stream {rst.StreamId}: {rst.ErrorCode}",
                rst.ErrorCode, Http2ErrorScope.Stream);
        case GoAwayFrame goAway:
            throw new Http2Exception(
                $"GOAWAY: lastStreamId={goAway.LastStreamId} error={goAway.ErrorCode}",
                goAway.ErrorCode, Http2ErrorScope.Connection);
    }
}
```

Also add `private readonly Queue<byte[]> _pendingPingAcks = new();` field.

**Acceptance criteria**:
- `DispatchControlFrameAsync` compiles
- Project builds when combined with Phase 33 changes

---

### Phase 33 — Implement HEADERS + DATA response building in `Http2Connection.cs`
- [ ] **Status**: pending

**Prerequisite**: Phase 32.

**Context**: The old code used `Http2DecodeResult.Responses` (pre-built by `Http2Decoder`).
The new approach builds `HttpResponseMessage` objects from raw `HeadersFrame` + `DataFrame`
objects using the local `HpackDecoder`.

**What to do**:
1. Add `private readonly Dictionary<int, List<byte>> _headerBlocks = new();`
   and `private readonly Dictionary<int, List<byte>> _dataBodies = new();`
2. Add `private readonly Dictionary<int, bool> _streamEndStream = new();`
3. Implement `TryBuildResponse(int streamId)` using `_hpack.Decode()` and
   `new HttpResponseMessage(status) { Content = new ByteArrayContent(body) }`.
4. Update `ReadResponseAsync` and `SendAndReceiveAsync` to:
   - Call `_frameDecoder.Decode(chunk)` instead of `_decoder.TryDecode(chunk, ...)`
   - Dispatch each frame: control frames via Phase 32 method, HEADERS/DATA via new helpers
   - Return `HttpResponseMessage` when `EndStream` is set on a DATA or HEADERS frame

**Acceptance criteria**:
- `ReadResponseAsync` and `SendAndReceiveAsync` compile without `Http2Decoder` references
- Existing integration tests that use `Http2Connection.SendAndReceiveAsync` still pass

---

### Phase 34 — Remove `ReadDecodeResultAsync`, add `GetStreamReceiveWindow`
- [ ] **Status**: pending

**Prerequisite**: Phase 33.

**Context**: `ReadDecodeResultAsync()` returns `Http2DecodeResult` directly and is
used in test code. `public Http2Decoder Decoder` is used in one test for
`conn.Decoder.GetStreamReceiveWindow(999)`.

**What to do**:
1. Delete `ReadDecodeResultAsync()` method.
   - Find all callers in test code; refactor them to use `ReadResponseAsync()` or
     `SendAndReceiveAsync()` directly.
2. Add `public int GetStreamReceiveWindow(int streamId)`:
   ```csharp
   public int GetStreamReceiveWindow(int streamId) =>
       _streamReceiveWindows.TryGetValue(streamId, out var w) ? w : 65535;
   ```
3. Update `Http2FlowControlTests.cs` line that calls `conn.Decoder.GetStreamReceiveWindow(999)`:
   ```csharp
   // Old
   var window = conn.Decoder.GetStreamReceiveWindow(999);
   // New
   var window = conn.GetStreamReceiveWindow(999);
   ```

**Acceptance criteria**:
- Zero `Http2Decoder` references in `Http2Connection.cs`
- `Http2DecodeResult` import removed from `Http2Connection.cs`
- `dotnet build src/TurboHttp.sln` passes with 0 errors
- All integration tests using `Http2Connection` still pass

---

### Phase 35 — Create `Http2IntegrationSession` skeleton
- [ ] **Status**: pending

**Prerequisite**: Phase 34 complete (Http2Connection compiles).

**File to create**: `src/TurboHttp.IntegrationTests/Shared/Http2IntegrationSession.cs`

**Context**: The integration test files (`Http2EdgeCaseTests`, `Http2ErrorTests`,
`Http2FlowControlTests`, `Http2PushPromiseTests`) create local `new Http2Decoder()`
instances for isolated protocol-assertion tests (no real TCP connection).
`Http2ProtocolSession` lives in `TurboHttp.Tests` and is not accessible from
`TurboHttp.IntegrationTests`. A minimal mirror is needed.

**What to do**:
- Copy the skeleton from Phase 1 exactly, but:
  - Rename class to `Http2IntegrationSession`
  - Namespace: `namespace TurboHttp.IntegrationTests.Shared;`
  - All method bodies throw `NotImplementedException`

**Acceptance criteria**:
- `Http2IntegrationSession` skeleton compiles in `TurboHttp.IntegrationTests`
- No production code modified

---

### Phase 36 — Implement `Http2IntegrationSession` full logic
- [ ] **Status**: pending

**Prerequisite**: Phase 35.

**What to do**:
- Copy all method bodies from `Http2ProtocolSession` (Phase 2) verbatim.
- The only difference is the class name and namespace.

**Acceptance criteria**:
- `Http2IntegrationSession` compiles with full logic
- No `NotImplementedException` remaining

---

### Phase 37 — Migrate `Http2EdgeCaseTests.cs` (6 refs)
- [ ] **Status**: pending

**Prerequisite**: Phase 36.

**File**: `src/TurboHttp.IntegrationTests/Http2/Http2EdgeCaseTests.cs`

**Replacement mapping**:
| Old | New |
|-----|-----|
| `var decoder = new Http2Decoder()` | `var session = new Http2IntegrationSession()` |
| `decoder.TryDecode(bytes, out var result)` | `var frames = session.Process(bytes.AsMemory())` |
| `Assert.True(decoder.TryDecode(...))` | `Assert.NotEmpty(session.Process(...))` |
| `result.Responses` | `session.Responses` |
| `result.ReceivedSettings` | `session.ReceivedSettings` |
| `result.GoAway` | `session.GoAwayFrame` |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All edge case tests pass

---

### Phase 38 — Migrate `Http2ErrorTests.cs` — first half (~7 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.IntegrationTests/Http2/Http2ErrorTests.cs`

**Context**: 14 total refs, ~large file. Split for tractability.
First half: tests up to approximately midpoint of the file.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `var decoder = new Http2Decoder()` | `var session = new Http2IntegrationSession()` |
| `Assert.Throws<Http2Exception>(() => decoder.TryDecode(bytes, out _))` | `Assert.Throws<Http2Exception>(() => session.Process(bytes.AsMemory()))` |

**Acceptance criteria**:
- First half migrated; first half tests pass

---

### Phase 39 — Migrate `Http2ErrorTests.cs` — second half (~7 refs)
- [ ] **Status**: pending

**Prerequisite**: Phase 38.

**What to do**:
- Migrate remaining refs in `Http2ErrorTests.cs`.
- Verify all 14 refs gone after this phase.

**Acceptance criteria**:
- Zero `Http2Decoder` references in `Http2ErrorTests.cs`
- All error tests pass

---

### Phase 40 — Migrate `Http2FlowControlTests.cs` (3 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.IntegrationTests/Http2/Http2FlowControlTests.cs`

**Context**: 3 local `Http2Decoder` refs. Also uses `conn.Decoder.GetStreamReceiveWindow(999)`
which was fixed in Phase 34.

**Replacement mapping**:
| Old | New |
|-----|-----|
| `var decoder = new Http2Decoder()` | `var session = new Http2IntegrationSession()` |
| `decoder.TryDecode(bytes, out var result)` | `session.Process(bytes.AsMemory())` |
| `result.*` | `session.*` |
| `conn.Decoder.GetStreamReceiveWindow(999)` | `conn.GetStreamReceiveWindow(999)` (Phase 34) |

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All flow control integration tests pass

---

### Phase 41 — Migrate `Http2PushPromiseTests.cs` (9 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.IntegrationTests/Http2/Http2PushPromiseTests.cs`

**What to do**:
- Same mechanical swap as Phase 37.

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All push promise tests pass

---

### Phase 42 — Grep verification: zero `Http2Decoder` refs in entire solution
- [ ] **Status**: pending

**Prerequisite**: Phases 3–41 all complete.

**What to do**:
```bash
grep -r "Http2Decoder\|Http2DecodeResult\|Http2StreamLifecycleState" \
  src/ --include="*.cs" | grep -v "/bin/" | grep -v "/obj/"
# Must return zero lines
```

If any refs remain, identify and fix them before proceeding to Phase 43.

**Acceptance criteria**:
- Zero lines returned by grep
- `dotnet build src/TurboHttp.sln` passes with 0 errors

---

### Phase 43 — Delete `Http2Decoder.cs`, `Http2DecodeResult.cs`, `Http2StreamLifecycleState.cs`
- [ ] **Status**: pending

**Prerequisite**: Phase 42 (grep confirms zero refs).

**Files to delete**:
- `src/TurboHttp/Protocol/Http2Decoder.cs`
- `src/TurboHttp/Protocol/Http2DecodeResult.cs`
- `src/TurboHttp/Protocol/Http2StreamLifecycleState.cs`

**What to do**:
1. Delete the three files.
2. `dotnet build src/TurboHttp.sln` — must succeed with 0 errors.
3. Remove any remaining `[Obsolete]` attributes in XML docs that reference `Http2Decoder`.

**Acceptance criteria**:
- All three files deleted
- `dotnet build` succeeds with 0 errors
- No warnings about `Http2Decoder`

---

### Phase 44 — Full RFC9113 regression run
- [ ] **Status**: pending

**Prerequisite**: Phase 43 complete.

**What to do**:
```bash
dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj \
  --filter "FullyQualifiedName~RFC9113" \
  --logger "console;verbosity=normal"
```

Verify test count ≥ previous count (no tests lost), 100% pass rate.

**Acceptance criteria**:
- RFC9113: ≥ 185 tests, 0 failures

---

### Phase 45 — Full solution test run (final validation gate)
- [ ] **Status**: pending

**Prerequisite**: Phase 44 complete.

**What to do**:
```bash
dotnet test src/TurboHttp.sln
```

Verify all tests pass. Update `MEMORY.md` to mark Http2Decoder removal complete.

**Acceptance criteria**:
- Full solution: 0 failures
- `Http2Decoder` does not appear anywhere in source
- `MEMORY.md` updated

---

## Stream Stage Tests — Phases 46–55

The `TurboHttp.StreamTests` project currently contains two monolithic files
(`EngineTests.cs`, `HostConnectionPoolFlowTests.cs`). Tests lack `DisplayName`
attributes and RFC/stream tags. No tests exist for `Http20Engine`, the top-level
`Engine` version demultiplexer, or `ConnectionStage`. These phases add coverage.

---

### Phase 46 — Extract `EngineTestBase.cs` from `EngineTests.cs`
- [ ] **Status**: pending

**File**: `src/TurboHttp.StreamTests/EngineTests.cs`

**What to do**:
1. Create `src/TurboHttp.StreamTests/EngineTestBase.cs` containing:
   - `EngineTestBase` abstract class (with `SendAsync`, `SendManyAsync`)
   - `SimpleMemoryOwner` helper
   - `EngineFakeConnectionStage` helper
2. Remove these types from `EngineTests.cs` (they remain only in the new file).
3. `EngineTests.cs` still contains `Http10EngineTests` and `Http11EngineTests` classes
   (moved out in Phase 47–48).

**Acceptance criteria**:
- `EngineTestBase.cs` compiles
- `dotnet test src/TurboHttp.StreamTests/` → 0 failures
- No tests lost

---

### Phase 47 — Extract `Http10EngineTests.cs` + add `DisplayName`
- [ ] **Status**: pending

**Prerequisite**: Phase 46 complete.

**What to do**:
1. Create `src/TurboHttp.StreamTests/Http10EngineTests.cs`.
2. Move all `Http10EngineTests` test methods into the new file.
3. Add `[Fact(DisplayName = "ST-10-001: description")]` to each test.
4. Remove `Http10EngineTests` class from `EngineTests.cs`.

**Acceptance criteria**:
- `Http10EngineTests.cs` compiles with all moved tests
- All `ST-10-xxx` tests pass
- `Http10EngineTests` removed from `EngineTests.cs`

---

### Phase 48 — Extract `Http11EngineTests.cs` + add `DisplayName` + delete `EngineTests.cs`
- [ ] **Status**: pending

**Prerequisite**: Phase 47 complete.

**What to do**:
1. Create `src/TurboHttp.StreamTests/Http11EngineTests.cs`.
2. Move all `Http11EngineTests` test methods into the new file.
3. Add `[Fact(DisplayName = "ST-11-001: description")]` to each test.
4. Delete `EngineTests.cs` (now empty after both classes moved out).

**Acceptance criteria**:
- `Http11EngineTests.cs` compiles with all moved tests
- `EngineTests.cs` deleted
- `dotnet test src/TurboHttp.StreamTests/` → 0 failures, same count as before split

---

### Phase 49 — Add `Http20EngineTests.cs` — first batch (ST-20-001 to ST-20-004)
- [ ] **Status**: pending

**Prerequisite**: Phase 48 complete.

**File to create**: `src/TurboHttp.StreamTests/Http20EngineTests.cs`

**Tests to add**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-20-001 | Simple GET returns 200 | `Http20Engine.CreateFlow()` produces valid request; fake H2 response decoded |
| ST-20-002 | Request encodes HPACK pseudo-headers | Raw bytes contain `:method`, `:path`, `:scheme`, `:authority` |
| ST-20-003 | POST with body sends DATA frame after HEADERS | Frame sequence: HEADERS then DATA |
| ST-20-004 | Response with body is decoded | Body bytes available after `Content.ReadAsByteArrayAsync()` |

**Fake server response pattern**: Build minimal H2 responses using
`HpackEncoder` + `HeadersFrame` + `DataFrame` byte arrays
(same pattern as existing fragmentation tests).

**Acceptance criteria**:
- All 4 tests compile and pass
- `dotnet test src/TurboHttp.StreamTests/` → 0 failures

---

### Phase 50 — Add `Http20EngineTests.cs` — second batch (ST-20-005 to ST-20-008)
- [ ] **Status**: pending

**Prerequisite**: Phase 49.

**Tests to add**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-20-005 | `Content-Encoding: gzip` response is decompressed | Decompressed body matches original |
| ST-20-006 | Multiple concurrent streams processed in order | N requests → N responses with correct stream IDs |
| ST-20-007 | SETTINGS frame from server is ACKed | Raw ACK bytes present in outbound after SETTINGS received |
| ST-20-008 | Connection preface is sent first | First 24 outbound bytes match RFC 9113 §3.4 preface |

**Acceptance criteria**:
- All 4 tests compile and pass
- `Http20EngineTests.cs` has 8 total tests (ST-20-001 to ST-20-008)

---

### Phase 51 — Add `EngineRoutingTests.cs` — basic routing (ST-ENG-001 to ST-ENG-003)
- [ ] **Status**: pending

**File to create**: `src/TurboHttp.StreamTests/EngineRoutingTests.cs`

**Context**: `Engine.cs` builds the `Partition → Http*Engine → Merge` graph.
No tests verify that requests with the correct `HttpVersion` flow to the correct
sub-engine.

**Tests to add**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-ENG-001 | HTTP/1.0 request routed to Http10Engine | `response.Version == HttpVersion.Version10` |
| ST-ENG-002 | HTTP/1.1 request routed to Http11Engine | `response.Version == HttpVersion.Version11` |
| ST-ENG-003 | HTTP/2.0 request routed to Http20Engine | `response.Version == HttpVersion.Version20` |

**Acceptance criteria**:
- 3 tests compile and pass

---

### Phase 52 — Add `EngineRoutingTests.cs` — concurrent + edge cases (ST-ENG-004 to ST-ENG-006)
- [ ] **Status**: pending

**Prerequisite**: Phase 51.

**Tests to add**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-ENG-004 | Mixed-version batch — each response version matches request | 3 requests × 3 versions → correct routing |
| ST-ENG-005 | Concurrent same-version requests — no cross-stream bleed | N concurrent → N correct responses |
| ST-ENG-006 | Unknown version request fails gracefully | Does not deadlock or hang the stream |

**Acceptance criteria**:
- All 6 routing tests pass (`EngineRoutingTests.cs` complete)
- `dotnet test src/TurboHttp.StreamTests/` → 0 failures

---

### Phase 53 — Add `HostConnectionPoolTests.cs` — concurrency limits (ST-POOL-001 to ST-POOL-003)
- [ ] **Status**: pending

**File to create**: `src/TurboHttp.StreamTests/HostConnectionPoolTests.cs`

**Context**: `HostConnectionPoolFlowTests.cs` verifies basic version routing.
Missing: pool concurrency limits, connection reuse, backpressure.

**Tests to add**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-POOL-001 | Requests up to pool limit complete without stalling | N ≤ limit requests all resolve |
| ST-POOL-002 | Request beyond pool limit is queued (backpressure) | (N+1)th request waits until a slot frees |
| ST-POOL-003 | Completed connection slot is reused for next request | Only one `FakeConnectionStage` used for sequential requests |

**Acceptance criteria**:
- 3 tests compile and pass

---

### Phase 54 — Add `HostConnectionPoolTests.cs` — connection reuse + teardown (ST-POOL-004 to ST-POOL-007)
- [ ] **Status**: pending

**Prerequisite**: Phase 53.

**Tests to add**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-POOL-004 | HTTP/1.0 connection is not reused (`Connection: close`) | Two sequential requests open two connections |
| ST-POOL-005 | HTTP/1.1 keep-alive connection is reused | Two sequential requests share one connection |
| ST-POOL-006 | Pool drains cleanly when upstream completes | No actor leak; materializer shuts down |
| ST-POOL-007 | Timeout on idle connection does not deadlock | Pool recovers after idle timeout |

**Acceptance criteria**:
- All 7 pool tests pass (`HostConnectionPoolTests.cs` complete)
- `dotnet test src/TurboHttp.StreamTests/` → 0 failures

---

### Phase 55 — Add `DisplayName` + cleanup `HostConnectionPoolFlowTests.cs`
- [ ] **Status**: pending

**Prerequisite**: Phase 54 (new pool tests in dedicated file).

**File**: `src/TurboHttp.StreamTests/HostConnectionPoolFlowTests.cs`

**What to do**:
1. Add `[Fact(DisplayName = "ST-POOL-F01: description")]` to each existing test
   (use F-prefix to distinguish from new ST-POOL-xxx tests in Phase 53–54).
2. Remove any tests that duplicate Phase 53–54 additions.
3. Convert repeated fixture calls to `[Theory] + [InlineData]` where applicable.

**Acceptance criteria**:
- All existing tests retain assertions and pass
- Each test has a `DisplayName`
- No duplicate tests between files
- `dotnet test src/TurboHttp.StreamTests/` → 0 failures

---

## Summary

### Http2Decoder Removal (Phases 0–45)

| Phase | Task | Decoder refs | Notes |
|-------|------|-------------|-------|
| 0 | Baseline audit | — | Record counts |
| 1 | `Http2ProtocolSession` skeleton | — | Compiles, no logic |
| 2 | `Http2ProtocolSession` full logic | — | All methods implemented |
| 3 | `01_ConnectionPrefaceTests.cs` | 1 | Trivial |
| 4 | `02_FrameParsingTests.cs` | 2 | Trivial |
| 5 | Audit `12_DecoderConnectionPrefaceTests.cs` | — | Read-only |
| 6 | Migrate + delete `12_` | 30 | Consolidate into `01_` |
| 7 | `03_StreamStateMachineTests.cs` | 25 | |
| 8 | `11_DecoderStreamValidationTests.cs` | 9 | |
| 9 | `04_SettingsTests.cs` | 23 | |
| 10 | `05_FlowControlTests.cs` | 23 | |
| 11 | `13_DecoderStreamFlowControlTests.cs` | 6 | |
| 12 | `06_HeadersTests.cs` | 29 | |
| 13 | `09_ContinuationFrameTests.cs` | 25 | |
| 14 | `07_ErrorHandlingTests.cs` | 20 | |
| 15 | `14_DecoderErrorCodeTests.cs` | 15 | |
| 16 | `08_GoAwayTests.cs` | 20 | |
| 17 | `15_RoundTripHandshakeTests.cs` | 19 | |
| 18 | `16_RoundTripMethodTests.cs` | 12 | |
| 19 | `17_RoundTripHpackTests.cs` | 15 | |
| 20 | `Http2SecurityTests.cs` | 6 | |
| 21 | `Http2CrossComponentValidationTests.cs` | 21 | |
| 22 | `Http2HighConcurrencyTests.cs` | 22 | |
| 23 | `Http2MaxConcurrentStreamsTests.cs` (½) | ~23 | |
| 24 | `Http2MaxConcurrentStreamsTests.cs` (½) | ~23 | |
| 25 | `Http2ResourceExhaustionTests.cs` | 29 | |
| 26 | `Http2FuzzHarnessTests.cs` | 30 | |
| 27 | `RFC9110/01_ContentEncodingGzipTests.cs` | 4 | |
| 28 | `RFC9110/02_ContentEncodingDeflateTests.cs` | 2 | |
| 29 | `Integration/TcpFragmentationTests.cs` | 9 | |
| 30 | Grep check: `TurboHttp.Tests` clean | — | Verify |
| 31 | `Http2Connection.cs` — field swap | 3 | Project won't compile yet |
| 32 | `Http2Connection.cs` — SETTINGS + PING dispatch | — | |
| 33 | `Http2Connection.cs` — HEADERS + DATA response building | — | |
| 34 | `Http2Connection.cs` — remove `ReadDecodeResultAsync` | — | Add `GetStreamReceiveWindow` |
| 35 | `Http2IntegrationSession` skeleton | — | |
| 36 | `Http2IntegrationSession` full logic | — | |
| 37 | `Http2EdgeCaseTests.cs` | 6 | |
| 38 | `Http2ErrorTests.cs` (½) | ~7 | |
| 39 | `Http2ErrorTests.cs` (½) | ~7 | |
| 40 | `Http2FlowControlTests.cs` | 3 | |
| 41 | `Http2PushPromiseTests.cs` | 9 | |
| 42 | Grep check: entire solution clean | — | Verify |
| 43 | Delete `Http2Decoder.cs` + support types | — | |
| 44 | RFC9113 regression (≥ 185 tests) | — | |
| 45 | Full solution test + MEMORY.md update | — | Final gate |
| **Total** | | **~477 refs** | |

### Stream Stage Tests (Phases 46–55)

| Phase | Task | New Tests | Notes |
|-------|------|-----------|-------|
| 46 | Extract `EngineTestBase.cs` | 0 | Infrastructure only |
| 47 | Extract `Http10EngineTests.cs` + DisplayName | 0 | Move + label |
| 48 | Extract `Http11EngineTests.cs` + delete original | 0 | Move + label |
| 49 | `Http20EngineTests.cs` batch 1 (ST-20-001..004) | 4 | |
| 50 | `Http20EngineTests.cs` batch 2 (ST-20-005..008) | 4 | |
| 51 | `EngineRoutingTests.cs` basic routing (ST-ENG-001..003) | 3 | |
| 52 | `EngineRoutingTests.cs` concurrent + edge (ST-ENG-004..006) | 3 | |
| 53 | `HostConnectionPoolTests.cs` limits (ST-POOL-001..003) | 3 | |
| 54 | `HostConnectionPoolTests.cs` reuse + teardown (ST-POOL-004..007) | 4 | |
| 55 | `HostConnectionPoolFlowTests.cs` DisplayName + cleanup | 0 | |
| **Total** | | **~21 new tests** | |
