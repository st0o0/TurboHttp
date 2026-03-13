# Test Audit Report

Generated: 2026-03-12 | Branch: `poc2`

---

## Section 1: Test Files Using `Http2ProtocolSession`

All 21 files are in `src/TurboHttp.Tests/RFC9113/`. Each file creates an `Http2ProtocolSession` instance to simulate server-side HTTP/2 message processing instead of calling production classes directly.

| # | File | Test Count | RFC Sections Covered |
|---|------|-----------|---------------------|
| 1 | `01_ConnectionPrefaceTests.cs` | 50 | RFC 9113 §3.4 (Connection Preface) |
| 2 | `02_FrameParsingTests.cs` | 32 | RFC 7540 §4.1 (Frame Header), §4.2 (Frame Sizes), §6.5, §6.7, §6.8, §6.9 |
| 3 | `03_StreamStateMachineTests.cs` | 25 | RFC 9113 §5.1 (Stream States), §6.1, §6.4 |
| 4 | `04_SettingsTests.cs` | 29 | RFC 7540 §3.5, §6.5 (SETTINGS), RFC 7541 §4.2 (HPACK) |
| 5 | `05_FlowControlTests.cs` | 38 | RFC 7540 §5.2 (Flow Control), §6.9, §6.9.1, §6.9.2 |
| 6 | `06_HeadersTests.cs` | 28 | RFC 9113 §8.2, §8.2.2, §8.3, §8.3.2 |
| 7 | `07_ErrorHandlingTests.cs` | 25 | RFC 7540 §5.4 (Error Codes) |
| 8 | `08_GoAwayTests.cs` | 20 | RFC 7540 §6.8 (GOAWAY), §6.4 (RST_STREAM) |
| 9 | `09_ContinuationFrameTests.cs` | 25 | RFC 9113 §6.10 (CONTINUATION) |
| 10 | `11_DecoderStreamValidationTests.cs` | 8 | RFC 7540 §5.1 (Stream States) |
| 11 | `13_DecoderStreamFlowControlTests.cs` | 5 | RFC 7540 §5.2 (Flow Control) |
| 12 | `14_DecoderErrorCodeTests.cs` | 14 | RFC 7540 §5.4 (Error Codes) |
| 13 | `15_RoundTripHandshakeTests.cs` | 19 | RFC 9113 §3.4, RFC 7540 §6 (multiple frame types) |
| 14 | `16_RoundTripMethodTests.cs` | 19 | RFC 9113 (HEADERS, CONTINUATION, HPACK) |
| 15 | `17_RoundTripHpackTests.cs` | 18 | RFC 9113, RFC 7541 (HPACK) |
| 16 | `Http2CrossComponentValidationTests.cs` | 20 | RFC 9113 (cross-component) |
| 17 | `Http2FuzzHarnessTests.cs` | 25 | RFC 9113 (fuzz testing) |
| 18 | `Http2HighConcurrencyTests.cs` | 20 | RFC 9113 (concurrency) |
| 19 | `Http2MaxConcurrentStreamsTests.cs` | 45 | RFC 9113, RFC 7540 §6.5.2 (MAX_CONCURRENT_STREAMS) |
| 20 | `Http2ResourceExhaustionTests.cs` | 30 | RFC 9113 (resource limits) |
| 21 | `Http2SecurityTests.cs` | 6 | RFC 9113 (security) |

**Total: ~521 test methods across 21 files**

---

## Section 2: RFC Sections Covered by `Http2ProtocolSession` Internals

`Http2ProtocolSession.cs` (~700 lines) is a stateful mini HTTP/2 stack used only in tests. It validates protocol rules that in production belong to `Http20ConnectionStage` and `Http20StreamStage`.

### RFC 9113 Sections Implemented

