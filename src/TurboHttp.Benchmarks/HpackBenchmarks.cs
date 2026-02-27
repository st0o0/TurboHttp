using BenchmarkDotNet.Attributes;
using TurboHttp.Protocol;
using System;
using System.Buffers;

namespace TurboHttp.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class HpackBenchmarks
{
    // Pre-encoded data for decode benchmarks
    private byte[] _staticOnlyEncoded = default!;
    private byte[] _coldEncoded = default!;
    private byte[] _warmEncoded = default!;

    // Raw bytes for Huffman benchmarks
    private byte[] _raw16chars = default!;
    private byte[] _raw256chars = default!;

    // Pre-encoded Huffman data for decode benchmarks
    private byte[] _huffman16chars = default!;
    private byte[] _huffman256chars = default!;

    // Pre-warmed encoder/decoder for warm-path benchmarks
    private HpackEncoder _warmEncoder = default!;
    private HpackDecoder _warmDecoder = default!;

    // Header sets reused in encode benchmarks
    private static readonly (string Name, string Value)[] StaticHeaders =
    [
        (":method", "GET"),
        (":path", "/"),
        (":scheme", "https"),
    ];

    private static readonly (string Name, string Value)[] ColdHeaders =
    [
        ("x-request-id", "abc123def456"),
        ("x-trace-id", "trace-0987654321"),
        ("x-correlation-id", "corr-abcdef"),
    ];

    private static readonly (string Name, string Value)[] WarmHeaders =
    [
        (":method", "GET"),
        (":path", "/api/v1/resource"),
        (":scheme", "https"),
        (":authority", "example.com"),
        ("content-type", "application/json"),
        ("accept", "application/json"),
    ];

    [GlobalSetup]
    public void Setup()
    {
        // Build static-only encoded block
        var staticEncoder = new HpackEncoder();
        _staticOnlyEncoded = staticEncoder.Encode(StaticHeaders).ToArray();

        // Build cold-encoded block (all literals, no table entries)
        var coldEncoder = new HpackEncoder();
        _coldEncoded = coldEncoder.Encode(ColdHeaders).ToArray();

        // Build warm encoder: encode headers 5 times to populate dynamic table
        _warmEncoder = new HpackEncoder();
        _warmDecoder = new HpackDecoder();
        for (int i = 0; i < 5; i++)
        {
            var encoded = _warmEncoder.Encode(WarmHeaders);
            _warmDecoder.Decode(encoded.Span);
        }
        // 6th encoding will use indexed references heavily
        _warmEncoded = _warmEncoder.Encode(WarmHeaders).ToArray();

        // Huffman test data
        _raw16chars = System.Text.Encoding.ASCII.GetBytes("Accept-Encoding");   // 15 chars
        _raw256chars = new byte[256];
        Array.Fill(_raw256chars, (byte)'x');

        _huffman16chars = HuffmanCodec.Encode(_raw16chars);
        _huffman256chars = HuffmanCodec.Encode(_raw256chars);
    }

    // ── HPACK Encode ────────────────────────────────────────────────────────────

    [Benchmark]
    public int Encode_StaticOnly()
    {
        var encoder = new HpackEncoder();
        return encoder.Encode(StaticHeaders).Length;
    }

    [Benchmark]
    public int Encode_Cold()
    {
        var encoder = new HpackEncoder();
        return encoder.Encode(ColdHeaders).Length;
    }

    [Benchmark]
    public int Encode_Warm()
    {
        return _warmEncoder.Encode(WarmHeaders).Length;
    }

    // ── HPACK Decode ────────────────────────────────────────────────────────────

    [Benchmark]
    public int Decode_StaticOnly()
    {
        var decoder = new HpackDecoder();
        return decoder.Decode(_staticOnlyEncoded).Count;
    }

    [Benchmark]
    public int Decode_Cold()
    {
        var decoder = new HpackDecoder();
        return decoder.Decode(_coldEncoded).Count;
    }

    [Benchmark]
    public int Decode_Warm()
    {
        return _warmDecoder.Decode(_warmEncoded).Count;
    }

    // ── Huffman Encode ───────────────────────────────────────────────────────────

    [Benchmark]
    public int Huffman_Encode_16chars()
    {
        return HuffmanCodec.Encode(_raw16chars).Length;
    }

    [Benchmark]
    public int Huffman_Encode_256chars()
    {
        return HuffmanCodec.Encode(_raw256chars).Length;
    }

    // ── Huffman Decode ───────────────────────────────────────────────────────────

    [Benchmark]
    public int Huffman_Decode_16chars()
    {
        return HuffmanCodec.Decode(_huffman16chars).Length;
    }

    [Benchmark]
    public int Huffman_Decode_256chars()
    {
        return HuffmanCodec.Decode(_huffman256chars).Length;
    }
}
