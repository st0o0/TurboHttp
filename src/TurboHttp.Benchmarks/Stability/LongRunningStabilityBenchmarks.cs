using System;
using System.Buffers;
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
using System.Net.Http;
using Microsoft.Extensions.Logging;
using TurboHttp.Protocol;

namespace TurboHttp.Benchmarks.Stability;

/// <summary>
/// BM-ENT-001..005: Long-running stability benchmarks.
/// Models steady-state sustained load (24-hour scenario kernel), high-volume
/// throughput capacity (10M request extrapolation basis), memory growth stability
/// across batch sizes, connection reuse efficiency under concurrent load, and
/// sustained concurrent throughput.
/// Server: in-process Kestrel on a dynamically assigned port (avoids cross-run
/// TIME_WAIT conflicts — each BDN child process gets a fresh OS-assigned port).
/// All benchmarks use a pre-established 50-connection keep-alive pool to avoid
/// OS TIME_WAIT accumulation during BenchmarkDotNet's pilot phase.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class LongRunningStabilityBenchmarks
{
    private IHost _server = null!;
    private TcpClient[] _pool = null!;
    private NetworkStream[] _streams = null!;
    private Http11Decoder[] _decoders = null!;
    private int _port;
    private string _baseUrl = null!;
    private const int PoolSize = 50;

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
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("ok");
                    });
                });
            })
            .Build();

        await _server.StartAsync();

        // Discover the dynamically assigned port.
        var kestrel = _server.Services.GetRequiredService<IServer>();
        var addrs = kestrel.Features.Get<IServerAddressesFeature>()!;
        _port = new Uri(addrs.Addresses.First()).Port;
        _baseUrl = $"http://127.0.0.1:{_port}";

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

            // Warm up each connection so BDN warmup iterations use live,
            // primed connections (avoids first-request JIT/connection overhead).
            await SendOnConnectionAsync(_streams[i], _decoders[i]);
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

    // ── BM-ENT-001: Sustained load throughput ────────────────────────────────

    /// <summary>
    /// BM-ENT-001: Sustained load throughput — 24-hour simulation kernel.
    /// Sends 100 sequential HTTP/1.1 GET requests on a single pre-established
    /// keep-alive connection. Models the steady-state throughput kernel of a
    /// 24-hour sustained load scenario: no connection-establishment overhead,
    /// purely the request/response pipeline (encode, WriteAsync, ReadAsync,
    /// decode). [OperationsPerInvoke(100)] normalises to per-request cost.
    /// </summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = 100)]
    public async Task BmEnt001_SustainedLoad_100Requests()
    {
        for (var i = 0; i < 100; i++)
        {
            await SendOnConnectionAsync(_streams[0], _decoders[0]);
        }
    }

    // ── BM-ENT-002: High-volume throughput ───────────────────────────────────

    /// <summary>
    /// BM-ENT-002: High-volume request throughput — 10M request extrapolation.
    /// Sends 1000 sequential requests on a single keep-alive connection.
    /// The Req/sec column gives the extrapolation basis: mean_ns × 1e7 projects
    /// the total wall-clock duration required to process 10 million requests
    /// at steady state. [OperationsPerInvoke(1000)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 1000)]
    public async Task BmEnt002_HighVolumeThroughput_1000Requests()
    {
        for (var i = 0; i < 1000; i++)
        {
            await SendOnConnectionAsync(_streams[1], _decoders[1]);
        }
    }

    // ── BM-ENT-003: Memory growth stability ──────────────────────────────────

    /// <summary>
    /// BM-ENT-003a: Memory stability — batch 10.
    /// Sends 10 sequential requests and reports [MemoryDiagnoser] allocated
    /// bytes per operation. Compare with BmEnt003b and BmEnt003c: if the
    /// allocated-bytes/op value is constant across all three batch sizes,
    /// memory growth per unit of work is flat (slope ≈ 0 per extra request),
    /// satisfying the "memory growth slope &lt; linear" production requirement.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 10)]
    public async Task BmEnt003a_MemoryStability_Batch10()
    {
        for (var i = 0; i < 10; i++)
        {
            await SendOnConnectionAsync(_streams[2], _decoders[2]);
        }
    }

    /// <summary>
    /// BM-ENT-003b: Memory stability — batch 100.
    /// Sends 100 sequential requests. Allocated bytes/op should match
    /// BmEnt003a, confirming that the HTTP pipeline does not accumulate
    /// per-batch or per-connection-reuse overhead.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 100)]
    public async Task BmEnt003b_MemoryStability_Batch100()
    {
        for (var i = 0; i < 100; i++)
        {
            await SendOnConnectionAsync(_streams[3], _decoders[3]);
        }
    }

    /// <summary>
    /// BM-ENT-003c: Memory stability — batch 1000.
    /// Sends 1000 sequential requests. If allocated bytes/op remains constant
    /// vs BmEnt003a and BmEnt003b across 10×, 100×, and 1000× batch sizes,
    /// the pipeline's memory growth slope is flat (zero per-request accumulation),
    /// confirming there is no allocator growth, cached-array bloat, or decoder
    /// remainder accumulation under sustained load.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 1000)]
    public async Task BmEnt003c_MemoryStability_Batch1000()
    {
        for (var i = 0; i < 1000; i++)
        {
            await SendOnConnectionAsync(_streams[4], _decoders[4]);
        }
    }

    // ── BM-ENT-004: Connection reuse efficiency ───────────────────────────────

    /// <summary>
    /// BM-ENT-004: Connection reuse efficiency — 50 concurrent requests.
    /// Fires all 50 pre-established keep-alive connections simultaneously,
    /// measuring the server's sustained-concurrency throughput when every
    /// connection is in active use. No connection establishment overhead —
    /// purely Kestrel dispatch, response serialisation, and client-side
    /// async fan-out on the pooled connections.
    /// [OperationsPerInvoke(50)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = PoolSize)]
    public async Task BmEnt004_ConnectionReuse_50Concurrent()
    {
        var tasks = new Task[PoolSize];
        for (var i = 0; i < PoolSize; i++)
        {
            tasks[i] = SendOnConnectionAsync(_streams[i], _decoders[i]);
        }

        await Task.WhenAll(tasks);
    }

    // ── BM-ENT-005: Sustained concurrent throughput ───────────────────────────

    /// <summary>
    /// BM-ENT-005: Sustained concurrent load — 20 concurrent requests.
    /// Fires 20 simultaneous requests on 20 pre-established connections,
    /// modelling a production environment where 20 consumers share a keep-alive
    /// connection pool against a single upstream service. The delta vs
    /// BmEnt001 (sequential) reveals the async fan-out overhead introduced by
    /// concurrent dispatch: Task.WhenAll scheduling, Kestrel I/O thread
    /// allocation, and response-completion ordering.
    /// [OperationsPerInvoke(20)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 20)]
    public async Task BmEnt005_SustainedConcurrentLoad_20()
    {
        var tasks = new Task[20];
        for (var i = 0; i < 20; i++)
        {
            tasks[i] = SendOnConnectionAsync(_streams[i], _decoders[i]);
        }

        await Task.WhenAll(tasks);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task SendOnConnectionAsync(NetworkStream stream, Http11Decoder decoder)
    {
        var encBuf = ArrayPool<byte>.Shared.Rent(512);
        var readBuf = ArrayPool<byte>.Shared.Rent(2048);
        try
        {
            // Local buffers: each concurrent task gets its own stack allocation,
            // avoiding data races on shared instance-level byte arrays.

            var req = new HttpRequestMessage(
                HttpMethod.Get, _baseUrl);
            var span = encBuf.AsSpan();
            var written = Http11Encoder.Encode(req, ref span);
            await stream.WriteAsync(encBuf.AsMemory(0, written));


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
        finally
        {
            ArrayPool<byte>.Shared.Return(encBuf);
            ArrayPool<byte>.Shared.Return(readBuf);
        }
    }
}