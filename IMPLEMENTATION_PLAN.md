# Implementation Plan — Http2Decoder Removal (Phases 44–62)

## Goal

Remove `Http2Decoder` (obsolete since Phase 39) and its supporting types
(`Http2DecodeResult`, `Http2StreamLifecycleState`) from the production library.
Replace every test reference with stage-based testing using
`Http2StageTestHelper` / the new `Http2ProtocolSession` helper.

**RFC compliance must not regress.** All 185 RFC 9113 tests must remain green.

---

## Migration Architecture

### Why a new helper is needed

`Http2Decoder` serves two roles in tests:

| Role | Replaced by |
|------|-------------|
| Parse raw bytes → frame objects | `Http2StageTestHelper.DecodeFrames()` (exists) |
| Stateful stream-state tracking across multiple `TryDecode` calls | **`Http2ProtocolSession`** (new, Phase 44) |

`Http2ProtocolSession` is a **test-only** lightweight state machine built on top
of the production `Http2FrameDecoder`. It provides the same accessor API as
`Http2Decoder` without duplicating the monolithic implementation.

### Replacement mapping

| Old (`Http2Decoder`) | New |
|----------------------|-----|
| `new Http2Decoder()` | `new Http2ProtocolSession()` |
| `TryDecode(bytes, out result)` | `session.Process(bytes)` → `IReadOnlyList<Http2Frame>` |
| `result.Responses[0].Response` | `Http2StageTestHelper.TryBuildResponseFromFrames(frames)` |
| `result.GoAway` / `IsGoingAway` | `session.IsGoingAway` / `session.GoAwayFrame` |
| `result.ReceivedSettings` | `session.ReceivedSettings` |
| `result.RstStreams` | `session.RstStreams` |
| `result.WindowUpdates` | `session.WindowUpdates` |
| `result.PingRequests` | `session.PingRequests` |
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

---

## Current State (after Phase 43)

### Files already clean (0 `Http2Decoder` references)
- `10_DecoderBasicFrameTests.cs`
- `18_EncoderBaselineTests.cs`
- `19_EncoderRfcTaggedTests.cs`
- `20_EncoderStreamSettingsTests.cs`
- `21_RequestEncoderFrameTests.cs`
- `Http2FrameTests.cs`
- `Http2EncoderSensitiveHeaderTests.cs`
- `Http2EncoderPseudoHeaderValidationTests.cs`

### Files needing migration

| File | Decoder refs | Priority |
|------|-------------|----------|
| `01_ConnectionPrefaceTests.cs` | 1 | Trivial |
| `02_FrameParsingTests.cs` | 2 | Trivial |
| `12_DecoderConnectionPrefaceTests.cs` | 30 | Preface group |
| `03_StreamStateMachineTests.cs` | 25 | State machine |
| `11_DecoderStreamValidationTests.cs` | 9 | State machine |
| `04_SettingsTests.cs` | 23 | Settings |
| `05_FlowControlTests.cs` | 23 | Flow control |
| `13_DecoderStreamFlowControlTests.cs` | 6 | Flow control |
| `06_HeadersTests.cs` | 29 | Headers |
| `09_ContinuationFrameTests.cs` | 25 | Headers |
| `07_ErrorHandlingTests.cs` | 20 | Errors |
| `14_DecoderErrorCodeTests.cs` | 15 | Errors |
| `08_GoAwayTests.cs` | 20 | GoAway |
| `15_RoundTripHandshakeTests.cs` | 19 | Round-trip |
| `16_RoundTripMethodTests.cs` | 12 | Round-trip |
| `17_RoundTripHpackTests.cs` | 15 | Round-trip |
| `Http2SecurityTests.cs` | 6 | Specialty |
| `Http2CrossComponentValidationTests.cs` | 21 | Specialty |
| `Http2HighConcurrencyTests.cs` | 22 | Specialty |
| `Http2MaxConcurrentStreamsTests.cs` | 46 | Specialty |
| `Http2ResourceExhaustionTests.cs` | 29 | Specialty |
| `Http2FuzzHarnessTests.cs` | 30 | Specialty |

---

## Phases

---

### Phase 44 — Create `Http2ProtocolSession` test helper
- [ ] **Status**: pending

**File to create**: `src/TurboHttp.Tests/Http2ProtocolSession.cs`

