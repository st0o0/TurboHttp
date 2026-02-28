using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using TurboHttp.Benchmarks.Utils;

namespace TurboHttp.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class HttpClientThroughputBenchmark
{
    private HttpClient _client = null!;
    private TestServer _server = null!;

    [Params(10, 100, 500)] public int RequestCount { get; set; }

    [Params("https://example.com")] public string Url { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _server = new TestServer();
        _server.Start();

        // High performance HttpClient configuration
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 100,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = DecompressionMethods.All
        };

        _client = new HttpClient(handler);
    }

    [GlobalCleanup]
    public void StopFixture()
    {
        _server.Dispose();
        _client.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task ThroughputAsync()
    {
        var tasks = new Task[RequestCount];

        for (var i = 0; i < RequestCount; i++)
        {
            tasks[i] = _client.GetAsync(Url);
        }

        await Task.WhenAll(tasks);
    }
}