| RFC Section | Method(s) | What It Does |
|-------------|-----------|--------------|
| **§3.4 Connection Preface** | (delegated to `Http2StageTestHelper.ValidateServerPreface()`) | Validates first frame is SETTINGS on stream 0 |
| **§4.3 HPACK** | `HandleHeaders()`, `HandleContinuation()` | Decodes headers via `HpackDecoder`; throws `CompressionError` on failure |
| **§5.1 Stream States** | `GetStreamState()`, `HandleHeaders()`, `HandleData()` | Full lifecycle: Idle -> Open -> Closed; validates DATA only on Open streams; HEADERS on stream 0 -> error |
| **§6.2 HEADERS Frame** | `HandleHeaders()` | Validates stream ID, state transitions, EndHeaders/EndStream flags |
| **§6.5 SETTINGS** | `HandleSettings()`, `ApplySettingsParameters()` | Parses SETTINGS parameters; validates MAX_FRAME_SIZE [16384..16777215], ENABLE_PUSH {0,1}, INITIAL_WINDOW_SIZE <= 2^31-1 |
| **§6.5.2 SETTINGS_INITIAL_WINDOW_SIZE** | `ApplySettingsParameters()` | Delta applied to all open stream send windows; overflow check |
| **§6.7 PING** | `HandlePing()` | Counts PING requests; flood protection (>1000 frames) |
| **§6.8 GOAWAY** | `HandleGoAway()` | Sets goaway state; rejects HEADERS on streams > lastStreamId |
| **§6.9 Flow Control** | `HandleData()` | Connection & stream receive/send windows; DATA size checked against both windows |
| **§6.9.2 WINDOW_UPDATE** | `HandleWindowUpdate()` | Updates connection/stream send windows; overflow check (>0x7FFFFFFF) |
| **§6.10 CONTINUATION** | `HandleContinuation()` | Enforces CONTINUATION must follow HEADERS without END_HEADERS; flood protection (>1000 frames) |
| **§8.2 Header Names** | `ValidateHeaders()` | All header names must be lowercase; rejects forbidden connection headers (connection, keep-alive, proxy-connection, transfer-encoding, upgrade) |
| **§8.3 Pseudo-Headers** | `ValidateHeaders()` | Pseudo-headers must appear before regular headers; rejects unknown pseudo-headers |
| **§8.3.2 Response :status** | `ValidateHeaders()` | Response must contain :status; rejects duplicates; rejects request pseudo-headers in response |

### Security Protections (Non-RFC, CVE-related)

| Protection | Threshold | Implementation |
|-----------|-----------|----------------|
| CONTINUATION flood | >1000 frames | `HandleContinuation()` |
| Empty DATA exhaustion | >10000 frames | `HandleData()` |
| SETTINGS flood | >100 frames | `HandleSettings()` |
| PING flood | >1000 frames | `HandlePing()` |
| CVE-2023-44487 Rapid RST_STREAM | >100 frames | RST handling |

### Helper Files

| File | RFC Coverage |
|------|-------------|
| `Http2StageTestHelper.cs` | §3.4 (server preface validation), frame header parsing |
| `Http2IntegrationSession.cs` | **FILE DOES NOT EXIST** (referenced in memory but absent) |

---

## Section 3: Tests Missing RFC Reference in DisplayName

A test is "missing RFC reference" if:
- It has `[Fact]` or `[Theory]` without any `DisplayName` attribute (bare), OR
- Its `DisplayName` does not contain the string "RFC"

### 3.1 TurboHttp.Tests — Bare `[Fact]`/`[Theory]` (no DisplayName at all)

| File | Bare Count |
|------|-----------|
| `RFC9113/18_EncoderBaselineTests.cs` | 23 |
| `RFC1945/05_EncoderIntegrationTests.cs` | 20 |
| `RFC7541/HpackTests.cs` | 15 |
| `IO/TcpOptionsFactoryTests.cs` | 15 |
| `RFC9112/07_EncoderLegacyTests.cs` | 14 |
| `RFC1945/02_EncoderHeaderTests.cs` | 12 |
| `IO/ConnectionStateTests.cs` | 11 |
| `RFC1945/04_EncoderSecurityTests.cs` | 9 |
| `RFC1945/01_EncoderRequestLineTests.cs` | 9 |
| `RFC1945/03_EncoderBodyTests.cs` | 8 |
| `RFC9112/09_DecoderHeaderTests.cs` | 7 |
| `RFC9113/Http2FrameTests.cs` | 6 |
| `RFC9112/11_DecoderChunkedTests.cs` | 6 |
| `RFC9112/10_DecoderBodyTests.cs` | 6 |
| `RFC9112/08_DecoderStatusLineTests.cs` | 5 |
| `RFC9112/05_EncoderBodyTests.cs` | 5 |
| `IO/ClientManagerProviderSelectionTests.cs` | 3 |
| `RFC9112/02_EncoderHostHeaderTests.cs` | 3 |
| `RFC9112/04_EncoderConnectionTests.cs` | 2 |
| `RFC9112/12_DecoderNoBodyTests.cs` | 1 |
| `RFC9112/03_EncoderHeaderTests.cs` | 1 |
| `RFC9112/01_EncoderRequestLineTests.cs` | 1 |
| **Total** | **182** |

