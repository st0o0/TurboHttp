using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace TurboHttp.Protocol;

public static class Http11Encoder
{
    public static long Encode(HttpRequestMessage request, ref Memory<byte> buffer)
    {
        var headers = MergeHeaders(request);
        EnforceRequiredHeaders(request, headers);

        var span = buffer.Span;
        var bytesWritten = 0L;

        // Request-Line
        bytesWritten += WriteAscii(ref span, request.Method.Method);
        bytesWritten += WriteAscii(ref span, " ");
        bytesWritten += WriteAscii(ref span, request.RequestUri!.PathAndQuery);
        bytesWritten += WriteAscii(ref span, " HTTP/1.1\r\n");

        // Headers
        foreach (var (name, values) in headers)
        {
            bytesWritten += WriteAscii(ref span, name);
            bytesWritten += WriteAscii(ref span, ": ");
            bytesWritten += WriteAscii(ref span, string.Join(", ", values));
            bytesWritten += WriteAscii(ref span, "\r\n");
        }

        // Header/Body Separator
        bytesWritten += WriteAscii(ref span, "\r\n");

        // Body
        if (request.Content == null)
        {
            return bytesWritten;
        }

        bytesWritten += CopyContentStream(request.Content, ref span);
        return bytesWritten;
    }

    private static int WriteAscii(ref Span<byte> span, string value)
    {
        var written = Encoding.ASCII.GetBytes(value.AsSpan(), span);
        span = span[written..];
        return written;
    }

    private static Dictionary<string, List<string>> MergeHeaders(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Request-Headers
        foreach (var header in request.Headers)
        {
            headers[header.Key] = new List<string>(header.Value);
        }

        // Content-Headers
        if (request.Content?.Headers == null)
        {
            return headers;
        }

        foreach (var header in request.Content.Headers)
        {
            if (!headers.TryGetValue(header.Key, out var list))
            {
                list = [];
                headers[header.Key] = list;
            }

            list.AddRange(header.Value);
        }

        return headers;
    }

    private static void EnforceRequiredHeaders(HttpRequestMessage request, Dictionary<string, List<string>> headers)
    {
        // Host Header (HTTP/1.1)
        var host = request.RequestUri!.IsDefaultPort
            ? request.RequestUri.Host
            : $"{request.RequestUri.Host}:{request.RequestUri.Port}";

        if (!headers.TryGetValue("Host", out var hostValues) ||
            hostValues.All(h => !string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
        {
            headers["Host"] = [host];
        }

        // Connection Header
        if (headers.TryGetValue("Connection", out var connValues) &&
            connValues.Any(v => v.Equals("close", StringComparison.OrdinalIgnoreCase))) return;
        if (!headers.TryGetValue("Connection", out _))
        {
            headers["Connection"] = [];
        }

        headers["Connection"].Add("keep-alive");
    }

    private static int CopyContentStream(HttpContent content, ref Span<byte> destination)
    {
        var stream = content.ReadAsStream();
        var total = 0;

        while (total < destination.Length)
        {
            var read = stream.Read(destination[total..]);
            if (read == 0) break;
            total += read;
        }

        return total;
    }
}