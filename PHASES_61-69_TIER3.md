---

# 🚀 HTTP/2 + HPACK Implementation Phases

## RFC 9113 & RFC 7541 Deep Compliance Roadmap

---

# 🟦 TIER 3 — Connection & Framing Layer

---

## Phase 1–2: Connection Preface & ALPN

### Objectives

* Implement TLS ALPN negotiation (`h2`)
* Validate HTTP/2 client/server preface exactly

### MUST

- [ ] Require TLS 1.2+
- [ ] Reject if ALPN ≠ `h2`
- [ ] Send and verify exact connection preface
- [ ] Fail fast on malformed preface

### Tests

* Invalid preface
* Partial preface
* Wrong ALPN
* Cleartext upgrade (if supported)

---

## Phase 3–4: Frame Parsing Core

### Objectives

Implement strict frame layer parser.

### MUST

- [ ] Parse 9-byte frame header exactly
- [ ] Enforce 24-bit length
- [ ] Validate frame type
- [ ] Validate stream ID rules
- [ ] Reject frames > SETTINGS_MAX_FRAME_SIZE

### MUST NOT

- [ ] Accept unknown flag combinations
- [ ] Accept invalid frame in stream state

### Tests

* Oversized frame
* Invalid type
* Stream ID misuse
* Zero-length violations

---

# 🟦 TIER 2 — Stream State Machine

---

## Phase 5–6: Full Stream Lifecycle

### Implement States

* idle
* open
* half-closed (local/remote)
* closed

### MUST

- [ ] Enforce valid transitions
- [ ] Reject invalid frame per state
- [ ] Auto-close stream on END_STREAM
- [ ] Send RST_STREAM when required

### Tests

* Frame on closed stream
* HEADERS on half-closed
* DATA before HEADERS

---

## Phase 7: GOAWAY & RST_STREAM Handling

### MUST

- [ ] Stop new streams after GOAWAY
- [ ] Process streams ≤ last-stream-id
- [ ] Immediately terminate on connection-level error
- [ ] Clean up stream resources

---

# 🟦 TIER 3 — SETTINGS & Flow Control

---

## Phase 8–9: SETTINGS Synchronization

### MUST

- [ ] Send SETTINGS immediately after preface
- [ ] Apply peer SETTINGS only after receipt
- [ ] Send SETTINGS ACK
- [ ] Validate:

    * MAX_CONCURRENT_STREAMS
    * INITIAL_WINDOW_SIZE
    * MAX_FRAME_SIZE
    * HEADER_TABLE_SIZE

### Tests

* Invalid SETTINGS value
* Missing ACK
* SETTINGS flood

---

## Phase 10–11: Flow Control Engine

### Implement

* Connection window
* Stream window

### MUST

- [ ] Track window sizes accurately
- [ ] Decrease window on DATA sent
- [ ] Send WINDOW_UPDATE when consuming data
- [ ] Reject overflow > 2^31-1

### MUST NOT

- [ ] Send DATA when window exhausted
- [ ] Allow window wraparound

### Tests

* Window exhaustion
* Window overflow
* Missing WINDOW_UPDATE

---

# 🟦 TIER 4 — HEADERS & DATA Semantics

---

## Phase 12–13: HEADERS Validation

### MUST

- [ ] Pseudo-headers first
- [ ] No duplicate pseudo-headers
- [ ] No uppercase header names
- [ ] No connection-specific headers
- [ ] Validate required pseudo-headers:

    * :method
    * :scheme
    * :path
    * :authority

### MUST NOT

- [ ] Allow pseudo-header after normal header
- [ ] Allow invalid ordering

---

## Phase 14: CONTINUATION Frames

### MUST

- [ ] Enforce END_HEADERS
- [ ] Require contiguous CONTINUATION frames
- [ ] Reject interleaved frames

---

# 🟦 TIER 5 — HPACK Core (RFC 7541)

---

## Phase 15–16: Static Table

### MUST

- [ ] Implement full static table
- [ ] Correct index resolution
- [ ] Reject invalid indices

---

## Phase 17–18: Dynamic Table Engine

### MUST

- [ ] FIFO eviction
- [ ] Track table size precisely
- [ ] Enforce HEADER_TABLE_SIZE limit
- [ ] Apply size updates only at allowed position

### MUST NOT

- [ ] Allow table size overflow
- [ ] Desync encoder/decoder

---

## Phase 19–20: Header Block Decoding

### Implement support for:

* Indexed representation
* Literal with incremental indexing
* Literal without indexing
* Never indexed
* Dynamic table size update

### MUST

- [ ] Decode prefix integers correctly
- [ ] Validate length fields
- [ ] Detect malformed encodings

---

# 🟦 TIER 6 — Huffman & Security

---

## Phase 21–22: Huffman Decoder

### MUST

- [ ] Implement canonical Huffman tree
- [ ] Reject:

    * Invalid code
    * EOS misuse
    * Overlong padding
    * Incomplete symbol

### Tests

* Random fuzzed Huffman
* Invalid bitstream
* Truncated symbol

---

## Phase 23: Header List Size Enforcement

### MUST

- [ ] Enforce MAX_HEADER_LIST_SIZE
- [ ] Abort stream if exceeded

---

# 🟦 TIER 7 — Advanced Robustness & Hardening

---

## Phase 24–25: Resource Exhaustion Protection

### MUST DEFEND AGAINST

* SETTINGS flood
* Rapid reset attack
* CONTINUATION flood
* PING flood
* Dynamic table abuse
* Stream ID exhaustion

---

## Phase 26: Error Mapping & Correct Codes

### MUST

- [ ] Distinguish stream vs connection errors
- [ ] Map correctly:

    * PROTOCOL_ERROR
    * FLOW_CONTROL_ERROR
    * FRAME_SIZE_ERROR
    * INTERNAL_ERROR
    * REFUSED_STREAM
    * CANCEL

---

# 🟦 TIER 8 — Integration Validation

---

## Phase 27–28: Cross-Component Validation

### Ensure

- [ ] HPACK failure → connection error
- [ ] Flow control independent from header decoding
- [ ] Stream cleanup on RST
- [ ] GOAWAY stops new stream creation
- [ ] No header injection via compression

---

# 🟦 TIER 9 — Stress & Fuzz Testing

---

## Phase 29: Fuzz Harness

### Include

* Random frame ordering
* Invalid lengths
* Invalid header encodings
* Window overflow attempts
* Table resizing storms

---

## Phase 30: High-Concurrency Validation

- [ ] 10k stream creation attempts
- [ ] Parallel header decoding
- [ ] Flow control saturation
- [ ] Connection teardown under load

---

# 🏁 Final Definition of Done

You are **fully RFC 9113 + RFC 7541 compliant** when:

- [ ] Frame parser rejects all malformed frames
- [ ] Stream state machine strictly enforced
- [ ] Flow control mathematically correct
- [ ] HPACK never desynchronizes
- [ ] Huffman decoder rejects invalid sequences
- [ ] No unbounded memory growth
- [ ] All MUST/MUST NOT satisfied
- [ ] Fuzz tests produce zero crashes
- [ ] No resource exhaustion vectors

---
