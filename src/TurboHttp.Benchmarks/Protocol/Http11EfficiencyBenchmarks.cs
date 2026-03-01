using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using TurboHttp.Protocol;

namespace TurboHttp.Benchmarks.Protocol;

/// <summary>
/// BM-PROTO-001..005: HTTP/1.1 protocol efficiency benchmarks.
/// Covers chunked decode throughput, header parsing latency, large header-set
/// parsing cost, pipelined request throughput, and mixed-verb encoding workload.
/// </summary>
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class Http11EfficiencyBenchmarks
{
    private IHost _server = null!;

    private const int Port = 5009;
    private const string BaseUrl = "http://127.0.0.1:5009";

    // Pre-built in-memory response buffers
    private byte[] _chunked8x512 = null!;
    private byte[] _response10Headers = null!;
    private byte[] _response50Headers = null!;

    // Shared encode / read buffers (reused to avoid per-iteration allocation)
    private readonly byte[] _encBuf = new byte[1024];
    private readonly byte[] _readBuf = new byte[4096];

    // Pre-created request messages for mixed-verb benchmark
    private HttpRequestMessage _getReq = null!;
    private HttpRequestMessage _postReq = null!;
    private HttpRequestMessage _putReq = null!;
    private HttpRequestMessage _deleteReq = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        // ── Kestrel server for pipeline benchmark ────────────────────────────
        _server = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseKestrel();
                web.UseUrls($"http://127.0.0.1:{Port}");
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

        // ── Pre-built response bytes ─────────────────────────────────────────
        _chunked8x512 = BuildChunkedResponse(chunkCount: 8, chunkSize: 512);
        _response10Headers = BuildResponseWithHeaders(headerCount: 10);
        _response50Headers = BuildResponseWithHeaders(headerCount: 50);

        // ── Request messages (reused across iterations) ──────────────────────
        _getReq = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
        _postReq = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new ByteArrayContent("hello"u8.ToArray())
        };
        _putReq = new HttpRequestMessage(HttpMethod.Put, BaseUrl)
        {
            Content = new ByteArrayContent("update"u8.ToArray())
        };
        _deleteReq = new HttpRequestMessage(HttpMethod.Delete, BaseUrl);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        _getReq.Dispose();
        _postReq.Dispose();
        _putReq.Dispose();
        _deleteReq.Dispose();
        await _server.StopAsync();
        _server.Dispose();
    }

    // ── BM-PROTO-001: Chunked encoding throughput ─────────────────────────────

    /// <summary>
    /// BM-PROTO-001: Chunked decode throughput.
    /// Decodes a pre-built HTTP/1.1 chunked response (8 × 512-byte chunks) per
    /// iteration. [OperationsPerInvoke(100)] normalises the reported mean to
    /// per-decode, revealing the throughput of the chunked-transfer path.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 100)]
    public void BmProto001_ChunkedDecoding_Throughput()
    {
        for (var i = 0; i < 100; i++)
        {
            using var decoder = new Http11Decoder();
            decoder.TryDecode(_chunked8x512.AsMemory(), out _);
        }
    }

    // ── BM-PROTO-002: Header parsing latency ─────────────────────────────────

    /// <summary>
    /// BM-PROTO-002: Header parsing latency.
    /// Decodes a pre-built HTTP/1.1 response that contains 10 custom headers
    /// and a 2-byte body. Isolates the header-line scanning cost.
    /// </summary>
    [Benchmark]
    public bool BmProto002_HeaderParsing_Latency()
    {
        using var decoder = new Http11Decoder();
        return decoder.TryDecode(_response10Headers.AsMemory(), out _);
    }

    // ── BM-PROTO-003: Large header sets parsing cost ──────────────────────────

    /// <summary>
    /// BM-PROTO-003: Large header-set parsing cost.
    /// Decodes a pre-built HTTP/1.1 response with 50 custom headers.
    /// Highlights the O(n) cost of scanning and allocating large header lists.
    /// </summary>
    [Benchmark]
    public bool BmProto003_LargeHeaderSet_ParsingCost()
    {
        using var decoder = new Http11Decoder();
        return decoder.TryDecode(_response50Headers.AsMemory(), out _);
    }

    // ── BM-PROTO-004: Pipeline request throughput ─────────────────────────────

    /// <summary>
    /// BM-PROTO-004: Pipeline request throughput.
    /// Sends 10 HTTP/1.1 GET requests on a single keep-alive connection and
    /// decodes all 10 responses sequentially. Models a pipelined request queue.
    /// [OperationsPerInvoke(10)] normalises the reported mean to per-request cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 10)]
    public async Task BmProto004_Pipeline_RequestThroughput()
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

    // ── BM-PROTO-005: Mixed verb workload performance ─────────────────────────

    /// <summary>
    /// BM-PROTO-005: Mixed verb workload encoding cost.
    /// Encodes GET, POST, PUT and DELETE requests in rotation using pre-created
    /// request messages. [OperationsPerInvoke(4)] normalises to per-verb cost.
    /// The heap buffer is pre-allocated; no per-iteration allocation from the
    /// buffer itself — only from HttpRequestMessage internals.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 4)]
    public int BmProto005_MixedVerb_Workload()
    {
        var total = 0;

        Span<byte> span = stackalloc byte[1024];
        total += Http11Encoder.Encode(_getReq, ref span);

        span = stackalloc byte[1024];
        total += Http11Encoder.Encode(_postReq, ref span);

        span = stackalloc byte[1024];
        total += Http11Encoder.Encode(_putReq, ref span);

        span = stackalloc byte[1024];
        total += Http11Encoder.Encode(_deleteReq, ref span);

        return total;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] BuildChunkedResponse(int chunkCount, int chunkSize)
    {
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n");

        var chunkSizeHex = chunkSize.ToString("X");
        var chunkHeader = Encoding.ASCII.GetBytes($"{chunkSizeHex}\r\n");
        var chunkData = new byte[chunkSize];
        Array.Fill(chunkData, (byte)'x');
        var crlf = "\r\n"u8.ToArray();
        var terminator = "0\r\n\r\n"u8.ToArray();

        var totalSize = header.Length
                        + chunkCount * (chunkHeader.Length + chunkData.Length + crlf.Length)
                        + terminator.Length;

        var result = new byte[totalSize];
        var offset = 0;

        Array.Copy(header, 0, result, offset, header.Length);
        offset += header.Length;

        for (var i = 0; i < chunkCount; i++)
        {
            Array.Copy(chunkHeader, 0, result, offset, chunkHeader.Length);
            offset += chunkHeader.Length;
            Array.Copy(chunkData, 0, result, offset, chunkData.Length);
            offset += chunkData.Length;
            Array.Copy(crlf, 0, result, offset, crlf.Length);
            offset += crlf.Length;
        }

        Array.Copy(terminator, 0, result, offset, terminator.Length);
        return result;
    }

    private static byte[] BuildResponseWithHeaders(int headerCount)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("Content-Length: 2\r\n");
        for (var i = 0; i < headerCount; i++)
        {
            sb.Append($"X-Custom-{i:D3}: value-{i:D6}\r\n");
        }

        sb.Append("\r\nok");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}