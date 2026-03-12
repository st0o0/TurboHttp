# Iteration 01 — TASK-004: Load Balancing Across Connections

## Task
TASK-004: Add configurable load balancing strategy (RoundRobin / LeastLoaded) for distributing requests across pooled connections per host.

## Changes Made

### `src/TurboHttp/IO/Stages/ConnectionPoolTypes.cs`
- Added `LoadBalancingStrategy` enum with `LeastLoaded` and `RoundRobin` values
- Extended `PoolConfig` record with `Strategy` parameter (default: `LeastLoaded`)

### `src/TurboHttp/IO/Stages/ConnectionPoolStage.cs`
- Refactored `FindOrCreateSlot` to use strategy-aware idle slot selection
- Added `SelectIdleSlot`, `SelectIdleLeastLoaded`, `SelectIdleRoundRobin` methods
- Added `RoundRobinIndex` to `HostPool` for cycling through connections
- **Bug fix**: Added `Id` to `ConnectionSlot` and included slot ID in response callback so `OnSubGraphResponse` correctly identifies which slot responded (was blindly decrementing the first busy slot)

### `src/TurboHttp.StreamTests/Stages/ConnectionPoolStageTests.cs`
- Added 9 new tests (POOL-015 through POOL-023):
  - POOL-015: Default strategy is LeastLoaded
  - POOL-016: Enum contains both values
  - POOL-017: Strategy is configurable (Theory)
  - POOL-018: RoundRobin distribution with echo flow
  - POOL-019: RoundRobin with gated busy connections
  - POOL-020: LeastLoaded with gated busy connections
  - POOL-021: Idle connections preferred (Theory for both strategies)
  - POOL-023: 6 requests across 3 connections distributed evenly (Theory)
- Added `CreateTrackingGatedFlowFactory` helper that records which connection index received each request

## Commands Run
- `dotnet build src/TurboHttp.sln --configuration Release` → 0 warnings, 0 errors
- `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj` → 2158/2158 passed
- `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` → 436/436 passed (25 pool tests)

## Acceptance Criteria
- [x] Load balancing strategy is configurable in `PoolConfig` (enum: `RoundRobin`, `LeastLoaded`)
- [x] `RoundRobin`: cycles through all active connections of a host
- [x] `LeastLoaded`: selects connection with fewest pending requests
- [x] Default strategy: `LeastLoaded`
- [x] Idle connections are preferred (don't create a new connection when one is idle)
- [x] Unit tests: multiple requests are distributed evenly across connections
- [x] Typecheck/build passes

## Deviations
None. All criteria met.
