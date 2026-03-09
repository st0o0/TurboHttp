# TurboHttp — Implementation Plan: Dynamic TcpOptions + Request Enrichment

---

## Context

`TurboHttpClient` is a skeleton. `SendAsync` throws `NotImplementedException`. The pipeline
exists but is wired with a static `TcpOptions` that the caller must supply before anything
starts — host, port, TLS, and timeout are not derived from the request at all.

Two concrete problems to solve:

| # | Problem | Current state |
|---|---------|---------------|
| 1 | `TcpOptions` is static | Caller supplies host/port before any request exists. Scheme-to-TLS mapping is missing. User config (timeout, reconnect) is never applied. |
| 2 | No request enrichment | `BaseAddress`, `DefaultRequestHeaders`, `DefaultRequestVersion` are stored on the client but never applied to outgoing requests. |

A third latent bug is also fixed along the way:

| # | Bug | |
|---|-----|-|
| 3 | `ClientManager` always creates `TcpClientProvider` | Even when `TlsOptions` is passed it ignores TLS. |

---

## Architecture overview (after this plan)

```
caller writes ──► ChannelWriter<HttpRequestMessage>
                         │
                         ▼  (TurboClientStreamManager materialises this graph once)
                  RequestEnricherStage   ← Phase ENR
                    ├─ Apply BaseAddress
                    ├─ Apply DefaultRequestVersion
                    └─ Merge DefaultRequestHeaders
                         │
                         ▼
                  HostRoutingStage       ← Phase HRS + TCP
                    └─ TcpOptionsFactory.Build(uri, TurboClientOptions)
                         │
                         ▼
                  Engine / ConnectionStage / ClientManager   ← Phase CLT
                    └─ TlsOptions → TlsClientProvider
                       TcpOptions → TcpClientProvider
                         │
                         ▼  Phase REQ
                  response.RequestMessage = correlatedRequest
                         │
                         ▼
                  ChannelReader<HttpResponseMessage>
                         │
caller reads ◄───────────┘


TurboHttpClient.SendAsync(request, ct)
    writes to ChannelWriter
    registers TCS in _pending map
    awaits TCS (keyed by request reference)
    ← response arrives via Sink callback → TCS.SetResult(response)
```

---

## Phase ENR — Request Enrichment Stage

**File:** `src/TurboHttp/Streams/Stages/RequestEnricherStage.cs`

The enricher lives **inside the stream graph** as a `FlowShape` stage so that enrichment
happens automatically for every element passing through the pipeline, without callers
needing to call anything explicitly.

- [x] **TASK-ENR-01** — `TurboClientOptions` defaults record

  **File:** `src/TurboHttp/Client/ITurboHttpClient.cs` (expand existing empty record)

  `TurboClientOptions` is currently `public record TurboClientOptions();`. Add TCP-level
  user config that feeds `TcpOptionsFactory` later, plus the client-level defaults that
  feed `RequestEnricherStage`.

  ```csharp
  public record TurboClientOptions
  {
      public Uri?    BaseAddress           { get; init; }
      public Version DefaultRequestVersion { get; init; } = HttpVersion.Version11;

      public TimeSpan ConnectTimeout        { get; init; } = TimeSpan.FromSeconds(10);
      public TimeSpan ReconnectInterval     { get; init; } = TimeSpan.FromSeconds(5);
      public int      MaxReconnectAttempts  { get; init; } = 10;
      public int      MaxFrameSize          { get; init; } = 128 * 1024;

      // TLS overrides — null means "decide from URI scheme"
      public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }
      public X509CertificateCollection?           ClientCertificates                  { get; init; }
      public SslProtocols                         EnabledSslProtocols                 { get; init; } = SslProtocols.None;
  }
  ```

  **Acceptance:** `new TurboClientOptions()` compiles with no required parameters.

