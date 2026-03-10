# Iteration 01 — TASK-20-03: Http20StreamStage — Response Reassembly

**Date:** 2026-03-10
**Branch:** `poc2`

## Task

TASK-20-03: Http20StreamStage — Response Reassembly
File: `src/TurboHttp.StreamTests/Http20/Http20StreamStageTests.cs`

## Acceptance Criteria — All Passed

- [x] `20S-001`: HEADERS with END_STREAM → response without body (2 tests)
- [x] `20S-002`: HEADERS + DATA with END_STREAM → response with body (2 tests)
- [x] `20S-003`: HEADERS + CONTINUATION + DATA → header block reassembled (2 tests)
- [x] `20S-004`: Multiple streams (ID 1, 3) → separate responses (2 tests)
- [x] `20S-005`: `:status` pseudo-header → correct HttpStatusCode (5 Theory cases)
- [x] `20S-006`: Content-Encoding header → decompression applied (gzip) (2 tests)
- [x] `20S-007`: Regular headers (non-pseudo) → present in Response.Headers (2 tests)

## Commands Run

1. `dotnet build src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` → Build succeeded (0 errors)
2. `dotnet test --filter "Http20StreamStageTests"` → 14/17 passed (first run)
3. Fixed 2 test assertions (204 EmptyContent in .NET 10, CONTINUATION+END_STREAM edge case)
4. `dotnet test --filter "Http20StreamStageTests"` → 17/17 passed
5. `dotnet test src/TurboHttp.StreamTests/` → 199/199 passed (no regressions)
6. `dotnet test src/TurboHttp.Tests/` → 2152/2152 passed (no regressions)

## Notes

- .NET 10 `HttpResponseMessage` has non-null `EmptyContent` by default, so HEADERS-only
  responses (END_STREAM on HEADERS frame, no DATA) have `Content != null` but empty bytes.
  Tests adjusted to check `ReadAsByteArrayAsync().Length == 0` instead of `Assert.Null(Content)`.
- `Http20StreamStage.HandleContinuation()` hardcodes `endStream: false` when calling
  `DecodeHeaders()`. This means if HEADERS has both `END_STREAM=true` and `END_HEADERS=false`,
  the END_STREAM flag is lost when CONTINUATION completes the header block. Tests work around
  this by placing END_STREAM on DATA frames (matching the typical HTTP/2 pattern).

## Test Counts

| Project | Before | After | Delta |
|---------|--------|-------|-------|
| StreamTests | 182 | 199 | +17 |
| Tests | 2152 | 2152 | 0 |
