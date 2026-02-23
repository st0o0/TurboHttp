# HTTP/1.0-1.1-2.0-(3.0) Implementierungs-Plan
## Akka.NET Streaming HttpClient mit TcpClientRunner Foundation

**Datum**: 24. Februar 2026  
**Ziel**: Performante `HttpRequestMessage` → `HttpResponseMessage` Streams (10k+ req/s)  
**Architektur**: Dual-Supervisoren + Akka.Streams Stages + **100% RFC-Konformität**

***

## 📋 **AUSFÜHRUNGS-TODO (12 Tage)**

### **TAG 1-2: CORE INFRASTRUKTUR** ⏱️ **2 Tage**
```
☐ [30min] HTTP Model: HttpRequestMessage/HttpResponseMessage typedefs
☐ [1h] TcpConnectionManagerActor: + HttpConnectionRequest Handler  
☐ [2h] HttpConnectionManagerActor: TOP-LEVEL HTTP SUPERVISOR
  - AcquireHttpConnection → "host:port:v1.1" → HttpConnectionPoolActor
☐ [2h] HttpConnectionPoolActor: 10x HttpConnectionActor pro Host:Port
☐ [3h] HttpConnectionActor: ChannelReader/Writer → BidiFlow Bridge
```

**🔴 Pain Points**: Actor-Refs zwischen Supervisoren, Channel Lifecycle

***

### **TAG 3-6: HTTP/1.1 STAGES** ⏱️ **4 Tage** *(KRITISCH!)*
```
HTTP/1.1 ENCODER (2 Tage):
☐ RFC 9112 Request-Line: "METHOD SP Request-Target SP HTTP/1.1"
☐ Header Folding (RFC 7230 §3.2): Multi-line Headers >8KB  
☐ Content-Length vs Transfer-Encoding: chunked
☐ Chunked Streaming: StreamContent → ByteString Pipeline
☐ Connection: close vs keep-alive State Machine

HTTP/1.1 DECODER (2 Tage):
☐ Status-Line Parser: HTTP/1.0 vs 1.1 Erkennung
☐ Header Unfolding + Malformed Recovery (RFC 7230 §2.5)
☐ Chunked Decoding inkl. Trailer Headers
☐ Partial Frame Buffering (TCP Boundaries)
☐ Pipeline State: Expect:100-continue, 408 Timeout
```

**🔴 Pain Points**: Partial Header Parsing, Chunk Extensions, Slowloris

***

### **TAG 7-11: HTTP/2.0 STAGES** ⏱️ **5 Tage** *(SEHR KRITISCH!)*
```
☐ SETTINGS Frame Negotiation + Ack Timeout
☐ HPACK Dynamic Table (RFC 7541) - Huffman + Blocking
☐ Stream State Machine (RFC 7540 §5.1): IDLE→OPEN→CLOSED 
☐ PRIORITY Tree + Dependency Handling
☐ Flow Control: WINDOW_UPDATE per Stream/Connection
☐ Server Push: PUSH_PROMISE Frame Processing
☐ RST_STREAM + GOAWAY graceful Shutdown
☐ PING Deadlock Detection
```

**🔴 Pain Points**: HPACK Table Blocking, Stream ID Exhaustion (2^31), HOL Recovery

***

### **TAG 12: HTTP/1.0 + RFC COMPLIANCE** ⏱️ **1 Tag**
```
☐ HTTP/1.0 Strict: Connection: close only (RFC 1945)
☐ RFC 7230-7235: Caching, Auth, Range Requests
☐ ETag/Last-Modified Conditional Requests
☐ Legacy Server Compatibility Mode
```

***

### **TAG 13: HTTP/3.0 BLUEPRINT** ⏱️ **1 Tag**
```
☐ QUIC Architecture: TcpConnectionManager → QuicConnectionManager
☐ QPACK über msquic Integration Blueprint
☐ Connection ID Migration Strategy
☐ 0-RTT Handshake Support Planning
```

***

### **TAG 14-16: ROBUSTNESS & LOAD TESTS** ⏱️ **3 Tage**
```
SECURITY:
☐ Buffer Overflow: MaxFrameSize Enforcement
☐ Slowloris: Request Timeout (30s)
☐ Request Smuggling: Header Normalization
☐ CRLF Injection Detection
☐ Header Injection Prevention

LOAD TESTS:
☐ Bombardier: 10k req/s → 5k req/s Ziel
☐ Connection Pool Exhaustion
☐ GC Pressure + Memory Leaks
☐ Chaos Engineering: Network Drops
```