**Implementation**:
```csharp
namespace TurboHttp.Tests;

/// <summary>
/// Stateful HTTP/2 protocol session for RFC 9113 unit tests.
/// Replaces Http2Decoder in tests — wraps Http2FrameDecoder with
/// stream-state tracking, flow-control accounting, and SETTINGS parsing.
/// NOT for production use.
/// </summary>
public sealed class Http2ProtocolSession
{
    // Core decoder
    private readonly Http2FrameDecoder _frameDecoder = new();

    // Stream state: Idle / Open / HalfClosedRemote / Closed
    private readonly Dictionary<int, Http2StreamLifecycleState> _streamStates = new();
    private readonly HashSet<int> _closedStreamIds = [];

    // Accumulated results from all Process() calls
    private readonly List<(int StreamId, HttpResponseMessage Response)> _responses = new();
    private readonly List<IReadOnlyList<(SettingsParameter, uint)>> _settings = new();
    private readonly List<byte[]> _pingRequests = new();
    private readonly List<(int StreamId, Http2ErrorCode Error)> _rstStreams = new();
    private readonly List<(int StreamId, int Increment)> _windowUpdates = new();

    // Flow control
    private int _connectionReceiveWindow = 65535;
    private long _connectionSendWindow = 65535;
    private readonly Dictionary<int, long> _streamSendWindows = new();
    private readonly Dictionary<int, int> _streamReceiveWindows = new();
    private int _initialWindowSize = 65535;

    // SETTINGS
    private int _maxConcurrentStreams = int.MaxValue;

    // GoAway
    private GoAwayFrame? _goAwayFrame;

    // Counters
    private int _pingCount;
    private int _activeStreamCount;

    // HPACK decoder (shared across frames, per RFC 9113 §4.3)
    private readonly HpackDecoder _hpack = new();

    // Partial HEADERS accumulation for CONTINUATION sequences
    private int _continuationStreamId;
    private List<byte>? _continuationBuffer;
    private byte _continuationEndStreamFlags;

    // ── Properties ──────────────────────────────────────────────────────────

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

    // ── Mutators ──────────────────────────────────────────────────────────

    public void SetConnectionReceiveWindow(int value) => _connectionReceiveWindow = value;
    public void SetStreamReceiveWindow(int streamId, int value) =>
        _streamReceiveWindows[streamId] = value;

    // ── Core API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Process incoming bytes. Decodes frames, updates state, accumulates results.
    /// Throws Http2Exception on protocol violations (mirrors Http2Decoder behaviour).
    /// </summary>
    public IReadOnlyList<Http2Frame> Process(ReadOnlyMemory<byte> data)
    {
        var frames = _frameDecoder.Decode(data);
        foreach (var frame in frames)
        {
            Dispatch(frame);
        }
        return frames;
    }

    // ── Private dispatch ────────────────────────────────────────────────────

    private void Dispatch(Http2Frame frame)
    {
        switch (frame)
        {
            case HeadersFrame h:   HandleHeaders(h);      break;
            case DataFrame d:      HandleData(d);         break;
            case SettingsFrame s:  HandleSettings(s);     break;
            case PingFrame p:      HandlePing(p);         break;
            case WindowUpdateFrame w: HandleWindowUpdate(w); break;
            case RstStreamFrame r: HandleRst(r);          break;
            case GoAwayFrame g:    _goAwayFrame = g;      break;
            case ContinuationFrame c: HandleContinuation(c); break;
        }
    }

    private void HandleHeaders(HeadersFrame frame)
    {
        var streamId = frame.StreamId;
        var currentState = GetStreamState(streamId);

        // RFC 9113 §5.1: HEADERS transitions Idle → Open (or HalfClosed)
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
            // Decode HPACK block
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
            // CONTINUATION expected
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

        // Update flow-control windows
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
                    // Update all open stream send windows (RFC 9113 §6.9.2)
                    foreach (var streamId in _streamSendWindows.Keys.ToList())
                    {
                        _streamSendWindows[streamId] = value;
                    }
                    break;
                case SettingsParameter.MaxFrameSize:
                    // Recorded implicitly (test assertions use frame size directly)
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
```

**Acceptance criteria**:
- `Http2ProtocolSession` compiles with zero errors
- `Http2StageTestHelperTests.cs` passes (extend if needed)
- No changes to production code

---

### Phase 45 — Finalize 01_ and 02_ (trivial tail)
- [ ] **Status**: pending

**Files**: `01_ConnectionPrefaceTests.cs` (1 ref), `02_FrameParsingTests.cs` (2 refs)

