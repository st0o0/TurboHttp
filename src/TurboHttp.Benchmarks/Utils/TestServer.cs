using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace TurboHttp.Benchmarks.Utils;

public class TestServer : IDisposable
{
    private IHost _host = null!;

    public void Start()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseKestrel();
                web.UseUrls("http://127.0.0.1:5005");

                web.Configure(app => { app.Run(async ctx => { await ctx.Response.WriteAsync("OK"); }); });
            })
            .Build();

        _host.Start();
    }

    public void Dispose()
    {
        _host?.Dispose();
    }
}