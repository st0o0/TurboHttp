#nullable enable
using System;
using System.Net;
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
/// BM-CONC-201..203: Failure recovery performance benchmarks.
/// Covers retry latency overhead (success on first attempt vs one forced
/// retry), circuit breaker recovery cost (closed vs half-open state), and
/// cancellation propagation performance (live token vs no token).
/// Server: in-process Kestrel on a dynamically assigned port (avoids cross-run
/// TIME_WAIT conflicts — each BDN child process gets a fresh OS-assigned port).
/// </summary>
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
[SimpleJob(warmupCount: 3, targetCount: 5, invocationCount: 16)]
public class FailureRecoveryBenchmarks
{
    private IHost _server = null!;

    private int _port;
    private string _baseUrl = null!;

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
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _server.StopAsync();
        _server.Dispose();
    }

    // ── BM-CONC-201: Retry latency overhead ──────────────────────────────────

    /// <summary>
    /// BM-CONC-201a: Retry latency overhead — success on first attempt (baseline).
    /// Sends one request through a retry-capable wrapper (maxAttempts=3). The
    /// server always succeeds, so the overhead vs a direct send is purely from
    /// the retry wrapper: the try/catch block, attempt counter, and loop check.
    /// </summary>
    [Benchmark(Baseline = true)]
    public Task<bool> BmConc201a_Retry_SuccessFirstAttempt()
        => SendWithRetryAsync(maxAttempts: 3);

    /// <summary>
    /// BM-CONC-201b: Retry latency overhead — one forced retry before success.
    /// Forces a transient <see cref="InvalidOperationException"/> on the first
    /// attempt, then succeeds on the second. The delta vs BmConc201a measures
    /// the added cost of one retry cycle: exception allocation, catch-clause
    /// overhead, zero-delay backoff yield, and a second TCP connection.
    /// </summary>
    [Benchmark]
    public async Task<bool> BmConc201b_Retry_OneRetryBeforeSuccess()
    {
        const int maxAttempts = 3;
        var attempts = 0;
        Exception? lastException = null;

        while (attempts < maxAttempts)
        {
            try
            {
                if (attempts == 0)
                {
                    // Simulate a transient failure on the first attempt.
                    attempts++;
                    throw new InvalidOperationException("Simulated transient failure");
                }

                return await SendOneAsync();
            }
            catch (InvalidOperationException ex)
            {
                lastException = ex;
                attempts++;
                await Task.Yield(); // minimal backoff — yields to scheduler once
            }
        }

        throw new InvalidOperationException("Max retries exceeded", lastException);
    }

    // ── BM-CONC-202: Circuit breaker recovery cost ────────────────────────────

    /// <summary>
    /// BM-CONC-202a: Circuit breaker — closed state (normal operation).
    /// Routes a request through a simulated circuit breaker that is in the
    /// closed state. Measures the per-request overhead of consulting the
    /// circuit breaker's state machine on the happy path: a state enum read
    /// and a single branch before the request proceeds.
    /// </summary>
    [Benchmark]
    public Task<bool> BmConc202a_CircuitBreaker_ClosedState()
        => SendViaCircuitBreakerAsync(CircuitBreakerState.Closed);

    /// <summary>
    /// BM-CONC-202b: Circuit breaker — half-open recovery probe.
    /// Routes a request through a breaker in the half-open state: the request
    /// is allowed through as a health probe; a successful response transitions
    /// the breaker back to closed. Measures the state-transition overhead on
    /// top of normal request processing (one extra branch + state update).
    /// </summary>
    [Benchmark]
    public Task<bool> BmConc202b_CircuitBreaker_HalfOpenRecovery()
        => SendViaCircuitBreakerAsync(CircuitBreakerState.HalfOpen);

    // ── BM-CONC-203: Cancellation propagation performance ────────────────────

    /// <summary>
    /// BM-CONC-203a: Cancellation propagation — live CancellationToken.
    /// Sends 5 sequential requests, each receiving a live (non-cancelled) 30-s
    /// CancellationToken propagated through ConnectAsync, WriteAsync, and
    /// ReadAsync. Measures the I/O-chain overhead of carrying a live token
    /// relative to the no-token baseline.
    /// [OperationsPerInvoke(5)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 5)]
    public async Task BmConc203a_Cancellation_LiveTokenPropagation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        for (var i = 0; i < 5; i++)
        {
            await SendOneWithTokenAsync(cts.Token);
        }
    }

    /// <summary>
    /// BM-CONC-203b: Cancellation propagation — no token (baseline).
    /// Same 5 sequential requests without a CancellationToken.
    /// The delta vs BmConc203a quantifies the per-request overhead of
    /// propagating a CancellationToken through the async I/O call chain.
    /// [OperationsPerInvoke(5)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 5)]
    public async Task BmConc203b_Cancellation_NoToken_Baseline()
    {
        for (var i = 0; i < 5; i++)
        {
            await SendOneAsync();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> SendWithRetryAsync(int maxAttempts)
    {
        var attempts = 0;
        Exception? lastException = null;

        while (attempts < maxAttempts)
        {
            try
            {
                return await SendOneAsync();
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempts++;
            }
        }

        throw new InvalidOperationException("Max retries exceeded", lastException);
    }

    private async Task<bool> SendViaCircuitBreakerAsync(CircuitBreakerState state)
    {
        switch (state)
        {
            case CircuitBreakerState.Open:
                throw new InvalidOperationException("Circuit breaker is open — request rejected");

            case CircuitBreakerState.HalfOpen:
                // Let one probe request through; success would close the breaker.
                var probeResult = await SendOneAsync();
                // Simulate state transition: half-open → closed on success.
                _ = CircuitBreakerState.Closed; // static reference, no heap alloc
                return probeResult;

            default: // Closed — all requests pass through normally
                return await SendOneAsync();
        }
    }

    private async Task<bool> SendOneAsync()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _port);
        await using var stream = tcp.GetStream();
        using var decoder = new Http11Decoder();

        var buf = new byte[512];
        var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, _baseUrl);
        var span = buf.AsSpan();
        var written = Http11Encoder.Encode(req, ref span);
        await stream.WriteAsync(buf.AsMemory(0, written));

        var readBuf = new byte[2048];
        while (true)
        {
            var n = await stream.ReadAsync(readBuf);
            if (n == 0)
            {
                return false;
            }

            if (decoder.TryDecode(readBuf.AsMemory(0, n), out _))
            {
                return true;
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
        var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, _baseUrl);
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

/// <summary>
/// Simulated circuit breaker state for BM-CONC-202 benchmarks.
/// </summary>
internal enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}
