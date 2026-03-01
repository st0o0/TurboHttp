using System;
using System.Net.Http;
using System.Text;
using BenchmarkDotNet.Attributes;
using TurboHttp.Protocol;

namespace TurboHttp.Benchmarks.Protocol;

/// <summary>
/// BM-PROTO-201..204: Serialization path benchmarks.
/// Exercises the encoder and decoder across payload size tiers:
///   201 — small (&lt;128 bytes): encode request + decode response in-memory.
///   202 — medium (~1 MB):      decode a pre-built 1 MB HTTP/1.1 response.
///   203 — large (&gt;5 MB):       decode a pre-built 5 MB HTTP/1.1 response.
///   204 — zero-copy:           encode a GET request into a stackalloc buffer
///                              ([MemoryDiagnoser] should report near-zero heap
///                              allocation for the encoder call itself).
/// All benchmarks are pure in-memory — no Kestrel server required.
/// </summary>
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class SerializationPathBenchmarks
{
    // Pre-built response byte arrays
    private byte[] _smallResponse = null!;    //  < 128 bytes total
    private byte[] _mediumResponse = null!;   // ~1 MB body
    private byte[] _largeResponse = null!;    // ~5 MB body

    // Pre-created request for zero-copy benchmark (reused across iterations)
    private HttpRequestMessage _smallGetReq = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Small response: status + Content-Length + 64-byte body
        var smallBody = new byte[64];
        Array.Fill(smallBody, (byte)'s');
        _smallResponse = BuildHttp11Response(200, smallBody);

        // Medium response: ~1 MB body
        var mediumBody = new byte[1 * 1024 * 1024];
        Array.Fill(mediumBody, (byte)'m');
        _mediumResponse = BuildHttp11Response(200, mediumBody);

        // Large response: ~5 MB body
        var largeBody = new byte[5 * 1024 * 1024];
        Array.Fill(largeBody, (byte)'l');
        _largeResponse = BuildHttp11Response(200, largeBody);

        _smallGetReq = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/ping");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _smallGetReq.Dispose();
    }

    // ── BM-PROTO-201: Small payload path (<128 bytes) ─────────────────────────

    /// <summary>
    /// BM-PROTO-201: Small payload path (&lt;128 bytes).
    /// Encodes a minimal GET request into a 512-byte stackalloc buffer, then
    /// decodes a pre-built &lt;128-byte response. Models the hot path for
    /// health-check or ping-style requests where payload is negligible.
    /// </summary>
    [Benchmark]
    public bool BmProto201_SmallPayload_Path()
    {
        // Encode: zero-copy, stackalloc
        Span<byte> encSpan = stackalloc byte[512];
        Http11Encoder.Encode(_smallGetReq, ref encSpan);

        // Decode: pre-built small response
        using var decoder = new Http11Decoder();
        return decoder.TryDecode(_smallResponse.AsMemory(), out _);
    }

    // ── BM-PROTO-202: Medium payload path (~1 MB) ─────────────────────────────

    /// <summary>
    /// BM-PROTO-202: Medium payload path (~1 MB).
    /// Decodes a pre-built HTTP/1.1 200 response with a 1 MB body in a single
    /// <see cref="Http11Decoder.TryDecode"/> call. Measures decoder throughput
    /// for API responses in the typical megabyte range.
    /// </summary>
    [Benchmark]
    public bool BmProto202_MediumPayload_Path()
    {
        using var decoder = new Http11Decoder();
        return decoder.TryDecode(_mediumResponse.AsMemory(), out _);
    }

    // ── BM-PROTO-203: Large payload streaming (>5 MB) ────────────────────────

    /// <summary>
    /// BM-PROTO-203: Large payload streaming (&gt;5 MB).
    /// Decodes a pre-built HTTP/1.1 200 response with a 5 MB body in a single
    /// <see cref="Http11Decoder.TryDecode"/> call. Reveals memory and throughput
    /// characteristics of the decoder at bulk-transfer scale.
    /// </summary>
    [Benchmark]
    public bool BmProto203_LargePayload_Streaming()
    {
        using var decoder = new Http11Decoder();
        return decoder.TryDecode(_largeResponse.AsMemory(), out _);
    }

    // ── BM-PROTO-204: Zero-copy path validation ───────────────────────────────

    /// <summary>
    /// BM-PROTO-204: Zero-copy path validation.
    /// Encodes a pre-created GET request into a <c>stackalloc</c> buffer
    /// (no heap allocation from the encoder itself). [MemoryDiagnoser] is
    /// expected to report near-zero Allocated bytes per operation, confirming
    /// that the <see cref="Http11Encoder"/> hot path avoids heap pressure.
    /// </summary>
    [Benchmark]
    public int BmProto204_ZeroCopy_Validation()
    {
        Span<byte> buf = stackalloc byte[512];
        return Http11Encoder.Encode(_smallGetReq, ref buf);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] BuildHttp11Response(int statusCode, byte[] body)
    {
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} OK\r\nContent-Length: {body.Length}\r\n\r\n");

        var response = new byte[header.Length + body.Length];
        Array.Copy(header, response, header.Length);
        Array.Copy(body, 0, response, header.Length, body.Length);
        return response;
    }
}
