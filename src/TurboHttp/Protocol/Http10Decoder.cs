using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace TurboHttp.Protocol;

public sealed class Http10Decoder
{
    private ReadOnlyMemory<byte> _remainder = ReadOnlyMemory<byte>.Empty;

    public bool TryDecode(ReadOnlyMemory<byte> incomingData, out HttpResponseMessage? response)
    {
        response = null;
        var working = Combine(_remainder, incomingData);
        _remainder = ReadOnlyMemory<byte>.Empty;

        var headerEnd = FindHeaderEnd(working.Span);
        if (headerEnd < 0)
        {
            _remainder = working;
            return false;
        }

        var headerBytes = working[..headerEnd].ToArray();
        var lines = SplitHeaderLines(headerBytes);
        if (lines.Length == 0) return false;

        var headers = ParseHeaders(lines[1..]);
        var bodyStart = headerEnd + GetHeaderDelimiterLength(working.Span, headerEnd);
        var bodyData = working[bodyStart..];

        if (headers.TryGetValue("Content-Length", out var clValues) &&
            int.TryParse(clValues.LastOrDefault(), out var len) && len >= 0)
        {
            if (bodyData.Length < len)
            {
                _remainder = working;
                return false;
            }

            response = BuildResponse(lines[0], headers, bodyData.Span[..len].ToArray());
            return true;
        }

        response = BuildResponse(lines[0], headers, bodyData.ToArray());
        return true;
    }

    public bool TryDecodeEof(out HttpResponseMessage? response)
    {
        response = null;
        if (_remainder.IsEmpty) return false;

        var span = _remainder.Span;
        var headerEnd = FindHeaderEnd(span);
        if (headerEnd < 0) return false;

        var headerBytes = _remainder[..headerEnd].ToArray();
        var lines = SplitHeaderLines(headerBytes);
        if (lines.Length == 0) return false;

        var headers = ParseHeaders(lines[1..]);
        var index = headerEnd + GetHeaderDelimiterLength(span, headerEnd);
        var body = _remainder[index..].ToArray();

        response = BuildResponse(lines[0], headers, body);
        _remainder = ReadOnlyMemory<byte>.Empty;
        return true;
    }

    public void Reset() => _remainder = ReadOnlyMemory<byte>.Empty;

    private static Dictionary<string, List<string>> ParseHeaders(string[] lines)
    {
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? lastHeader = null;

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;

            if ((rawLine[0] == ' ' || rawLine[0] == '\t') && lastHeader != null)
            {
                headers[lastHeader].Add(headers[lastHeader].Last() + " " + rawLine.Trim());
                continue;
            }

            var colon = rawLine.IndexOf(':');
            if (colon <= 0) continue;

            var name = rawLine[..colon].Trim();
            var value = rawLine[(colon + 1)..].Trim();

            if (!headers.ContainsKey(name))
            {
                headers[name] = [];
            }

            headers[name].Add(value);
            lastHeader = name;
        }

        return headers;
    }

    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Length", "Content-Encoding",
        "Content-Language", "Content-Location", "Content-MD5",
        "Content-Range", "Content-Disposition", "Expires", "Last-Modified"
    };

    private static HttpResponseMessage BuildResponse(string statusLine, Dictionary<string, List<string>> headers,
        byte[] body)
    {
        var parts = statusLine.Split(' ', 3);
        var statusCode = 500;
        if (parts.Length >= 2 && int.TryParse(parts[1], out var code)) statusCode = code;

        var reasonPhrase = parts.Length > 2 ? parts[2] : string.Empty;
        var response = new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            ReasonPhrase = reasonPhrase,
            Version = new Version(1, 0)
        };

        ByteArrayContent? content = null;
        if (body.Length > 0)
        {
            content = new ByteArrayContent(body);
            response.Content = content;
        }

        foreach (var (name, values) in headers)
        {
            foreach (var value in values)
            {
                if (ContentHeaders.Contains(name) && content != null)
                {
                    content.Headers.TryAddWithoutValidation(name, value);
                }
                else
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        return response;
    }

    private static ReadOnlyMemory<byte> Combine(ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var merged = new byte[a.Length + b.Length];
        a.Span.CopyTo(merged.AsSpan());
        b.Span.CopyTo(merged.AsSpan(a.Length));
        return merged;
    }

    private static int FindHeaderEnd(ReadOnlySpan<byte> span)
    {
        for (var i = 0; i < span.Length - 1; i++)
        {
            if ((span[i] == '\r' && span[i + 1] == '\n' && i + 3 < span.Length && span[i + 2] == '\r' &&
                 span[i + 3] == '\n') ||
                (span[i] == '\n' && span[i + 1] == '\n'))
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetHeaderDelimiterLength(ReadOnlySpan<byte> span, int headerEnd)
    {
        if (headerEnd + 3 < span.Length && span[headerEnd] == '\r' && span[headerEnd + 1] == '\n' &&
            span[headerEnd + 2] == '\r' && span[headerEnd + 3] == '\n')
        {
            return 4;
        }

        return 2;
    }

    private static string[] SplitHeaderLines(byte[] headerBytes)
    {
        var headerText = Encoding.GetEncoding("ISO-8859-1").GetString(headerBytes);
        return headerText.Split(["\r\n", "\n"], StringSplitOptions.None);
    }
}