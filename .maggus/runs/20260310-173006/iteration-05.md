# Iteration 05 — TASK-20-02: Http20DecoderStage — Frame Parsing

**Date:** 2026-03-10
**Task:** TASK-20-02
**Branch:** poc2

## Summary

Created `Http20DecoderStageRfcTests.cs` with 13 RFC-tagged tests covering all 5 acceptance criteria for Http20DecoderStage frame parsing per RFC 9113 §4.1.

## Commands Run

1. `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~Http20DecoderStageRfcTests"` — initial run failed with 4 compile errors (wrong property names for PingFrame.OpaqueData→Data, WindowUpdateFrame.windowSizeIncrement→increment)
2. Fixed property/parameter names to match actual API (PingFrame.Data, WindowUpdateFrame.Increment)
3. `dotnet test` — all 13 tests passed

## Acceptance Criteria

- [x] `20D-RFC-001`: Complete frame → correctly decoded (3 tests: HEADERS, PING, WINDOW_UPDATE)
- [x] `20D-RFC-002`: Frame split across 2 TCP segments → reassembled (3 tests: midpoint, inside header, header/payload boundary)
- [x] `20D-RFC-003`: 2 frames in one TCP segment → both decoded (2 tests: 2 frames, 3 frames)
- [x] `20D-RFC-004`: SETTINGS frame (Type 0x4) → flags and parameters correct (2 tests: full params, ACK)
- [x] `20D-RFC-005`: DATA frame → stream ID and payload correct (3 tests: normal, empty, large 1KB)

## Files Changed

- **Created:** `src/TurboHttp.StreamTests/Http20/Http20DecoderStageRfcTests.cs` (13 tests)
- **Modified:** `.maggus/plan_1.md` (marked 5 criteria as complete)
- **Created:** `COMMIT.md`

## Deviations

None. All criteria met.
