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

namespace TurboHttp.Benchmarks.Concurrency;

/// <summary>
/// BM-CONC-001..004: Concurrency scaling benchmarks.
/// Uses a pre-established keep-alive connection pool (50 connections) to avoid
/// OS TIME_WAIT accumulation during BenchmarkDotNet's pilot phase. Measures
/// concurrent request throughput at 20 / 50 concurrency levels, ThreadPool
/// saturation cost, scheduling fairness variance, and async continuation
/// overhead across 50 sequential round-trips on a single connection.
/// Server: in-process Kestrel on a dynamically assigned port (avoids cross-run
/// TIME_WAIT conflicts — each BDN child process gets a fresh OS-assigned port).
/// </summary>
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class ConcurrencyScalingBenchmarks
{
    private IHost _server = null!;

    // Pre-established connection pool — 50 keep-alive connections.
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

            // Warm up each connection with one request so BDN warmup uses live,
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

    // ── BM-CONC-001: Concurrent request scaling curve ─────────────────────────

    /// <summary>
    /// BM-CONC-001a: Scaling curve — 20 concurrent requests.
    /// Fires 20 simultaneous HTTP/1.1 GET requests on 20 pre-established
    /// keep-alive connections and waits for all to complete. Models the
    /// server-side cost of dispatching 20 in-flight requests concurrently:
    /// Kestrel I/O thread allocation, response serialisation, and async
    /// completion scheduling.
    /// [OperationsPerInvoke(20)] normalises to per-request cost.
    /// </summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = 20)]
    public Task BmConc001a_Scale_20Concurrent()
        => SendPooledBatchAsync(0, 20);

    /// <summary>
    /// BM-CONC-001b: Scaling curve — 50 concurrent requests.
    /// Same pattern using all 50 pre-established connections. The delta vs
    /// BmConc001a reveals the per-request cost increase when concurrency
    /// grows 2.5×: Kestrel thread-scheduling overhead, response queue depth,
    /// and async continuation fan-out on the ThreadPool.
    /// [OperationsPerInvoke(50)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 50)]
    public Task BmConc001b_Scale_50Concurrent()
        => SendPooledBatchAsync(0, PoolSize);

    // ── BM-CONC-002: ThreadPool saturation point ──────────────────────────────

    /// <summary>
    /// BM-CONC-002: ThreadPool saturation point.
    /// Saturates the ThreadPool with CPU-bound work (ProcessorCount × 4 spinning
    /// tasks) concurrently with 20 TurboHttp async requests on pooled connections.
    /// Reveals latency degradation under ThreadPool pressure — async continuations
    /// queue behind compute workers when the pool is exhausted.
    /// [OperationsPerInvoke(20)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 20)]
    public async Task BmConc002_ThreadPool_SaturationPoint()
    {
        var workerCount = Environment.ProcessorCount * 4;
        var cpuTasks = new Task[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            cpuTasks[i] = Task.Run(() =>
            {
                var sum = 0.0;
                for (var j = 0; j < 20_000; j++)
                {
                    sum += Math.Sqrt(j);
                }

                return sum;
            });
        }

        // Issue 20 async requests on pooled connections while
        // ThreadPool workers are spinning — measures async latency
        // under compute saturation.
        await SendPooledBatchAsync(0, 20);
        await Task.WhenAll(cpuTasks);
    }

    // ── BM-CONC-003: Request scheduling fairness ──────────────────────────────

    /// <summary>
    /// BM-CONC-003: Request scheduling fairness.
    /// Fires 20 concurrent requests on pooled connections and records
    /// per-task elapsed ticks. Returns the range (max − min) of completion
    /// timestamps as a scheduling-fairness proxy: a small range indicates the
    /// async scheduler distributes work evenly across concurrent requests.
    /// [OperationsPerInvoke(20)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 20)]
    public async Task<long> BmConc003_Scheduling_Fairness()
    {
        const int count = 20;
        var timings = new long[count];
        var tasks = new Task[count];

        for (var i = 0; i < count; i++)
        {
            var idx = i;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            tasks[i] = SendOnConnectionAsync(_streams[idx], _decoders[idx])
                .ContinueWith(_ =>
                {
                    timings[idx] = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                });
        }

        await Task.WhenAll(tasks);

        var min = long.MaxValue;
        var max = long.MinValue;
        foreach (var t in timings)
        {
            if (t < min)
            {
                min = t;
            }

            if (t > max)
            {
                max = t;
            }
        }

        return max - min; // scheduling jitter in Stopwatch ticks
    }

    // ── BM-CONC-004: Async continuation overhead ──────────────────────────────

    /// <summary>
    /// BM-CONC-004: Async continuation overhead.
    /// Sends 50 sequential requests on a single pooled connection (_pool[0]).
    /// Each request involves a WriteAsync + ReadAsync await pair, producing
    /// 100 real I/O-completion continuations scheduled on the ThreadPool.
    /// Isolates continuation-scheduling cost from connection-establishment
    /// overhead (no new TCP connections are opened in this benchmark).
    /// [OperationsPerInvoke(50)] normalises to per-continuation cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 50)]
    public async Task BmConc004_Async_ContinuationOverhead()
    {
        for (var i = 0; i < 50; i++)
        {
            await SendOnConnectionAsync(_streams[0], _decoders[0]);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendPooledBatchAsync(int startIdx, int count)
    {
        var tasks = new Task[count];
        for (var i = 0; i < count; i++)
        {
            var idx = startIdx + i;
            tasks[i] = SendOnConnectionAsync(_streams[idx], _decoders[idx]);
        }

        await Task.WhenAll(tasks);
    }

    private async Task SendOnConnectionAsync(
        NetworkStream stream,
        Http11Decoder decoder)
    {
        // Local buffers: each concurrent task gets its own stack, avoiding
        // data races on shared instance-level byte arrays.
        var encBuf = new byte[512];
        var req = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Get, _baseUrl);
        var span = encBuf.AsSpan();
        var written = Http11Encoder.Encode(req, ref span);
        await stream.WriteAsync(encBuf.AsMemory(0, written));

        var readBuf = new byte[2048];
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