- [ ] **TASK-ENR-02** — `RequestEnricherStage`

  **File:** `src/TurboHttp/Streams/Stages/RequestEnricherStage.cs`

  A `GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>` — every element that
  flows through is enriched in-place and forwarded downstream.

  ```csharp
  internal sealed class RequestEnricherStage
      : GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>
  {
      private readonly Uri?               _baseAddress;
      private readonly Version            _defaultVersion;
      private readonly HttpRequestHeaders _defaultHeaders;

      public RequestEnricherStage(
          Uri?               baseAddress,
          Version            defaultVersion,
          HttpRequestHeaders defaultHeaders) { ... }

      protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
          => new Logic(this);

      private sealed class Logic : InAndOutGraphStageLogic
      {
          // onPush: enrich Grab(_inlet), then Push(_outlet, enriched)
      }
  }
  ```

  Enrichment rules applied in `onPush` (same semantics as before, now inline in the stage):

  1. **URI** — if `request.RequestUri` is `null` or relative:
     - `_baseAddress` must not be `null` → `FailStage(new InvalidOperationException(...))`
     - `request.RequestUri = new Uri(_baseAddress, request.RequestUri ?? "")`

  2. **Version** — if `request.Version == HttpVersion.Version11` AND
     `_defaultVersion != HttpVersion.Version11`:
     - `request.Version = _defaultVersion`

  3. **Headers** — for each header in `_defaultHeaders`:
     - If `request.Headers` does not already contain that name → add it

  Stage is a pure pass-through: it mutates the element and pushes it forward, one-for-one.
  No buffering, no async.

- [ ] **TASK-ENR-03** — Unit tests for `RequestEnricherStage`

  **File:** `src/TurboHttp.StreamTests/Streams/RequestEnricherStageTests.cs`

  Use `Source.From(requests).Via(new RequestEnricherStage(...)).RunWith(Sink.Seq(), mat)`.
  No real TCP needed — pure stream logic test using `AkkaSpec` / `TestKit`.

  - [ ] **ENR-001** Null URI + BaseAddress → RequestUri becomes BaseAddress root
  - [ ] **ENR-002** Relative URI "/ping" + BaseAddress "http://a.test" → "http://a.test/ping"
  - [ ] **ENR-003** Absolute URI → RequestUri unchanged even when BaseAddress is set
  - [ ] **ENR-004** Null URI, null BaseAddress → stage fails with InvalidOperationException
  - [ ] **ENR-005** Relative URI, null BaseAddress → stage fails with InvalidOperationException
  - [ ] **ENR-006** request.Version == 1.1 (default), defaultVersion == 2.0 → version becomes 2.0
  - [ ] **ENR-007** request.Version == 1.1 (default), defaultVersion == 1.1 → version unchanged
  - [ ] **ENR-008** request.Version explicitly set to 1.0 → unchanged regardless of defaultVersion
  - [ ] **ENR-009** request.Version explicitly set to 2.0 → unchanged regardless of defaultVersion
  - [ ] **ENR-010** DefaultRequestHeaders has X-Foo:bar → merged into request
  - [ ] **ENR-011** Request already has X-Foo:existing → not overridden; existing value kept
  - [ ] **ENR-012** DefaultRequestHeaders has two headers → both merged
  - [ ] **ENR-013** DefaultRequestHeaders empty → no headers added; request unchanged
  - [ ] **ENR-014** Same header name, different casing in request vs defaults → treated as same; not doubled
  - [ ] **ENR-015** DefaultRequestHeaders has multiple values for one name → all values added as one entry
  - [ ] **ENR-016** 3 requests in sequence → all 3 enriched independently, order preserved

---

## Phase TCP — Dynamic `TcpOptions` factory

**File:** `src/TurboHttp/IO/TcpOptionsFactory.cs`  (new file in the IO namespace)

- [ ] **TASK-TCP-01** — `TcpOptionsFactory`

  Pure static class. Converts a request URI + user config into the correct `TcpOptions`
  (or `TlsOptions`) instance.

  ```csharp
  internal static class TcpOptionsFactory
  {
      internal static TcpOptions Build(Uri requestUri, TurboClientOptions clientOptions)
  }
  ```

  Rules:

  1. **Host** — `requestUri.Host`
  2. **Port** — `requestUri.Port` when not `-1`; else `443` for `https`/`wss`, `80` otherwise
  3. **AddressFamily** — `UriHostNameType` → `InterNetwork` / `InterNetworkV6` / `Unspecified`
  4. **TLS** — scheme is `"https"` or `"wss"` → return `TlsOptions` instead of `TcpOptions`:
     ```csharp
     new TlsOptions
     {
         Host = host, Port = port, AddressFamily = af,
         TargetHost                          = host,   // SNI
         ServerCertificateValidationCallback = clientOptions.ServerCertificateValidationCallback,
         ClientCertificates                  = clientOptions.ClientCertificates,
         EnabledSslProtocols                 = clientOptions.EnabledSslProtocols,
         ConnectTimeout        = clientOptions.ConnectTimeout,
         ReconnectInterval     = clientOptions.ReconnectInterval,
         MaxReconnectAttempts  = clientOptions.MaxReconnectAttempts,
         MaxFrameSize          = clientOptions.MaxFrameSize,
     }
     ```
  5. **Plain TCP** — all other schemes → `TcpOptions` with the same scalar fields