**What to do**:
- `01_`: one lingering `new Http2Decoder()` — already only used for ValidateServerPreface;
  replace with `Http2StageTestHelper.ValidateServerPreface(bytes)` directly.
- `02_`: two `new Http2Decoder()` — both decode frames;
  replace with `Http2StageTestHelper.DecodeFrames(bytes)`.

**Acceptance criteria**:
- `grep -c Http2Decoder 01_ConnectionPrefaceTests.cs` → `0`
- `grep -c Http2Decoder 02_FrameParsingTests.cs` → `0`
- Both test classes pass with `dotnet test --filter "RFC9113"`

---

### Phase 46 — Migrate `12_DecoderConnectionPrefaceTests.cs` (30 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/RFC9113/12_DecoderConnectionPrefaceTests.cs`

**Context**: This class duplicates most of `01_ConnectionPrefaceTests.cs`
but through the `Http2Decoder` lens. After migration, consolidate unique
tests into `01_` and delete `12_` if it becomes empty.

**What to do**:
1. Replace all `new Http2Decoder()` + `ValidateServerPreface(bytes)` calls
   with `Http2StageTestHelper.ValidateServerPreface(bytes)`.
2. Replace `TryDecode(bytes, out _)` frame-decode calls with
   `Http2StageTestHelper.DecodeFrames(bytes)`.
3. Move any unique test cases not covered by `01_` into `01_`.
4. Delete `12_DecoderConnectionPrefaceTests.cs` (all tests either moved or redundant).

**Acceptance criteria**:
- File deleted or empty
- `01_ConnectionPrefaceTests.cs` has zero regressions
- `dotnet test --filter "RFC9113"` passes

---

### Phase 47 — Migrate `03_StreamStateMachineTests.cs` (25 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/RFC9113/03_StreamStateMachineTests.cs`

**Context**: Heavy use of `GetStreamLifecycleState()`, `GetActiveStreamCount()`,
`TryDecode()` in sequence to verify Idle→Open→Closed transitions.

**What to do**:
- Replace `new Http2Decoder()` → `new Http2ProtocolSession()`
- Replace `decoder.TryDecode(bytes, out _)` → `session.Process(bytes)`
- Replace `decoder.GetStreamLifecycleState(id)` → `session.GetStreamState(id)`
- Replace `decoder.GetActiveStreamCount()` → `session.ActiveStreamCount`
- Replace `decoder.Reset()` → `session = new Http2ProtocolSession()`

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All stream state machine tests pass

---

### Phase 48 — Migrate `11_DecoderStreamValidationTests.cs` (9 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/RFC9113/11_DecoderStreamValidationTests.cs`

**What to do**:
- Same pattern as Phase 47 (stream state, smaller file)
- `session.Process(bytes)` + `session.GetStreamState(id)` + `session.ActiveStreamCount`

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All tests pass

---

### Phase 49 — Migrate `04_SettingsTests.cs` (23 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/RFC9113/04_SettingsTests.cs`

**Context**: Tests verify SETTINGS parameter application —
`GetMaxConcurrentStreams()`, `GetConnectionReceiveWindow()`,
`GetConnectionSendWindow()`, initial window size propagation.

**What to do**:
- Replace `new Http2Decoder()` → `new Http2ProtocolSession()`
- `decoder.TryDecode(bytes, out var result)` → `session.Process(bytes)`
- `decoder.GetMaxConcurrentStreams()` → `session.MaxConcurrentStreams`
- `decoder.GetConnectionReceiveWindow()` → `session.ConnectionReceiveWindow`
- `decoder.GetConnectionSendWindow()` → `session.ConnectionSendWindow`
- `result.ReceivedSettings` → `session.ReceivedSettings`

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All SETTINGS tests pass

---

### Phase 50 — Migrate `05_FlowControlTests.cs` + `13_DecoderStreamFlowControlTests.cs` (29 refs total)
- [ ] **Status**: pending

**Files**:
- `src/TurboHttp.Tests/RFC9113/05_FlowControlTests.cs` (23 refs)
- `src/TurboHttp.Tests/RFC9113/13_DecoderStreamFlowControlTests.cs` (6 refs)

**Context**: Flow control windows — connection + per-stream send/receive.
Uses `GetStreamSendWindow()`, `GetStreamReceiveWindow()`,
`SetConnectionReceiveWindow()`, `GetConnectionSendWindow()`.

