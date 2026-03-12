# Iteration 03 — TASK-003: Multi-Connection per Host — Dynamic Scaling

## Task
**TASK-003**: As a developer, I want the pool to automatically create additional ConnectionStage instances for a host when existing connections are saturated.

## Changes Made

### `src/TurboHttp/IO/Stages/ConnectionPoolStage.cs`
- Replaced `GetOrCreateSlot` (single-connection) with `FindOrCreateSlot` that:
  1. Prefers idle active connections
  2. Materialises a new slot if under `MaxConnectionsPerHost`
  3. Returns `null` when at max and all busy (triggers queuing)
- Extracted `MaterialiseNewSlot` from the old method
- Added `SendToSlot` helper for consistent in-flight tracking
- Added `DrainPendingQueue` — dispatches queued DataItems to idle slots after each response
- Added `PendingDataItems` queue to `HostPool` for internal backpressure buffering

### `src/TurboHttp.StreamTests/Stages/ConnectionPoolStageTests.cs`
- Added `CreateCountingEchoFlowFactory` — counts factory invocations
- Added `CreateGatedFlowFactory` — holds DataItems until gates are released (simulates busy connections)
- Added `CreateStage(factory, config)` overload
- Added `WaitForConditionAsync` polling helper
- **POOL-011**: 3 parallel requests → 3 connections (MaxConnections=3) — gated flow verifies 3 factory calls
- **POOL-012**: 4 requests, MaxConnections=2 → 2 connections created, 2 queued, all 4 eventually complete
- **POOL-013**: MaxConnections=1 → single connection with sequential queuing
- **POOL-014**: Verifies each sub-graph receives ConnectItem with original TcpOptions

## Commands Run
- `dotnet build src/TurboHttp.sln --configuration Release` → 0 errors, 0 warnings
- `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter ConnectionPoolStageTests` → 14/14 pass
- `dotnet test src/TurboHttp.sln` → 2817/2817 pass (2158 Tests + 425 StreamTests + 234 IntegrationTests)

## Acceptance Criteria
All 10 criteria met. No deviations or skips.

## Notes
- The slot-attribution in `OnSubGraphResponse` (decrement first slot with count > 0) is imprecise — it doesn't track which slot produced a given response. This works correctly for connection count/queue behavior but may need refinement in TASK-004 (load balancing). Not in scope for TASK-003.
