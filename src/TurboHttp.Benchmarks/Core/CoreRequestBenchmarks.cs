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
/// BM-CORE-001..005: Request Performance benchmarks.
/// Measures warm/cold request latency, sequential throughput,
/// and roundtrip latency for TurboHttp (raw TCP) vs HttpClient.
/// </summary>
[Config(typeof(MicroBenchmarkConfig))]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class CoreRequestBenchmarks
{
    private IHost _server = null!;
    private HttpClient _httpClient = null!;
    private TcpClient _persistentTcp = null!;
    private NetworkStream _persistentStream = null!;
    private Http11Decoder _persistentDecoder = null!;

    private const int Port = 5006;
    private const string BaseUrl = "http://127.0.0.1:5006";

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

        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _httpClient = new HttpClient(handler);

        _persistentTcp = new TcpClient();
        await _persistentTcp.ConnectAsync(IPAddress.Loopback, Port);
        _persistentStream = _persistentTcp.GetStream();
        _persistentDecoder = new Http11Decoder();

        // Warm up the persistent connection so BDN warmup iterations use a live connection.
        var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        var span = _encBuf.AsSpan();
        var written = Http11Encoder.Encode(req, ref span);
        await _persistentStream.WriteAsync(_encBuf.AsMemory(0, written));
        while (true)
        {
            var n = await _persistentStream.ReadAsync(_readBuf);
            if (n == 0)
            {
                break;
            }

            if (_persistentDecoder.TryDecode(_readBuf.AsMemory(0, n), out _))
            {
                break;
            }
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _persistentDecoder.Dispose();
        _persistentStream.Dispose();
        _persistentTcp.Dispose();
        _httpClient.Dispose();
        await _server.StopAsync();
        _server.Dispose();
    }

    /// <summary>
    /// BM-CORE-001: P50/P99 warm request latency — TurboHttp keep-alive connection.
    /// BenchmarkDotNet computes percentile statistics automatically.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task<bool> BmCore001_WarmLatency_TurboHttp()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        var span = _encBuf.AsSpan();
        var written = Http11Encoder.Encode(req, ref span);
        await _persistentStream.WriteAsync(_encBuf.AsMemory(0, written));

        while (true)
        {
            var n = await _persistentStream.ReadAsync(_readBuf);
            if (n == 0)
            {
                return false;
            }

            if (_persistentDecoder.TryDecode(_readBuf.AsMemory(0, n), out _))
            {
                return true;
            }
        }
    }

    /// <summary>
    /// BM-CORE-001b: P50/P99 warm request latency — HttpClient with pooled connection.
    /// </summary>
    [Benchmark]
    public Task<HttpResponseMessage> BmCore001b_WarmLatency_HttpClient()
        => _httpClient.GetAsync(BaseUrl);

    /// <summary>
    /// BM-CORE-002: Cold-start request latency — fresh TCP connection per invocation.
    /// Models the cost of a new connection without session reuse.
    /// </summary>
    [Benchmark]
    public async Task<bool> BmCore002_ColdStart_TurboHttp()
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
                return false;
            }

            if (decoder.TryDecode(readBuf.AsMemory(0, n), out _))
            {
                return true;
            }
        }
    }

    /// <summary>
    /// BM-CORE-003: Sequential throughput — 100 GET requests on a keep-alive connection.
    /// [OperationsPerInvoke(100)] normalises the reported mean/allocation per request.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 100)]
    public async Task BmCore003_Throughput_100Sequential()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port);
        await using var stream = tcp.GetStream();
        using var decoder = new Http11Decoder();

        var buf = new byte[512];
        var readBuf = new byte[4096];

        for (var i = 0; i < 100; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
            var span = buf.AsSpan();
            var written = Http11Encoder.Encode(req, ref span);
            await stream.WriteAsync(buf.AsMemory(0, written));

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

    /// <summary>
    /// BM-CORE-004: Single roundtrip latency over localhost — new connection per call.
    /// Baseline for absolute roundtrip cost.
    /// </summary>
    [Benchmark]
    public async Task<int> BmCore004_Roundtrip_Localhost()
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
                return 0;
            }

            if (decoder.TryDecode(readBuf.AsMemory(0, n), out var responses))
            {
                return responses.Count;
            }
        }
    }

    /// <summary>
    /// BM-CORE-005: Roundtrip with 1 ms simulated WAN propagation delay.
    /// Models the latency contribution of a single network hop.
    /// </summary>
    [Benchmark]
    public async Task<int> BmCore005_Roundtrip_SimulatedWan()
    {
        await Task.Delay(1); // simulate WAN propagation

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
                return 0;
            }

            if (decoder.TryDecode(readBuf.AsMemory(0, n), out var responses))
            {
                return responses.Count;
            }
        }
    }
}