### 3.2 TurboHttp.Tests — DisplayName Present but No RFC Reference

| File | Non-RFC Count |
|------|--------------|
| `Integration/RedirectHandlerTests.cs` | 51 |
| `Integration/CookieJarTests.cs` | 42 |
| `Integration/CrossFeatureIntegrityTests.cs` | 40 |
| `Integration/RetryEvaluatorTests.cs` | 40 |
| `Integration/ConnectionReuseEvaluatorTests.cs` | 25 |
| `Integration/TcpFragmentationTests.cs` | 22 |
| `Integration/PerHostConnectionLimiterTests.cs` | 18 |
| `Integration/HttpDecodeErrorMessagesTests.cs` | 12 |
| `Integration/Phase60ValidationGateTests.cs` | 6 |
| `Integration/TurboClientOptionsTests.cs` | 6 |
| **Total** | **262** |

### 3.3 TurboHttp.StreamTests — DisplayName Present but No RFC Reference

All StreamTests have `DisplayName` attributes, but the following files use custom prefixes without RFC references:

| File | Non-RFC Count | Current Prefix |
|------|--------------|----------------|
| `Streams/RequestEnricherStageTests.cs` | 16 | `ENRICH-` |
| `Streams/RedirectStageTests.cs` | 15 | `REDIR-` |
| `Streams/RetryStageTests.cs` | 13 | `RETRY-` |
| `Streams/CacheLookupStageTests.cs` | 12 | `CLOOK-` |
| `Streams/CacheStorageStageTests.cs` | 12 | `CSTO-` |
| `Http11/Http11DecoderStageChunkedRfcTests.cs` | 11 | `11D-CH-` |
| `Streams/ConnectionReuseStageTests.cs` | 10 | `CREUSE-` |
| `Streams/DecompressionStageTests.cs` | 10 | `DECOMP-` |
| `Http20/Http20StreamStageMemoryTests.cs` | 9 | `20SM-` |
| `IO/ConnectionPoolStageTests.cs` | 8 | `POOL-STAGE-` |
| `Streams/EnginePipelineWiringTests.cs` | 8 | `EPIPE-` |
| `Http11/Http1XCorrelationStageTests.cs` | 7 | `COR1X-` |
| `Http20/Http20CorrelationStageTests.cs` | 7 | `COR20-` |
| `Stages/StageLifecycleTests.cs` | 6 | `LIFE-` |
| `Streams/CookieInjectionStageTests.cs` | 6 | `CINJ-` |
| `Streams/CookieStorageStageTests.cs` | 6 | `CSTO-` |
| `Streams/ExtractOptionsStageTests.cs` | 6 | `XOPT-` |
| `Streams/EngineVersionRoutingTests.cs` | 5 | `EROUTE-` |
| `Stages/EncoderStageBufferTests.cs` | 4 | `BUF-` |
| `Http11/Http11ResponseCorrelationTests.cs` | 4 | `11RC-` |
| **Total** | **175** |

### 3.4 TurboHttp.IntegrationTests — DisplayName Present but No RFC Reference

