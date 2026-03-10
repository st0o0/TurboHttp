# Iteration 02 — TASK-10-04: Http10 Stage Round-Trip — Headers & Body

## Task
**ID:** TASK-10-04
**Title:** Http10 Stage Round-Trip — Headers & Body

## Commands Run
1. `dotnet build src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` — Build succeeded, 0 errors.
2. `dotnet test ... --filter "FullyQualifiedName~Http10StageRoundTripHeaderBodyTests"` — 4 passed, 1 failed (header name casing).
3. Fixed B-004 assertion to use case-insensitive header name check (HttpRequestMessage normalizes `X-Request-Id` → `X-Request-ID`).
4. `dotnet test ... --filter "FullyQualifiedName~Http10StageRoundTripHeaderBodyTests"` — 5/5 passed.

## Acceptance Criteria
- [x] `10RT-B-001`: Empty body → Content-Length: 0
- [x] `10RT-B-002`: Large body (64 KB) → correctly serialized and deserialized
- [x] `10RT-B-003`: Binary body (bytes 0x00–0xFF) → byte-for-byte identical
- [x] `10RT-B-004`: Custom headers in request → present in wire format
- [x] `10RT-B-005`: Response with multiple headers → all correctly parsed

## Files Changed
- **Created:** `src/TurboHttp.StreamTests/Http10/Http10StageRoundTripHeaderBodyTests.cs` — 5 tests
- **Updated:** `.maggus/plan_1.md` — marked 10RT-B-001..005 as complete

## Deviations
- B-004: HttpRequestMessage normalizes header names (e.g. `X-Request-Id` → `X-Request-ID`). Assertions use case-insensitive comparison on the wire format to accommodate .NET's header normalization.