**What to do**:
- `new Http2Decoder()` → `new Http2ProtocolSession()`
- `decoder.TryDecode(bytes, out _)` → `session.Process(bytes)`
- `decoder.GetStreamSendWindow(id)` → `session.GetStreamSendWindow(id)`
- `decoder.GetStreamReceiveWindow(id)` → `session.GetStreamReceiveWindow(id)`
- `decoder.SetConnectionReceiveWindow(v)` → `session.SetConnectionReceiveWindow(v)`
- `decoder.GetConnectionSendWindow()` → `session.ConnectionSendWindow`

**Acceptance criteria**:
- Zero `Http2Decoder` references in both files
- All flow control tests pass

---

### Phase 51 — Migrate `06_HeadersTests.cs` + `09_ContinuationFrameTests.cs` (54 refs total)
- [ ] **Status**: pending

**Files**:
- `src/TurboHttp.Tests/RFC9113/06_HeadersTests.cs` (29 refs)
- `src/TurboHttp.Tests/RFC9113/09_ContinuationFrameTests.cs` (25 refs)

**Context**: Header block parsing and CONTINUATION frame sequences.
Tests verify pseudo-header ordering, HPACK encoding, multi-frame header blocks.
Uses `TryDecode` + `result.Responses` + stream lifecycle state.

**What to do**:
- `new Http2Decoder()` → `new Http2ProtocolSession()`
- `decoder.TryDecode(bytes, out var result)` → `var frames = session.Process(bytes)`
- `result.Responses.First(r => r.StreamId == id).Response` →
  `Http2StageTestHelper.TryBuildResponseFromFrames(frames, id)`
  OR `session.Responses.Last(r => r.StreamId == id).Response`
- `GetStreamLifecycleState(id)` → `session.GetStreamState(id)`

**Acceptance criteria**:
- Zero `Http2Decoder` references in both files
- All header and continuation tests pass

---

### Phase 52 — Migrate `07_ErrorHandlingTests.cs` + `14_DecoderErrorCodeTests.cs` (35 refs total)
- [ ] **Status**: pending

**Files**:
- `src/TurboHttp.Tests/RFC9113/07_ErrorHandlingTests.cs` (20 refs)
- `src/TurboHttp.Tests/RFC9113/14_DecoderErrorCodeTests.cs` (15 refs)

**Context**: Error code propagation — `Http2Exception` with correct
`ErrorCode` and `IsConnectionError` / `IsStreamError`. Many tests use
`Assert.Throws<Http2Exception>(() => decoder.TryDecode(...))`.

**What to do**:
- `new Http2Decoder()` → `new Http2ProtocolSession()`
- `Assert.Throws<Http2Exception>(() => decoder.TryDecode(bytes, out _))` →
  `Assert.Throws<Http2Exception>(() => session.Process(bytes))`
- Verify `Http2ProtocolSession` throws on the same protocol violations

**Note**: If `Http2ProtocolSession` does not yet throw for a specific error
(because `Http2FrameDecoder` handles it), add the validation to `Http2ProtocolSession`.

**Acceptance criteria**:
- Zero `Http2Decoder` references in both files
- All error-handling tests pass with correct `Http2ErrorCode` assertions

---

### Phase 53 — Migrate `08_GoAwayTests.cs` (20 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/RFC9113/08_GoAwayTests.cs`

**Context**: GOAWAY frame semantics — `IsGoingAway`, `GetGoAwayLastStreamId()`,
behaviour after GOAWAY received.

**What to do**:
- `new Http2Decoder()` → `new Http2ProtocolSession()`
- `decoder.TryDecode(bytes, out var result)` → `session.Process(bytes)`
- `decoder.IsGoingAway` → `session.IsGoingAway`
- `decoder.GetGoAwayLastStreamId()` → `session.GoAwayLastStreamId`
- `result.GoAway` → `session.GoAwayFrame`

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All GoAway tests pass

---

### Phase 54 — Migrate `15_RoundTripHandshakeTests.cs` (19 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/RFC9113/15_RoundTripHandshakeTests.cs`

**Context**: Round-trip tests encode a request with `Http2RequestEncoder`
then decode the response bytes with `Http2Decoder`. Pattern:
`encoder.Encode(request)` → bytes → `decoder.TryDecode(bytes, out result)`.

