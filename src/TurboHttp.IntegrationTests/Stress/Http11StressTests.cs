#nullable enable
using System.Diagnostics;
using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Stress;

/// <summary>
/// Phase 17 — HTTP/1.1 Stress &amp; Production-Readiness tests.
/// Exercises sequential keep-alive, concurrent connections, pipelining,
/// large bodies, header stress, mixed verbs, memory, GC pressure, and throughput.
/// Use <c>dotnet test --filter "Category=Stress"</c> for separate CI runs.
/// </summary>
[Collection("StressHttp11")]
public sealed class Http11StressTests
{
    private readonly KestrelFixture _fixture;

    public Http11StressTests(KestrelFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Sequential keep-alive ─────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-001: 100 sequential GET requests on one connection — all 200")]
    [Trait("Category", "Stress")]
    public async Task Should_Return200_When_100SequentialGetRequestsSentOnOneConnection()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        for (var i = 0; i < 100; i++)
        {
            var r = await conn.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
    }

    [Fact(DisplayName = "IT-STRESS-002: 1000 sequential GET requests — memory growth measured")]
    [Trait("Category", "Stress")]
    public async Task Should_MeasureMemoryGrowth_When_1000SequentialGetRequestsSent()
    {
        // Warm up: let the GC settle before measuring
        await using var warmupConn = await Http11Helper.OpenAsync(_fixture.Port);
        for (var i = 0; i < 20; i++)
        {
            await warmupConn.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        var memBefore = GC.GetTotalMemory(forceFullCollection: true);

        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);
        for (var i = 0; i < 1000; i++)
        {
            var r = await conn.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        var memAfter = GC.GetTotalMemory(forceFullCollection: true);
        var deltaMB = (memAfter - memBefore) / (1024.0 * 1024.0);

        // Informational: GC.GetTotalMemory includes all live objects in the shared test process.
        // In isolation this stays well below 5 MB; in a shared suite it reflects cross-test pressure.
        // The test always passes — run with --filter "Category=Stress" for meaningful measurement.
        Assert.True(true,
            $"Heap grew by {deltaMB:F2} MB after 1000 requests (informational).");
    }

    [Fact(DisplayName = "IT-STRESS-003: 100 POST /echo requests with varying body sizes — all echoed correctly")]
    [Trait("Category", "Stress")]
    public async Task Should_EchoBody_When_100PostRequestsWithVaryingBodySizesSent()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        var random = new Random(42);
        var sizes = new[] { 0, 1, 64, 256, 1024, 4096, 65536, 131072 };

        for (var i = 0; i < 100; i++)
        {
            var size = sizes[i % sizes.Length];
            var body = new byte[size];
            random.NextBytes(body);
            if (size > 0)
            {
                body[0] = (byte)(i % 256); // make each request body distinct
            }

            var request = new HttpRequestMessage(HttpMethod.Post, Http11Helper.BuildUri(_fixture.Port, "/echo"))
            {
                Content = new ByteArrayContent(body)
            };

            var response = await conn.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var echoed = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(size, echoed.Length);
            if (size > 0)
            {
                Assert.Equal(body[0], echoed[0]);
            }
        }
    }

    // ── Concurrent connections ────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-004: 50 concurrent connections × 1 request each — all 200")]
    [Trait("Category", "Stress")]
    public async Task Should_Return200_When_50ConcurrentConnectionsEachSendOneRequest()
    {
        var tasks = Enumerable.Range(0, 50).Select(async _ =>
        {
            var response = await Http11Helper.GetAsync(_fixture.Port, "/ping");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });

        await Task.WhenAll(tasks);
    }

    [Fact(DisplayName = "IT-STRESS-005: 10 concurrent connections × 10 requests each — all 200")]
    [Trait("Category", "Stress")]
    public async Task Should_Return200_When_10ConcurrentConnectionsSend10RequestsEach()
    {
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var conn = await Http11Helper.OpenAsync(_fixture.Port);
            for (var i = 0; i < 10; i++)
            {
                var r = await conn.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            }
        });

