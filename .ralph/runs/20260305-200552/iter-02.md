# Iteration 02 — Phase 70 Step 3: RFC-Based Folder Structure

## Task Selected
**Phase 70 Step 3: RFC-Based Folder Structure** (🟦 TIER 9 — Test Suite Audit & RFC Refactoring)

Restructure tests into RFC-organized subdirectories:
```
/Tests
  /RFC9113  — HTTP/2 framing, stream state, flow control tests
  /RFC7541  — HPACK static/dynamic table, Huffman, header block tests
```

## Surface Area Classification
- Test project filesystem structure: 14 files moved to 2 new subdirectories
- No production code changed, no namespaces changed
- No .csproj changes needed (SDK-style projects auto-include all .cs in subdirectories)

## Verification Level
**L1** — Build + test run.
Reason: Only test file locations changed. No production API changes, no namespace changes, no logic changes. Compiler will auto-discover files in new subdirs. Must verify zero regressions.

## Skills Consulted
- CLAUDE.md (project constitution, Allman braces, no namespace mandate)
- IMPLEMENTATION_PLAN.md (Phase 70 Step 3 target structure)
- iter-01.md (Step 2 completed; Step 3 is next)

## What Was Implemented

### RFC9113/ — 9 files (HTTP/2 RFC 9113 tests)
| Old path | New path |
|---|---|
| Http2ConnectionPrefaceTests.cs | RFC9113/01_ConnectionPrefaceTests.cs |
| Http2FrameParsingCoreTests.cs | RFC9113/02_FrameParsingTests.cs |
| Http2StreamLifecycleTests.cs | RFC9113/03_StreamStateMachineTests.cs |
| Http2SettingsSynchronizationTests.cs | RFC9113/04_SettingsTests.cs |
| Http2FlowControlTests.cs | RFC9113/05_FlowControlTests.cs |
| Http2DecoderHeadersValidationTests.cs | RFC9113/06_HeadersTests.cs |
| Http2ErrorMappingTests.cs | RFC9113/07_ErrorHandlingTests.cs |
| Http2GoAwayRstStreamTests.cs | RFC9113/08_GoAwayTests.cs |
| Http2ContinuationFrameTests.cs | RFC9113/09_ContinuationFrameTests.cs |

### RFC7541/ — 5 files (HPACK RFC 7541 tests)
| Old path | New path |
|---|---|
| HpackStaticTableTests.cs | RFC7541/01_StaticTableTests.cs |
| HpackDynamicTableTests.cs | RFC7541/02_DynamicTableTests.cs |
| HuffmanDecoderTests.cs | RFC7541/04_HuffmanTests.cs |
| HpackHeaderBlockDecodingTests.cs | RFC7541/05_HeaderBlockDecodingTests.cs |
| HpackHeaderListSizeTests.cs | RFC7541/06_TableSizeTests.cs |

### Files kept at root (no clear RFC mapping or mixed concerns)
- HpackTests.cs (mixed HPACK integration)
- Http2DecoderTests.cs (mixed decoder integration)
- Http2EncoderTests.cs (mixed encoder baseline)
- Http2EncoderPseudoHeaderValidationTests.cs
- Http2EncoderSensitiveHeaderTests.cs
- Http2FrameTests.cs (frame serialization, complements parsing)
- Http2SecurityTests.cs
- Http2RoundTripTests.cs (integration)
- Http2ResourceExhaustionTests.cs (Tier 7 robustness)
- Http2CrossComponentValidationTests.cs (Tier 8 integration)
- Http2FuzzHarnessTests.cs (Tier 9 stress)
- Http2HighConcurrencyTests.cs (Tier 9 stress)
- Http2DecoderMaxConcurrentStreamsTests.cs
- All HTTP/1.0, HTTP/1.1 tests
- All Tier 2 tests (ContentEncoding, CookieJar, RedirectHandler, etc.)

### Note on 03_IntegerEncodingTests.cs
Plan specified this file for RFC7541 folder, but no dedicated integer-encoding test file exists. Integer encoding is tested within HpackHeaderBlockDecodingTests.cs. Skipped creation — will be addressed in Step 6 (rename/restructure) or Step 5 (coverage gap analysis) if coverage is insufficient.

## Commands Run + Outcomes

```
mkdir -p src/TurboHttp.Tests/RFC9113 src/TurboHttp.Tests/RFC7541
→ Directories created

mv Http2ConnectionPrefaceTests.cs RFC9113/01_ConnectionPrefaceTests.cs
mv Http2FrameParsingCoreTests.cs RFC9113/02_FrameParsingTests.cs
mv Http2StreamLifecycleTests.cs RFC9113/03_StreamStateMachineTests.cs
mv Http2SettingsSynchronizationTests.cs RFC9113/04_SettingsTests.cs
mv Http2FlowControlTests.cs RFC9113/05_FlowControlTests.cs
mv Http2DecoderHeadersValidationTests.cs RFC9113/06_HeadersTests.cs
mv Http2ErrorMappingTests.cs RFC9113/07_ErrorHandlingTests.cs
mv Http2GoAwayRstStreamTests.cs RFC9113/08_GoAwayTests.cs
mv Http2ContinuationFrameTests.cs RFC9113/09_ContinuationFrameTests.cs
→ RFC9113 files moved (9 files)

mv HpackStaticTableTests.cs RFC7541/01_StaticTableTests.cs
mv HpackDynamicTableTests.cs RFC7541/02_DynamicTableTests.cs
mv HuffmanDecoderTests.cs RFC7541/04_HuffmanTests.cs
mv HpackHeaderBlockDecodingTests.cs RFC7541/05_HeaderBlockDecodingTests.cs
mv HpackHeaderListSizeTests.cs RFC7541/06_TableSizeTests.cs
→ RFC7541 files moved (5 files)

dotnet build ./src/TurboHttp.sln --configuration Release --no-restore
→ 0 Error(s), 30 Warning(s) — pre-existing warnings only

dotnet test ./src/TurboHttp.sln --configuration Release --no-build
→ Passed! Failed: 0, Passed: 2083, Total: 2083 (unit)
→ Passed! Failed: 0, Passed: 407, Total: 407 (integration)
→ Passed! Failed: 0, Passed: 24, Total: 24 (stream)
→ Total: 2514 tests — ZERO REGRESSIONS
```

## Deviations / Skips
- Class names inside files are NOT renamed (e.g., `Http2ConnectionPrefaceTests` class stays as-is). Renaming classes is Step 6 work (invariant test naming).
- Http2ContinuationFrameTests.cs included as RFC9113/09 (not in original 8-file plan) since CONTINUATION is RFC 9113 §6.10.
- 03_IntegerEncodingTests.cs not created — no dedicated file exists; deferred to Steps 5/6.

## Follow-ups Noticed (Deferred)
- Step 4: Add RFC mapping XML doc comments to each moved file
- Step 5: RFC Coverage Matrix — identify uncovered MUST statements
- Step 6: Rename test methods to RFC-traceable naming convention
- Step 7: Negative path hardening (many `- [ ]` items)
- Partial duplicate audit: HpackTests.cs, Http2DecoderTests.cs, Http2EncoderTests.cs (from Step 2 deferred list)