**What to do**:
- `new Http2Decoder()` → `new Http2ProtocolSession()`
- `decoder.TryDecode(serverBytes, out var result)` → `session.Process(serverBytes)`
- `result.Responses` → `session.Responses`
- Keep encoder path unchanged

**Acceptance criteria**:
- Zero `Http2Decoder` references in file
- All handshake round-trip tests pass

---

### Phase 55 — Migrate `16_RoundTripMethodTests.cs` + `17_RoundTripHpackTests.cs` (27 refs total)
- [ ] **Status**: pending

**Files**:
- `src/TurboHttp.Tests/RFC9113/16_RoundTripMethodTests.cs` (12 refs)
- `src/TurboHttp.Tests/RFC9113/17_RoundTripHpackTests.cs` (15 refs)

**Context**: Same round-trip pattern as Phase 54. `17_` also checks HPACK
dynamic table state via decoded header values.

**What to do**:
- Same mechanical swap as Phase 54
- For HPACK assertions: compare decoded header values from `session.Responses`

**Acceptance criteria**:
- Zero `Http2Decoder` references in both files
- All round-trip and HPACK tests pass

---

### Phase 56 — Migrate `Http2SecurityTests.cs` (6 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/RFC9113/Http2SecurityTests.cs`

**Context**: Security-focused tests: sensitive header NeverIndex,
stream ID exhaustion, malformed frame injection.

**What to do**:
- `new Http2Decoder()` → `new Http2ProtocolSession()`
- `TryDecode` → `session.Process` (6 occurrences)

**Acceptance criteria**:
- Zero `Http2Decoder` references
- All security tests pass

---

### Phase 57 — Migrate `Http2CrossComponentValidationTests.cs` (21 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/RFC9113/Http2CrossComponentValidationTests.cs`

**Context**: Cross-component tests validate encoder output is parseable
by the decoder. Mix of frame inspection + response extraction.

**What to do**:
- `new Http2Decoder()` → `new Http2ProtocolSession()`
- `decoder.TryDecode(bytes, out var result)` → `session.Process(bytes)`
- `result.Responses` → `session.Responses`
- `result.ReceivedSettings` → `session.ReceivedSettings`

**Acceptance criteria**:
- Zero `Http2Decoder` references
- All cross-component tests pass

---

### Phase 58 — Migrate `Http2HighConcurrencyTests.cs` (22 refs)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/RFC9113/Http2HighConcurrencyTests.cs`

**Context**: Multi-stream concurrency. Creates many streams, verifies
`GetActiveStreamCount()` and stream state for each. May use `Reset()` between runs.

**What to do**:
- `new Http2Decoder()` → `new Http2ProtocolSession()`
- `decoder.GetActiveStreamCount()` → `session.ActiveStreamCount`
- `decoder.GetStreamLifecycleState(id)` → `session.GetStreamState(id)`
- `decoder.Reset()` → `session = new Http2ProtocolSession()`

**Acceptance criteria**:
- Zero `Http2Decoder` references
- All high-concurrency tests pass

---

### Phase 59 — Migrate `Http2MaxConcurrentStreamsTests.cs` (46 refs — largest file)
- [ ] **Status**: pending

**File**: `src/TurboHttp.Tests/RFC9113/Http2MaxConcurrentStreamsTests.cs`

**Context**: Tests the `MAX_CONCURRENT_STREAMS` SETTINGS parameter.
46 references, 665 lines. Heavy use of `GetMaxConcurrentStreams()`,
`GetActiveStreamCount()`, `GetClosedStreamIdCount()`.

**What to do**:
- `new Http2Decoder()` → `new Http2ProtocolSession()`
- `decoder.GetMaxConcurrentStreams()` → `session.MaxConcurrentStreams`
- `decoder.GetActiveStreamCount()` → `session.ActiveStreamCount`
- `decoder.GetClosedStreamIdCount()` → `session.ClosedStreamCount`
- `decoder.TryDecode(bytes, out _)` → `session.Process(bytes)`

**Note**: Verify `Http2ProtocolSession` throws `RefusedStream` when
`MAX_CONCURRENT_STREAMS` is exceeded (it does per Phase 44 implementation).

**Acceptance criteria**:
- Zero `Http2Decoder` references
- All max-concurrent-streams tests pass including exception assertions

---

### Phase 60 — Migrate `Http2ResourceExhaustionTests.cs` + `Http2FuzzHarnessTests.cs` (59 refs total)
- [ ] **Status**: pending

