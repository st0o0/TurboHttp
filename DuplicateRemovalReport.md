# Duplicate Removal Report — TurboHttp Test Suite

**Generated**: Phase 70 Step 2 — Duplicate Detection
**Date**: 2026-03-05
**Source**: TestInventory.md (Phase 70 Step 1)

---

## Summary

| Category | Files | Tests Affected | Action |
|----------|------:|---------------:|--------|
| Pure duplicate (DELETE) | 1 | 6 | HuffmanTests.cs deleted; 2 unique tests migrated |
| Partial overlap (AUDIT) | 4 | ~195 | Deferred to Phase 70 Steps 3-6 |
| Naming collision (RENAME) | 5 pairs | ~200+ test IDs | Deferred to Phase 70 naming pass |

---

## Category 1: Pure Duplicates — RESOLVED

### HuffmanTests.cs (6 tests) → DELETED

**Verdict**: Full overlap with `HuffmanDecoderTests.cs`. File deleted after migrating 2 unique tests.

| HuffmanTests.cs Method | HuffmanDecoderTests.cs Coverage | Status |
|------------------------|----------------------------------|--------|
| `Decode_WwwExample_MatchesRfc7541` | HF-001 (exact duplicate) | REMOVED |
| `Decode_NoCache_MatchesRfc7541` (via RoundTrip) | HF-002 round-trip | REMOVED |
| `RoundTrip_EncodeThenDecode_ReturnsOriginal` (11 cases) | RT-001..RT-008, HF-010/HF-011 | REMOVED |
| `Encode_WwwExample_MatchesRfc7541` | No encode-direction RFC vector existed | MIGRATED → ED-004 |
| `Encode_NoCache_MatchesRfc7541` | No encode-direction RFC vector existed | MIGRATED → ED-005 |
| `Encode_AlwaysCompressesOrEqualCommonHeaders` | ED-002 (same intent, different data) | REMOVED (subsumed) |

**Action taken**: Migrated ED-004/ED-005 to HuffmanDecoderTests.cs. Deleted HuffmanTests.cs.

---

## Category 2: Partial Overlap — DEFERRED to Future Iterations

### HpackTests.cs (56 tests) — PARTIAL OVERLAP

**Verdict**: ~60-70% covered by RFC-structured HPACK files; ~15-20 tests are unique integration scenarios.

| HpackTests.cs Group | Covered By | Overlap % | Unique Tests |
|---------------------|-----------|-----------|-------------|
| Static entry indexing (5 tests) | HpackStaticTableTests ST-001..028 | ~80% | 0-1 |
| Dynamic table encode/decode (12 tests) | HpackDynamicTableTests DT-001..045 | ~70% | 3-4 |
| Sensitive headers (6 tests) | Http2EncoderSensitiveHeaderTests | ~100% | 0 |
| Table size updates (5 tests) | HpackDynamicTableTests | ~60% | 2 |
| Huffman round-trips (4 tests) | HuffmanDecoderTests RT-001..012 | ~100% | 0 |
| Multi-block HPACK state (8 tests) | HpackHeaderBlockDecodingTests HD-001+ | ~50% | 4 |
| Encoder-decoder integration (16 tests) | No exact RFC equivalent | ~30% | 10-12 |

**Recommendation**: Audit per-test, migrate unique integration tests to `HpackHeaderBlockDecodingTests.cs`, delete `HpackTests.cs`.
**Deferred to**: Phase 70 Step 4 (RFC-structured files).

---

### Http2DecoderTests.cs (89 tests) — BASELINE SPRAWL

**Verdict**: Significant overlap with 6 specialized files; ~25-30 baseline tests cover unique integration paths.

| Http2DecoderTests.cs Group | Covered By | Overlap |
|----------------------------|-----------|---------|
| SETTINGS frame decode (8 tests) | Http2SettingsSynchronizationTests SS-001+ | High |
| PING request/ack (6 tests) | Http2SettingsSynchronizationTests | High |
| HEADERS / stream lifecycle (12 tests) | Http2StreamLifecycleTests SS-001..025 | Medium |
| Flow control / WINDOW_UPDATE (10 tests) | Http2FlowControlTests FC-001..038 | High |
| Frame parsing (8 tests) | Http2FrameParsingCoreTests FP-001..032 | High |
| GOAWAY / RST_STREAM (6 tests) | Http2GoAwayRstStreamTests GR-001..020 | High |
| Full response decode (15 tests) | Http2RoundTripTests RT-2-001+ | Medium |
| Error / exception paths (10 tests) | Http2ErrorMappingTests EM-001..025 | Medium |
| Connection preface (4 tests) | Http2ConnectionPrefaceTests CP-001+ | High |
| Unique integration scenarios (~10) | None | UNIQUE |

**Recommendation**: Migrate unique integration tests to Http2RoundTripTests.cs or Http2CrossComponentValidationTests.cs. Delete Http2DecoderTests.cs.
**Deferred to**: Phase 70 Step 4 (RFC-structured refactoring).

---

### Http2FrameTests.cs (6 tests) — NOT A DUPLICATE

**Verdict**: Tests frame SERIALIZATION (Serialize() method). Http2FrameParsingCoreTests tests PARSING (reading frames from bytes). These are complementary, not duplicate.

| Http2FrameTests.cs Test | Direction | Http2FrameParsingCoreTests Equivalent |
|-------------------------|-----------|---------------------------------------|
| `SettingsFrame_Serialize_CorrectFormat` | Encode | FP-009..015 (SETTINGS parsing, inverse direction) |
| `SettingsAck_Serialize_EmptyPayload` | Encode | FP-009 (parsing direction) |
| `PingFrame_Serialize_8BytePayload` | Encode | No exact equivalent |
| `WindowUpdateFrame_Serialize_CorrectIncrement` | Encode | FP-023..025 (parsing direction) |
| `DataFrame_Serialize_WithEndStream` | Encode | FP-002..006 (parsing direction) |
| `GoAwayFrame_Serialize_WithDebugData` | Encode | FP-026..030 (parsing direction) |