| File | Non-RFC Count | Current Prefix |
|------|--------------|----------------|
| `Http11/07_Http11CacheTests.cs` | 15 | `H11-CACHE-` |
| `Shared/01_TurboHttpClientTests.cs` | 10 | `CLIENT-` |
| `Http11/01_Http11BasicTests.cs` | 10 | `H11-` |
| `Http10/01_Http10EngineBasicTests.cs` | 10 | `ENG-INT-` |
| `Http20/01_Http20BasicTests.cs` | 9 | `H20-` |
| `Http11/06_Http11RetryTests.cs` | 9 | `H11-RETRY-` |
| `Shared/05_EdgeCaseTests.cs` | 8 | `EDGE-` |
| `Shared/03_CrossFeatureTests.cs` | 8 | `CROSS-` |
| `Http11/03_Http11ConnectionTests.cs` | 8 | `H11-CONN-` |
| `Http11/02_Http11ChunkedTests.cs` | 8 | `H11-CHUNK-` |
| `Http11/08_Http11ContentEncodingTests.cs` | 7 | `H11-CE-` |
| `Http11/05_Http11CookieTests.cs` | 7 | `H11-COOK-` |
| `Http11/04_Http11RedirectTests.cs` | 7 | `H11-REDIR-` |
| `Http20/06_Http20RedirectTests.cs` | 6 | `H20-REDIR-` |
| `Http20/02_Http20MultiplexTests.cs` | 6 | `H20-MUX-` |
| `Http10/04_Http10CookieTests.cs` | 6 | `H10-COOK-` |
| `Http10/03_Http10RedirectTests.cs` | 6 | `H10-REDIR-` |
| `Shared/04_TlsTests.cs` | 5 | `TLS-` |
| `Shared/02_VersionNegotiationTests.cs` | 5 | `VERNEG-` |
| `Http20/04_Http20HpackTests.cs` | 5 | `H20-HPACK-` |
| `Http10/06_Http10ContentEncodingTests.cs` | 5 | `H10-CE-` |
| `Http10/05_Http10RetryTests.cs` | 5 | `H10-RETRY-` |
| `Http10/02_Http10ConnectionTests.cs` | 5 | `H10-CONN-` |
| `Http20/11_Http20ErrorHandlingTests.cs` | 4 | `H20-ERR-` |
| `Http20/10_Http20ContentEncodingTests.cs` | 4 | `H20-CE-` |
| `Http20/09_Http20CacheTests.cs` | 4 | `H20-CACHE-` |
| `Http20/08_Http20RetryTests.cs` | 4 | `H20-RETRY-` |
| `Http20/07_Http20CookieTests.cs` | 4 | `H20-COOK-` |
| `Http20/05_Http20SettingsPingTests.cs` | 4 | `H20-SP-` |
| `Http20/03_Http20FlowControlTests.cs` | 3 | `H20-FC-` |
| **Total** | **200** |

### Grand Total: Tests Missing RFC Reference

| Project | Bare (no DisplayName) | DisplayName without RFC | Total |
|---------|----------------------|------------------------|-------|
| TurboHttp.Tests | 182 | 262 | **444** |
| TurboHttp.StreamTests | 0 | 175 | **175** |
| TurboHttp.IntegrationTests | 0 | 200 | **200** |
| **All Projects** | **182** | **637** | **819** |

---

## Section 4: Integration Test File -> RFC Folder Mapping (TurboHttp.Tests/Integration/)

These files currently live flat in `src/TurboHttp.Tests/Integration/` and should be dissolved into RFC folders within `TurboHttp.Tests/`.

| Current File | Target RFC Folder | RFC Section | Rationale |
|-------------|-------------------|-------------|-----------|
| `CookieJarTests.cs` | `RFC6265/` | RFC 6265 §5.3 | Cookie storage, domain/path matching, Secure/HttpOnly/SameSite |
| `RedirectHandlerTests.cs` | `RFC9110/` | RFC 9110 §15.4 | 301/302/303/307/308 redirect handling, method rewriting |
| `RetryEvaluatorTests.cs` | `RFC9110/` | RFC 9110 §9.2 | Idempotency-based retry, Retry-After parsing |
| `PerHostConnectionLimiterTests.cs` | `RFC9110/` | RFC 9110 | Per-host concurrency limits |
| `ConnectionReuseEvaluatorTests.cs` | `RFC9112/` | RFC 9112 §9 | Keep-alive/close decision, HTTP/1.0 opt-in |
| `HttpDecodeErrorMessagesTests.cs` | `RFC9112/` | RFC 9112 §4-§6 | Decode error classification |
| `TcpFragmentationTests.cs` | `RFC9112/` | RFC 9112 §6 / RFC 1945 | TCP boundary handling (split across 1.0 and 1.1) |
| `CrossFeatureIntegrityTests.cs` | `RFC9110/` | RFC 9110 + RFC 6265 | Cross-feature: redirects + cookies + auth header stripping |
| `Phase60ValidationGateTests.cs` | `RFC9110/` | RFC 9110 §5.1, §15 + RFC 9112 §6.3 | Cross-protocol validation (methods, status codes, headers) |
| `TurboClientOptionsTests.cs` | `RFC9110/` | RFC 9110 | Client configuration (BaseAddress, DefaultRequestVersion) |

### Suggested Numbering After Move

