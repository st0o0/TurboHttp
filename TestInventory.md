# Test Inventory — TurboHttp Test Suite

**Generated**: Phase 70 Step 1 — Full Test Inventory
**Total [Fact]/[Theory] occurrences**: 1760 across 47 test files
**Total test cases (including InlineData)**: ~2528 (last verified total)
**Date**: 2026-03-05

---

## Summary Table

| File | Tests | RFC Reference | RFC in Names? | Duplicate Candidate? |
|------|------:|---------------|:-------------:|:--------------------:|
| ContentEncodingTests.cs | 29 | RFC 9110 §8.4 | Yes (CE-enc, CE-11) | No |
| ConnectionReuseEvaluatorTests.cs | 25 | RFC 9112 §9 | Yes (CM-001⚠) | No |
| CookieJarTests.cs | 42 | RFC 6265 | Yes (CM-001⚠) | No |
| CrossFeatureIntegrityTests.cs | 40 | Phase 59 (multi-RFC) | Yes (CFI-001..060) | No |
| HpackDynamicTableTests.cs | 45 | RFC 7541 §4 | Yes (DT-001) | No |
| HpackHeaderBlockDecodingTests.cs | 46 | RFC 7541 §6 | Yes (HD-001) | Maybe |
| HpackHeaderListSizeTests.cs | 26 | RFC 7540 §6.5.2 / RFC 7541 §4.1 | Yes (HL-001) | No |
| HpackStaticTableTests.cs | 28 | RFC 7541 Appendix A | Yes (ST-001) | No |
| HpackTests.cs | 56 | None | No | **YES** |
| Http10DecoderTests.cs | 79 | RFC 1945 (implicit) | No | Maybe |
| Http10EncoderTests.cs | 83 | RFC 1945 (implicit) | No | No |
| Http10RoundTripTests.cs | 50 | RFC 1945 (implicit) | Partial (RT-10-008) | No |
| Http11DecoderChunkExtensionTests.cs | 35 | RFC 9112 §7.1.1 | Yes (9112-chunkext-001) | No |
| Http11DecoderTests.cs | 99 | RFC 9112 (implicit) | No | **YES** |
| Http11EncoderTests.cs | 54 | RFC 9112 (implicit) | No | No |
| Http11RoundTripTests.cs | 61 | RFC 9112 (implicit) | Partial (RT-11-021) | No |
| Http11SecurityTests.cs | 14 | RFC 9112 (security) | Yes (SEC-001) | No |
| Http2ConnectionPrefaceTests.cs | 23 | RFC 9113 §3.4 | Yes (RFC9113-3.4-CP-001) | No |
| Http2ContinuationFrameTests.cs | 25 | RFC 7540 §6.10 | Yes (CC-001) | No |
| Http2CrossComponentValidationTests.cs | 20 | RFC 9113 (Phase 27-28) | Yes (CC-001..020) | No |
| Http2DecoderHeadersValidationTests.cs | 28 | RFC 9113 §8.2 | Yes (HV-001) | No |
| Http2DecoderMaxConcurrentStreamsTests.cs | 45 | RFC 7540 §5.1.2 / §6.5.2 | Yes (MCS-001) | No |
| Http2DecoderTests.cs | 89 | None (baseline) | No | **YES** |
| Http2EncoderPseudoHeaderValidationTests.cs | 45 | RFC 7540 §8.1.2.1 | Yes (7540-8.1.2.1-c001) | No |
| Http2EncoderSensitiveHeaderTests.cs | 35 | RFC 7541 §7.1.3 | Yes (7541-7.1.3-s001) | No |
| Http2EncoderTests.cs | 54 | None (baseline) | No | Maybe |
| Http2ErrorMappingTests.cs | 25 | RFC 7540 §5.4 | Yes (EM-001) | No |
| Http2FlowControlTests.cs | 38 | RFC 7540 §5.2 / §6.9 | Yes (FC-001) | No |
| Http2FrameParsingCoreTests.cs | 32 | RFC 7540 §4 | Yes (RFC7540-4.1-FP-001) | No |
| Http2FrameTests.cs | 6 | None (baseline) | No | **YES** |
| Http2FuzzHarnessTests.cs | 25 | RFC 9113 / RFC 7541 | Yes (FZ-001) | No |
| Http2GoAwayRstStreamTests.cs | 20 | RFC 7540 §6.8 / §6.4 | Yes (GR-001) | No |
| Http2HighConcurrencyTests.cs | 20 | RFC 9113 (Phase 30) | Yes (HC-001) | No |
| Http2ResourceExhaustionTests.cs | 30 | RFC 9113 (Phase 24-25) | Yes (RE-010..083) | No |
| Http2RoundTripTests.cs | 55 | RFC 9113 / RFC 7541 | Partial (RT-2-001) | No |
| Http2SecurityTests.cs | 8 | RFC 7541 (HPACK security) | Yes (SEC-h2-001) | No |
| Http2SettingsSynchronizationTests.cs | 28 | RFC 7540 §6.5 | Yes (RFC7540-3.5-SS-001) | No |
| Http2StreamLifecycleTests.cs | 25 | RFC 9113 §5.1 | Yes (SS-001) | No |
| HttpDecodeErrorMessagesTests.cs | 34 | RFC 9112 / Phase 34 | Yes (34-msg-001) | No |
| HttpSafeLoggerTests.cs | 30 | Phase 57 (safe logging) | Yes (SL-001) | No |
| HuffmanDecoderTests.cs | 33 | RFC 7541 §5.2 / Appendix B | Yes (HF-001) | No |
| HuffmanTests.cs | 6 | None | No | **YES** |
| PerHostConnectionLimiterTests.cs | 18 | RFC 9112 §9.4 | Yes (CL-001) | No |
| Phase60ValidationGateTests.cs | 38 | RFC 9110 / RFC 9112 | Yes (P60-9110-001) | No |
| RedirectHandlerTests.cs | 51 | RFC 9110 §15.4 | Yes (RH-001) | No |
| RetryEvaluatorTests.cs | 40 | RFC 9110 §9.2 | Yes (RE-001⚠) | No |
| TcpFragmentationTests.cs | 22 | RFC 9112 / RFC 9113 (TCP) | No | No |