**Recommendation**: Keep Http2FrameTests.cs. Add RFC header comment and [DisplayName] attributes.
**Action**: No deletion — add RFC traceability only (Phase 70 Step 6).

---

### Http2EncoderTests.cs (54 tests) — PARTIAL OVERLAP

**Verdict**: ~30-40% overlap with Http2EncoderPseudoHeaderValidationTests and Http2EncoderSensitiveHeaderTests; baseline tests cover general encode paths.

| Group | Covered By | Overlap |
|-------|-----------|---------|
| Pseudo-header ordering (12 tests) | Http2EncoderPseudoHeaderValidationTests | High |
| Sensitive headers (8 tests) | Http2EncoderSensitiveHeaderTests | High |
| Basic encode paths (20 tests) | No RFC-structured equivalent | UNIQUE |
| Frame splitting / continuation (8 tests) | No RFC-structured equivalent | UNIQUE |
| HPACK interaction (6 tests) | HpackHeaderBlockDecodingTests | Low |

**Recommendation**: Audit per-test. Keep unique encode path tests. Migrate pseudo-header/sensitive tests that are pure duplicates.
**Deferred to**: Phase 70 Step 4.

---

## Category 3: Naming Collisions — DEFERRED

These collisions do not cause test failures (xUnit distinguishes by class name) but make test IDs ambiguous for RFC traceability.

| Prefix | File A | File B | Recommended Fix |
|--------|--------|--------|----------------|
| `CM-xxx` | ConnectionReuseEvaluatorTests | CookieJarTests | Rename CR-xxx in ConnectionReuseEvaluatorTests |
| `RE-xxx` | RetryEvaluatorTests (RE-001+) | Http2ResourceExhaustionTests (RE-010+) | Rename Http2ResourceExhaustionTests to RX-xxx |
| `CC-xxx` | Http2ContinuationFrameTests | Http2CrossComponentValidationTests | Rename CF-xxx in ContinuationFrameTests |
| `SS-xxx` | Http2SettingsSynchronizationTests | Http2StreamLifecycleTests | Rename SE-xxx in SettingsSynchronizationTests |
| `SEC-xxx` | Http11SecurityTests (SEC-001+) | Http2SecurityTests (SEC-h2-001) | Partial collision; SEC-h2 prefix avoids it |

**Deferred to**: Phase 70 Step 6 (Convert Behavior Tests to Invariant Tests).

---

## Parameterization Opportunities — DEFERRED

These test groups can be converted from copy-paste [Fact] to [Theory]:

### Http11DecoderTests.cs — Start-Line Parsing (HTTP method variations)
Currently: ~8 separate [Fact] methods, one per HTTP method (GET, POST, PUT, DELETE, HEAD, OPTIONS, PATCH)
Convert to: `[Theory] [InlineData("GET")] [InlineData("POST")] ...`
Estimated: 7 tests → 1 [Theory] with 7 InlineData items.

### Http10DecoderTests.cs — Status Code Handling
Currently: ~10 separate [Fact] methods for 1xx/2xx/3xx/4xx/5xx status codes
Convert to: `[Theory] [InlineData(200, "OK")] [InlineData(404, "Not Found")] ...`

### Http2FlowControlTests.cs — Window Size Boundary Tests
Currently: Multiple [Fact] methods testing different window sizes
Convert to: `[Theory] [InlineData(65535)] [InlineData(0)] [InlineData(2147483647)] ...`

**Deferred to**: Phase 70 Step 6.

---

## Test Overlap Summary by RFC Area

### Identified Overlaps
- [ ] Same assertion tested in multiple files: **YES** — HuffmanTests (RESOLVED), HpackTests, Http2DecoderTests
- [ ] Same edge case with minor variation: **YES** — HpackTests encode/decode round-trips overlap with HpackDynamicTableTests
- [ ] Copy-pasted negative tests: **YES** — Http2DecoderTests SETTINGS/PING negative paths covered by Http2SettingsSynchronizationTests
- [ ] Redundant fuzz tests: **NO** — Http2FuzzHarnessTests.cs is the only fuzz file; no redundancy detected

---

## Files with Redundant Fuzz Coverage

No redundant fuzz tests detected. `Http2FuzzHarnessTests.cs` (25 tests, FZ-001..FZ-025) is the sole fuzz file.
All 25 tests cover distinct scenarios: random frame ordering, invalid lengths, invalid header encodings, window overflow attempts, table resizing storms.

---

## Completed Actions

1. **HuffmanTests.cs DELETED** — 2 unique encode-direction RFC tests migrated to HuffmanDecoderTests.cs (ED-004, ED-005). 4 tests confirmed as exact or transitive duplicates.

## Deferred Actions (Phase 70 Steps 3-6)

2. **HpackTests.cs** — Audit ~15-20 unique integration tests; migrate; delete file.
3. **Http2DecoderTests.cs** — Audit ~10 unique integration tests; migrate; delete file.
4. **Http2EncoderTests.cs** — Audit ~30 unique baseline tests; keep unique; remove ~20 duplicates.
5. **Http2FrameTests.cs** — Add RFC header + [DisplayName]; no deletion.
6. **Naming collisions** — Rename CM/RE/CC/SS prefixes in 4 files.
7. **Parameterization** — Convert 3+ copy-paste groups to [Theory].
