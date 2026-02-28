using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
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
/// BM-CONC-101..103: Burst traffic benchmarks.
/// Covers spike load simulation (TurboHttp pooled connections vs HttpClient),
/// request queue backpressure via SemaphoreSlim over a pre-established pool,
/// and timeout handling cost with/without a CancellationToken propagated
/// through ConnectAsync, WriteAsync, and ReadAsync.
/// Server: in-process Kestrel on a dynamically assigned port (avoids cross-run
/// TIME_WAIT conflicts — each BDN child process gets a fresh OS-assigned port).
/// </summary>
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
[SimpleJob(warmupCount: 3, targetCount: 5, invocationCount: 16)]
public class BurstTrafficBenchmarks
{
    private IHost _server = null!;
    private HttpClient _httpClient = null!;

    // Pre-established keep-alive pool eliminates TIME_WAIT accumulation during
    // BenchmarkDotNet's pilot phase while still modelling spike concurrency.
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

        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = PoolSize + 10,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _httpClient = new HttpClient(handler);

        _pool = new TcpClient[PoolSize];
        _streams = new NetworkStream[PoolSize];
        _decoders = new Http11Decoder[PoolSize];

        for (var i = 0; i < PoolSize; i++)
        {
            _pool[i] = new TcpClient();
            await _pool[i].ConnectAsync(IPAddress.Loopback, _port);
            _streams[i] = _pool[i].GetStream();
            _decoders[i] = new Http11Decoder();

            // Prime each connection with one warm-up request.
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

        _httpClient.Dispose();
        await _server.StopAsync();
        _server.Dispose();
    }

    // ── BM-CONC-101: Spike load simulation ───────────────────────────────────

    /// <summary>
    /// BM-CONC-101a: Spike load — TurboHttp (0 → PoolSize concurrent).
    /// Fires all 50 pooled connections simultaneously, simulating a sudden
    /// traffic spike arriving at zero ramp-up. Models the server-side cost of
    /// dispatching PoolSize requests in parallel: Kestrel I/O scheduling,
    /// response queue depth, and client-side async fan-out.
    /// [OperationsPerInvoke(50)] normalises to per-request cost.
    /// </summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = PoolSize)]
    public async Task BmConc101a_SpikeLoad_TurboHttp()
    {
        var tasks = new Task[PoolSize];
        for (var i = 0; i < PoolSize; i++)
        {
            var idx = i;
            tasks[i] = SendOnConnectionAsync(_streams[idx], _decoders[idx]);
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// BM-CONC-101b: Spike load — HttpClient baseline.
    /// Same 50-request simultaneous burst using HttpClient with a pooled
    /// SocketsHttpHandler. Reveals the per-request cost delta between
    /// TurboHttp pooled raw-TCP and HttpClient's managed connection pool
    /// under identical burst conditions.
    /// [OperationsPerInvoke(50)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = PoolSize)]
    public async Task BmConc101b_SpikeLoad_HttpClient()
    {
        var tasks = new Task<HttpResponseMessage>[PoolSize];
        for (var i = 0; i < PoolSize; i++)
        {
            tasks[i] = _httpClient.GetAsync(_baseUrl);
        }

        var responses = await Task.WhenAll(tasks);
        foreach (var r in responses)
        {
            r.Dispose();
        }
    }

    // ── BM-CONC-102: Request queue backpressure ───────────────────────────────

    /// <summary>
    /// BM-CONC-102: Request queue backpressure performance.
    /// Submits all 50 pool connections concurrently but admits only 10 at a
    /// time through a SemaphoreSlim(10). Measures the overhead of queuing,
    /// semaphore-wait, request execution, and semaphore-release — modelling
    /// a backpressure gate over a bounded pool of upstream connections.
    /// [OperationsPerInvoke(50)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = PoolSize)]
    public async Task BmConc102_Backpressure_QueueThrottle()
    {
        using var semaphore = new SemaphoreSlim(10, 10);
        var tasks = new Task[PoolSize];
        for (var i = 0; i < PoolSize; i++)
        {
            var idx = i;
            tasks[i] = SendThrottledAsync(semaphore, _streams[idx], _decoders[idx]);
        }

        await Task.WhenAll(tasks);
    }

    // ── BM-CONC-103: Timeout handling cost ───────────────────────────────────

    /// <summary>
    /// BM-CONC-103a: Timeout handling cost — with CancellationToken.
    /// Sends 10 sequential requests, each on a fresh TCP connection carrying
    /// a live 30-second CancellationToken propagated through ConnectAsync,
    /// WriteAsync, and ReadAsync. Measures the overhead of token propagation
    /// through the full I/O chain vs the no-token baseline.
    /// Fresh connections are used to measure the complete token propagation
    /// path including ConnectAsync.
    /// [OperationsPerInvoke(10)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 10)]
    public async Task BmConc103a_Timeout_WithCancellationToken()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        for (var i = 0; i < 10; i++)
        {
            await SendOneWithTokenAsync(cts.Token);
        }
    }

    /// <summary>
    /// BM-CONC-103b: Timeout handling cost — no CancellationToken (baseline).
    /// Same 10 sequential fresh-connection requests without a CancellationToken.
    /// The delta vs BmConc103a quantifies the per-request overhead of carrying
    /// a CancellationToken through ConnectAsync, WriteAsync, and ReadAsync.
    /// [OperationsPerInvoke(10)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 10)]
    public async Task BmConc103b_Timeout_NoCancellationToken()
    {
        for (var i = 0; i < 10; i++)
        {
            await SendOneFreshAsync();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendThrottledAsync(
        SemaphoreSlim semaphore,
        NetworkStream stream,
        Http11Decoder decoder)
    {
        await semaphore.WaitAsync();
        try
        {
            await SendOnConnectionAsync(stream, decoder);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task SendOnConnectionAsync(
        NetworkStream stream,
        Http11Decoder decoder)
    {
        var encBuf = new byte[512];
        var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl);
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

    private async Task SendOneFreshAsync()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _port);
        await using var stream = tcp.GetStream();
        using var decoder = new Http11Decoder();

        var buf = new byte[512];
        var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl);
        var span = buf.AsSpan();
        var written = Http11Encoder.Encode(req, ref span);
        await stream.WriteAsync(buf.AsMemory(0, written));

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

    private async Task SendOneWithTokenAsync(CancellationToken cancellationToken)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _port, cancellationToken);
        await using var stream = tcp.GetStream();
        using var decoder = new Http11Decoder();

        var buf = new byte[512];
        var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl);
        var span = buf.AsSpan();
        var written = Http11Encoder.Encode(req, ref span);
        await stream.WriteAsync(buf.AsMemory(0, written), cancellationToken);

        var readBuf = new byte[2048];
        while (true)
        {
            var n = await stream.ReadAsync(readBuf.AsMemory(), cancellationToken);
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