- [ ] **TASK-TCP-02** — Unit tests for `TcpOptionsFactory`

  **File:** `src/TurboHttp.Tests/IO/TcpOptionsFactoryTests.cs`

  - [ ] **TCP-001** "http://example.com"       → TcpOptions, Host="example.com", Port=80
  - [ ] **TCP-002** "https://example.com"      → TlsOptions, Host="example.com", Port=443
  - [ ] **TCP-003** "http://example.com:8080"  → TcpOptions, Port=8080
  - [ ] **TCP-004** "https://example.com:8443" → TlsOptions, Port=8443
  - [ ] **TCP-005** "http://1.2.3.4"           → TcpOptions, AddressFamily=InterNetwork
  - [ ] **TCP-006** "http://[::1]"             → TcpOptions, AddressFamily=InterNetworkV6
  - [ ] **TCP-007** "http://hostname"          → TcpOptions, AddressFamily=Unspecified
  - [ ] **TCP-008** clientOptions.ConnectTimeout=30s       → result.ConnectTimeout == 30s
  - [ ] **TCP-009** clientOptions.ReconnectInterval=2s     → result.ReconnectInterval == 2s
  - [ ] **TCP-010** clientOptions.MaxReconnectAttempts=3   → result.MaxReconnectAttempts == 3
  - [ ] **TCP-011** clientOptions.MaxFrameSize=256*1024    → result.MaxFrameSize == 256*1024
  - [ ] **TCP-012** "https" + ServerCertificateValidationCallback set → callback on TlsOptions
  - [ ] **TCP-013** "http"  + ServerCertificateValidationCallback set → TcpOptions (callback ignored — plain TCP)
  - [ ] **TCP-014** TlsOptions.TargetHost == Host  (SNI set automatically)
  - [ ] **TCP-015** "wss://example.com" → TlsOptions (same as https)

---

## Phase CLT — `ClientManager` TLS selection

**File:** `src/TurboHttp/IO/ClientManager.cs`

- [ ] **TASK-CLT-01** — Detect `TlsOptions` and create the right provider

  Current code always does `new TcpClientProvider(msg.Options)`.
  Change the `Handle(CreateTcpRunner)` method:

  ```csharp
  var provider = msg.StreamProvider ?? msg.Options switch
  {
      TlsOptions tls => (IClientProvider)new TlsClientProvider(tls),
      TcpOptions tcp =>                   new TcpClientProvider(tcp)
  };
  ```

  **Acceptance:** Compile. Existing tests pass.

- [ ] **TASK-CLT-02** — Unit tests for provider selection

  **File:** `src/TurboHttp.Tests/IO/ClientManagerProviderSelectionTests.cs`

  - [ ] **CLT-001** TcpOptions passed → no StreamProvider → would create TcpClientProvider
  - [ ] **CLT-002** TlsOptions passed → no StreamProvider → would create TlsClientProvider
  - [ ] **CLT-003** StreamProvider explicitly set → that provider used regardless of Options type

  > Since `TcpClientProvider.GetStream()` opens a real socket, test via the
  > `StreamProvider` injection path: pass a mock `IClientProvider` and assert the mock's
  > `GetStream()` was called. For CLT-001/002, verify indirectly via the `TcpOptions`
  > type passed to a stub.

---

## Phase HRS — `HostRoutingStage` options merging

**File:** `src/TurboHttp/Streams/HostRoutingStage.cs`

- [ ] **TASK-HRS-01** — Inject `TurboClientOptions` into `HostRoutingStage`

  `HostRoutingStage` currently builds `TcpOptions` inline with only host/port/AddressFamily.
  It has no access to user config (timeout, reconnect, TLS callbacks).

  1. Add `TurboClientOptions _clientOptions` field; set in constructor:
     ```csharp
     public HostRoutingStage(TurboClientOptions clientOptions) { ... }
     ```

  2. In `Logic.onPush`, replace the inline `new TcpOptions { ... }` block with:
     ```csharp
     var options = TcpOptionsFactory.Build(uri, _clientOptions);
     ```

  3. Pool cache key: extend from `"{host}:{port}"` to `"{scheme}:{host}:{port}"` so that
     `http://a.test:80` and `https://a.test:80` (different TLS) get separate pools.