```
RFC6265/
  CookieJarTests.cs               (rename to match folder convention)

RFC9110/
  01_ContentEncodingDecoderTests.cs   (existing)
  02_ContentEncodingIntegrationTests.cs (existing)
  03_ContentEncodingBrotliTests.cs    (existing)
  04_RetryEvaluatorTests.cs
  05_RedirectHandlerTests.cs
  06_PerHostConnectionLimiterTests.cs
  07_CrossFeatureIntegrityTests.cs
  08_Phase60ValidationGateTests.cs
  09_TurboClientOptionsTests.cs

RFC9112/
  (01-21 existing + 3 preserved)
  22_ConnectionReuseEvaluatorTests.cs
  23_HttpDecodeErrorMessagesTests.cs
  24_TcpFragmentationTests.cs
```

---

## Section 5: StreamTests File -> RFC Folder Mapping

Currently `TurboHttp.StreamTests/` is organized by HTTP version (`Http10/`, `Http11/`, `Http20/`). The plan calls for reorganization into RFC folders.

### Http10/ -> RFC1945/

| Current File | Target | RFC Section |
|-------------|--------|-------------|
| `Http10/Http10EncoderStageTests.cs` | `RFC1945/Http10EncoderStageTests.cs` | RFC 1945 §5.1 |
| `Http10/Http10EncoderStageRfcTests.cs` | `RFC1945/Http10EncoderStageRfcTests.cs` | RFC 1945 §5.1 |
| `Http10/Http10DecoderStageTests.cs` | `RFC1945/Http10DecoderStageTests.cs` | RFC 1945 §6 |
| `Http10/Http10DecoderStageRfcTests.cs` | `RFC1945/Http10DecoderStageRfcTests.cs` | RFC 1945 §6.1, §4.2 |
| `Http10/Http10StageRoundTripMethodTests.cs` | `RFC1945/Http10StageRoundTripMethodTests.cs` | RFC 1945 §8 |
| `Http10/Http10StageRoundTripHeaderBodyTests.cs` | `RFC1945/Http10StageRoundTripHeaderBodyTests.cs` | RFC 1945 §4, §7 |
| `Http10/Http10EngineRfcRoundTripTests.cs` | `RFC1945/Http10EngineRfcRoundTripTests.cs` | RFC 1945 |
| `Http10/Http10StageTcpFragmentationTests.cs` | `RFC1945/Http10StageTcpFragmentationTests.cs` | RFC 1945 |

### Http11/ -> RFC9112/

| Current File | Target | RFC Section |
|-------------|--------|-------------|
| `Http11/Http11EncoderStageTests.cs` | `RFC9112/Http11EncoderStageTests.cs` | RFC 9112 §3.2 |
| `Http11/Http11EncoderStageRfcTests.cs` | `RFC9112/Http11EncoderStageRfcTests.cs` | RFC 9112 §3.2, §7.2 |
| `Http11/Http11DecoderStageTests.cs` | `RFC9112/Http11DecoderStageTests.cs` | RFC 9112 §6 |
| `Http11/Http11DecoderStageChunkedRfcTests.cs` | `RFC9112/Http11DecoderStageChunkedRfcTests.cs` | RFC 9112 §7.1 |
| `Http11/Http11ResponseCorrelationTests.cs` | `RFC9112/Http11ResponseCorrelationTests.cs` | RFC 9112 §9.3 |
| `Http11/Http1XCorrelationStageTests.cs` | `RFC9112/Http1XCorrelationStageTests.cs` | RFC 9112 §9.3 |
| `Http11/Http11StageRoundTripPipelineTests.cs` | `RFC9112/Http11StageRoundTripPipelineTests.cs` | RFC 9112 §9.3 |
| `Http11/Http11StageConnectionMgmtTests.cs` | `RFC9112/Http11StageConnectionMgmtTests.cs` | RFC 9112 §9.6, §9.8 |
| `Http11/Http11StageFragmentationTests.cs` | `RFC9112/Http11StageFragmentationTests.cs` | RFC 9112 §6 |
| `Http11/Http11StageStatusCodeTests.cs` | `RFC9112/Http11StageStatusCodeTests.cs` | RFC 9110 §15 |
| `Http11/Http11EngineRfcRoundTripTests.cs` | `RFC9112/Http11EngineRfcRoundTripTests.cs` | RFC 9112 |

### Http20/ -> RFC9113/