---

## Duplicate Candidates (Detailed)

### 1. HuffmanTests.cs vs HuffmanDecoderTests.cs
**Verdict: DUPLICATE — HuffmanTests should be merged into HuffmanDecoderTests**

`HuffmanTests.cs` (6 tests, no RFC refs, no DisplayName):
- `Encode_WwwExample_MatchesRfc7541` — covered by HuffmanDecoderTests HF-001
- `Encode_NoCache_MatchesRfc7541` — covered by HuffmanDecoderTests
- `Decode_WwwExample_MatchesRfc7541` — covered by HuffmanDecoderTests
- `Decode_Custom_RoundTrip` — basic round-trip
- `Encode_EmptyArray_ReturnsEmpty` — edge case
- `Decode_EmptyArray_ReturnsEmpty` — edge case

`HuffmanDecoderTests.cs` (33 tests) has full RFC 7541 Appendix B coverage with HF-001..HF-033.

**Recommendation**: Delete `HuffmanTests.cs` after verifying HuffmanDecoderTests covers all cases.

---

### 2. HpackTests.cs (56 tests) vs HpackStaticTableTests + HpackDynamicTableTests + HpackHeaderBlockDecodingTests
**Verdict: PARTIAL OVERLAP — HpackTests has some unique tests but many are subsumed**

`HpackTests.cs` has no RFC refs and no DisplayName on most tests. Key categories:
- Static entry indexing (covered by HpackStaticTableTests ST-001..ST-028)
- Dynamic table encode/decode round-trips (partially covered by HpackDynamicTableTests DT-001..DT-045)
- Sensitive headers (covered by Http2EncoderSensitiveHeaderTests)
- Table size updates (covered by HpackDynamicTableTests)
- Huffman round-trips (covered by HuffmanDecoderTests)

**Recommendation**: Review HpackTests for unique cases, migrate unique ones to appropriate RFC-structured files, delete remainder.

---

### 3. Http2DecoderTests.cs (89 tests, no RFC refs) — Baseline Sprawl
**Verdict: PARTIAL OVERLAP — Many baseline tests covered by specialized files**

Coverage overlaps with:
- `Http2ConnectionPrefaceTests.cs` (preface/SETTINGS)
- `Http2FrameParsingCoreTests.cs` (frame parsing)
- `Http2SettingsSynchronizationTests.cs` (SETTINGS sync)
- `Http2FlowControlTests.cs` (flow control)
- `Http2DecoderHeadersValidationTests.cs` (headers validation)
- `Http2StreamLifecycleTests.cs` (stream states)

**Recommendation**: Classify Http2DecoderTests into groups; identify unique baseline tests; migrate to RFC-structured files; keep only truly unique integration tests.

---

### 4. Http2FrameTests.cs (6 tests) vs Http2FrameParsingCoreTests.cs (32 tests)
**Verdict: LIKELY OVERLAP**

`Http2FrameTests.cs` tests `SettingsFrame.Serialize`, `SettingsAck`, `DataFrame`, `HeadersFrame`, etc. — frame construction, not parsing. These may be needed but should be audited against Http2FrameParsingCoreTests.