- [ ] **TASK-HRS-02** — Unit tests for `HostRoutingStage` options merging

  **File:** `src/TurboHttp.StreamTests/Streams/HostRoutingStageOptionsTests.cs`

  Use `Source.From([request]) → HostRoutingStage → Sink.Ignore` with a materializer.
  Verify pool creation by inspecting which `TcpOptions` reach the `ConnectionStage`.
  Hook via `TestClientManagerProxy` (from TEMP.md WS-2 TASK-INF-04) or by reading
  `connectionStage.Options` from the stage after materialization.

  - [ ] **HRS-001** http URI → pool created with TcpOptions (not TlsOptions)
  - [ ] **HRS-002** https URI → pool created with TlsOptions
  - [ ] **HRS-003** clientOptions.ConnectTimeout=20s → resulting TcpOptions.ConnectTimeout == 20s
  - [ ] **HRS-004** Two requests to same host:port:scheme → same pool reused (no second creation)
  - [ ] **HRS-005** Two requests to different host → two separate pools
  - [ ] **HRS-006** http://a.test and https://a.test → two separate pools (different scheme)

---

## Phase REQ — `response.RequestMessage` correlation

`SendAsync` needs to match each `HttpResponseMessage` back to the `HttpRequestMessage`
that caused it. This requires `response.RequestMessage` to be set inside the stream.

- [ ] **TASK-REQ-01** — Audit current decoder stages

  **File:** Check `Http11DecoderStage`, `Http20StreamStage`.

  Open question: does either stage set `response.RequestMessage`?

  - If yes → skip TASK-REQ-02.
  - If no (expected) → implement TASK-REQ-02.

- [ ] **TASK-REQ-02** — Correlation in `Http11Engine` (if needed)

  **File:** `src/TurboHttp/Streams/Http11Engine.cs`

  The existing `Http11EngineTest` / `ExtractOptionsStage` / `ConnectionV2Stage` sketches
  in `Http11Engine.cs` already show this direction. Adopt that approach:

  HTTP/1.1 delivers responses strictly in order. The engine graph keeps a
  `Queue<HttpRequestMessage>` alongside the encoder:

  ```
           ┌─ EncoderStage (bytes out) ─── TCP ──→
  Request ──┤
           └─ side channel: request enqueued ─────→ DecoderStage post-process
                                                     response.RequestMessage = queue.Dequeue()
  ```

  Implementation:

  1. `RequestSplitterStage` — on each inbound `HttpRequestMessage`:
     - pushes bytes to outlet 0 (existing encoder path)
     - pushes the `HttpRequestMessage` itself to outlet 1 (side channel)

  2. `ResponseCorrelatorStage` — combines `(HttpResponseMessage, HttpRequestMessage)`:
     - waits for both; sets `response.RequestMessage = request`

  For HTTP/2, correlation already exists via stream IDs inside `Http20StreamStage` —
  verify separately (TASK-REQ-01).

- [ ] **TASK-REQ-03** — Unit tests for HTTP/1.1 correlation

  **File:** `src/TurboHttp.StreamTests/Streams/Http11ResponseCorrelationTests.cs`

  - [ ] **REQ-001** Single request/response pair → response.RequestMessage == request
  - [ ] **REQ-002** 5 sequential requests → each response.RequestMessage matches the correct request in order
  - [ ] **REQ-003** response.RequestMessage is the exact same object instance (reference equality)
  - [ ] **REQ-004** Http11Engine flow with fake TCP (EngineFakeConnectionStage) — correlation preserved

---

## Phase MGR — `TurboClientStreamManager`

**File:** `src/TurboHttp/Client/TurboClientStreamManager.cs`

The stream manager owns the graph lifecycle. It creates the channels, materialises the
pipeline once on construction, and exposes the raw `ChannelWriter`/`ChannelReader` ends
for callers to use directly.

