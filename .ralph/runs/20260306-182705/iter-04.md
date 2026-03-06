# Iteration 04 — Phase 3: Migrate 01_ConnectionPrefaceTests.cs

## Task Selected
**Phase 3 — Migrate `01_ConnectionPrefaceTests.cs` (1 ref)**

## Surface Area Classification
- Test-only change in `TurboHttp.Tests`
- No production code touched
- Single file: `src/TurboHttp.Tests/RFC9113/01_ConnectionPrefaceTests.cs`

## Verification Level
**L0** — Pure test-file check, no I/O coordination, no external dependencies.
The task only requires confirming 0 `Http2Decoder` refs remain in the file and that the tests pass.

## Skills Consulted
- None required (file inspection + test run only)

## Discovery
Upon reading `01_ConnectionPrefaceTests.cs`, it was found to contain **0** `Http2Decoder` references — the migration was already performed as part of the previous commit `3c1dca3` ("Phase 41 Step 1: Migrate 01_ConnectionPrefaceTests to Http2StageTestHelper"). The IMPLEMENTATION_PLAN.md checkbox had not yet been updated to reflect this.

## Commands Run

### 1. Count Http2Decoder refs in file
```
Grep pattern=Http2Decoder path=...01_ConnectionPrefaceTests.cs output_mode=count
```
**Result**: 0 occurrences ✓

### 2. Run Http2ConnectionPrefaceTests
```
dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~Http2ConnectionPrefaceTests"
```
**Result**: Passed: 23, Failed: 0, Skipped: 0, Total: 23 ✓

### 3. Run RFC9113 suite (full acceptance criterion)
```
dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC9113"
```
**Result**: Passed: 649, Failed: 43, Total: 692
- The 43 failures are pre-existing (Phase 0 baseline: 44 pre-existing failures total)
- None of the failures are in `01_ConnectionPrefaceTests.cs`
- Failures are in `02_FrameParsingTests.cs` and other files — unchanged from baseline ✓

## Deviations / Skips
- No code changes were needed; file was already clean from a prior commit.
- Acceptance criteria fully satisfied without modification.

## Follow-ups Noticed (Deferred)
- Phase 4 (`02_FrameParsingTests.cs`, 2 refs) is the next task.
- Some pre-existing failures in `02_FrameParsingTests.cs` (FP-017, FP-013) are unrelated to decoder migration and were present at baseline.
