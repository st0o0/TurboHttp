# PROJECT_CONTEXT.md — TurboHttp

> **For Claude Code:** This file describes the project's purpose, architecture, current state, and invariants.
> Read this before making any changes. Update it when significant architectural decisions are made.

---

## What Is This Project?

**TurboHttp** is a high-performance HTTP protocol library for .NET 10, built on top of Akka.NET.
It implements the **client side** of HTTP/1.0, HTTP/1.1, and HTTP/2 with full RFC compliance.

**Scope is strictly client-side:**
- **Encoders** turn `HttpRequestMessage` into bytes (client sends request)
- **Decoders** turn bytes into `HttpResponseMessage` (client receives response)
- Server-side request parsing and response encoding are **out of scope**

---

## Repository Layout

```
D:/GIT/Akka.Streams.Http/
├── src/
│   ├── TurboHttp/                        # Main library (.NET 10)
│   │   ├── Protocol/                     # Pure protocol logic (no I/O)
│   │   │   ├── Http10Encoder.cs          # RFC 1945 request encoder
│   │   │   ├── Http10Decoder.cs          # RFC 1945 response decoder
│   │   │   ├── Http11Encoder.cs          # RFC 9112 request encoder
│   │   │   ├── Http11Decoder.cs          # RFC 9112 response decoder
│   │   │   ├── Http2Encoder.cs           # RFC 7540 request encoder
│   │   │   ├── Http2Decoder.cs           # RFC 7540 response decoder
│   │   │   ├── Http2Frame.cs             # Frame types + serialization
│   │   │   ├── Http2FrameWriter.cs       # Frame write helpers
│   │   │   ├── HpackEncoder.cs           # RFC 7541 header compression
│   │   │   ├── HpackDecoder.cs           # RFC 7541 header decompression
│   │   │   ├── HuffmanCodec.cs           # Huffman en/decode (Appendix B)
│   │   │   ├── WellKnownHeaders.cs       # ReadOnlySpan<byte> constants
│   │   │   ├── HttpDecodeResult.cs       # Result<T> for decoders
│   │   │   ├── HttpDecodeError.cs        # Error enum (20+ cases)
│   │   │   ├── HttpDecoderException.cs   # Exception wrapper
│   │   │   ├── Http2DecodeResult.cs      # Rich HTTP/2 result type
│   │   │   ├── Http2Exception.cs         # HTTP/2 protocol exception
│   │   │   └── Http2SizePredictor.cs     # Buffer pre-allocation helper
│   │   └── IO/                           # Akka.NET actor-based I/O
│   │       ├── TcpConnectionManager.cs   # Actor supervisor
│   │       ├── TcpClientRunner.cs        # Per-connection lifecycle
│   │       ├── TcpClientState.cs         # Shared channel state
│   │       └── TcpClientByteMover.cs     # TCP ↔ channel bridge
│   ├── TurboHttp.Tests/                  # Unit tests (xUnit)
│   ├── TurboHttp.IntegrationTests/       # Integration tests
│   └── TurboHttp.sln
├── tasks/                                # PRD task files (for Ralph agent)
├── CLAUDE.md                             # Claude instructions (authoritative)
├── PROJECT_CONTEXT.md                    # This file
├── TOOLING.md                            # Build/test/debug workflows
├── RFC_TEST_MATRIX.md                    # 150+ RFC test cases with coverage status
├── IMPLEMENTATION_PLAN.md                # 14-week phased roadmap
└── QUICK_REFERENCE.md                    # Code patterns & templates
```

---

## Architecture

### Layered Design

```
┌─────────────────────────────────────────────┐
│             Protocol Layer                   │
│  Http10/Http11/Http2 Encoders + Decoders    │
│  HPACK (HpackEncoder/Decoder + Huffman)     │
│  Pure functions / stateful, no I/O          │
└─────────────────────┬───────────────────────┘
                      │
┌─────────────────────▼───────────────────────┐
│               I/O Layer                      │
│  Akka.NET Actors + System.Threading.Channels│
│  TcpConnectionManagerActor                  │
│  TcpClientRunner (per-connection)           │
└─────────────────────┬───────────────────────┘
                      │
┌─────────────────────▼───────────────────────┐
│              Network (TCP)                   │
│  Servus.Akka TcpStream abstraction          │
└─────────────────────────────────────────────┘
```

### Encoder Contract

```csharp
// Stateless — static methods only
// Input:  HttpRequestMessage
// Output: bytes written into caller-provided buffer
// Pattern: ref Span<byte> or IBufferWriter<byte> for zero-allocation

Http10Encoder.Encode(request, ref buffer);
Http11Encoder.Encode(request, ref span);
Http2Encoder.Encode(request, writer);  // IBufferWriter<byte>
```

### Decoder Contract

```csharp
// Stateful — holds _remainder for partial frames across TCP reads
// Input:  ReadOnlyMemory<byte> (may be partial)
// Output: HttpDecodeResult / Http2DecodeResult
// Must call Reset() between connections

decoder.TryDecode(data, out result);     // normal path
decoder.TryDecodeEof(out result);        // connection closed path
decoder.Reset();                          // reuse for new connection
```