- [ ] **TASK-MGR-01** — `TurboClientStreamManager`

  ```csharp
  public sealed class TurboClientStreamManager
  {
      public ChannelWriter<HttpRequestMessage>  Requests  { get; }
      public ChannelReader<HttpResponseMessage> Responses { get; }

      public TurboClientStreamManager(TurboClientOptions options, ActorSystem system)
      {
          var requestsChannel  = Channel.CreateUnbounded<HttpRequestMessage>();
          var responsesChannel = Channel.CreateUnbounded<HttpResponseMessage>();

          Requests  = requestsChannel.Writer;
          Responses = responsesChannel.Reader;

          var defaultHeadersHolder = new HttpRequestMessage();
          // caller populates defaultHeadersHolder.Headers externally before first use
          // — or pass HttpRequestHeaders directly in constructor

          ChannelSource
              .FromReader(requestsChannel.Reader)
              .Via(new RequestEnricherStage(
                       options.BaseAddress,
                       options.DefaultRequestVersion,
                       defaultHeadersHolder.Headers))
              .Via(Flow.FromGraph(new HostRoutingStage(options)))
              .RunWith(
                  Sink.ForEach<HttpResponseMessage>(r =>
                      responsesChannel.Writer.TryWrite(r)),
                  system.Materializer());
      }
  }
  ```

  **Open question (OQ-2 resolved):** Use `ChannelSource.FromReader` from Servus.Akka if
  available; otherwise write a minimal `ChannelReaderSource` custom stage. Check
  Servus.Akka API before implementing.

  **Acceptance:** Creating `TurboClientStreamManager` materialises the graph without
  throwing. Writing to `Requests` and reading from `Responses` works end-to-end in an
  integration test.

- [ ] **TASK-MGR-02** — Unit tests for `TurboClientStreamManager` channels

  **File:** `src/TurboHttp.StreamTests/Client/TurboClientStreamManagerTests.cs`

  - [ ] **MGR-001** Manager creates without throwing; Requests + Responses are non-null
  - [ ] **MGR-002** Writing a request to Requests channel → request appears enriched downstream (via a fake stage probe)
  - [ ] **MGR-003** Writing a response into the internal sink callback → readable from Responses channel
  - [ ] **MGR-004** Manager handles backpressure: Requests channel blocks when internal queue is full (bounded channel test)

---

## Phase CLI — `TurboHttpClient` wiring

**File:** `src/TurboHttp/Client/ITurboHttpClient.cs` + `TurboHttpClient.cs`

`TurboHttpClient` becomes a thin wrapper over `TurboClientStreamManager`. It handles
`DefaultRequestHeaders` storage, `SendAsync` correlation, and `CancelPendingRequests`.

- [ ] **TASK-CLI-01** — Fix `DefaultRequestHeaders` backing field

  `HttpRequestHeaders` cannot be instantiated directly. Borrow from a dummy message:

  ```csharp
  private readonly HttpRequestMessage _defaultHeadersHolder = new();
  public HttpRequestHeaders DefaultRequestHeaders => _defaultHeadersHolder.Headers;
  ```

  Pass `_defaultHeadersHolder.Headers` into `TurboClientStreamManager` constructor so the
  stage always sees the current state of the headers collection.

  **Acceptance:** `client.DefaultRequestHeaders.Add("X-Test", "1")` does not throw.

- [ ] **TASK-CLI-02** — `TurboHttpClient` constructor — create `TurboClientStreamManager`

  ```csharp
  public TurboHttpClient(TurboClientOptions clientOptions, ActorSystem system)
  {
      _options = clientOptions;
      _manager = new TurboClientStreamManager(clientOptions, system, DefaultRequestHeaders);

      // drain Responses channel → complete pending TCS entries
      _ = DrainResponsesAsync(_manager.Responses, _cts.Token);
  }

  private async Task DrainResponsesAsync(
      ChannelReader<HttpResponseMessage> reader,
      CancellationToken ct)
  {
      await foreach (var response in reader.ReadAllAsync(ct))
      {
          if (response.RequestMessage is not null &&
              _pending.TryRemove(response.RequestMessage, out var tcs))
          {
              tcs.TrySetResult(response);
          }
      }
  }
  ```

- [ ] **TASK-CLI-03** — Implement `SendAsync`

  ```csharp
  public async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken  cancellationToken)
  {
      var tcs = new TaskCompletionSource<HttpResponseMessage>(
          TaskCreationOptions.RunContinuationsAsynchronously);

      _pending.TryAdd(request, tcs);

      await _manager.Requests.WriteAsync(request, cancellationToken);

      return await tcs.Task.WaitAsync(Timeout, cancellationToken);
  }
  ```

  Note: enrichment (BaseAddress, version, default headers) happens inside
  `RequestEnricherStage` within the stream — `SendAsync` writes the raw request.
  The pending map key is the original request reference; `response.RequestMessage`
  must be that same reference (set by TASK-REQ-02).