***

## **🏗️ ARCHITEKTUR DIAGRAMM**

```
Source<HttpRequestMessage> 
       ↓
[Http11EncoderStage]
       ↓ (IMemoryOwner<byte>)
TcpConnectionManager ── TcpClientRunner ── ChannelReader/Writer
       ↑                                        ↓
HttpConnectionManager ── HttpConnectionPool ── HttpConnectionActor
       ↑                                        ↓
[Http11DecoderStage] ◄──────────────────────────┘
       ↓
Sink<HttpResponseMessage>
```

```
SUPERVISOR HIERARCHIE:
ActorSystem
├── TcpConnectionManager (TCP) ← DEIN CODE 100%
│   └── TcpClientRunner[N]
└── HttpConnectionManager (HTTP) ← TOP LEVEL!
    └── HttpConnectionPool[host:port:v1.1]
        └── HttpConnectionActor[N]
```

***

## **⚠️ KRITISCHE PAIN POINTS & MITIGATION**

| **Problem** | **Impact** | **Lösung** | **Zeit** |
|-------------|------------|------------|----------|
| **Partial Frame Parsing** | 🔴 **Blocker** | PipeReader State Machine | 2 Tage |
| **HPACK Blocking** | 🔴 **Blocker** | Dedicated Actor/Stream | 1 Tag |
| **Memory Leaks** | 🟡 **Kritisch** | IMemoryOwner Disposal Tracker | 1 Tag |
| **Backpressure** | 🟡 **Kritisch** | Channel.WaitToReadAsync() | 1 Tag |
| **Connection Lifecycle** | 🟡 **Kritisch** | IdleTimeout + HealthChecks | 1 Tag |
| **Request Smuggling** | 🔴 **Security** | Header Normalization | 0.5 Tag |

***

## **📊 PERFORMANCE ZIEL EINSTUFUNG**

| **Version** | **req/s** | **Connections** | **Latency** |
|-------------|-----------|-----------------|-------------|
| HTTP/1.1 | 8k req/s | 100 | P99 < 50ms |
| HTTP/2.0 | 50k req/s | 10 | P99 < 20ms |
| HTTP/3.0 | 100k req/s | 5 | P99 < 10ms |

***

## **✅ CHECKLIST RFC-KONFORMITÄT**

```
□ RFC 9110  HTTP Semantics (Methods/Status Codes)
□ RFC 9112  HTTP/1.1 Message Framing
□ RFC 9113  HTTP/2 Framing + HPACK
□ RFC 7230  HTTP/1.1 Header Rules
□ RFC 7231  HTTP/1.1 Methods
□ RFC 7540  HTTP/2 Protocol
□ RFC 7541  HPACK Header Compression
□ RFC 9114  HTTP/3 QUIC (Blueprint)
```

***

## **🚀 EXECUTION PRIORITÄTEN**

```
**WOCHE 1 MVP (HTTP/1.1):**
Day 1-2:  Core Infra + Dual Supervisoren ✅
Day 3-4: HTTP/1.1 Encoder Stage ✅  
Day 5-6: HTTP/1.1 Decoder Stage ✅
Day 7:   Load Tests + 5k req/s ✅

**WOCHE 2 PRODUCTION:**
Day 8-12: HTTP/2.0 Stages (HPACK + Streams)
Day 13:   HTTP/1.0 + RFC Compliance
Day 14-16: Security + Robustness
```

***

## **📦 DELIVERABLES**

```
✅ [Day 7] HTTP/1.1 MVP: 5k req/s, Chunked, Keep-Alive
✅ [Day 12] HTTP/2.0 Full: 50k req/s, HPACK, Multiplexing  
✅ [Day 16] Production Ready: Security, RFC Compliance
🔄 [Future] HTTP/3.0 Blueprint: QUIC Migration Path
```

***

**START MORGEN 9:00**: **Phase 1 (Dual Supervisoren)** → **Day 1: 14:00 READY!**

**Kritischer Path**: **HTTP/1.1 Stages (Tag 3-6)** – plane **extra Pufferzeit** für Partial Frame Parsing + Chunked Edge Cases!

***

**Brauchst du diese TODO-Liste als Markdown-File zum Download?** Ich kann sie dir als copy-paste-ready Code-Block formatieren! 🚀