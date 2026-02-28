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
/// BM-ENT-101..103: Network variability simulation benchmarks.
/// Covers latency jitter tolerance (1ms client-side inter-request delay vs none),
/// packet fragmentation handling (Http11Decoder fed 1 byte at a time vs full-buffer
/// reads), and connection reset recovery (TCP RST abort + reconnect overhead).
/// Server: in-process Kestrel on a dynamically assigned port (avoids cross-run
/// TIME_WAIT conflicts — each BDN child process gets a fresh OS-assigned port).
/// invocationCount: 16 caps fresh-connection usage in BM-ENT-103 benchmarks to
/// prevent Windows TIME_WAIT exhaustion (16 × 2 conns × 8 iterations = 256 ≪ 16,384).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, targetCount: 5, invocationCount: 16)]
public class NetworkVariabilityBenchmarks
{
    private IHost _server = null!;
    private TcpClient[] _pool = null!;
    private NetworkStream[] _streams = null!;
    private Http11Decoder[] _decoders = null!;
    private int _port;
    private string _baseUrl = null!;
    private const int PoolSize = 20;

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

            // Warm up each connection with one request.
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

    // ── BM-ENT-101: Latency jitter tolerance ─────────────────────────────────

    /// <summary>
    /// BM-ENT-101a: Latency jitter tolerance — no inter-request delay (baseline).
    /// Sends 10 sequential HTTP/1.1 GET requests on a single pooled keep-alive
    /// connection with no artificial delays. Establishes the baseline throughput
    /// for the steady-state jitter-free path.
    /// [OperationsPerInvoke(10)] normalises to per-request cost.
    /// </summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = 10)]
    public async Task BmEnt101a_JitterTolerance_NoDelay()
    {
        for (var i = 0; i < 10; i++)
        {
            await SendOnConnectionAsync(_streams[0], _decoders[0]);
        }
    }

    /// <summary>
    /// BM-ENT-101b: Latency jitter tolerance — 1ms client-side inter-request delay.
    /// Sends 10 sequential requests with a 1ms Task.Delay between each, simulating
    /// network jitter that causes variable inter-request gaps. The delta vs
    /// BmEnt101a measures the async-scheduler overhead of yielding between requests
    /// during jitter windows: task re-queuing on the ThreadPool, ContinuationScheduler
    /// overhead, and the added I/O-completion latency introduced by the delay.
    /// [OperationsPerInvoke(10)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 10)]
    public async Task BmEnt101b_JitterTolerance_With1msDelay()
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(1); // simulate 1ms network jitter between requests
            await SendOnConnectionAsync(_streams[1], _decoders[1]);
        }
    }

    // ── BM-ENT-102: Packet fragmentation handling ─────────────────────────────

    /// <summary>
    /// BM-ENT-102a: Packet fragmentation — full-buffer reads (baseline).
    /// Sends 5 sequential requests and reads each response into a 2048-byte buffer.
    /// On localhost, Kestrel typically delivers the full response in a single
    /// ReadAsync call, so TryDecode succeeds on the first call per response.
    /// Establishes the baseline decoder throughput under ideal (unfragmented) delivery.
    /// [OperationsPerInvoke(5)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 5)]
    public async Task BmEnt102a_Fragmentation_FullBufferRead()
    {
        for (var i = 0; i < 5; i++)
        {
            await SendOnConnectionAsync(_streams[2], _decoders[2]);
        }
    }

    /// <summary>
    /// BM-ENT-102b: Packet fragmentation — byte-by-byte reads.
    /// Sends 5 sequential requests and feeds each response to Http11Decoder
    /// one byte at a time, simulating TCP segment fragmentation (e.g., small MTU,
    /// Nagle-disabled path, or IP-level fragmentation). This exercises the decoder's
    /// partial-data accumulation path (_remainder field) across every individual byte
    /// of the status line, header fields, and body. The delta vs BmEnt102a measures
    /// decoder overhead under worst-case receive fragmentation.
    /// [OperationsPerInvoke(5)] normalises to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 5)]
    public async Task BmEnt102b_Fragmentation_ByteByByte()
    {
        for (var i = 0; i < 5; i++)
        {
            await SendFragmentedAsync(_streams[3], _decoders[3]);
        }
    }

    // ── BM-ENT-103: Connection reset recovery ────────────────────────────────

    /// <summary>
    /// BM-ENT-103a: Connection reset recovery — normal request (baseline).
    /// Establishes a fresh TCP connection, sends one HTTP/1.1 GET request, and
    /// reads the complete response. Establishes the baseline cost of full
    /// connection-establishment + request round-trip on a single fresh connection.
    /// </summary>
    [Benchmark]
    public async Task<bool> BmEnt103a_ResetRecovery_Normal()
    {
        return await SendOneFreshAsync();
    }

    /// <summary>
    /// BM-ENT-103b: Connection reset recovery — TCP RST abort then reconnect.
    /// Opens a TCP connection and sends a request, then immediately resets the
    /// connection (LingerTime=0 triggers TCP RST instead of graceful FIN) before
    /// reading the response, simulating an unexpected mid-flight connection reset.
    /// A second fresh connection is then established and the request completes
    /// successfully. The delta vs BmEnt103a measures the total recovery overhead:
    /// RST teardown, ephemeral port reclamation, and new connection establishment.
    /// </summary>
    [Benchmark]
    public async Task<bool> BmEnt103b_ResetRecovery_AfterAbort()
    {
        // Abort phase: connect, write request, then RST (no response read).
        using (var abortTcp = new TcpClient())
        {
            await abortTcp.ConnectAsync(IPAddress.Loopback, _port);
            await using var abortStream = abortTcp.GetStream();
            var abortBuf = new byte[512];
            var abortReq = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get, _baseUrl);
            var abortSpan = abortBuf.AsSpan();
            var abortWritten = Http11Encoder.Encode(abortReq, ref abortSpan);
            await abortStream.WriteAsync(abortBuf.AsMemory(0, abortWritten));

            // Set SO_LINGER with timeout=0: closing the socket now sends TCP RST
            // instead of the normal FIN/FIN-ACK graceful teardown sequence.
            abortTcp.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.Linger,
                new LingerOption(true, 0));
        } // Socket disposed here — triggers the RST

        // Recovery phase: fresh connection, complete the request.
        return await SendOneFreshAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendOnConnectionAsync(
        NetworkStream stream,
        Http11Decoder decoder)
    {
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

    private async Task SendFragmentedAsync(
        NetworkStream stream,
        Http11Decoder decoder)
    {
        // Send the request normally (fragmentation is applied on the receive side).
        var encBuf = new byte[512];
        var req = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Get, _baseUrl);
        var span = encBuf.AsSpan();
        var written = Http11Encoder.Encode(req, ref span);
        await stream.WriteAsync(encBuf.AsMemory(0, written));

        // Read response one byte at a time to simulate TCP fragmentation:
        // each ReadAsync call delivers exactly one byte to the decoder,
        // exercising TryDecode's partial-data (_remainder) accumulation path
        // for every byte of the status line, headers, and body.
        var oneByte = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(oneByte);
            if (n == 0)
            {
                return;
            }

            if (decoder.TryDecode(oneByte.AsMemory(0, 1), out _))
            {
                return;
            }
        }
    }

    private async Task<bool> SendOneFreshAsync()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _port);
        await using var stream = tcp.GetStream();
        using var decoder = new Http11Decoder();

        var buf = new byte[512];
        var req = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Get, _baseUrl);
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