- [ ] **TASK-CLI-04** — Unit tests for `TurboHttpClient.SendAsync`

  **File:** `src/TurboHttp.StreamTests/Client/TurboHttpClientSendAsyncTests.cs`

  Use `EngineFakeConnectionStage` (already in `EngineTestBase`) or a plain fake Akka
  graph to avoid real TCP. Tests must cover:

  - [ ] **CLI-001** Single request → single response returned
  - [ ] **CLI-002** BaseAddress applied before request enters pipeline — assert raw bytes reaching fake TCP contain the absolute URI
  - [ ] **CLI-003** DefaultRequestVersion applied → raw bytes use the correct request line
  - [ ] **CLI-004** DefaultRequestHeaders merged → X-Default header present in raw bytes
  - [ ] **CLI-005** Explicit headers on request not overridden by DefaultRequestHeaders
  - [ ] **CLI-006** Timeout expires before response → TaskCanceledException thrown
  - [ ] **CLI-007** CancellationToken cancelled → TaskCanceledException thrown
  - [ ] **CLI-008** 5 sequential requests all complete in order
  - [ ] **CLI-009** 10 concurrent requests all complete (Task.WhenAll)

- [ ] **TASK-CLI-05** — `CancelPendingRequests` implementation

  ```csharp
  public void CancelPendingRequests()
  {
      foreach (var (_, tcs) in _pending)
      {
          tcs.TrySetCanceled();
      }
      _pending.Clear();
  }
  ```

  - [ ] **CLI-010** CancelPendingRequests() → all in-flight SendAsync tasks throw OperationCanceledException
  - [ ] **CLI-011** After CancelPendingRequests(), new SendAsync works normally

---

## Phase ITG — Integration smoke tests

**File:** `src/TurboHttp.IntegrationTests/Client/TurboHttpClientIntegrationTests.cs`
**Fixtures:** `[Collection("Http11Integration")]`

These tests use the real `TurboHttpClient` against a live Kestrel instance (via the
existing `KestrelFixture`). They are the final proof that the entire chain works.

- [ ] **ITG-001** GET http://127.0.0.1:{Port}/ping → 200, body == "pong"
- [ ] **ITG-002** BaseAddress set → relative URI "/ping" resolves correctly
- [ ] **ITG-003** DefaultRequestVersion = 1.0 → response.Version == 1.0
- [ ] **ITG-004** DefaultRequestHeaders["X-Test"] = "hello" → /headers/echo echoes it back
- [ ] **ITG-005** POST /echo with body → body echoed, Content-Length correct
- [ ] **ITG-006** GET /status/404 → 404 status code, no exception
- [ ] **ITG-007** GET /status/500 → 500 status code, no exception
- [ ] **ITG-008** 10 concurrent GETs all return 200 (Task.WhenAll)
- [ ] **ITG-009** https URI (if TLS fixture available) → TlsOptions used, TLS handshake succeeds
- [ ] **ITG-010** Timeout = 100ms, GET /slow/500 → TaskCanceledException within ~200ms

---

## Open questions (check before starting)

| # | Question | Where to look |
|---|----------|---------------|
| OQ-1 | Does `Http11DecoderStage` or `Http20StreamStage` already set `response.RequestMessage`? | `src/TurboHttp/Streams/Stages/Http11DecoderStage.cs`, `Http20StreamStage.cs` |
| OQ-2 | Does Servus.Akka expose `ChannelSource.FromReader` or similar? If not, write a minimal `ChannelReaderSource` stage in TASK-MGR-01. | Servus.Akka source / NuGet package |
| OQ-3 | Should `DefaultRequestHeaders` be passed into `TurboClientStreamManager` as a constructor parameter (live reference), or should the stage snapshot it at startup? | If headers can be mutated after construction, pass the live `HttpRequestHeaders` reference. |
| OQ-4 | `DefaultVersionPolicy` (`HttpVersionPolicy.RequestVersionExact` vs `Negotiate`) — does `RequestEnricherStage` need to consult it before applying `DefaultRequestVersion`? | See `HttpVersionPolicy` enum meaning |
| OQ-5 | For the `Http11EngineTest` / `ExtractOptionsStage` / `ConnectionV2Stage` sketches already in `Http11Engine.cs` — should TASK-REQ-02 adopt that direction or use `RequestSplitterStage` + `ResponseCorrelatorStage`? | Discuss with Ralph before starting TASK-REQ-02 |
