# 🟦 Phase 70 — Full Test Suite Audit & RFC Refactoring

## 🎯 Objective

Transform the existing test suite into:

* RFC-structured
* Deduplicated
* Traceable
* Gap-validated
* Spec-aligned
* Audit-ready

At the end of this phase, every test can be mapped to a specific RFC section.

---

# 🔍 Step 1 — Full Test Inventory

## Task: Extract & Classify

- [ ] Enumerate all test files
- [ ] Enumerate all test methods
- [ ] Extract:

    * Target component
    * Covered feature
    * Expected behavior
    * Error condition (if any)

Produce:

```
TestInventory.md
```

With structure:

```
TestName
→ Component
→ Behavior
→ RFC reference (if known)
→ Duplicate candidate? (yes/no)
```

---

# 🧹 Step 2 — Duplicate Detection

## Identify:

- [ ] Same assertion tested in multiple files
- [ ] Same edge case with minor variation
- [ ] Copy-pasted negative tests
- [ ] Redundant fuzz tests

### Strategy

Group tests by:

* Frame type
* Error code
* Stream state
* HPACK representation type

Then:

* Merge parameterizable tests
* Convert copy-paste tests → `[Theory]` with inline data
* Remove pure duplicates

Deliverable:

```
DuplicateRemovalReport.md
```

---

# 🏗 Step 3 — RFC-Based Folder Structure

Restructure tests into:

```
/Tests
  /RFC9113
    01_ConnectionPrefaceTests.cs
    02_FrameParsingTests.cs
    03_StreamStateMachineTests.cs
    04_SettingsTests.cs
    05_FlowControlTests.cs
    06_HeadersTests.cs
    07_ErrorHandlingTests.cs
    08_GoAwayTests.cs
  /RFC7541
    01_StaticTableTests.cs
    02_DynamicTableTests.cs
    03_IntegerEncodingTests.cs
    04_HuffmanTests.cs
    05_HeaderBlockDecodingTests.cs
    06_TableSizeUpdateTests.cs
```

---

# 📚 Step 4 — One Test File Per RFC Section

Each file must begin with explicit RFC mapping:

```csharp
/// <summary>
/// RFC 9113 §6.1 – Frame Header
/// Ensures correct parsing and validation of frame headers.
/// </summary>
```

And each test:

```csharp
/// RFC 9113 §6.5.2
/// MUST treat invalid SETTINGS value as connection error.
```

This creates full traceability.

---

# 📊 Step 5 — RFC Coverage Matrix

Create:

```
RFC_TEST_MATRIX.md
```

Structure:

| RFC Section | Requirement          | Covered By Test              | Status              |
| ----------- | -------------------- | ---------------------------- | ------------------- |
| 6.1         | Frame header length  | FrameHeader_LengthValidation | ✅                   |
| 6.5.2       | SETTINGS validation  | Settings_InvalidValue        | ✅                   |
| 5.1         | Stream state machine | Stream_InvalidTransition     | ⚠ Missing edge case |

Then:

- [ ] Identify uncovered MUST statements
- [ ] Add missing tests
- [ ] Mark SHOULD separately

---

# 🔬 Step 6 — Convert Behavior Tests to Invariant Tests

Replace vague tests like:

```
ShouldHandleInvalidFrame()
```

With:

```
RFC9113_6_1_FrameLength_MustRejectOversizedFrame()
```

Naming format:

```
RFC<Number>_<Section>_<ShortRequirementDescription>
```

Example:

```
RFC9113_5_1_StreamState_MustRejectDataOnIdleStream
RFC7541_4_2_Huffman_MustRejectEOSMisuse
```

---

# 🧠 Step 7 — Negative Path Hardening

## 🟦 RFC 9110 — HTTP Semantics (Core)

### Message Semantics Violations

- [ ] Body present in 204 response
- [ ] Body present in 304 response
- [ ] Body present in HEAD response
- [ ] Missing required pseudo-semantics (e.g. no method)
- [ ] Invalid method token (illegal characters)
- [ ] Unknown method incorrectly rejected (must allow extension methods)
- [ ] Invalid status code (non 3-digit)
- [ ] 1xx treated as final response
- [ ] Multiple final responses processed
- [ ] Conflicting Content-Length vs actual body size
- [ ] Multiple differing Content-Length headers
- [ ] Content-Length + Transfer-Encoding both present (invalid combination)

---

### Header Handling Errors

- [ ] Header names treated case-sensitive
- [ ] Invalid header field name characters
- [ ] obs-fold (obsolete line folding) accepted incorrectly
- [ ] Connection-specific header forwarded incorrectly
- [ ] Duplicate single-value headers not rejected where required
- [ ] Invalid media type syntax accepted
- [ ] Invalid Content-Encoding accepted without error

---

### Redirect Semantics Violations

- [ ] 303 not rewritten to GET
- [ ] 307/308 incorrectly changing method
- [ ] Redirect without Location header accepted
- [ ] Infinite redirect loop not detected
- [ ] Authorization leaked across origins
- [ ] HTTPS → HTTP downgrade allowed unintentionally

---

### Retry & Idempotency Violations

- [ ] POST automatically retried
- [ ] Partial body retried without rewind
- [ ] Retry-After ignored
- [ ] Non-idempotent method retried on network failure

---

## 🟦 RFC 9111 — HTTP Caching

(If caching implemented)

### Cache-Control Violations

