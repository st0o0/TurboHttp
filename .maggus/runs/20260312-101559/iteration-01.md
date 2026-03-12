# Iteration 01 — TASK-040: Http20Engine Cache Integration Tests

## Task
TASK-040: Add integration tests for Http20Engine caching (cache hit, stale revalidation, 304 merge, no-store bypass, POST invalidation).

## Commands Run
1. `dotnet build src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj --configuration Release` → 0 errors, 1 warning (pre-existing)
2. `dotnet test --filter "FullyQualifiedName~Http20CacheTests"` → **5/5 passed** (751ms)

## Files Created
- `src/TurboHttp.IntegrationTests/Http20/09_Http20CacheTests.cs` — 5 tests covering RFC 9111 cache semantics over HTTP/2

## Tests
| ID | Name | Status |
|----|------|--------|
| 20E-INT-046 | Cached response served from cache | PASS |
| 20E-INT-047 | Stale entry triggers conditional revalidation | PASS |
| 20E-INT-048 | 304 Not Modified merges headers/preserves body | PASS |
| 20E-INT-049 | no-store response not cached | PASS |
| 20E-INT-050 | POST invalidates cache entry | PASS |

## Acceptance Criteria
- [x] File created: `src/TurboHttp.IntegrationTests/Http20/09_Http20CacheTests.cs`
- [x] 5 tests: cached served, stale conditional, 304 merge, no-store bypass, POST invalidation
- [x] All tests pass: `dotnet test --filter "FullyQualifiedName~Http20CacheTests"`

## Deviations
None.
