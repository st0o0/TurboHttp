using BenchmarkDotNet.Attributes;
using TurboHttp.Protocol;
using System.Buffers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TurboHttp.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, targetCount: 5)]
public class DecoderBenchmarks
{
    private byte[] _http10_200_NoBody = null!;
    private byte[] _http10_200_1KB = null!;
    private byte[] _http11_200_NoBody = null!;
    private byte[] _http11_200_1KB = null!;
    private byte[] _http11_200_Chunked_8Chunks = null!;
    private byte[] _http11_200_64KB = null!;
    private byte[] _http2_SingleDataFrame = null!;
    private byte[] _http2_8DataFrames = null!;

    [GlobalSetup]
    public void Setup()
    {
        // HTTP/1.0: 200 OK with no body
        _http10_200_NoBody = "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        // HTTP/1.0: 200 OK with 1KB body
        var body1KB = new byte[1024];
        Array.Fill(body1KB, (byte)'x');
        _http10_200_1KB = BuildHttp10Response(200, body1KB);

        // HTTP/1.1: 200 OK with no body
        _http11_200_NoBody = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        // HTTP/1.1: 200 OK with 1KB body
        _http11_200_1KB = BuildHttp11Response(200, body1KB);

        // HTTP/1.1: 200 OK with chunked encoding (8 chunks)
        _http11_200_Chunked_8Chunks = BuildHttp11ChunkedResponse(8);

        // HTTP/1.1: 200 OK with 64KB body
        var body64KB = new byte[64 * 1024];
        Array.Fill(body64KB, (byte)'y');
        _http11_200_64KB = BuildHttp11Response(200, body64KB);

        // HTTP/2: HEADERS frame (200 OK) + single DATA frame
        _http2_SingleDataFrame = BuildHttp2Response(new byte[512]);
        // HTTP/2: HEADERS frame + 8 DATA frames
        _http2_8DataFrames = BuildHttp2MultipleDataFrames(8);
    }

    [Benchmark]
    public bool Http10_Decode_200_NoBody()
    {
        var decoder = new Http10Decoder();
        var buffer = _http10_200_NoBody.AsMemory();
        return decoder.TryDecode(buffer, out _);
    }

    [Benchmark]
    public bool Http10_Decode_200_1KB()
    {
        var decoder = new Http10Decoder();
        var buffer = _http10_200_1KB.AsMemory();
        return decoder.TryDecode(buffer, out _);
    }

    [Benchmark]
    public bool Http11_Decode_200_NoBody()
    {
        var decoder = new Http11Decoder();
        var buffer = _http11_200_NoBody.AsMemory();
        return decoder.TryDecode(buffer, out _);
    }

    [Benchmark]
    public bool Http11_Decode_200_1KB()
    {
        var decoder = new Http11Decoder();
        var buffer = _http11_200_1KB.AsMemory();
        return decoder.TryDecode(buffer, out _);
    }

    [Benchmark]
    public bool Http11_Decode_200_Chunked_8Chunks()
    {
        var decoder = new Http11Decoder();
        var buffer = _http11_200_Chunked_8Chunks.AsMemory();
        return decoder.TryDecode(buffer, out _);
    }

    [Benchmark]
    public bool Http11_Decode_200_64KB()
    {
        var decoder = new Http11Decoder();
        var buffer = _http11_200_64KB.AsMemory();
        return decoder.TryDecode(buffer, out _);
    }

    [Benchmark]
    public bool Http2_Decode_SingleDataFrame()
    {
        var decoder = new Http2Decoder();
        var buffer = _http2_SingleDataFrame.AsMemory();
        return decoder.TryDecode(buffer, out _);
    }

    [Benchmark]
    public bool Http2_Decode_8DataFrames()
    {
        var decoder = new Http2Decoder();
        var buffer = _http2_8DataFrames.AsMemory();
        return decoder.TryDecode(buffer, out _);
    }

    private static byte[] BuildHttp10Response(int statusCode, byte[] body)
    {
        var header = System.Text.Encoding.ASCII.GetBytes(
            $"HTTP/1.0 {statusCode} OK\r\nContent-Length: {body.Length}\r\n\r\n");
        var response = new byte[header.Length + body.Length];
        Array.Copy(header, response, header.Length);
        Array.Copy(body, 0, response, header.Length, body.Length);
        return response;
    }

    private static byte[] BuildHttp11Response(int statusCode, byte[] body)
    {
        var header = System.Text.Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} OK\r\nContent-Length: {body.Length}\r\n\r\n");
        var response = new byte[header.Length + body.Length];
        Array.Copy(header, response, header.Length);
        Array.Copy(body, 0, response, header.Length, body.Length);
        return response;
    }

    private static byte[] BuildHttp11ChunkedResponse(int chunkCount)
    {
        using var pool = MemoryPool<byte>.Shared;
        using var buffer = pool.Rent(64 * 1024);
        var span = buffer.Memory.Span;
        var offset = 0;

        var headerLine = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n"u8;
        headerLine.CopyTo(span[offset..]);
        offset += headerLine.Length;

        for (var i = 0; i < chunkCount; i++)
        {
            var chunkSizeLine = "1\r\n"u8.ToArray();
            chunkSizeLine.CopyTo(span[offset..]);
            offset += chunkSizeLine.Length;

            span[offset] = (byte)'x';
            offset += 1;

            "\r\n"u8.CopyTo(span[offset..]);
            offset += 2;
        }

        "0\r\n\r\n"u8.CopyTo(span[offset..]);
        offset += 5;

        return span[..offset].ToArray();
    }

    // Build a minimal HTTP/2 HEADERS frame (200 OK, END_STREAM+END_HEADERS) + DATA payload
    // HPACK: 0x88 = indexed representation, index 8 = ":status: 200"
    private static byte[] BuildHttp2Response(byte[] payload)
    {
        // HEADERS frame: length=1, type=0x01, flags=0x05(END_STREAM|END_HEADERS), stream=1
        var headersFrame = new byte[] { 0x00, 0x00, 0x01, 0x01, 0x05, 0x00, 0x00, 0x00, 0x01, 0x88 };

        var result = new byte[headersFrame.Length + payload.Length];
        Array.Copy(headersFrame, result, headersFrame.Length);
        Array.Copy(payload, 0, result, headersFrame.Length, payload.Length);
        return result;
    }

    private static byte[] BuildHttp2MultipleDataFrames(int frameCount)
    {
        // HEADERS frame: END_HEADERS only (no END_STREAM, body follows)
        var headersFrame = new byte[] { 0x00, 0x00, 0x01, 0x01, 0x04, 0x00, 0x00, 0x00, 0x01, 0x88 };

        var frames = new List<byte[]> { headersFrame };
        var chunkSize = 512;

        for (var i = 0; i < frameCount; i++)
        {
            var isLast = i == frameCount - 1;
            var payload = new byte[chunkSize];
            Array.Fill(payload, (byte)('a' + (i % 26)));

            // DATA frame: 9-byte header + payload
            var frameHeader = new byte[9];
            frameHeader[0] = (byte)((chunkSize >> 16) & 0xFF);
            frameHeader[1] = (byte)((chunkSize >> 8) & 0xFF);
            frameHeader[2] = (byte)(chunkSize & 0xFF);
            frameHeader[3] = 0x00; // DATA frame type
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