- [ ] no-store response cached
- [ ] private response cached in shared cache
- [ ] must-revalidate ignored
- [ ] no-cache not revalidated
- [ ] stale response served without validation
- [ ] Vary header ignored
- [ ] Weak ETag used as strong validator
- [ ] Incorrect Age header calculation
- [ ] Heuristic freshness applied when prohibited
- [ ] Authenticated response cached improperly

---

## 🟦 RFC 9112 — HTTP/1.1

### Start-Line Parsing

- [ ] Invalid request line format accepted
- [ ] Invalid HTTP version accepted
- [ ] Multiple spaces in start line misparsed
- [ ] Overlong request line not rejected
- [ ] Invalid CRLF handling
- [ ] LF without CR accepted

---

### Header Parsing

- [ ] Invalid header delimiter accepted
- [ ] Missing colon accepted
- [ ] Leading whitespace incorrectly accepted
- [ ] Header size limit not enforced
- [ ] Total header size unlimited
- [ ] Invalid chunked trailer parsing

---

### Transfer-Encoding Violations

- [ ] Invalid chunk size accepted
- [ ] Non-hex chunk size accepted
- [ ] Missing terminating chunk not rejected
- [ ] Chunk extensions misparsed
- [ ] Body read beyond declared Content-Length
- [ ] Transfer-Encoding other than chunked accepted incorrectly

---

### Persistent Connection Violations

- [ ] Connection reused after protocol error
- [ ] Connection reused when body not fully read
- [ ] Connection reused after Connection: close
- [ ] Keep-Alive parameters ignored incorrectly

---

### Request Smuggling Protection

- [ ] Content-Length ambiguity not rejected
- [ ] TE/CL conflict not rejected
- [ ] Multiple Content-Length values not validated
- [ ] Trailing CRLF injection accepted

---

## 🟦 RFC 9113 — HTTP/2 (Extended Negative Set)

(Expanding your list)

- [ ] Invalid frame length
- [ ] Invalid stream ID
- [ ] Frame on closed stream
- [ ] Flow control overflow
- [ ] CONTINUATION interleaving
- [ ] Missing END_HEADERS
- [ ] SETTINGS applied before ACK
- [ ] SETTINGS with invalid value accepted
- [ ] WINDOW_UPDATE overflow > 2^31-1
- [ ] DATA sent when window = 0
- [ ] Pseudo-header after regular header
- [ ] Duplicate pseudo-header
- [ ] Uppercase header name accepted
- [ ] Connection-specific header allowed
- [ ] PRIORITY dependency loop
- [ ] GOAWAY ignored
- [ ] New stream created after GOAWAY
- [ ] RST_STREAM not terminating stream
- [ ] HPACK error treated as stream error (must be connection error)
- [ ] Frame size > SETTINGS_MAX_FRAME_SIZE accepted
- [ ] CONTINUATION without preceding HEADERS accepted

---

## 🟦 RFC 7541 — HPACK (Extended Negative Set)

(Expanding your list)

- [ ] Invalid static index
- [ ] Dynamic table overflow
- [ ] Illegal size update position
- [ ] Invalid Huffman padding
- [ ] Integer overflow in prefix decoding
- [ ] Huffman EOS symbol misused
- [ ] Incomplete Huffman code accepted
- [ ] Overlong padding not rejected
- [ ] Dynamic table size update exceeding limit
- [ ] Negative effective table size
- [ ] Decoder state desync across header blocks
- [ ] Index 0 accepted
- [ ] Index > table size accepted
- [ ] Header block exceeding MAX_HEADER_LIST_SIZE not rejected
- [ ] Excessive dynamic table churn not bounded

---

## 🔒 Cross-Protocol Attack Surface Tests

These should exist regardless of protocol:

- [ ] Header injection via CRLF
- [ ] Response splitting
- [ ] Compression bomb
- [ ] Memory exhaustion via headers
- [ ] CPU exhaustion via pathological Huffman
- [ ] Stream exhaustion attack
- [ ] SETTINGS flood
- [ ] Rapid reset attack
- [ ] Large header list attack
- [ ] Frame flood

---

# 🧪 Step 8 — Remove Behavior Overlap Between Layers

Ensure:

* HPACK tests test only HPACK
* HTTP/2 tests do not test compression internals
* Integration tests test interaction only

No cross-layer duplication.

---

# 🧹 Step 9 — Enforce Test Quality Rules

All tests must:

- [ ] Assert exact error code
- [ ] Assert connection vs stream error
- [ ] Assert stream state after failure
- [ ] Assert no memory leak (where possible)
- [ ] Avoid timing-based flakiness
- [ ] Avoid network dependency unless integration category

---

# 📈 Step 10 — Coverage & Mutation Validation

After cleanup:

- [ ] Run code coverage
- [ ] Ensure:

    * Frame parser: 100%
    * Stream state machine: 100%
    * HPACK decoder: 100%
- [ ] Confirm critical guards cannot be removed without test failure
- [ ] Run mutation testing

---

# 🏁 Definition of Done for Phase 31

Your test suite is considered fully refactored when:

- [ ] Every test maps to an RFC section
- [ ] All MUST requirements covered
- [ ] No duplicate logical tests
- [ ] Clear separation:

    * Unit
    * Integration
    * Stress
    * Fuzz
- [ ] Coverage near 100% on critical code paths
- [ ] No flaky tests
- [ ] Test names encode RFC traceability
- [ ] Mutation testing passes

---