**Files**:
- `src/TurboHttp.Tests/RFC9113/Http2ResourceExhaustionTests.cs` (29 refs)
- `src/TurboHttp.Tests/RFC9113/Http2FuzzHarnessTests.cs` (30 refs)

**Context**:
- `Http2ResourceExhaustionTests`: large DATA payloads, rapid stream creation,
  memory exhaustion scenarios. Uses flow control + stream count accessors.
- `Http2FuzzHarnessTests`: random/malformed input sequences. Most tests
  call `TryDecode` expecting either a result or a thrown `Http2Exception`.

**What to do**:
- Same mechanical swap as previous phases
- Fuzz tests: `Assert.Throws<Http2Exception>(() => session.Process(bytes))`
  or `session.Process(bytes)` where no exception is expected

**Acceptance criteria**:
- Zero `Http2Decoder` references in both files
- All fuzz and resource exhaustion tests pass

---

### Phase 61 — Delete `Http2Decoder.cs`, `Http2DecodeResult.cs`, `Http2StreamLifecycleState.cs`
- [ ] **Status**: pending

**Prerequisite**: All previous phases complete, zero `Http2Decoder` references
in the entire solution (confirmed by grep).

**Files to delete**:
- `src/TurboHttp/Protocol/Http2Decoder.cs`
- `src/TurboHttp/Protocol/Http2DecodeResult.cs`
- `src/TurboHttp/Protocol/Http2StreamLifecycleState.cs`

**Verification before delete**:
```bash
grep -r "Http2Decoder\|Http2DecodeResult\|Http2StreamLifecycleState" \
  src/ --include="*.cs" | grep -v "/bin/" | grep -v "/obj/"
# Must return zero lines
```

**What to do**:
1. Run verification grep above
2. Delete the three files
3. `dotnet build src/TurboHttp.sln` — must succeed with 0 errors
4. Remove `[Obsolete]` attributes that referenced Http2Decoder from any
   remaining comments or XML docs

**Acceptance criteria**:
- All three files deleted
- `dotnet build` succeeds with 0 errors, 0 warnings about Http2Decoder
- Solution compiles cleanly

---

### Phase 62 — Validation gate: full RFC9113 regression run
- [ ] **Status**: pending

**Prerequisite**: Phase 61 complete.

**What to do**:
1. Run full RFC9113 suite:
   ```bash
   dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj \
     --filter "FullyQualifiedName~RFC9113" \
     --logger "console;verbosity=normal"
   ```
2. Verify test count: must be ≥ 185 (no tests lost)
3. Verify pass rate: 100%
4. Run full solution test:
   ```bash
   dotnet test src/TurboHttp.sln
   ```
5. Update `MEMORY.md`: mark Http2Decoder removal complete

**Acceptance criteria**:
- RFC9113: ≥ 185 tests, 0 failures
- Full solution: 0 failures
- `Http2Decoder` does not appear anywhere in source

---

## Summary

| Phase | Task | Decoder refs | Effort |
|-------|------|-------------|--------|
| 44 | Create `Http2ProtocolSession` | — | 2h |
| 45 | Finalize 01_ + 02_ | 3 | 30min |
| 46 | Migrate 12_ (preface dup) | 30 | 1h |
| 47 | Migrate 03_ (state machine) | 25 | 1.5h |
| 48 | Migrate 11_ (stream validation) | 9 | 45min |
| 49 | Migrate 04_ (settings) | 23 | 1h |
| 50 | Migrate 05_ + 13_ (flow control) | 29 | 1.5h |
| 51 | Migrate 06_ + 09_ (headers) | 54 | 2h |
| 52 | Migrate 07_ + 14_ (errors) | 35 | 1.5h |
| 53 | Migrate 08_ (GoAway) | 20 | 1h |
| 54 | Migrate 15_ (round-trip handshake) | 19 | 1h |
| 55 | Migrate 16_ + 17_ (round-trip) | 27 | 1.5h |
| 56 | Migrate Http2SecurityTests | 6 | 30min |
| 57 | Migrate Http2CrossComponent | 21 | 1h |
| 58 | Migrate Http2HighConcurrency | 22 | 1h |
| 59 | Migrate Http2MaxConcurrent | 46 | 2h |
| 60 | Migrate Http2ResourceExhaustion + Http2Fuzz | 59 | 2h |
| 61 | Delete Http2Decoder + support types | — | 30min |
| 62 | Validation gate | — | 1h |
| **Total** | | **~428 refs** | **~23h** |
