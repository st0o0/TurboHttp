using System.Net;
using System.Text.Json;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Protocol;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Integration tests for Http11Engine cookie handling.
/// Verifies RFC 6265 cookie storage, attribute handling (Path, Domain, Secure, HttpOnly,
/// Max-Age, SameSite), sorting, multiple Set-Cookie headers, and cross-redirect persistence
/// against real Kestrel cookie routes.
/// </summary>
public sealed class Http11CookieTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http11CookieTests(KestrelFixture fixture)
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
    /// Sends a request with CookieJar integration — injects cookies before sending,
    /// stores cookies from response.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithCookiesAsync(
        HttpRequestMessage request,
        CookieJar cookieJar)
    {
        if (request.RequestUri is not null)
        {
            cookieJar.AddCookiesToRequest(request.RequestUri, ref request);
        }

        var response = await SendAsync(request);
        response.RequestMessage = request;

        if (request.RequestUri is not null)
        {
            cookieJar.ProcessResponse(request.RequestUri, response);
        }

        return response;
    }

    /// <summary>
    /// Sends a request and manually follows redirects with CookieJar support.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRedirectAndCookiesAsync(
        HttpRequestMessage request,
        CookieJar cookieJar)
    {
        var handler = new RedirectHandler();
        var current = request;

        for (var i = 0; i <= RedirectPolicy.Default.MaxRedirects; i++)
        {
            if (current.RequestUri is not null)
            {
                cookieJar.AddCookiesToRequest(current.RequestUri, ref current);
            }

            var response = await SendAsync(current);
            response.RequestMessage = current;

            if (current.RequestUri is not null)
            {
                cookieJar.ProcessResponse(current.RequestUri, response);
            }

            if (!RedirectHandler.IsRedirect(response))
            {
                return response;
            }

            current = handler.BuildRedirectRequest(current, response);
            current.Version = HttpVersion.Version11;
        }

        throw new RedirectException(
            "Exceeded redirect loop in test helper",
            RedirectError.MaxRedirectsExceeded);
    }

    private HttpRequestMessage CreateGet(string path)
    {
        return new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version11
        };
    }

    private static Dictionary<string, string> ParseCookieEcho(string body)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(body)
               ?? new Dictionary<string, string>();
    }

    [Fact(DisplayName = "COOKIE-11-001: Set-Cookie stored and sent back on subsequent request")]
    public async Task Cookie_StoredAndSentBack()
    {
        var jar = new CookieJar();

        // Step 1: Set cookie
        var setResponse = await SendWithCookiesAsync(CreateGet("/cookie/set/session/abc123"), jar);
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        // Step 2: Echo cookies back
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("session"), "Cookie 'session' should be sent back");
        Assert.Equal("abc123", cookies["session"]);
    }

    [Fact(DisplayName = "COOKIE-11-002: Multiple cookies accumulate across requests")]
    public async Task Cookies_AccumulateAcrossRequests()
    {
        var jar = new CookieJar();

        await SendWithCookiesAsync(CreateGet("/cookie/set/first/one"), jar);
        await SendWithCookiesAsync(CreateGet("/cookie/set/second/two"), jar);

        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.Equal("one", cookies["first"]);
        Assert.Equal("two", cookies["second"]);
    }

    [Fact(DisplayName = "COOKIE-11-003: Cookie with Path attribute restricts scope (RFC 6265 §5.1.4)")]
    public async Task Cookie_PathAttribute_RestrictsScope()
    {
        var jar = new CookieJar();

        // Set cookie scoped to /cookie/echo path
        await SendWithCookiesAsync(CreateGet("/cookie/set-path/pathcookie/val/cookie/echo"), jar);

        // Request to /cookie/echo — path matches, cookie should be sent
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("pathcookie"), "Cookie should be sent for matching path");
    }

    [Fact(DisplayName = "COOKIE-11-004: Cookie with Domain attribute enables subdomain matching (RFC 6265 §5.1.3)")]
    public async Task Cookie_DomainAttribute_SubdomainMatching()
    {
        var jar = new CookieJar();

        // Set cookie with Domain=127.0.0.1
        await SendWithCookiesAsync(CreateGet("/cookie/set-domain/domcookie/domval/127.0.0.1"), jar);

        // Echo — should see the cookie since request host matches domain
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("domcookie"), "Cookie should be sent when domain matches request host");
        Assert.Equal("domval", cookies["domcookie"]);
    }

    [Fact(DisplayName = "COOKIE-11-005: Secure cookie not sent over HTTP (RFC 6265 §5.4)")]
    public async Task SecureCookie_NotSentOverHttp()
    {
        var jar = new CookieJar();

        // Set a Secure cookie over HTTP (server still sets it in header)
        await SendWithCookiesAsync(CreateGet("/cookie/set-secure/sectoken/secret"), jar);

        // Echo over HTTP — Secure cookie should NOT be included
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.False(cookies.ContainsKey("sectoken"),
            "Secure cookie must not be sent over plain HTTP");
    }

    [Fact(DisplayName = "COOKIE-11-006: HttpOnly cookie stored and sent back normally")]
    public async Task HttpOnlyCookie_StoredAndSentBack()
    {
        var jar = new CookieJar();

        await SendWithCookiesAsync(CreateGet("/cookie/set-httponly/hocookie/hoval"), jar);

        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        // HttpOnly is a browser-side restriction; server-side jar should still send it
        Assert.True(cookies.ContainsKey("hocookie"), "HttpOnly cookie should be sent by HTTP client");
        Assert.Equal("hoval", cookies["hocookie"]);
    }

    [Fact(DisplayName = "COOKIE-11-007: Max-Age=0 deletes existing cookie (RFC 6265 §5.3)")]
    public async Task MaxAgeZero_DeletesCookie()
    {
        var jar = new CookieJar();

        // Set a cookie
        await SendWithCookiesAsync(CreateGet("/cookie/set/deleteme/present"), jar);

        // Verify it's there
        var echo1 = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body1 = await echo1.Content.ReadAsStringAsync();
        Assert.Contains("deleteme", body1);

        // Delete it via Max-Age=0
        await SendWithCookiesAsync(CreateGet("/cookie/delete/deleteme"), jar);

        // Verify it's gone
        var echo2 = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body2 = await echo2.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body2);

        Assert.False(cookies.ContainsKey("deleteme"),
            "Cookie should be removed after Max-Age=0");
    }

    [Fact(DisplayName = "COOKIE-11-008: Expired cookie (Max-Age past) not sent")]
    public async Task ExpiredCookie_NotSent()
    {
        var jar = new CookieJar();

        // Set a cookie that expires in 0 seconds (immediately expired)
        await SendWithCookiesAsync(CreateGet("/cookie/set-expires/expcookie/val/0"), jar);

        // Echo — expired cookie should not be sent
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.False(cookies.ContainsKey("expcookie"),
            "Expired cookie (Max-Age=0) should not be stored or sent");
    }

    [Fact(DisplayName = "COOKIE-11-009: Multiple Set-Cookie headers in single response all stored")]
    public async Task MultipleSetCookieHeaders_AllStored()
    {
        var jar = new CookieJar();

        // /cookie/set-multiple sets alpha, beta, gamma in one response
        await SendWithCookiesAsync(CreateGet("/cookie/set-multiple"), jar);

        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.Equal("one", cookies["alpha"]);
        Assert.Equal("two", cookies["beta"]);
        Assert.Equal("three", cookies["gamma"]);
    }

    [Fact(DisplayName = "COOKIE-11-010: Cookie sorting — longer path first, then creation order (RFC 6265 §5.4)")]
    public async Task Cookie_Sorting_LongerPathFirst_ThenCreationOrder()
    {
        var jar = new CookieJar();

        // Set cookie with short path (/)
        await SendWithCookiesAsync(CreateGet("/cookie/set/shortpath/s"), jar);

        // Set cookie with longer path (/cookie/echo)
        await SendWithCookiesAsync(CreateGet("/cookie/set-path/longpath/l/cookie/echo"), jar);

        // Echo — longpath (longer path) should appear before shortpath
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();

        // The raw Cookie header sent to the server determines order.
        // We verify both cookies are present.
        var cookies = ParseCookieEcho(body);
        Assert.True(cookies.ContainsKey("shortpath"), "shortpath cookie should be present");
        Assert.True(cookies.ContainsKey("longpath"), "longpath cookie should be present");

        // Verify sorting via the raw body — longpath should appear before shortpath
        var longIdx = body.IndexOf("longpath", StringComparison.Ordinal);
        var shortIdx = body.IndexOf("shortpath", StringComparison.Ordinal);
        Assert.True(longIdx < shortIdx,
            "Cookie with longer path should be sorted first (RFC 6265 §5.4)");
    }

    [Fact(DisplayName = "COOKIE-11-011: Cookies preserved across 302 redirect")]
    public async Task Cookies_PreservedAcrossRedirect()
    {
        var jar = new CookieJar();

        // /cookie/set-and-redirect sets redirect_cookie and 302s to /cookie/echo
        var response = await SendWithRedirectAndCookiesAsync(
            CreateGet("/cookie/set-and-redirect"), jar);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("redirect_cookie"),
            "Cookie set during redirect should be sent to redirect target");
        Assert.Equal("from-redirect", cookies["redirect_cookie"]);
    }

    [Fact(DisplayName = "COOKIE-11-012: SameSite attribute stored correctly")]
    public async Task SameSite_StoredAndSentBack()
    {
        var jar = new CookieJar();

        // Set a cookie with SameSite=Lax
        await SendWithCookiesAsync(CreateGet("/cookie/set-samesite/sscookie/ssval/Lax"), jar);

        // Same-site request — cookie should be sent (SameSite enforcement is caller's
        // responsibility per CookieJar design; the cookie should still be stored and sent
        // in same-origin requests regardless of SameSite value)
        var echoResponse = await SendWithCookiesAsync(CreateGet("/cookie/echo"), jar);
        var body = await echoResponse.Content.ReadAsStringAsync();
        var cookies = ParseCookieEcho(body);

        Assert.True(cookies.ContainsKey("sscookie"),
            "SameSite cookie should be stored and sent for same-origin requests");
        Assert.Equal("ssval", cookies["sscookie"]);
    }
}
