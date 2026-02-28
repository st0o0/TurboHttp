using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using TurboHttp.Protocol;

namespace TurboHttp.Benchmarks.Core;

/// <summary>
/// BM-CORE-101..104: Memory efficiency benchmarks.
/// Measures per-request allocations, GC generation-0 pressure, LOH allocation
/// behaviour, and peak heap growth during a concurrent burst.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class CoreMemoryBenchmarks
{
    private IHost _server = null!;

    private const int Port = 5007;
    private const string BaseUrl = "http://127.0.0.1:5007";

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

    /// <summary>
    /// BM-CORE-101: Bytes allocated per single TurboHttp request (encode + send + decode).
    /// [MemoryDiagnoser] reports Allocated bytes normalised per operation.
    /// </summary>
    [Benchmark]
    public async Task<bool> BmCore101_BytesAllocated_PerRequest()
    {
        return await SendOneAsync();
    }

    /// <summary>
    /// BM-CORE-102: GC Gen0 allocation pressure over 100 sequential requests.
    /// [OperationsPerInvoke(100)] normalises the allocation rate to per-request.
    /// [MemoryDiagnoser] reports Gen0/1/2 collections per 1000 operations.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 100)]
    public async Task BmCore102_GcGen0_Per100Requests()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port);
        await using var stream = tcp.GetStream();
        using var decoder = new Http11Decoder();

        var buf = new byte[512];
        var readBuf = new byte[2048];

        for (var i = 0; i < 100; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
            var span = buf.AsSpan();
            var written = Http11Encoder.Encode(req, ref span);
            await stream.WriteAsync(buf.AsMemory(0, written));

            while (true)
            {
                var n = await stream.ReadAsync(readBuf);
                if (n == 0) { break; }
                if (decoder.TryDecode(readBuf.AsMemory(0, n), out _)) { break; }
            }
        }
    }

    /// <summary>
    /// BM-CORE-103: LOH allocation detection.
    /// Allocates a 4 MB buffer (above the 85 000-byte LOH threshold) and returns
    /// its GC generation. Expected result: 2 (Large Object Heap).
    /// </summary>
    [Benchmark]
    public int BmCore103_LohDetection_4MBBuffer()
    {
        // 4 MB is well above the 85 KB LOH threshold; GC.GetGeneration returns 2.
        var largeBuffer = new byte[4 * 1024 * 1024];
        return GC.GetGeneration(largeBuffer);
    }

    /// <summary>
    /// BM-CORE-104: Peak heap size during a burst of 10 concurrent requests.
    /// Returns the live heap growth (bytes) measured before and after the burst
    /// without forcing a collection, to capture peak live object pressure.
    /// </summary>
    [Benchmark]
    public async Task<long> BmCore104_PeakHeap_BurstLoad()
    {
        var before = GC.GetTotalMemory(false);

        var tasks = new Task[10];
        for (var i = 0; i < 10; i++)
        {
            tasks[i] = SendOneAsync();
        }

        await Task.WhenAll(tasks);

        var after = GC.GetTotalMemory(false);
        return Math.Max(0L, after - before);
    }

    private async Task<bool> SendOneAsync()
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
            if (n == 0) { return false; }
            if (decoder.TryDecode(readBuf.AsMemory(0, n), out _)) { return true; }
        }
    }
}
