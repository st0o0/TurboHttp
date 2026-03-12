# Iteration 01 — TASK-001: ConnectionPoolStage — Custom GraphStage Foundation

## Task
**TASK-001**: ConnectionPoolStage — Custom GraphStage Foundation

## Summary
Implemented the `ConnectionPoolStage` as a custom `GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>` that serves as the foundation for the dynamic connection pool. All 9 acceptance criteria are met.

## Files Created
- `src/TurboHttp/IO/Stages/ConnectionPoolTypes.cs` — `RoutedTransportItem`, `RoutedDataItem`, `PoolConfig` records
- `src/TurboHttp/IO/Stages/ConnectionPoolStage.cs` — GraphStage with inner `Logic` class, `StageActor`, host registration, unknown-key rejection
- `src/TurboHttp.StreamTests/Stages/ConnectionPoolStageTests.cs` — 8 unit tests

## Files Modified
- `.maggus/plan_3.md` — marked all TASK-001 acceptance criteria as `[x]`

## Commands Run
| Command | Outcome |
|---------|---------|
| `dotnet build src/TurboHttp.sln --configuration Release` | Initial: 1 error (TcpOptions constructor), 1 warning (DefaultConfig hides). Fixed both → Build succeeded, 0 warnings, 0 errors |
| `dotnet test --filter "ConnectionPoolStage"` | 8/8 passed |
| `dotnet test src/TurboHttp.sln` | 2810 total tests. 2809 passed, 1 pre-existing failure (RFC-9113-ENG-004, confirmed failing before changes) |

## Acceptance Criteria Status
- [x] `ConnectionPoolStage` inherits from `GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>`
- [x] Inner `Logic` class with correct inlet/outlet handling (Pull/Push)
- [x] Constructor takes `Func<ConnectionStage> connectionStageFactory` and `PoolConfig`
- [x] Stage uses `StageActor` for async actor communication (same pattern as `ConnectionStage`)
- [x] ConnectItem on inlet → registered in internal state (host known, no connection yet)
- [x] DataItem on inlet without prior ConnectItem for that PoolKey → stage failure with descriptive error
- [x] Empty pass-through (only ConnectItems, no DataItems) completes without error
- [x] Unit tests for foundation (connect registers host, unknown key is rejected)
- [x] Typecheck/build passes

## Deviations
None. All criteria met.

## Notes
- `TcpOptions` uses `required` init properties, not positional constructor — tests use object initializer syntax.
- `PoolConfig` type extracted from commented-out `ConnectionDemuxStage.cs` and placed in new `ConnectionPoolTypes.cs`.
- The commented-out code in `ConnectionDemuxStage.cs` is left as-is (cleanup scheduled for TASK-012).
- Pre-existing test failure `RFC-9113-ENG-004` confirmed unrelated to this change.