---

## Current Implementation Status

### Protocol Layer

| Component | Status | RFC | Notes |
|-----------|--------|-----|-------|
| Http10Encoder | Complete | RFC 1945 | No Host/TE/Connection headers emitted |
| Http10Decoder | Mostly complete | RFC 1945 | Missing: 304 no-body, chunked-as-raw-body |
| Http11Encoder | Complete | RFC 9112 | Zero-allocation, mandatory Host header |
| Http11Decoder | Complete | RFC 9112 | Chunked, pipelining, configurable limits |
| Http2Encoder | Complete | RFC 7540 | Stream IDs, HPACK, continuation frames |
| Http2Decoder | Complete | RFC 7540 | Frame dispatch, stream state, HPACK |
| HpackEncoder | Complete | RFC 7541 | NeverIndexed for sensitive headers |
| HpackDecoder | Complete | RFC 7541 | Dynamic table, static table |
| HuffmanCodec | Complete | RFC 7541 | Appendix B table |

### Known Gaps (from RFC_TEST_MATRIX.md)

| Test ID | Gap | Priority |
|---------|-----|----------|
| 1945-enc-001..009 | Http10Encoder tests all missing | P0 |
| 1945-dec-003 | Partial: not all RFC 1945 status codes tested | P1 |
| 1945-dec-004 | 304 Not Modified → no body | P0 |
| 1945-dec-006 | Chunked transfer-encoding as raw body (HTTP/1.0) | P1 |
| RFC 7230/7231 | Most encoder/decoder tests not yet written | P0 |
| RFC 7540 | Stream state, flow control tests incomplete | P0 |
| RFC 7541 | Appendix C examples not tested | P1 |

---

## Key Design Invariants

### Zero-Allocation Hot Paths
- Encoders MUST use `ref Span<byte>` or `IBufferWriter<byte>` — no string allocations
- `WellKnownHeaders.cs` provides `ReadOnlySpan<byte>` constants for all header names
- Temporary buffers use `ArrayPool<byte>.Shared` and must be returned in `finally`

### Stateful Decoders
- Every decoder holds `_remainder` for partial TCP frames
- On success, `_remainder` is updated to leftover bytes
- `Reset()` clears all state — must be called between connections
- Decoders that allocate implement `IDisposable`

### Error Handling
- `HttpDecodeError` enum (not exceptions) for parse failures — callers inspect result
- `HpackException` for RFC 7541 violations (compression errors → connection-level error)
- `Http2Exception` for RFC 7540 protocol errors (with `Http2ErrorCode`)
- Never swallow errors silently; always propagate via typed result

### Testing
- Test files mirror implementation: `Http11Decoder.cs` → `Http11DecoderTests.cs`
- All test methods use `[DisplayName]` with `Should_ExpectedBehavior_When_Condition` pattern
- Round-trip tests: encode request → decode response (not just one direction)
- RFC test IDs from `RFC_TEST_MATRIX.md` should appear in test `[DisplayName]`

### C# Style
- Allman braces (opening brace on new line)
- 4 spaces, no tabs
- Private fields: `_camelCase`
- `sealed` by default; `#nullable enable` on all new/modified files
- Never `async void`, never `.Result`/`.Wait()`

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Akka.Streams | 1.5.60 | Actor-based stream processing |
| Servus.Akka | 0.3.10 | TCP abstraction layer |
| .NET | 10.0 | Target framework |
| xUnit | 2.9.3 | Unit test framework |
| Akka.Streams.TestKit | 1.5.60 | Akka test utilities |

---

## RFC Compliance Scope

| RFC | Title | Status |
|-----|-------|--------|
| RFC 1945 | HTTP/1.0 | Client encode+decode |
| RFC 7230 | HTTP/1.1 Message Syntax | Client encode+decode |
| RFC 7231 | HTTP/1.1 Semantics | Status codes, methods |
| RFC 7233 | Range Requests | P2 — planned |
| RFC 7232 | Conditional Requests | P2 — planned |
| RFC 7540 | HTTP/2 | Client encode+decode |
| RFC 7541 | HPACK | Full encoder+decoder |
| RFC 9112 | HTTP/1.1 (replaces 7230) | Client encode+decode |

---

## Useful Entry Points for Navigation

| Task | File |
|------|------|
| Add HTTP/1.1 encoder test | `src/TurboHttp.Tests/Http11EncoderTests.cs` |
| Add HTTP/1.1 decoder test | `src/TurboHttp.Tests/Http11DecoderTests.cs` |
| Fix HTTP/2 frame parsing | `src/TurboHttp/Protocol/Http2Decoder.cs` |
| Add HPACK test | `src/TurboHttp.Tests/HpackTests.cs` |
| Fix encoder zero-alloc | `src/TurboHttp/Protocol/Http11Encoder.cs` |
| Add header constant | `src/TurboHttp/Protocol/WellKnownHeaders.cs` |
| Error enum | `src/TurboHttp/Protocol/HttpDecodeError.cs` |
| RFC test matrix | `RFC_TEST_MATRIX.md` |
| Phased plan | `IMPLEMENTATION_PLAN.md` |