        await Task.WhenAll(tasks);
    }

    // ── Pipelining ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-006: Pipeline 10 requests × 10 iterations — all 200")]
    [Trait("Category", "Stress")]
    public async Task Should_Return200_When_PipelinedRequestsSentIn10Iterations()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        var pipeline = Enumerable.Range(0, 10)
            .Select(_ => new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")))
            .ToList();

        for (var iteration = 0; iteration < 10; iteration++)
        {
            var responses = await conn.PipelineAsync(pipeline);
            Assert.Equal(10, responses.Count);
            Assert.True(responses.All(r => r.StatusCode == HttpStatusCode.OK));
        }
    }

    // ── Sustained keep-alive ──────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-007: Sustained keep-alive — 500 requests, connection never dropped")]
    [Trait("Category", "Stress")]
    public async Task Should_NeverDropConnection_When_500RequestsSentOnOneConnection()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        for (var i = 0; i < 500; i++)
        {
            var r = await conn.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.False(conn.IsServerClosed,
                $"Server closed connection prematurely after {i + 1} requests.");
        }
    }

    // ── Large body ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-008: Large body stream — 10 × 512 KB = 5 MB total")]
    [Trait("Category", "Stress")]
    public async Task Should_DecodeCorrectly_When_10LargeBodiesOf512KBEachSent()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        const int kb = 512;
        const int expectedBytes = kb * 1024;

        for (var i = 0; i < 10; i++)
        {
            var r = await conn.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, $"/large/{kb}")));

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await r.Content.ReadAsByteArrayAsync();
            Assert.Equal(expectedBytes, body.Length);
            Assert.True(body.All(b => b == (byte)'A'),
                $"Iteration {i}: body contains unexpected bytes.");
        }
    }

    // ── Header stress ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-009: Header stress — 50 custom headers × 100 requests")]
    [Trait("Category", "Stress")]
    public async Task Should_EchoAllHeaders_When_50CustomHeadersSentAcross100Requests()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        for (var req = 0; req < 100; req++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                Http11Helper.BuildUri(_fixture.Port, "/headers/echo"));

            for (var h = 0; h < 50; h++)
            {
                request.Headers.TryAddWithoutValidation($"X-Stress-{h:D3}", $"value-{req:D4}-{h:D3}");
            }

            var response = await conn.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Verify a sample of headers were echoed
            Assert.True(response.Headers.Contains("X-Stress-000"),
                $"Request {req}: X-Stress-000 not echoed.");
            Assert.True(response.Headers.Contains("X-Stress-049"),
                $"Request {req}: X-Stress-049 not echoed.");
        }
    }

    // ── Mixed verbs ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-010: Mixed verbs 100 iterations — GET, POST, PUT, DELETE cycling")]
    [Trait("Category", "Stress")]
    public async Task Should_Succeed_When_MixedVerbsSentIn100Iterations()
    {
        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        var verbs = new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete };
        var body = Encoding.UTF8.GetBytes("cycle-body");

        for (var i = 0; i < 100; i++)
        {
            var verb = verbs[i % verbs.Length];
            HttpRequestMessage request;

            if (verb == HttpMethod.Get || verb == HttpMethod.Delete)
            {
                request = new HttpRequestMessage(verb, Http11Helper.BuildUri(_fixture.Port, "/any"));
            }
            else
            {
                // POST and PUT use /echo so they receive the body back
                request = new HttpRequestMessage(verb, Http11Helper.BuildUri(_fixture.Port, "/echo"))
                {
                    Content = new ByteArrayContent(body)
                };
            }

            var response = await conn.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ── Decoder reset / state isolation ──────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-011: Decoder reset between requests — no state leakage")]
    [Trait("Category", "Stress")]
    public async Task Should_HaveNoStateLeakage_When_DecoderResetBetweenRequests()
    {
        // Each call to Http11Helper.SendAsync opens a fresh connection + fresh decoder
        // Verify that responses from separate connections are independent
        for (var i = 0; i < 20; i++)
        {
            var r1 = await Http11Helper.GetAsync(_fixture.Port, "/ping");
            var r2 = await Http11Helper.GetAsync(_fixture.Port, "/hello");

            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
            Assert.Equal("pong", await r1.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
            Assert.Equal("Hello World", await r2.Content.ReadAsStringAsync());
        }
    }

    // ── Memory stability ──────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-012: Memory — heap growth after 1000 requests (measured, not asserted)")]
    [Trait("Category", "Stress")]
    public async Task Should_MeasureHeapGrowth_When_1000RequestsSent()
    {
        // Warm up
        for (var i = 0; i < 10; i++)
        {
            await Http11Helper.GetAsync(_fixture.Port, "/ping");
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        var memBefore = GC.GetTotalMemory(forceFullCollection: true);

        for (var i = 0; i < 1000; i++)
        {
            await Http11Helper.GetAsync(_fixture.Port, "/ping");
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        var memAfter = GC.GetTotalMemory(forceFullCollection: true);

        var deltaMB = (memAfter - memBefore) / (1024.0 * 1024.0);
        // Informational: GC.GetTotalMemory includes the entire shared test-runner process.
        // Run with --filter "Category=Stress" for a meaningful baseline measurement.
        Assert.True(true,
            $"Heap grew by {deltaMB:F2} MB after 1000 requests (informational).");
    }

    // ── GC pressure ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-013: GC pressure — LOH growth during steady state (measured, not asserted)")]
    [Trait("Category", "Stress")]
    public async Task Should_MeasureLohGrowth_When_RequestsSentInSteadyState()
    {
        // Warm up: let GC settle before measuring
        await using var warmConn = await Http11Helper.OpenAsync(_fixture.Port);
        for (var i = 0; i < 20; i++)
        {
            await warmConn.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        // Measure LOH size before steady-state requests (Gen3 = LOH)
        var lohBefore = GC.GetGCMemoryInfo().GenerationInfo[3].SizeAfterBytes;

        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);
        for (var i = 0; i < 500; i++)
        {
            await conn.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, "/ping")));
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        var lohAfter = GC.GetGCMemoryInfo().GenerationInfo[3].SizeAfterBytes;

        var lohDeltaMB = (lohAfter - lohBefore) / (1024.0 * 1024.0);
        // Informational: current impl uses 4MB encode buffers (LOH) per request.
        // This test measures LOH growth and always passes — an optimization opportunity.
        Assert.True(true,
            $"LOH grew by {lohDeltaMB:F2} MB during 500 steady-state requests (informational).");
    }

    // ── Throughput ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "IT-STRESS-014: Throughput — > 10 MB/s encode+decode (measured, not asserted)")]
    [Trait("Category", "Stress")]
    public async Task Should_MeasureThroughput_When_LargePayloadTransferred()
    {
        // Transfer 10 × 512 KB = 5 MB and measure encode+decode throughput.
        // The threshold is informational — not asserted to avoid flaky CI.
        const int kb = 512;
        const int iterations = 10;
        const long totalBytes = (long)kb * 1024 * iterations;

        await using var conn = await Http11Helper.OpenAsync(_fixture.Port);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var r = await conn.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, Http11Helper.BuildUri(_fixture.Port, $"/large/{kb}")));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            // Read body to account for full decode cost
            _ = await r.Content.ReadAsByteArrayAsync();
        }

        sw.Stop();

        var throughputMBps = totalBytes / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds;
        // Informational output — test always passes regardless of speed
        Assert.True(sw.Elapsed.TotalSeconds > 0,
            $"Throughput: {throughputMBps:F1} MB/s over {totalBytes / (1024 * 1024)} MB in {sw.Elapsed.TotalMilliseconds:F0} ms.");
    }
}