---

### 5. Http2EncoderTests.cs (54 tests) vs Http2EncoderPseudoHeaderValidationTests + Http2EncoderSensitiveHeaderTests
**Verdict: PARTIAL OVERLAP**

`Http2EncoderTests.cs` baseline tests overlap with the dedicated encoder-specific test files.

---

### 6. Http11DecoderTests.cs (99 tests, no RFC refs) — Baseline Sprawl
**Verdict: PARTIAL OVERLAP**

Likely overlaps with:
- `Http11DecoderChunkExtensionTests.cs` (chunk extensions)
- `Http11SecurityTests.cs` (header limits)
- `Http11RoundTripTests.cs` (round-trip scenarios)

---

## Naming Conflicts

| Prefix | Files | Conflict |
|--------|-------|---------|
| `CM-xxx` | `ConnectionReuseEvaluatorTests.cs` AND `CookieJarTests.cs` | Both use CM-001..CM-00n — COLLISION |
| `RE-xxx` | `RetryEvaluatorTests.cs` AND `Http2ResourceExhaustionTests.cs` | Both use RE-001..RE-0nn — COLLISION |
| `CC-xxx` | `Http2ContinuationFrameTests.cs` AND `Http2CrossComponentValidationTests.cs` | Both use CC-001..CC-0nn — COLLISION |
| `SS-xxx` | `Http2SettingsSynchronizationTests.cs` AND `Http2StreamLifecycleTests.cs` | Both use SS-001..SS-0nn — COLLISION |
| `SEC-xxx` | `Http11SecurityTests.cs` AND `Http2SecurityTests.cs` | SEC-001 in HTTP/1.1; SEC-h2-001 in HTTP/2 — Partial |

---

## RFC Coverage Map

### RFC 9110 — HTTP Semantics
| Section | Requirement | Test File(s) | Status |
|---------|------------|--------------|--------|
| §8.4 | Content-Encoding | ContentEncodingTests | ✅ |
| §9.2 | Idempotency / Retry | RetryEvaluatorTests | ✅ |
| §15.4 | Redirect handling | RedirectHandlerTests | ✅ |
| §9 (methods) | All HTTP methods | Phase60ValidationGateTests | ✅ |
| §15 (status codes) | Status code semantics | Phase60ValidationGateTests | ✅ |
| §6 | Header fields | Http11DecoderTests (implicit) | ⚠ No RFC ref |
| Cross-feature | Feature composition | CrossFeatureIntegrityTests | ✅ |

### RFC 9112 — HTTP/1.1
| Section | Requirement | Test File(s) | Status |
|---------|------------|--------------|--------|
| §4 | Request line parsing | Http11DecoderTests | ⚠ No RFC ref |
| §5.1 | Header field parsing | Http11DecoderTests | ⚠ No RFC ref |
| §6.3 | Chunk size / extensions | Http11DecoderChunkExtensionTests | ✅ |
| §7 | Transfer-Encoding | Http11DecoderTests | ⚠ No RFC ref |
| §9 | Persistent connections | ConnectionReuseEvaluatorTests | ✅ |
| §9.4 | Connection limits | PerHostConnectionLimiterTests | ✅ |
| Security | Header count limits | Http11SecurityTests | ✅ |

### RFC 9113 — HTTP/2
| Section | Requirement | Test File(s) | Status |
|---------|------------|--------------|--------|
| §3.4 | Connection preface | Http2ConnectionPrefaceTests | ✅ |
| §4 / §4.1 | Frame header | Http2FrameParsingCoreTests | ✅ |
| §5.1 | Stream state machine | Http2StreamLifecycleTests | ✅ |
| §5.4 | Error handling | Http2ErrorMappingTests | ✅ |
| §6.4 | RST_STREAM | Http2GoAwayRstStreamTests | ✅ |
| §6.5 | SETTINGS | Http2SettingsSynchronizationTests | ✅ |
| §6.8 | GOAWAY | Http2GoAwayRstStreamTests | ✅ |
| §6.9 | WINDOW_UPDATE | Http2FlowControlTests | ✅ |
| §6.10 | CONTINUATION | Http2ContinuationFrameTests | ✅ |
| §8.2 | Header validation | Http2DecoderHeadersValidationTests | ✅ |
| §8.3 | Pseudo-headers | Http2EncoderPseudoHeaderValidationTests | ✅ |
| Security | Resource exhaustion | Http2ResourceExhaustionTests | ✅ |
| Security | Concurrency | Http2HighConcurrencyTests | ✅ |
| Robustness | Fuzz harness | Http2FuzzHarnessTests | ✅ |

