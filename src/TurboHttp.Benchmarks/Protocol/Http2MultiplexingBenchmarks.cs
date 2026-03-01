using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using BenchmarkDotNet.Attributes;
using TurboHttp.Protocol;

namespace TurboHttp.Benchmarks.Protocol;

/// <summary>
/// BM-PROTO-101..105: HTTP/2 multiplexing and protocol efficiency benchmarks.
/// Covers concurrent stream encoding throughput, stream scheduling overhead,
/// HPACK compression efficiency, frame decoding throughput, and flow-control
/// frame handling (large DATA frames near the max-frame-size boundary).
/// All benchmarks are pure in-memory — no Kestrel server required.
/// </summary>
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class Http2MultiplexingBenchmarks
{
    // Pre-built HTTP/2 response buffers
    private byte[] _headers1DataFrame = null!;
    private byte[] _headers8DataFrames = null!;
    private byte[] _headersLargeDataFrame = null!;

    // Warm encoder/decoder pre-populated with representative headers
    private Http2Encoder _warmEncoder = null!;

    // Shared encode buffer (pre-allocated to avoid per-iteration heap pressure)
    private readonly byte[] _encBuf = new byte[256 * 1024]; // 256 KB

    // Header sets for HPACK efficiency measurement
    private static readonly (string Name, string Value)[] CommonHeaders =
    [
        (":method", "GET"),
        (":path", "/api/v1/resource"),
        (":scheme", "https"),
        (":authority", "api.example.com"),
        ("accept", "application/json"),
        ("content-type", "application/json"),
        ("authorization", "Bearer token-abc-123"),
        ("user-agent", "TurboHttp/1.0"),
        ("accept-encoding", "gzip, deflate"),
        ("cache-control", "no-cache"),
    ];

    [GlobalSetup]
    public void Setup()
    {
        // HEADERS + 1 DATA frame (END_STREAM on DATA, 512 bytes)
        _headers1DataFrame = BuildHttp2Response(dataPayload: new byte[512]);

        // HEADERS + 8 DATA frames (END_STREAM on last, 512 bytes each)
        _headers8DataFrames = BuildHttp2MultipleDataFrames(frameCount: 8, framePayloadSize: 512);

        // HEADERS + 1 large DATA frame (16 000 bytes — near HTTP/2 max frame size)
        _headersLargeDataFrame = BuildHttp2Response(dataPayload: new byte[16_000]);

        // Pre-warm encoder: encode CommonHeaders several times to populate HPACK table
        _warmEncoder = new Http2Encoder();
        for (var i = 0; i < 5; i++)
        {
            var req = MakeGetRequest($"https://api.example.com/api/v1/resource/{i}");
            foreach (var (name, value) in CommonHeaders)
            {
                if (name.StartsWith(':'))
                {
                    continue;
                } // pseudo-headers set on the request

                req.Headers.TryAddWithoutValidation(name, value);
            }

            var buf = _encBuf.AsMemory();
            _warmEncoder.Encode(req, ref buf);
        }
    }

    // ── BM-PROTO-101: Concurrent stream throughput ────────────────────────────

    /// <summary>
    /// BM-PROTO-101: Concurrent stream throughput.
    /// Encodes 8 HTTP/2 GET requests sequentially with a single warm encoder,
    /// simulating 8 multiplexed streams on one connection. Stream IDs increment
    /// as 1, 3, 5, …, 15.
    /// [OperationsPerInvoke(8)] normalises the reported mean to per-stream cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 8)]
    public int BmProto101_ConcurrentStream_Throughput()
    {
        var encoder = new Http2Encoder();
        var total = 0;
        for (var i = 0; i < 8; i++)
        {
            var req = MakeGetRequest($"https://api.example.com/stream/{i}");
            var buf = _encBuf.AsMemory();
            var (_, written) = encoder.Encode(req, ref buf);
            total += written;
        }

        return total;
    }

    // ── BM-PROTO-102: Stream scheduling overhead ──────────────────────────────

    /// <summary>
    /// BM-PROTO-102: Stream scheduling overhead — cold encoder per request.
    /// Each invocation creates a fresh <see cref="Http2Encoder"/> and encodes a
    /// single request. Measures the cold-start cost: HPACK table initialisation,
    /// stream-ID reset, and first-request encoding with no table entries.
    /// Compared against BmProto101, the delta reveals warm-table benefit.
    /// </summary>
    [Benchmark]
    public int BmProto102_Stream_SchedulingOverhead()
    {
        var encoder = new Http2Encoder();
        var req = MakeGetRequest("https://api.example.com/stream/0");
        var buf = _encBuf.AsMemory();
        var (_, written) = encoder.Encode(req, ref buf);
        return written;
    }

    // ── BM-PROTO-103: HPACK compression efficiency ────────────────────────────

    /// <summary>
    /// BM-PROTO-103: HPACK compression efficiency.
    /// Encodes the same 10-header set 10 times with a warm encoder and returns
    /// the total encoded bytes. Lower is better: a populated HPACK dynamic table
    /// replaces repeated literal headers with 1-byte indexed references, so
    /// successive calls should produce progressively shorter output.
    /// [OperationsPerInvoke(10)] normalises to per-request encoded bytes.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 10)]
    public int BmProto103_Hpack_CompressionEfficiency()
    {
        var total = 0;
        for (var i = 0; i < 10; i++)
        {
            var req = MakeGetRequest("https://api.example.com/api/v1/resource");
            var buf = _encBuf.AsMemory();
            var (_, written) = _warmEncoder.Encode(req, ref buf);
            total += written;
        }

        return total;
    }

    // ── BM-PROTO-104: Frame decoding throughput ───────────────────────────────

    /// <summary>
    /// BM-PROTO-104: Frame decoding throughput.
    /// Decodes a pre-built HTTP/2 response consisting of one HEADERS frame
    /// followed by 8 DATA frames (512 bytes each, END_STREAM on the last).
    /// [OperationsPerInvoke(8)] normalises to per-frame cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 8)]
    public bool BmProto104_Frame_DecodingThroughput()
    {
        var decoder = new Http2Decoder();
        return decoder.TryDecode(_headers8DataFrames.AsMemory(), out _);
    }

    // ── BM-PROTO-105: Flow control window behavior ────────────────────────────

    /// <summary>
    /// BM-PROTO-105: Flow control window behaviour — large DATA frame.
    /// Decodes a pre-built HTTP/2 response consisting of one HEADERS frame
    /// followed by a single 16 000-byte DATA frame (just below the default
    /// HTTP/2 max-frame-size of 16 384 bytes). Measures the decoder cost when
    /// handling near-maximum-size frames, which exercise the flow-control path.
    /// </summary>
    [Benchmark]
    public bool BmProto105_FlowControl_MaxFrame()
    {
        var decoder = new Http2Decoder();
        return decoder.TryDecode(_headersLargeDataFrame.AsMemory(), out _);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HttpRequestMessage MakeGetRequest(string url)
        => new HttpRequestMessage(HttpMethod.Get, url);

    /// <summary>
    /// Builds: HEADERS frame (END_STREAM + END_HEADERS, HPACK 0x88 = :status 200)
    ///       + raw DATA payload (treated as continuation — no frame header, so
    ///         the decoder sees it as extra bytes after the complete HEADERS frame).
    /// This mirrors the pattern used in <see cref="DecoderBenchmarks"/>.
    /// </summary>
    private static byte[] BuildHttp2Response(byte[] dataPayload)
    {
        // HEADERS: length=1, type=0x01, flags=0x05 (END_STREAM|END_HEADERS), stream=1
        // HPACK block: 0x88 = indexed :status 200
        var headersFrame = new byte[]
        {
            0x00, 0x00, 0x01, // length = 1
            0x01, // type = HEADERS
            0x05, // flags = END_STREAM | END_HEADERS
            0x00, 0x00, 0x00, 0x01, // stream ID = 1
            0x88 // HPACK: :status 200 (static table index 8)
        };

        var result = new byte[headersFrame.Length + dataPayload.Length];
        Array.Copy(headersFrame, result, headersFrame.Length);
        Array.Copy(dataPayload, 0, result, headersFrame.Length, dataPayload.Length);
        return result;
    }

    private static byte[] BuildHttp2MultipleDataFrames(int frameCount, int framePayloadSize)
    {
        // HEADERS: END_HEADERS only (END_STREAM on last DATA frame)
        var headersFrame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x01,
            0x04, // flags = END_HEADERS only
            0x00, 0x00, 0x00, 0x01,
            0x88
        };

        var frames = new List<byte[]> { headersFrame };

        for (var i = 0; i < frameCount; i++)
        {
            var isLast = i == frameCount - 1;
            var payload = new byte[framePayloadSize];
            Array.Fill(payload, (byte)('a' + (i % 26)));

            var frameHeader = new byte[9];
            frameHeader[0] = (byte)((framePayloadSize >> 16) & 0xFF);
            frameHeader[1] = (byte)((framePayloadSize >> 8) & 0xFF);
            frameHeader[2] = (byte)(framePayloadSize & 0xFF);
            frameHeader[3] = 0x00; // DATA
            frameHeader[4] = isLast ? (byte)0x01 : (byte)0x00; // END_STREAM on last
            frameHeader[5] = 0x00;
            frameHeader[6] = 0x00;
            frameHeader[7] = 0x00;
            frameHeader[8] = 0x01; // stream ID = 1

            var frame = new byte[frameHeader.Length + payload.Length];
            Array.Copy(frameHeader, frame, frameHeader.Length);
            Array.Copy(payload, 0, frame, frameHeader.Length, payload.Length);
            frames.Add(frame);
        }

        var totalLength = frames.Sum(f => f.Length);
        var response = new byte[totalLength];
        var offset = 0;
        foreach (var frame in frames)
        {
            Array.Copy(frame, 0, response, offset, frame.Length);
            offset += frame.Length;
        }

        return response;
    }
}