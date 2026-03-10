# Iteration 01 — TASK-20-07: Http20ConnectionStage — Flow Control (WINDOW_UPDATE)

## Task
**ID:** TASK-20-07
**Title:** Http20ConnectionStage — Flow Control (WINDOW_UPDATE)
**File created:** `src/TurboHttp.StreamTests/Http20/Http20ConnectionStageFlowControlTests.cs`

## Commands Run

1. `dotnet build src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` — 0 errors, 1 warning (pre-existing)
2. `dotnet test --filter FlowControl` — 13/15 passed, 2 timeout failures
3. Fixed timeout issues: replaced `Sink.Seq` with `Sink.First` for outbound tests where stream never completes
4. `dotnet test --filter FlowControl` — 15/15 passed
5. `dotnet test src/TurboHttp.StreamTests/` — 227/227 passed (all stream tests)
6. `dotnet test src/TurboHttp.Tests/` — 2152/2152 passed (all unit tests)

## Acceptance Criteria

- [x] `20CW-001`: Inbound DATA → connection window decremented (2 tests)
- [x] `20CW-002`: Inbound DATA → stream window decremented (2 tests)
- [x] `20CW-003`: Inbound DATA → WINDOW_UPDATE(stream=0) sent (1 test)
- [x] `20CW-004`: Inbound DATA → WINDOW_UPDATE(stream=N) sent (2 tests)
- [x] `20CW-005`: Connection window < 0 → stage fails with exception (1 test)
- [x] `20CW-006`: Stream window < 0 → stage fails with exception (1 test)
- [x] `20CW-007`: Outbound DATA → connection window decremented (2 tests)
- [x] `20CW-008`: WINDOW_UPDATE(stream=0) received → connection window incremented (2 tests)
- [x] `20CW-009`: WINDOW_UPDATE(stream=N) received → stream window incremented (2 tests)

**Total:** 15 new tests, all passing.

## Deviations
None. All 9 acceptance criteria met.

## Notes
- Outbound tests required `Sink.First` instead of `Sink.Seq` because with `Source.Never` on the server side, the BidiShape stage never completes, causing `Sink.Seq` to timeout.
- The `RunWithRequestsAsync` helper was not used for 20CW-008 (connection window increment) — a custom graph with `Sink.First` was used instead for the same timeout reason.
