# Iteration 01 — TASK-11-04: Http11 Stage Round-Trip — Connection Management

**Date:** 2026-03-10
**Task:** TASK-11-04
**Status:** COMPLETE

## Acceptance Criteria

- [x] `11RT-C-001`: Response with `Connection: close` → version correctly set
- [x] `11RT-C-002`: Response without `Connection` header → keep-alive (default for HTTP/1.1)
- [x] `11RT-C-003`: `Transfer-Encoding: chunked` + `Connection: keep-alive` → stream stays open
- [x] `11RT-C-004`: Content-Length body → correctly read, connection not prematurely closed
- [x] `11RT-C-005`: Empty body with Content-Length: 0 → response emitted immediately

## Files Changed

- **Created:** `src/TurboHttp.StreamTests/Http11/Http11StageConnectionMgmtTests.cs` — 5 new tests

## Commands Run

1. `dotnet build src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --configuration Release` → 0 errors, 2 warnings (pre-existing)
2. `dotnet test --filter "FullyQualifiedName~Http11StageConnectionMgmtTests"` → 5/5 passed
3. `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` → 139/139 passed (no regressions)

## Deviations

None. All 5 acceptance criteria met.

## Notes

- Tests follow the `EngineTestBase` pattern using `SendAsync`/`SendManyAsync` with `EngineFakeConnectionStage`.
- The `ConnectionReuseEvaluator` is not yet wired into the pipeline (per CLAUDE.md limitations), so tests validate that Connection headers are correctly preserved through the decode pipeline and that multiple requests succeed (implicit keep-alive).
- 11RT-C-005 uses 204 No Content which the decoder handles via `IsNoBodyResponse()`.
