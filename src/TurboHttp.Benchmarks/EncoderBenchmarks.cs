using BenchmarkDotNet.Attributes;
using TurboHttp.Protocol;
using System;
using System.Net.Http;
using System.Buffers;

namespace TurboHttp.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class EncoderBenchmarks
{
    private HttpRequestMessage _simpleGet10 = default!;
    private HttpRequestMessage _simpleGet11 = default!;
    private HttpRequestMessage _post1KB = default!;
    private HttpRequestMessage _post64KB = default!;
    private HttpRequestMessage _withHeaders10 = default!;
    private HttpRequestMessage _withHeaders20 = default!;
    private readonly byte[] _encodeBuffer = new byte[70_000];

    [GlobalSetup]
    public void Setup()
    {
        _simpleGet10 = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new System.Uri("http://example.com/hello")
        };

        _simpleGet11 = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new System.Uri("http://example.com/hello"),
            Version = new System.Version(1, 1)
        };

        var body1KB = new byte[1024];
        System.Array.Fill(body1KB, (byte)'x');
        _post1KB = new HttpRequestMessage(HttpMethod.Post, "http://example.com/echo")
        {
            Content = new ByteArrayContent(body1KB)
        };

        var body64KB = new byte[64 * 1024];
        System.Array.Fill(body64KB, (byte)'y');
        _post64KB = new HttpRequestMessage(HttpMethod.Post, "http://example.com/echo")
        {
            Content = new ByteArrayContent(body64KB)
        };

        _withHeaders10 = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new System.Uri("http://example.com/test")
        };
        _withHeaders10.Headers.Add("User-Agent", "BenchmarkClient/1.0");
        _withHeaders10.Headers.Add("Accept", "application/json");
        _withHeaders10.Headers.Add("Accept-Encoding", "gzip, deflate");
        _withHeaders10.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        _withHeaders10.Headers.Add("Cache-Control", "no-cache");
        _withHeaders10.Headers.Add("DNT", "1");
        _withHeaders10.Headers.Add("Pragma", "no-cache");
        _withHeaders10.Headers.Add("Referer", "http://example.com/");
        _withHeaders10.Headers.Add("X-Custom-Header", "value123");
        _withHeaders10.Headers.Add("X-Benchmark", "1");

        _withHeaders20 = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new System.Uri("http://example.com/test")
        };
        _withHeaders20.Headers.Add("User-Agent", "BenchmarkClient/1.0");
        _withHeaders20.Headers.Add("Accept", "application/json");
        _withHeaders20.Headers.Add("Accept-Encoding", "gzip, deflate");
        _withHeaders20.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        _withHeaders20.Headers.Add("Accept-Charset", "utf-8");
        _withHeaders20.Headers.Add("Cache-Control", "no-cache");
        _withHeaders20.Headers.Add("DNT", "1");
        _withHeaders20.Headers.Add("If-None-Match", "abc123");
        _withHeaders20.Headers.Add("If-Modified-Since", "Mon, 10 Feb 2025 00:00:00 GMT");
        _withHeaders20.Headers.Add("Pragma", "no-cache");
        _withHeaders20.Headers.Add("Referer", "http://example.com/");
        _withHeaders20.Headers.Add("Range", "bytes=0-1023");
        _withHeaders20.Headers.Add("X-Custom-1", "value1");
        _withHeaders20.Headers.Add("X-Custom-2", "value2");
        _withHeaders20.Headers.Add("X-Custom-3", "value3");
        _withHeaders20.Headers.Add("X-Custom-4", "value4");
        _withHeaders20.Headers.Add("X-Custom-5", "value5");
        _withHeaders20.Headers.Add("X-Custom-6", "value6");
        _withHeaders20.Headers.Add("X-Custom-7", "value7");
    }

    [Benchmark]
    public int Http10_Encode_SimpleGet()
    {
        var buffer = _encodeBuffer.AsMemory();
        return Http10Encoder.Encode(_simpleGet10, ref buffer);
    }

    [Benchmark]
    public int Http10_Encode_WithHeaders_10()
    {
        var buffer = _encodeBuffer.AsMemory();
        return Http10Encoder.Encode(_withHeaders10, ref buffer);
    }

    [Benchmark]
    public int Http11_Encode_SimpleGet()
    {
        Span<byte> buffer = stackalloc byte[512];
        return Http11Encoder.Encode(_simpleGet11, ref buffer);
    }

    [Benchmark]
    public int Http11_Encode_Post_1KB()
    {
        Span<byte> buffer = stackalloc byte[2048];
        return Http11Encoder.Encode(_post1KB, ref buffer);
    }

    [Benchmark]
    public int Http11_Encode_Post_64KB()
    {
        var buffer = _encodeBuffer.AsMemory();
        return (int)Http11Encoder.Encode(_post64KB, ref buffer);
    }

    [Benchmark]
    public int Http11_Encode_WithHeaders_20()
    {
        Span<byte> buffer = stackalloc byte[4096];
        return Http11Encoder.Encode(_withHeaders20, ref buffer);
    }

    [Benchmark]
    public int Http2_Encode_ColdHpackTable()
    {
        var encoder = new Http2Encoder();
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api/v1/resource");
        var buffer = _encodeBuffer.AsMemory();
        var (_, bytesWritten) = encoder.Encode(req, ref buffer);
        return bytesWritten;
    }

    [Benchmark]
    public int Http2_Encode_WarmHpackTable_10Requests()
    {
        var encoder = new Http2Encoder();
        var totalEncoded = 0;

        for (int i = 0; i < 10; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"https://example.com/api/v1/resource/{i}");
            var buffer = _encodeBuffer.AsMemory();
            var (_, bytesWritten) = encoder.Encode(req, ref buffer);
            totalEncoded += bytesWritten;
        }

        return totalEncoded;
    }
}