### RFC 7541 — HPACK
| Section | Requirement | Test File(s) | Status |
|---------|------------|--------------|--------|
| Appendix A | Static table (61 entries) | HpackStaticTableTests | ✅ |
| §4 | Dynamic table engine | HpackDynamicTableTests | ✅ |
| §5.2 | Huffman encoding | HuffmanDecoderTests | ✅ |
| §6 | Header block decoding | HpackHeaderBlockDecodingTests | ✅ |
| §6.5.2 | MAX_HEADER_LIST_SIZE | HpackHeaderListSizeTests | ✅ |
| §7.1.3 | Sensitive headers (NeverIndexed) | Http2EncoderSensitiveHeaderTests | ✅ |

### RFC 6265 — HTTP Cookies
| Section | Requirement | Test File(s) | Status |
|---------|------------|--------------|--------|
| §5.1.3 | Domain matching | CookieJarTests | ✅ |
| §5.2 | Cookie attributes | CookieJarTests | ✅ |
| §5.3 | Cookie storage | CookieJarTests | ✅ |

---

## Files Lacking RFC Traceability (Priority for Phase 70 Refactoring)

These files lack RFC section references in either the class summary or test names:

1. **HpackTests.cs** — 56 tests, no RFC mapping, no DisplayName (HIGH PRIORITY — merge/delete)
2. **HuffmanTests.cs** — 6 tests, no RFC mapping, no DisplayName (HIGH PRIORITY — delete)
3. **Http10DecoderTests.cs** — 79 tests, no DisplayName, no explicit RFC refs
4. **Http10EncoderTests.cs** — 83 tests, no DisplayName, no explicit RFC refs
5. **Http11DecoderTests.cs** — 99 tests, no DisplayName, no explicit RFC refs
6. **Http11EncoderTests.cs** — 54 tests, no DisplayName, no explicit RFC refs
7. **Http2DecoderTests.cs** — 89 tests, no RFC ref (baseline sprawl)
8. **Http2EncoderTests.cs** — 54 tests, no RFC ref (baseline)
9. **Http2FrameTests.cs** — 6 tests, no RFC ref (baseline)
10. **TcpFragmentationTests.cs** — 22 tests, no DisplayName, no explicit RFC refs

**Total tests lacking RFC traceability: ~546 (31% of total)**

---

## Files With Good RFC Traceability (Examples)

1. `Http2ConnectionPrefaceTests.cs` — RFC9113-3.4-CP-001..008 format ✅
2. `Http2FrameParsingCoreTests.cs` — RFC7540-4.1-FP-001..032 format ✅
3. `HpackStaticTableTests.cs` — ST-001..028 with class-level RFC summary ✅
4. `HpackDynamicTableTests.cs` — DT-001..045 with per-section comments ✅
5. `Http11DecoderChunkExtensionTests.cs` — 9112-chunkext-001..035 format ✅
6. `Http2EncoderPseudoHeaderValidationTests.cs` — 7540-8.1.2.1-c001..c045 format ✅
7. `Http2EncoderSensitiveHeaderTests.cs` — 7541-7.1.3-s001..s035 format ✅

---

## Recommendations (Priority Order)

### P1 — Delete duplicates (immediate wins)
1. Delete `HuffmanTests.cs` after confirming `HuffmanDecoderTests.cs` covers all cases
2. Audit `HpackTests.cs` against RFC-structured files; extract unique tests; delete file

### P2 — Fix naming collisions
1. Rename `ConnectionReuseEvaluatorTests.cs` CM-xxx → CR-xxx (ConnectionReuse)
2. Rename `Http2ResourceExhaustionTests.cs` RE-xxx → RX-xxx (ResourceExhaustion) or keep RE-010+ to avoid collision with RetryEvaluator RE-001+
3. Rename `Http2ContinuationFrameTests.cs` CC-xxx → CF-xxx (ContinuationFrame)
4. Rename `Http2CrossComponentValidationTests.cs` CC-xxx → CV-xxx (CrossValidation)
5. Rename `Http2SettingsSynchronizationTests.cs` SS-xxx → SE-xxx (SettingsSync)

### P3 — Add RFC refs to baseline test files
1. Add `/// <summary>RFC 9112 §...` headers to Http11DecoderTests.cs
2. Add `[Fact(DisplayName = "RFC9112-...")]` to Http10DecoderTests.cs / Http11DecoderTests.cs
3. Refactor Http2DecoderTests.cs into RFC-referenced groups or migrate to specialized files

### P4 — RFC folder restructure (Step 3 of Phase 70)
Reorganize into `/RFC9110`, `/RFC9112`, `/RFC9113`, `/RFC7541` subfolders as per Phase 70 plan.
