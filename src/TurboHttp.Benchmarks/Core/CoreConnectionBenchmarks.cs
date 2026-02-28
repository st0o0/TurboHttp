using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboHttp.Protocol;

namespace TurboHttp.Benchmarks.Core;

/// <summary>
/// BM-CORE-201..204: Connection handling benchmarks.
/// Measures keep-alive vs new-connection costs, TLS session-reuse overhead
/// (modelled via connection-reset), TCP connection acquisition latency,
/// and idle connection retention after a 50 ms idle period.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class CoreConnectionBenchmarks
{
    private IHost _server = null!;

    private const int Port = 5008;
    private const string BaseUrl = "http://127.0.0.1:5008";

    private readonly byte[] _encBuf = new byte[512];
    private readonly byte[] _readBuf = new byte[2048];

    [GlobalSetup]
    public async Task Setup()
    {
        _server = Host.CreateDefaultBuilder()
            .ConfigureLogging(x => x.ClearProviders())
            .ConfigureWebHostDefaults(web =>
            {
                web.UseKestrel();
                web.UseUrls($"http://127.0.0.1:{Port}");
                web.Configure(app =>
                {
                    app.Run(async ctx =>
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("pong");
                    });
                });
            })
            .Build();

        await _server.StartAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _server.StopAsync();
        _server.Dispose();
    }

    // ── BM-CORE-201: Connection reuse ratio ────────────────────────────────

    /// <summary>
    /// BM-CORE-201a: Connection reuse — 10 requests on a single keep-alive connection.
    /// Demonstrates the throughput benefit of connection reuse.
    /// </summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = 10)]
    public async Task BmCore201a_KeepAlive_10Requests()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port);
        await using var stream = tcp.GetStream();
        using var decoder = new Http11Decoder();

        for (var i = 0; i < 10; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
            var span = _encBuf.AsSpan();
            var written = Http11Encoder.Encode(req, ref span);
            await stream.WriteAsync(_encBuf.AsMemory(0, written));

            while (true)
            {
                var n = await stream.ReadAsync(_readBuf);
                if (n == 0)
                {
                    break;
                }

                if (decoder.TryDecode(_readBuf.AsMemory(0, n), out _))
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// BM-CORE-201b: No reuse — new TCP connection per request, 10 requests total.
    /// Measures the overhead cost eliminated by connection reuse.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 10)]
    public async Task BmCore201b_NewConnection_10Requests()
    {
        for (var i = 0; i < 10; i++)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, Port);
            await using var stream = tcp.GetStream();
            using var decoder = new Http11Decoder();

            var buf = new byte[512];
            var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
            var span = buf.AsSpan();
            var written = Http11Encoder.Encode(req, ref span);
            await stream.WriteAsync(buf.AsMemory(0, written));

            var readBuf = new byte[2048];
            while (true)
            {
                var n = await stream.ReadAsync(readBuf);
                if (n == 0)
                {
                    break;
                }

                if (decoder.TryDecode(readBuf.AsMemory(0, n), out _))
                {
                    break;
                }
            }
        }
    }

    // ── BM-CORE-202: TLS session reuse cost ───────────────────────────────

    /// <summary>
    /// BM-CORE-202: TLS session-reuse cost estimation.
    /// Note: TurboHttp targets plaintext TCP. This benchmark models the cost of
    /// TLS renegotiation by forcing a connection reset (close + reopen) per request.
    /// The delta vs BmCore201a represents the minimum overhead that TLS session reuse
    /// eliminates per request.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 5)]
    public async Task BmCore202_TlsSessionReuse_Estimated()
    {
        // Each iteration opens a fresh TCP connection (simulates TLS renegotiation).
        for (var i = 0; i < 5; i++)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, Port);
            await using var stream = tcp.GetStream();
            using var decoder = new Http11Decoder();

            var buf = new byte[512];
            var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
            var span = buf.AsSpan();
            var written = Http11Encoder.Encode(req, ref span);
            await stream.WriteAsync(buf.AsMemory(0, written));

            var readBuf = new byte[2048];
            while (true)
            {
                var n = await stream.ReadAsync(readBuf);
                if (n == 0)
                {
                    break;
                }

                if (decoder.TryDecode(readBuf.AsMemory(0, n), out _))
                {
                    break;
                }
            }
        }
    }

    // ── BM-CORE-203: Connection acquisition latency ───────────────────────

    /// <summary>
    /// BM-CORE-203: Connection acquisition latency.
    /// Measures only the cost of establishing a TCP connection (no request/response).
    /// This isolates the TCP handshake overhead from request processing cost.
    /// </summary>
    [Benchmark]
    public async Task BmCore203_ConnectionAcquisition_Latency()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port);
        // Immediately dispose — only TCP connect is measured.
    }

    // ── BM-CORE-204: Idle connection retention ────────────────────────────

    /// <summary>
    /// BM-CORE-204: Idle connection retention — connect, idle 50 ms, then send.
    /// Verifies that Kestrel keeps the connection alive during a brief idle period
    /// and that TurboHttp correctly resumes I/O after inactivity.
    /// </summary>
    [Benchmark]
    public async Task<bool> BmCore204_IdleConnection_Reuse()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port);
        await using var stream = tcp.GetStream();
        using var decoder = new Http11Decoder();

        await Task.Delay(50); // simulate idle period

        var buf = new byte[512];
        var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
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
}