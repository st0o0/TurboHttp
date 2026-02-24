using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9112 compliant HTTP/1.1 request encoder with zero-allocation patterns.
/// Writes directly to Span&lt;byte&gt; for maximum efficiency.
/// </summary>
public static class Http11Encoder
{
    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Encodes an HTTP/1.1 request directly into a span.
    /// Zero-allocation - writes directly to the provided buffer.
    /// </summary>
    /// <param name="request">The HTTP request to encode</param>
    /// <param name="buffer">Target buffer (advanced as data is written)</param>
    /// <returns>Total bytes written</returns>
    public static int Encode(HttpRequestMessage request, ref Span<byte> buffer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        var bytesWritten = 0;

        // 1. Request-Line (RFC 9112 Section 3)
        bytesWritten += WriteRequestLine(request, ref buffer);

        // 2. Host header (RFC 9112 Section 5.4 - MUST be present and first)
        bytesWritten += WriteHostHeader(request.RequestUri, ref buffer);

        // 3. Request headers (excluding Host which we already wrote)
        bytesWritten += WriteHeaders(request.Headers, ref buffer, skipHost: true);

        // 4. Content headers (if body present)
        if (request.Content != null)
        {
            bytesWritten += WriteHeaders(request.Content.Headers, ref buffer, skipHost: false);
        }

        // 5. Connection header (if not already set, default to keep-alive)
        bytesWritten += WriteConnectionHeaderIfNeeded(request.Headers, ref buffer);

        // 6. Header/body separator
        bytesWritten += WriteCrlf(ref buffer);

        // 7. Body (if present)
        if (request.Content != null)
        {
            bytesWritten += WriteBody(request.Content, ref buffer);
        }

        return bytesWritten;
    }

    /// <summary>
    /// Encodes an HTTP/1.1 request into a Memory buffer (legacy compatibility).
    /// </summary>
    public static long Encode(HttpRequestMessage request, ref Memory<byte> buffer)
    {
        var span = buffer.Span;
        var written = Encode(request, ref span);
        buffer = buffer[written..];
        return written;
    }

    // ── Request Line ────────────────────────────────────────────────────────────

    private static int WriteRequestLine(HttpRequestMessage request, ref Span<byte> buffer)
    {
        var bytesWritten = 0;

        // Method (GET, POST, etc.)
        bytesWritten += WriteAscii(ref buffer, request.Method.Method);

        // Space
        bytesWritten += WriteBytes(ref buffer, " "u8);

        // Request-target (RFC 9112 Section 3.2)
        var pathAndQuery = request.RequestUri!.PathAndQuery;
        if (string.IsNullOrEmpty(pathAndQuery))
        {
            pathAndQuery = "/";
        }

        bytesWritten += WriteAscii(ref buffer, pathAndQuery);

        // HTTP/1.1 and CRLF
        bytesWritten += WriteBytes(ref buffer, " HTTP/1.1\r\n"u8);

        return bytesWritten;
    }

    // ── Host Header ─────────────────────────────────────────────────────────────

    private static int WriteHostHeader(Uri uri, ref Span<byte> buffer)
    {
        var bytesWritten = 0;

        bytesWritten += WriteBytes(ref buffer, "Host: "u8);
        bytesWritten += WriteAscii(ref buffer, uri.Host);

        // Include port if non-default
        if (!uri.IsDefaultPort)
        {
            bytesWritten += WriteBytes(ref buffer, ":"u8);
            bytesWritten += WriteInt(ref buffer, uri.Port);
        }

        bytesWritten += WriteCrlf(ref buffer);

        return bytesWritten;
    }

    // ── Headers ─────────────────────────────────────────────────────────────────

    private static int WriteHeaders(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
        ref Span<byte> buffer,
        bool skipHost)
    {
        var bytesWritten = 0;

        foreach (var header in headers)
        {
            // Skip Host - we handle it separately
            if (skipHost && header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip Connection - we handle it separately
            if (header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bytesWritten += WriteHeader(ref buffer, header.Key, header.Value);
        }

        return bytesWritten;
    }

    private static int WriteHeader(ref Span<byte> buffer, string name, IEnumerable<string> values)
    {
        var bytesWritten = 0;

        // Header name
        bytesWritten += WriteAscii(ref buffer, name);
        bytesWritten += WriteBytes(ref buffer, ": "u8);

        // Values joined with comma (RFC 9110 Section 5.3)
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                bytesWritten += WriteBytes(ref buffer, ", "u8);
            }

            bytesWritten += WriteAscii(ref buffer, value);
            first = false;
        }

        bytesWritten += WriteCrlf(ref buffer);

        return bytesWritten;
    }

    private static int WriteConnectionHeaderIfNeeded(HttpRequestHeaders headers, ref Span<byte> buffer)
    {
        var bytesWritten = 0;

        // Check if Connection header is already set
        if (headers.Connection.Any(value => value.Equals("close", StringComparison.OrdinalIgnoreCase)))
        {
            bytesWritten += WriteBytes(ref buffer, "Connection: close\r\n"u8);
            return bytesWritten;
        }

        // Other connection values - write them with keep-alive
        bytesWritten += WriteBytes(ref buffer, "Connection: "u8);

        var first = true;
        foreach (var value in headers.Connection)
        {
            if (!first)
            {
                bytesWritten += WriteBytes(ref buffer, ", "u8);
            }
            bytesWritten += WriteAscii(ref buffer, value);
            first = false;
        }

        if (!first) bytesWritten += WriteBytes(ref buffer, ", "u8);
        bytesWritten += WriteBytes(ref buffer, "keep-alive\r\n"u8);

        return bytesWritten;
    }

    // ── Body ────────────────────────────────────────────────────────────────────

    private static int WriteBody(HttpContent content, ref Span<byte> buffer)
    {
        using var stream = content.ReadAsStream();
        var total = 0;

        while (buffer.Length > 0)
        {
            var read = stream.Read(buffer);
            if (read == 0)
            {
                break;
            }

            buffer = buffer[read..];
            total += read;
        }

        return total;
    }

    // ── Low-Level Write Utilities ───────────────────────────────────────────────

    /// <summary>
    /// Writes bytes directly to span and advances it.
    /// </summary>
    private static int WriteBytes(ref Span<byte> buffer, ReadOnlySpan<byte> data)
    {
        data.CopyTo(buffer);
        buffer = buffer[data.Length..];
        return data.Length;
    }

    /// <summary>
    /// Writes ASCII string directly to span and advances it.
    /// </summary>
    private static int WriteAscii(ref Span<byte> buffer, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var written = Encoding.ASCII.GetBytes(value.AsSpan(), buffer);
        buffer = buffer[written..];
        return written;
    }

    /// <summary>
    /// Writes CRLF and advances span.
    /// </summary>
    private static int WriteCrlf(ref Span<byte> buffer)
    {
        buffer[0] = (byte)'\r';
        buffer[1] = (byte)'\n';
        buffer = buffer[2..];
        return 2;
    }

    /// <summary>
    /// Writes an integer as ASCII digits without heap allocation.
    /// </summary>
    private static int WriteInt(ref Span<byte> buffer, int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be non-negative");
        }

        // Max int32 is 10 digits
        Span<byte> temp = stackalloc byte[10];
        var pos = temp.Length;

        do
        {
            temp[--pos] = (byte)('0' + value % 10);
            value /= 10;
        } while (value > 0);

        var length = temp.Length - pos;
        temp[pos..].CopyTo(buffer);
        buffer = buffer[length..];

        return length;
    }
}