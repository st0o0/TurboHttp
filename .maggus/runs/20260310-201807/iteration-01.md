# Iteration 01 — TASK-20-06: Http20ConnectionStage GOAWAY Handling

## Task
**TASK-20-06** — Http20ConnectionStage GOAWAY Handling (RFC 9113 §6.8)

## Changes Made

### Production Code
- **`src/TurboHttp/Streams/Stages/Http20ConnectionStage.cs`**: Added GOAWAY rejection logic in the `_inletRequest` onPush handler. After `_goAwayReceived` is set, any new request frame causes `FailStage(new Http2Exception(...))`.

### Test Code
- **`src/TurboHttp.StreamTests/Http20/Http20ConnectionStageGoAwayTests.cs`** (new, 3 tests):
  - `20CG-001`: Verifies `_goAwayReceived` flag is set (indirectly, by confirming subsequent request rejection)
  - `20CG-002`: Verifies GOAWAY frame is forwarded downstream with correct LastStreamId, ErrorCode, and DebugData
  - `20CG-003`: Verifies that after GOAWAY, new requests cause Http2Exception with "GOAWAY" in message

## Commands Run
1. `dotnet test --filter "FullyQualifiedName~GoAway"` → 3/3 passed
2. `dotnet test src/TurboHttp.StreamTests/` → 212/212 passed (no regressions)

## Acceptance Criteria
- [x] `20CG-001`: GOAWAY received → `_goAwayReceived` flag set
- [x] `20CG-002`: GOAWAY frame forwarded downstream
- [x] `20CG-003`: After GOAWAY → new requests rejected (on stage extension)

## Deviations
None.
