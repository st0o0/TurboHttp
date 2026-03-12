using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Integration tests for Http11Engine retry handling.
/// Verifies RFC 9110 §9.2 retry semantics (idempotent retry on 503/408,
/// non-retry on POST, Retry-After seconds/date, max count, succeed-after-N)
/// against real Kestrel retry routes.
/// </summary>
public sealed class Http11RetryTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http11RetryTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/1.1 request through the Http11Engine pipeline.
    /// Each call materialises a fresh pipeline.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var engine = new Http11Engine();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(
                    new ConnectItem(tcpOptions)))
                .Via(new ConnectionStage(_clientManager));

        var flow = engine.CreateFlow().Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Sends a request and manually retries using <see cref="RetryEvaluator"/>.
    /// Each retry materialises a new Http11Engine pipeline.
    /// </summary>
    private async Task<(HttpResponseMessage Response, int AttemptCount)> SendWithRetryAsync(
        HttpRequestMessage request,
        RetryPolicy? policy = null)
    {
        policy ??= RetryPolicy.Default;
        var attemptCount = 0;

        for (var i = 0; i <= policy.MaxRetries; i++)
        {
            attemptCount++;

            // Clone the request for each attempt (original may be disposed after read)
            var attemptRequest = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = HttpVersion.Version11
            };
            foreach (var header in request.Headers)
            {
                attemptRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync();
                attemptRequest.Content = new ByteArrayContent(bytes);
                foreach (var header in request.Content.Headers)
                {
                    attemptRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            var response = await SendAsync(attemptRequest);

            var decision = RetryEvaluator.Evaluate(
                attemptRequest,
                response,
                attemptCount: attemptCount,
                policy: policy);

            if (!decision.ShouldRetry)
            {
                return (response, attemptCount);
            }

            // Honour Retry-After delay if present (capped for test speed)
            if (decision.RetryAfterDelay is { } delay && delay > TimeSpan.Zero)
            {
                var capped = delay > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : delay;
                await Task.Delay(capped);
            }
        }

        // Should not reach here — last attempt returned with no retry
        throw new InvalidOperationException("Exceeded retry loop in test helper");
    }

    [Fact(DisplayName = "RETRY-11-001: GET /retry/503 retries on 503 Service Unavailable")]
    public async Task Get_503_RetriesIdempotentRequest()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/503")
        {
            Version = HttpVersion.Version11
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        // Server always returns 503, so all retries exhaust and we get 503 back
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        // Default policy allows 3 retries, so total attempts = 3
        Assert.Equal(3, attemptCount);
    }

    [Fact(DisplayName = "RETRY-11-002: GET /retry/408 retries on 408 Request Timeout")]
    public async Task Get_408_RetriesIdempotentRequest()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/408")
        {
            Version = HttpVersion.Version11
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        // Server always returns 408, so all retries exhaust and we get 408 back
        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
        // Default policy allows 3 retries, so total attempts = 3
        Assert.Equal(3, attemptCount);
    }

    [Fact(DisplayName = "RETRY-11-003: HEAD /retry/503 retries on 503 (idempotent)")]
    public async Task Head_503_RetriesIdempotentRequest()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Head,
            $"http://127.0.0.1:{_fixture.Port}/retry/503")
        {
            Version = HttpVersion.Version11
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, attemptCount);
    }

    [Fact(DisplayName = "RETRY-11-004: PUT /retry/503 retries on 503 (idempotent)")]
    public async Task Put_503_RetriesIdempotentRequest()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"http://127.0.0.1:{_fixture.Port}/retry/503")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, attemptCount);
    }

    [Fact(DisplayName = "RETRY-11-005: DELETE /retry/503 retries on 503 (idempotent)")]
    public async Task Delete_503_RetriesIdempotentRequest()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"http://127.0.0.1:{_fixture.Port}/retry/503")
        {
            Version = HttpVersion.Version11
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, attemptCount);
    }

    [Fact(DisplayName = "RETRY-11-006: POST /retry/non-idempotent-503 is NOT retried (RFC 9110 §9.2.2)")]
    public async Task Post_503_NotRetried()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"http://127.0.0.1:{_fixture.Port}/retry/non-idempotent-503")
        {
            Version = HttpVersion.Version11,
            Content = new StringContent("payload")
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        // POST is not idempotent — must not be retried
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, attemptCount);
    }

    [Fact(DisplayName = "RETRY-11-007: GET /retry/503-retry-after/2 returns Retry-After delay (seconds)")]
    public async Task Get_503_RetryAfterSeconds_ParsesDelay()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/503-retry-after/2")
        {
            Version = HttpVersion.Version11
        };

        // Use a policy with max 2 retries so test completes faster
        var policy = new RetryPolicy { MaxRetries = 2, RespectRetryAfter = true };

        // Send first request and evaluate retry decision manually to check Retry-After
        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var decision = RetryEvaluator.Evaluate(request, response, attemptCount: 1, policy: policy);

        Assert.True(decision.ShouldRetry);
        Assert.NotNull(decision.RetryAfterDelay);
        Assert.Equal(TimeSpan.FromSeconds(2), decision.RetryAfterDelay.Value);
    }

    [Fact(DisplayName = "RETRY-11-008: GET /retry/503-retry-after-date returns Retry-After delay (HTTP-date)")]
    public async Task Get_503_RetryAfterDate_ParsesDelay()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/503-retry-after-date")
        {
            Version = HttpVersion.Version11
        };

        var policy = new RetryPolicy { MaxRetries = 2, RespectRetryAfter = true };

        // Send request and evaluate — server sets Retry-After to 10 seconds from now
        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var decision = RetryEvaluator.Evaluate(request, response, attemptCount: 1, policy: policy);

        Assert.True(decision.ShouldRetry);
        Assert.NotNull(decision.RetryAfterDelay);
        // Server sets date to 10 seconds from now; allow a 5-second window for test timing
        Assert.InRange(decision.RetryAfterDelay.Value, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
    }

    [Fact(DisplayName = "RETRY-11-009: Max retry count (3) enforced — stops after MaxRetries")]
    public async Task MaxRetryCount_Enforced()
    {
        var policy = new RetryPolicy { MaxRetries = 3 };

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/503")
        {
            Version = HttpVersion.Version11
        };

        var (response, attemptCount) = await SendWithRetryAsync(request, policy);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        // MaxRetries = 3 means at most 3 attempts total
        Assert.Equal(3, attemptCount);
    }

    [Fact(DisplayName = "RETRY-11-010: GET /retry/succeed-after/2 succeeds on second attempt")]
    public async Task SucceedAfter2_RetriesUntilSuccess()
    {
        var uniqueKey = Guid.NewGuid().ToString("N");

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/retry/succeed-after/2?key={uniqueKey}")
        {
            Version = HttpVersion.Version11
        };

        var (response, attemptCount) = await SendWithRetryAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("success", body);
        // First attempt returns 503, second returns 200
        Assert.Equal(2, attemptCount);
    }
}
