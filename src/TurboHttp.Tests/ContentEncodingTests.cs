#nullable enable

using System.IO.Compression;
using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

/// <summary>
/// RFC 9110 §8.4 — Content-Encoding handling tests.
/// Covers decompression (gzip, deflate, br, identity), stacked encodings,
/// encoder Accept-Encoding header injection, and decoder integration.
/// </summary>
public sealed class ContentEncodingTests
{
    // ── Compression helpers ──────────────────────────────────────────────────

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] BrotliCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var br = new BrotliStream(output, CompressionLevel.Fastest))
        {
            br.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] BuildHttp11ResponseBytes(
        int statusCode,
        string? contentEncoding,
        byte[] body,
        string contentType = "text/plain")
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} OK\r\n");
        sb.Append($"Content-Type: {contentType}\r\n");
        if (contentEncoding != null)
        {
            sb.Append($"Content-Encoding: {contentEncoding}\r\n");
        }

        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    private static byte[] BuildHttp10ResponseBytes(
        int statusCode,
        string? contentEncoding,
        byte[] body)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.0 {statusCode} OK\r\n");
        sb.Append("Content-Type: text/plain\r\n");
        if (contentEncoding != null)
        {
            sb.Append($"Content-Encoding: {contentEncoding}\r\n");
        }

        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    private static byte[] BuildHttp2Response(
        int streamId,
        string? contentEncoding,
        byte[] body)
    {
        var hpack = new HpackEncoder();
        var headers = new List<(string, string)>
        {
            (":status", "200"),
            ("content-type", "text/plain"),
        };
        if (contentEncoding != null)
        {
            headers.Add(("content-encoding", contentEncoding));
        }

        headers.Add(("content-length", body.Length.ToString()));

        var headerBlock = hpack.Encode(headers);
        var frames = new List<byte>();

        // HEADERS frame
        var payloadLen = headerBlock.Length;
        var headersFrame = new byte[9 + payloadLen];
        headersFrame[0] = (byte)(payloadLen >> 16);
        headersFrame[1] = (byte)(payloadLen >> 8);
        headersFrame[2] = (byte)payloadLen;
        headersFrame[3] = 0x01; // HEADERS
        headersFrame[4] = 0x04; // END_HEADERS
        headersFrame[5] = 0x00;
        headersFrame[6] = 0x00;
        headersFrame[7] = 0x00;
        headersFrame[8] = (byte)streamId;
        headerBlock.Span.CopyTo(headersFrame.AsSpan(9));
        frames.AddRange(headersFrame);

        // DATA frame
        var dataFrame = new byte[9 + body.Length];
        dataFrame[0] = (byte)(body.Length >> 16);
        dataFrame[1] = (byte)(body.Length >> 8);
        dataFrame[2] = (byte)body.Length;
        dataFrame[3] = 0x00; // DATA
        dataFrame[4] = 0x01; // END_STREAM
        dataFrame[5] = 0x00;
        dataFrame[6] = 0x00;
        dataFrame[7] = 0x00;
        dataFrame[8] = (byte)streamId;
        body.CopyTo(dataFrame.AsSpan(9));
        frames.AddRange(dataFrame);

        return [.. frames];
    }

    // ── 1. Http11Encoder — Accept-Encoding injection ─────────────────────────

    [Fact(DisplayName = "CE-enc-001: Should_AddAcceptEncoding_When_NotAlreadySet")]
    public void Should_AddAcceptEncoding_When_NotAlreadySet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);

        var encoded = Encoding.ASCII.GetString(buffer, 0, written);

        Assert.Contains("Accept-Encoding: gzip, deflate, br", encoded);
    }

    [Fact(DisplayName = "CE-enc-002: Should_NotOverrideAcceptEncoding_When_AlreadySet")]
    public void Should_NotOverrideAcceptEncoding_When_AlreadySet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Add("Accept-Encoding", "gzip");

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);

        var encoded = Encoding.ASCII.GetString(buffer, 0, written);

        Assert.Contains("Accept-Encoding: gzip", encoded);
        Assert.DoesNotContain("Accept-Encoding: gzip, deflate, br", encoded);
    }

    [Fact(DisplayName = "CE-enc-003: Should_AddAcceptEncoding_For_Post_With_Body")]
    public void Should_AddAcceptEncoding_For_Post_With_Body()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("hello"u8.ToArray())
        };

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http11Encoder.Encode(request, ref span);

        var encoded = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Accept-Encoding: gzip, deflate, br", encoded);
    }

    // ── 2. Http11Decoder — gzip decompression ────────────────────────────────

    [Fact(DisplayName = "CE-11-001: Should_DecompressGzip_When_ContentEncodingIsGzip")]
    public void Should_DecompressGzip_When_ContentEncodingIsGzip()
    {
        var original = "Hello, World!"u8.ToArray();
        var compressed = GzipCompress(original);

        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        Assert.NotNull(responses);
        Assert.Single(responses);

        var body = responses[0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-11-002: Should_DecompressDeflate_When_ContentEncodingIsDeflate")]
    public void Should_DecompressDeflate_When_ContentEncodingIsDeflate()
    {
        var original = "Hello, deflate!"u8.ToArray();
        var compressed = DeflateCompress(original);

        var responseBytes = BuildHttp11ResponseBytes(200, "deflate", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-11-003: Should_DecompressBrotli_When_ContentEncodingIsBr")]
    public void Should_DecompressBrotli_When_ContentEncodingIsBr()
    {
        var original = "Hello, Brotli!"u8.ToArray();
        var compressed = BrotliCompress(original);

        var responseBytes = BuildHttp11ResponseBytes(200, "br", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-11-004: Should_LeaveBodyUnchanged_When_ContentEncodingIsIdentity")]
    public void Should_LeaveBodyUnchanged_When_ContentEncodingIsIdentity()
    {
        var original = "plain text"u8.ToArray();
        var responseBytes = BuildHttp11ResponseBytes(200, "identity", original);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-11-005: Should_LeaveBodyUnchanged_When_NoContentEncoding")]
    public void Should_LeaveBodyUnchanged_When_NoContentEncoding()
    {
        var original = "plain text"u8.ToArray();
        var responseBytes = BuildHttp11ResponseBytes(200, null, original);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-11-006: Should_RemoveContentEncodingHeader_After_Decompression")]
    public void Should_RemoveContentEncodingHeader_After_Decompression()
    {
        var original = "Hello"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var response = responses![0];

        Assert.Empty(response.Content!.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "CE-11-007: Should_UpdateContentLength_After_Decompression")]
    public void Should_UpdateContentLength_After_Decompression()
    {
        var original = "Hello, World!"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var response = responses![0];

        Assert.Equal(original.Length, response.Content!.Headers.ContentLength);
    }

    [Fact(DisplayName = "CE-11-008: Should_ThrowDecompressionFailed_When_UnknownEncoding")]
    public void Should_ThrowDecompressionFailed_When_UnknownEncoding()
    {
        var body = "test"u8.ToArray();
        var responseBytes = BuildHttp11ResponseBytes(200, "zstd", body);
        var decoder = new Http11Decoder();

        var ex = Assert.Throws<HttpDecoderException>(() =>
            decoder.TryDecode(responseBytes, out _));

        Assert.Equal(HttpDecodeError.DecompressionFailed, ex.DecodeError);
    }

    [Fact(DisplayName = "CE-11-009: Should_ThrowDecompressionFailed_When_CorruptGzipData")]
    public void Should_ThrowDecompressionFailed_When_CorruptGzipData()
    {
        // Valid gzip header (10 bytes) + garbage deflate data — cannot be decompressed
        var corrupt = new byte[]
        {
            0x1f, 0x8b,             // gzip magic
            0x08,                   // compression method = deflate
            0x00,                   // flags = none
            0x00, 0x00, 0x00, 0x00, // mtime
            0x00,                   // xfl
            0xff,                   // OS = unknown
            0xDE, 0xAD, 0xBE, 0xEF, 0xDE, 0xAD, 0xBE, 0xEF  // garbage deflate data
        };
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", corrupt);
        var decoder = new Http11Decoder();

        Assert.Throws<HttpDecoderException>(() =>
            decoder.TryDecode(responseBytes, out _));
    }

    [Fact(DisplayName = "CE-11-010: Should_HandleEmptyGzip_When_EmptyBodyWithGzipEncoding")]
    public void Should_HandleEmptyGzip_When_EmptyBodyWithGzipEncoding()
    {
        var compressed = GzipCompress([]);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Empty(body);
    }

    [Fact(DisplayName = "CE-11-011: Should_DecompressGzip_CaseInsensitive_Encoding")]
    public void Should_DecompressGzip_CaseInsensitive_Encoding()
    {
        var original = "test"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "GZIP", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-11-012: Should_DecompressXGzip_When_ContentEncodingIsXGzip")]
    public void Should_DecompressXGzip_When_ContentEncodingIsXGzip()
    {
        var original = "x-gzip test"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "x-gzip", compressed);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-11-013: Should_NotDecompress_204_NoBody_Response")]
    public void Should_NotDecompress_204_NoBody_Response()
    {
        var responseBytes = Encoding.ASCII.GetBytes(
            "HTTP/1.1 204 No Content\r\nContent-Encoding: gzip\r\n\r\n");
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        Assert.Equal(HttpStatusCode.NoContent, responses![0].StatusCode);
        var body = responses[0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Empty(body);
    }

    [Fact(DisplayName = "CE-11-014: Should_NotConfuse_TransferEncoding_With_ContentEncoding")]
    public void Should_NotConfuse_TransferEncoding_With_ContentEncoding()
    {
        var original = "hello chunked"u8.ToArray();
        var chunkHex = original.Length.ToString("x");
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 200 OK\r\n");
        sb.Append("Content-Type: text/plain\r\n");
        sb.Append("Transfer-Encoding: chunked\r\n");
        sb.Append("\r\n");
        sb.Append($"{chunkHex}\r\n");
        sb.Append(Encoding.UTF8.GetString(original));
        sb.Append("\r\n0\r\n\r\n");

        var responseBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-11-015: Should_DecompressStackedEncodings_GzipThenBr")]
    public void Should_DecompressStackedEncodings_GzipThenBr()
    {
        // "gzip, br" means: gzip was applied first, br was applied last (outermost).
        // Decode in reverse: br first, then gzip.
        var original = "stacked encoding test"u8.ToArray();
        var gzipped = GzipCompress(original);
        var gzippedThenBrotli = BrotliCompress(gzipped);

        var responseBytes = BuildHttp11ResponseBytes(200, "gzip, br", gzippedThenBrotli);
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    // ── 3. Http10Decoder — decompression ─────────────────────────────────────

    [Fact(DisplayName = "CE-10-001: Should_DecompressGzip_When_Http10_ContentEncoding_Gzip")]
    public void Should_DecompressGzip_When_Http10_ContentEncoding_Gzip()
    {
        var original = "HTTP/1.0 gzip test"u8.ToArray();
        var compressed = GzipCompress(original);

        var responseBytes = BuildHttp10ResponseBytes(200, "gzip", compressed);
        var decoder = new Http10Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var response));
        Assert.NotNull(response);
        var body = response!.Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-10-002: Should_RemoveContentEncoding_After_Http10_Decompression")]
    public void Should_RemoveContentEncoding_After_Http10_Decompression()
    {
        var original = "test"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp10ResponseBytes(200, "gzip", compressed);
        var decoder = new Http10Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var response));
        Assert.Empty(response!.Content!.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "CE-10-003: Should_UpdateContentLength_After_Http10_Decompression")]
    public void Should_UpdateContentLength_After_Http10_Decompression()
    {
        var original = "hello http10"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp10ResponseBytes(200, "gzip", compressed);
        var decoder = new Http10Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var response));
        Assert.Equal(original.Length, response!.Content!.Headers.ContentLength);
    }

    [Fact(DisplayName = "CE-10-004: Should_DecompressBrotli_In_Http10_Response")]
    public void Should_DecompressBrotli_In_Http10_Response()
    {
        var original = "brotli in http/1.0"u8.ToArray();
        var compressed = BrotliCompress(original);
        var responseBytes = BuildHttp10ResponseBytes(200, "br", compressed);
        var decoder = new Http10Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var response));
        var body = response!.Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    // ── 4. Http2Decoder — decompression ──────────────────────────────────────

    [Fact(DisplayName = "CE-h2-001: Should_DecompressGzip_When_Http2_ContentEncoding_Gzip")]
    public void Should_DecompressGzip_When_Http2_ContentEncoding_Gzip()
    {
        var original = "HTTP/2 gzip test"u8.ToArray();
        var compressed = GzipCompress(original);

        var responseBytes = BuildHttp2Response(1, "gzip", compressed);
        var decoder = new Http2Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var result));
        Assert.Single(result.Responses);

        var body = result.Responses[0].Response.Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-h2-002: Should_RemoveContentEncoding_After_Http2_Decompression")]
    public void Should_RemoveContentEncoding_After_Http2_Decompression()
    {
        var original = "test"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp2Response(1, "gzip", compressed);
        var decoder = new Http2Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var result));
        var response = result.Responses[0].Response;

        Assert.Empty(response.Content!.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "CE-h2-003: Should_UpdateContentLength_After_Http2_Decompression")]
    public void Should_UpdateContentLength_After_Http2_Decompression()
    {
        var original = "hello http2 gzip"u8.ToArray();
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp2Response(1, "gzip", compressed);
        var decoder = new Http2Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var result));
        var response = result.Responses[0].Response;

        Assert.Equal(original.Length, response.Content!.Headers.ContentLength);
    }

    [Fact(DisplayName = "CE-h2-004: Should_LeaveBodyUnchanged_When_Http2_NoContentEncoding")]
    public void Should_LeaveBodyUnchanged_When_Http2_NoContentEncoding()
    {
        var original = "plain http2"u8.ToArray();
        var responseBytes = BuildHttp2Response(1, null, original);
        var decoder = new Http2Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var result));
        var body = result.Responses[0].Response.Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    // ── 5. Edge cases ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "CE-edge-001: Should_DecompressLargeGzipBody_64KB")]
    public void Should_DecompressLargeGzipBody_64KB()
    {
        var original = new byte[64 * 1024];
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i % 256);
        }

        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed);
        var decoder = new Http11Decoder(maxBodySize: 200_000);

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }

    [Fact(DisplayName = "CE-edge-002: Should_DecompressGzip_Utf8_Multibyte_Content")]
    public void Should_DecompressGzip_Utf8_Multibyte_Content()
    {
        var original = Encoding.UTF8.GetBytes("こんにちは世界 — Hello, World!");
        var compressed = GzipCompress(original);
        var responseBytes = BuildHttp11ResponseBytes(200, "gzip", compressed,
            contentType: "text/plain; charset=utf-8");
        var decoder = new Http11Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var responses));
        var body = responses![0].Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
        Assert.Equal("こんにちは世界 — Hello, World!", Encoding.UTF8.GetString(body));
    }

    [Fact(DisplayName = "CE-edge-003: Should_DecompressBrotli_Via_Http2")]
    public void Should_DecompressBrotli_Via_Http2()
    {
        var original = "HTTP/2 brotli test"u8.ToArray();
        var compressed = BrotliCompress(original);
        var responseBytes = BuildHttp2Response(1, "br", compressed);
        var decoder = new Http2Decoder();

        Assert.True(decoder.TryDecode(responseBytes, out var result));
        var body = result.Responses[0].Response.Content!.ReadAsByteArrayAsync().Result;
        Assert.Equal(original, body);
    }
}
