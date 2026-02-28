using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;
using Microsoft.Extensions.Logging;
using TurboHttp.Protocol;

namespace TurboHttp.Benchmarks.Stability;

/// <summary>
/// BM-ENT-201..203: Cloud / microservice pattern benchmarks.
/// Covers gateway-style fan-out (3 and 10 parallel upstream requests per inbound call),
/// authentication token refresh overhead (stable token vs per-request refresh vs
/// every-3rd-request refresh), and telemetry streaming workload (small 2-byte payload
/// vs 1KB JSON payload, and 10 concurrent 1KB streams).
/// Server: in-process Kestrel with path-based routing on a dynamically assigned port:
///   /      → "ok" (2-byte response, models small API reply)
///   /large  → "x" × 1024 (1KB JSON body, models telemetry event payload)
/// All benchmarks use a pre-established 10-connection keep-alive pool to eliminate
/// TIME_WAIT accumulation during BenchmarkDotNet's pilot phase.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class CloudMicroserviceBenchmarks
{
    private IHost _server = null!;
    private TcpClient[] _pool = null!;
    private NetworkStream[] _streams = null!;
    private Http11Decoder[] _decoders = null!;
    private int _port;
    private string _smallUrl = null!;
    private string _largeUrl = null!;

    // Pre-computed token: reading this field incurs no allocation — baseline for
    // token-refresh benchmarks (BM-ENT-202).
    private string _cachedToken = null!;

    private const int PoolSize = 10;

    [GlobalSetup]
    public async Task Setup()
    {
        _server = Host.CreateDefaultBuilder()
            .ConfigureLogging(x => x.ClearProviders())
            .ConfigureWebHostDefaults(web =>
            {
                web.UseKestrel();
                web.UseUrls("http://127.0.0.1:0"); // OS assigns a free port
                web.Configure(app =>
                {
                    app.Run(async ctx =>
                    {
                        if (ctx.Request.Path == "/large")
                        {
                            // 1KB JSON-like telemetry payload
                            ctx.Response.ContentType = "application/json";
                            ctx.Response.ContentLength = 1024;
                            await ctx.Response.WriteAsync(new string('x', 1024));
                        }
                        else
                        {
                            ctx.Response.StatusCode = 200;
                            await ctx.Response.WriteAsync("ok");
                        }
                    });
                });
            })
            .Build();

        await _server.StartAsync();

        // Discover the dynamically assigned port.
        var kestrel = _server.Services.GetRequiredService<IServer>();
        var addrs = kestrel.Features.Get<IServerAddressesFeature>()!;
        _port = new Uri(addrs.Addresses.First()).Port;
        _smallUrl = $"http://127.0.0.1:{_port}/";
        _largeUrl = $"http://127.0.0.1:{_port}/large";

        // Pre-compute a stable bearer token for the no-refresh baseline.
        _cachedToken = $"Bearer {Guid.NewGuid():N}";

        // Pre-establish PoolSize keep-alive connections.
        _pool = new TcpClient[PoolSize];
        _streams = new NetworkStream[PoolSize];
        _decoders = new Http11Decoder[PoolSize];

        for (var i = 0; i < PoolSize; i++)
        {
            _pool[i] = new TcpClient();
            await _pool[i].ConnectAsync(IPAddress.Loopback, _port);
            _streams[i] = _pool[i].GetStream();
            _decoders[i] = new Http11Decoder();

            // Warm up each connection with a small-response request.
            await SendOnConnectionAsync(_streams[i], _decoders[i], _smallUrl);
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        for (var i = 0; i < PoolSize; i++)
        {
            _decoders[i].Dispose();
            _streams[i].Dispose();
            _pool[i].Dispose();
        }

        await _server.StopAsync();
        _server.Dispose();
    }

    // ── BM-ENT-201: Gateway-style request fan-out ─────────────────────────────

    /// <summary>
    /// BM-ENT-201a: Gateway fan-out — 3 parallel upstream requests (baseline).
    /// Fires 3 simultaneous HTTP/1.1 GET requests on 3 pre-established pooled
    /// connections, modelling a gateway that fans out one inbound client request
    /// to 3 upstream microservices in parallel and awaits all responses.
    /// Measures total fan-out latency from first dispatch to last response
    /// completion. [OperationsPerInvoke(3)] normalises to per-upstream cost.
    /// </summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = 3)]
    public Task BmEnt201a_Gateway_Fanout3()
    {
        var t0 = SendOnConnectionAsync(_streams[0], _decoders[0], _smallUrl);
        var t1 = SendOnConnectionAsync(_streams[1], _decoders[1], _smallUrl);
        var t2 = SendOnConnectionAsync(_streams[2], _decoders[2], _smallUrl);
        return Task.WhenAll(t0, t1, t2);
    }

    /// <summary>
    /// BM-ENT-201b: Gateway fan-out — 10 parallel upstream requests.
    /// Same gateway fan-out pattern using all 10 pooled connections simultaneously.
    /// The delta vs BmEnt201a reveals per-request scheduling overhead as fan-out
    /// width grows from 3 to 10: Task fan-out cost, Kestrel I/O thread scheduling
    /// under higher concurrency, and client-side async completion aggregation.
    /// [OperationsPerInvoke(10)] normalises to per-upstream cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = PoolSize)]
    public async Task BmEnt201b_Gateway_Fanout10()
    {
        var tasks = new Task[PoolSize];
        for (var i = 0; i < PoolSize; i++)
        {
            var idx = i;
            tasks[i] = SendOnConnectionAsync(_streams[idx], _decoders[idx], _smallUrl);
        }

        await Task.WhenAll(tasks);
    }

    // ── BM-ENT-202: Authentication token refresh overhead ────────────────────

    /// <summary>
    /// BM-ENT-202a: Auth token refresh — stable cached token (baseline).
    /// Sends 5 sequential requests using a pre-computed bearer token string
    /// (read from a class field, zero allocation). Models the steady-state case
    /// where a long-lived token is valid for the entire request batch — the "auth
    /// overhead" contribution is purely a field read with no string allocation.
    /// [OperationsPerInvoke(5)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 5)]
    public async Task BmEnt202a_AuthToken_StableToken()
    {
        for (var i = 0; i < 5; i++)
        {
            _ = _cachedToken; // stable token read — no allocation
            await SendOnConnectionAsync(_streams[0], _decoders[0], _smallUrl);
        }
    }

    /// <summary>
    /// BM-ENT-202b: Auth token refresh — refresh before every request.
    /// Sends 5 sequential requests, generating a new bearer token
    /// (Guid.NewGuid() formatted as a hex string) before each one. Models the
    /// worst-case authentication policy where every request requires a fresh token
    /// (e.g., single-use tokens or very short TTL). The delta vs BmEnt202a measures
    /// the per-request overhead of a token refresh cycle: Guid generation, string
    /// formatting, and the resulting heap allocation.
    /// [OperationsPerInvoke(5)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 5)]
    public async Task BmEnt202b_AuthToken_RefreshEveryRequest()
    {
        for (var i = 0; i < 5; i++)
        {
            var token = $"Bearer {Guid.NewGuid():N}"; // token refresh allocation
            _ = token; // consumed by hypothetical auth-header injection
            await SendOnConnectionAsync(_streams[1], _decoders[1], _smallUrl);
        }
    }

    /// <summary>
    /// BM-ENT-202c: Auth token refresh — refresh every 3rd request.
    /// Sends 5 sequential requests, refreshing the bearer token on requests 0
    /// and 3 (i.e., at indices 0 % 3 == 0) and reusing the cached token otherwise.
    /// Models a typical short-lived token policy (e.g., 3-minute TTL with refresh
    /// triggered every Nth request). The amortised refresh cost should be ≈ 2/5 of
    /// the per-request overhead observed in BmEnt202b.
    /// [OperationsPerInvoke(5)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 5)]
    public async Task BmEnt202c_AuthToken_RefreshEvery3rd()
    {
        string? token = null;
        for (var i = 0; i < 5; i++)
        {
            if (i % 3 == 0)
            {
                token = $"Bearer {Guid.NewGuid():N}"; // refresh on 0th and 3rd
            }

            _ = token;
            await SendOnConnectionAsync(_streams[2], _decoders[2], _smallUrl);
        }
    }

    // ── BM-ENT-203: Telemetry streaming workload ──────────────────────────────

    /// <summary>
    /// BM-ENT-203a: Telemetry streaming — small 2-byte payload (baseline).
    /// Streams a single 2-byte "ok" response over a pooled keep-alive connection.
    /// Establishes the baseline streaming pipeline cost: encoder, WriteAsync,
    /// ReadAsync, and decoder overhead with minimal payload size.
    /// </summary>
    [Benchmark]
    public Task BmEnt203a_TelemetryStream_SmallPayload()
        => SendOnConnectionAsync(_streams[3], _decoders[3], _smallUrl);

    /// <summary>
    /// BM-ENT-203b: Telemetry streaming — 1KB payload.
    /// Streams a 1024-byte JSON response over a pooled keep-alive connection,
    /// modelling a typical telemetry event or metric export payload. The delta vs
    /// BmEnt203a reveals the incremental decode overhead for a larger body:
    /// Content-Length parsing, body byte accumulation, and the additional ReadAsync
    /// data volume. Buffer size 4096 ensures the full ~1.1KB response (headers +
    /// 1024-byte body) fits in a single ReadAsync call.
    /// </summary>
    [Benchmark]
    public Task BmEnt203b_TelemetryStream_1KbPayload()
        => SendOnConnectionAsync(_streams[4], _decoders[4], _largeUrl, bufSize: 4096);

    /// <summary>
    /// BM-ENT-203c: Telemetry streaming — 10 concurrent 1KB streams.
    /// Fires 10 simultaneous 1KB streaming requests on all 10 pooled connections,
    /// modelling a telemetry aggregator receiving concurrent event streams from 10
    /// microservices. Measures parallel streaming throughput and memory allocation
    /// when decoding 10 × 1KB responses concurrently (10 × ~1.1KB in flight).
    /// [OperationsPerInvoke(10)] normalises to per-stream cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = PoolSize)]
    public async Task BmEnt203c_TelemetryStream_10Concurrent()
    {
        var tasks = new Task[PoolSize];
        for (var i = 0; i < PoolSize; i++)
        {
            var idx = i;
            tasks[i] = SendOnConnectionAsync(_streams[idx], _decoders[idx], _largeUrl, bufSize: 4096);
        }

        await Task.WhenAll(tasks);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task SendOnConnectionAsync(
        NetworkStream stream,
        Http11Decoder decoder,
        string url,
        int bufSize = 2048)
    {
        // Local buffer per call — no shared mutable state between concurrent tasks.
        var encBuf = new byte[512];
        var req = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Get, url);
        var span = encBuf.AsSpan();
        var written = Http11Encoder.Encode(req, ref span);
        await stream.WriteAsync(encBuf.AsMemory(0, written));

        var readBuf = new byte[bufSize];
        while (true)
        {
            var n = await stream.ReadAsync(readBuf);
            if (n == 0)
            {
                return;
            }

            if (decoder.TryDecode(readBuf.AsMemory(0, n), out _))
            {
                return;
            }
        }
    }
}