| Current File | Target | RFC Section |
|-------------|--------|-------------|
| `Http20/Http20EncoderStageTests.cs` | `RFC9113/Http20EncoderStageTests.cs` | RFC 9113 §4.1 |
| `Http20/Http20EncoderStageRfcTests.cs` | `RFC9113/Http20EncoderStageRfcTests.cs` | RFC 9113 §4.1 |
| `Http20/Http20DecoderStageTests.cs` | `RFC9113/Http20DecoderStageTests.cs` | RFC 9113 §4.1 |
| `Http20/Http20DecoderStageRfcTests.cs` | `RFC9113/Http20DecoderStageRfcTests.cs` | RFC 9113 §4.1 |
| `Http20/Http20ConnectionPrefaceRfcTests.cs` | `RFC9113/Http20ConnectionPrefaceRfcTests.cs` | RFC 9113 §3.4 |
| `Http20/Http20ConnectionStageSettingsTests.cs` | `RFC9113/Http20ConnectionStageSettingsTests.cs` | RFC 9113 §6.5 |
| `Http20/Http20ConnectionStagePingTests.cs` | `RFC9113/Http20ConnectionStagePingTests.cs` | RFC 9113 §6.7 |
| `Http20/Http20ConnectionStageGoAwayTests.cs` | `RFC9113/Http20ConnectionStageGoAwayTests.cs` | RFC 9113 §6.8 |
| `Http20/Http20ConnectionStageFlowControlTests.cs` | `RFC9113/Http20ConnectionStageFlowControlTests.cs` | RFC 9113 §6.9 |
| `Http20/Http20StreamStageTests.cs` | `RFC9113/Http20StreamStageTests.cs` | RFC 9113 §8 |
| `Http20/Http20StreamStageMemoryTests.cs` | `RFC9113/Http20StreamStageMemoryTests.cs` | RFC 9113 §8 |
| `Http20/Http20StreamIdRfcTests.cs` | `RFC9113/Http20StreamIdRfcTests.cs` | RFC 9113 §5.1.1 |
| `Http20/StreamIdAllocatorStageTests.cs` | `RFC9113/StreamIdAllocatorStageTests.cs` | RFC 9113 §5.1.1 |
| `Http20/Http20PseudoHeaderRfcTests.cs` | `RFC9113/Http20PseudoHeaderRfcTests.cs` | RFC 9113 §8.3 |
| `Http20/Http20ForbiddenHeaderRfcTests.cs` | `RFC9113/Http20ForbiddenHeaderRfcTests.cs` | RFC 9113 §8.2.2 |
| `Http20/Http20HpackStreamTests.cs` | `RFC9113/Http20HpackStreamTests.cs` | RFC 7541 |
| `Http20/Http20CorrelationStageTests.cs` | `RFC9113/Http20CorrelationStageTests.cs` | RFC 9113 §5.1 |
| `Http20/Http20EngineRfcRoundTripTests.cs` | `RFC9113/Http20EngineRfcRoundTripTests.cs` | RFC 9113 |
| `Http20/Request2FrameStageTests.cs` | `RFC9113/Request2FrameStageTests.cs` | RFC 9113 §8.1 |
| `Http20/PrependPrefaceStageTests.cs` | `RFC9113/PrependPrefaceStageTests.cs` | RFC 9113 §3.4 |

### Unchanged Folders

| Current Folder | Stays As | Rationale |
|---------------|----------|-----------|
| `Streams/` | `Streams/` | Business logic stages (Redirect, Retry, Cookie, Cache, Decompression) — cross-cutting, no single RFC |
| `Stages/` | `Stages/` | Cross-cutting stage infrastructure (lifecycle, buffer management) |
| `IO/` | `IO/` | Connection pool stage — infrastructure, no direct RFC |

---

## Open Questions (from plan_5.md)

1. **`Phase60ValidationGateTests.cs`** — covers RFC 9110 (methods, status codes, headers) and RFC 9112 (Transfer-Encoding + Content-Length conflict). Recommended target: `RFC9110/` since the majority of tests are semantics-focused.

2. **`EngineVersionRoutingTests.cs`** — tests internal Engine version demultiplexing without a direct RFC clause. An internal code tag `EROUTE-` is sufficient; alternatively reference RFC 9112/RFC 9113 version negotiation.

3. **`Http2StageTestHelper.cs`** — still needed after PSS tasks as a frame-level decode helper. Should be moved to `RFC9113/` folder.
