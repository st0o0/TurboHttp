using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace TurboMqtt.Protocol;

public sealed class Http11Decoder
{
    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;

    public bool TryDecode(ReadOnlyMemory<byte> incomingData, out ImmutableList<HttpResponseMessage> responses)
    {
        var builder = ImmutableList.CreateBuilder<HttpResponseMessage>();
        responses = ImmutableList<HttpResponseMessage>.Empty;

        var working = Combine(_remainder, incomingData);
        _remainder = ReadOnlyMemory<byte>.Empty;

        while (working.Length > 0)
        {
            var result = TryParseOne(working, out var response, out var consumed);

            if (result.Success)
            {
                if ((int)response!.StatusCode >= 100 && (int)response.StatusCode < 200)
                {
                    working = working[consumed..];
                    continue;
                }

                builder.Add(response);
                working = working[consumed..];
                continue;
            }

            if (result.Error == HttpDecodeError.NeedMoreData)
            {
                _remainder = working;
                break;
            }

            _remainder = ReadOnlyMemory<byte>.Empty;
            throw new HttpDecoderException(result.Error!.Value);
        }

        if (builder.Count > 0)
        {
            responses = builder.ToImmutable();
            return true;
        }

        return false;
    }

    public void Reset() => _remainder = ReadOnlyMemory<byte>.Empty;

    private static HttpDecodeResult TryParseOne(
        ReadOnlyMemory<byte> buffer,
        out HttpResponseMessage? response,
        out int consumed)
    {
        response = null;
        consumed = 0;

        var span = buffer.Span;

        // 1. Search status-Line
        var headerEnd = FindCrlfCrlf(span);
        if (headerEnd < 0)
            return HttpDecodeResult.Incomplete();

        var headerText = Encoding.ASCII.GetString(span[..headerEnd]);
        var lines = headerText.Split("\r\n");

        if (lines.Length == 0 || string.IsNullOrEmpty(lines[0]))
            return HttpDecodeResult.Fail(HttpDecodeError.InvalidStatusLine);

        // Status line: "HTTP/1.1 200 OK"
        var statusParts = lines[0].Split(' ', 3);
        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], out var statusCode))
            return HttpDecodeResult.Fail(HttpDecodeError.InvalidStatusLine);

        var reasonPhrase = statusParts.Length > 2 ? statusParts[2] : string.Empty;

        // Headers
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (!headers.TryGetValue(name, out var values))
            {
                headers[name] = values = [];
            }

            values.Add(value);
        }

        response = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)statusCode,
            ReasonPhrase = reasonPhrase,
            RequestMessage = null,
            Version = new Version(1, 1)
        };

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }

        var bodyStart = headerEnd + 4;
        var bodyData = buffer[bodyStart..];

        if (IsNoBodyResponse(statusCode))
        {
            consumed = bodyStart;
            return HttpDecodeResult.Ok();
        }

        var (bodyBytes, bodyConsumed, trailerHeaders) = ParseBody(bodyData.Span, headers);
        if (bodyBytes == null)
        {
            return HttpDecodeResult.Incomplete();
        }

        var content = new ByteArrayContent(bodyBytes);

        foreach (var (name, values) in headers.Where(h => IsContentHeader(h.Key)))
        {
            foreach (var value in values)
            {
                content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        // Trailer Headers
        if (trailerHeaders != null)
        {
            foreach (var (name, values) in trailerHeaders)
            {
                foreach (var value in values)
                {
                    content.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        response.Content = content;
        consumed = bodyStart + bodyConsumed;
        return HttpDecodeResult.Ok();
    }

    private static bool IsNoBodyResponse(int statusCode) =>
        statusCode is >= 100 and < 200 or 204 or 304;

    private static (byte[]? body, int consumed, Dictionary<string, List<string>>? trailers)
        ParseBody(ReadOnlySpan<byte> data, Dictionary<string, List<string>> headers)
    {
        var transferEncoding = GetSingleHeader(headers, "Transfer-Encoding");
        var contentLength = GetContentLengthHeader(headers);

        if (!string.IsNullOrEmpty(transferEncoding) &&
            transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            return ParseChunkedBody(data);
        }

        if (contentLength.HasValue)
        {
            if (data.Length < contentLength.Value)
            {
                return (null, 0, null);
            }

            var body = data[..contentLength.Value].ToArray();
            return (body, contentLength.Value, null);
        }

        return ([], 0, null);
    }

    private static (byte[] body, int consumed, Dictionary<string, List<string>>? trailers) ParseChunkedBody(
        ReadOnlySpan<byte> data)
    {
        var assembled = new MemoryStream();
        var pos = 0;
        Dictionary<string, List<string>>? trailers = null;

        while (pos < data.Length)
        {
            var lineEnd = FindCrLf(data, pos);
            if (lineEnd < 0) return (null!, 0, null);

            var sizeSpan = data[pos..lineEnd];
            var sizeHex = Encoding.ASCII.GetString(sizeSpan[..Math.Min(sizeSpan.IndexOf((byte)';'), sizeSpan.Length)])
                .Trim();

            if (!TryParseHex(sizeHex, out var chunkSize))
            {
                throw new HttpDecoderException(HttpDecodeError.InvalidChunkedEncoding);
            }

            pos = lineEnd + 2;

            if (chunkSize == 0)
            {
                var trailerEnd = FindCrlfCrlf(data[pos..]);
                if (trailerEnd < 0)
                {
                    return (null!, 0, null);
                }

                if (trailerEnd > 0)
                {
                    var trailerText = Encoding.ASCII.GetString(data.Slice(pos, trailerEnd));
                    trailers = ParseTrailerHeaders(trailerText);
                }

                return (assembled.ToArray(), pos + trailerEnd + 4, trailers);
            }

            if (pos + chunkSize + 2 > data.Length)
                return (null!, 0, null);

            assembled.Write(data.Slice(pos, chunkSize));
            pos += chunkSize + 2;
        }

        return (null!, 0, null);
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static int? GetContentLengthHeader(Dictionary<string, List<string>> headers) =>
        GetSingleHeader(headers, "Content-Length") is { } cl &&
        int.TryParse(cl, out var len)
            ? len
            : null;

    private static string? GetSingleHeader(Dictionary<string, List<string>> headers, string name) =>
        headers.TryGetValue(name, out var values) && values.Count == 1
            ? values[0]
            : null;

    private static bool IsContentHeader(string name) =>
        name.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("content-type", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("content-", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, List<string>> ParseTrailerHeaders(string trailerText)
    {
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in trailerText.Split("\r\n"))
        {
            if (string.IsNullOrEmpty(line)) continue;
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (!headers.TryGetValue(name, out var values))
                    headers[name] = values = [];
                values.Add(value);
            }
        }

        return headers;
    }

    private static ReadOnlyMemory<byte> Combine(ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var merged = new byte[a.Length + b.Length];
        a.Span.CopyTo(merged);
        b.Span.CopyTo(merged.AsSpan(a.Length));
        return merged;
    }

    private static int FindCrlfCrlf(ReadOnlySpan<byte> span)
    {
        for (var i = 0; i <= span.Length - 4; i++)
        {
            if (span[i] == '\r' && span[i + 1] == '\n' && span[i + 2] == '\r' && span[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindCrLf(ReadOnlySpan<byte> span, int start)
    {
        for (var i = start; i < span.Length - 1; i++)
        {
            if (span[i] == '\r' && span[i + 1] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryParseHex(string hex, out int value)
    {
        value = 0;
        return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out value);
    }
}