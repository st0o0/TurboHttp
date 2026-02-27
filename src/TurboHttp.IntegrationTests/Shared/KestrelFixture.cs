using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Shared Kestrel fixture for HTTP/1.0 integration tests.
/// Starts a real in-process Kestrel server on a random port and registers
/// all routes used by Phase 12 tests.
/// </summary>
public sealed class KestrelFixture : IAsyncLifetime
{
    private WebApplication? _app;

    /// <summary>The TCP port Kestrel is listening on after <see cref="InitializeAsync"/>.</summary>
    public int Port { get; private set; }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();

        RegisterRoutes(app);

        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var addrFeature = server.Features.Get<IServerAddressesFeature>()!;
        Port = new Uri(addrFeature.Addresses.First()).Port;

        _app = app;
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static void RegisterRoutes(WebApplication app)
    {
        // ── Basic ──────────────────────────────────────────────────────────────

        // GET /hello → 200 "Hello World"
        // HEAD /hello → 200, headers only (body suppressed by ASP.NET Core middleware)
        app.MapMethods("/hello", ["GET", "HEAD"], (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = 11;
            return Results.Content("Hello World", "text/plain");
        });

        // GET /ping → 200 "pong"
        app.MapGet("/ping", () => Results.Content("pong", "text/plain"));

        // GET /large/{kb} → 200, kb*1024 bytes of 'A'
        app.MapGet("/large/{kb:int}", (int kb) =>
        {
            var body = new byte[kb * 1024];
            Array.Fill(body, (byte)'A');
            return Results.Bytes(body, "application/octet-stream");
        });

        // GET /status/{code} → returns the requested status code
        app.MapGet("/status/{code:int}", async (HttpContext ctx, int code) =>
        {
            ctx.Response.StatusCode = code;
            if (code != 204 && code != 304)
            {
                var body = "ok"u8.ToArray();
                ctx.Response.ContentLength = body.Length;
                await ctx.Response.Body.WriteAsync(body);
            }
        });

        // GET /content/{*ct} → 200, Content-Type from catch-all path segment(s)
        // e.g. /content/text/html  → Content-Type: text/html
        //      /content/application/json → Content-Type: application/json
        app.MapGet("/content/{*ct}", async (HttpContext ctx, string ct) =>
        {
            ctx.Response.ContentType = ct;
            var body = "body"u8.ToArray();
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // GET /methods → 200, body = request method
        app.MapGet("/methods", (HttpContext ctx) => Results.Content(ctx.Request.Method, "text/plain"));

        // ── Body ──────────────────────────────────────────────────────────────

        // POST /echo → 200, echoes request body verbatim with same Content-Type
        app.MapPost("/echo", async (HttpContext ctx) =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var body = ms.ToArray();
            var contentType = ctx.Request.ContentType ?? "application/octet-stream";
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength = body.Length;
            await ctx.Response.Body.WriteAsync(body);
        });

        // ── Headers ───────────────────────────────────────────────────────────

        // GET /headers/echo → echoes X-* request headers back as response headers
        app.MapGet("/headers/echo", (HttpContext ctx) =>
        {
            foreach (var header in ctx.Request.Headers)
            {
                if (header.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.Headers[header.Key] = header.Value;
                }
            }

            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // GET /headers/set?Foo=Bar → sets response headers from query parameters
        app.MapGet("/headers/set", (HttpContext ctx) =>
        {
            foreach (var param in ctx.Request.Query)
            {
                ctx.Response.Headers[param.Key] = param.Value;
            }

            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });

        // GET /auth → 401 without Authorization, 200 with any Authorization value
        app.MapGet("/auth", (HttpContext ctx) =>
        {
            if (!ctx.Request.Headers.ContainsKey("Authorization"))
            {
                return Results.StatusCode(401);
            }

            return Results.Ok();
        });

        // GET /multiheader → response has two X-Value: a, b headers (same name)
        app.MapGet("/multiheader", (HttpContext ctx) =>
        {
            ctx.Response.Headers.Append("X-Value", "alpha");
            ctx.Response.Headers.Append("X-Value", "beta");
            ctx.Response.ContentLength = 0;
            return Results.Empty;
        });
    }
}
