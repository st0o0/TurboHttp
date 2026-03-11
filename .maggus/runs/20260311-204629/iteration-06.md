# Iteration 06 — TASK-025: Http11Engine Chunked Transfer Integration Tests

## Task
TASK-025: Http11Engine Chunked Transfer Integration Tests

## Commands Run
1. `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj --filter "FullyQualifiedName~Http11ChunkedTests"` — **8/8 passed**

## Changes Made
- Created `src/TurboHttp.IntegrationTests/Http11/02_Http11ChunkedTests.cs` with 8 tests:
  - 11E-CHUNK-001: GET /chunked/1 returns decoded chunked body (1KB)
  - 11E-CHUNK-002: GET /chunked/exact/5/100 reassembles multiple chunks (5×100B)
  - 11E-CHUNK-003: POST /echo/chunked echoes request body as chunked response
  - 11E-CHUNK-004: GET /chunked/exact/1/0 handles zero-length chunk body
  - 11E-CHUNK-005: GET /chunked/trailer returns body with trailer header
  - 11E-CHUNK-006: GET /chunked/100 returns 100KB chunked body
  - 11E-CHUNK-007: HEAD /chunked/1 returns no body for chunked endpoint
  - 11E-CHUNK-008: GET /chunked/md5 returns chunked body with Content-MD5 header

## Acceptance Criteria
- [x] File created: `src/TurboHttp.IntegrationTests/Http11/02_Http11ChunkedTests.cs`
- [x] 8 tests: chunked decoded, multi-chunk, chunked POST, zero-length final, trailers, large (100KB), HEAD for chunked, MD5 trailer
- [x] All tests pass: `dotnet test --filter "FullyQualifiedName~Http11ChunkedTests"`

## Deviations
None.
