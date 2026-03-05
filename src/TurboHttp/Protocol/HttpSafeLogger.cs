#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace TurboHttp.Protocol;

/// <summary>
/// A log entry for an HTTP request with sensitive data redacted.
/// </summary>
public sealed record HttpRequestLogEntry
{
    /// <summary>HTTP method (e.g. GET, POST).</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>Request URI as a string, or null if not set.</summary>
    public string? RequestUri { get; init; }

    /// <summary>HTTP version negotiated for this request.</summary>
    public Version? Version { get; init; }

    /// <summary>
    /// Request headers with sensitive header values replaced by <see cref="HttpSafeLogger.RedactedValue"/>.
    /// </summary>
    public IReadOnlyList<(string Name, string Value)> Headers { get; init; } = Array.Empty<(string, string)>();

    /// <summary>
    /// Body content if body logging is enabled, null otherwise.
    /// Value may be truncated to the policy's <see cref="HttpLoggingPolicy.MaxBodyLogLength"/>.
    /// The string "[body logging disabled]" indicates logging was not enabled for this entry.
    /// </summary>
    public string? Body { get; init; }
}

/// <summary>
/// A log entry for an HTTP response with sensitive data redacted.
/// </summary>
public sealed record HttpResponseLogEntry
{
    /// <summary>HTTP status code (e.g. 200, 404).</summary>
    public int StatusCode { get; init; }

    /// <summary>HTTP reason phrase (e.g. "OK", "Not Found").</summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>HTTP version negotiated for this response.</summary>
    public Version? Version { get; init; }

    /// <summary>
    /// Response headers with sensitive header values replaced by <see cref="HttpSafeLogger.RedactedValue"/>.
    /// </summary>
    public IReadOnlyList<(string Name, string Value)> Headers { get; init; } = Array.Empty<(string, string)>();

    /// <summary>
    /// Body content if body logging is enabled, null otherwise.
    /// Value may be truncated to the policy's <see cref="HttpLoggingPolicy.MaxBodyLogLength"/>.
    /// </summary>
    public string? Body { get; init; }
}

/// <summary>
/// Creates sanitized log entries from HTTP request and response objects.
/// </summary>
/// <remarks>
/// Invariants enforced regardless of policy:
/// <list type="bullet">
///   <item>Sensitive headers (Authorization, Proxy-Authorization, Cookie, Set-Cookie) are always redacted.</item>
///   <item>Bodies are not logged by default.</item>
///   <item>The original request/response objects are never mutated.</item>
/// </list>
/// </remarks>
public static class HttpSafeLogger
{
    /// <summary>Placeholder value used in place of a redacted header value.</summary>
    public const string RedactedValue = "[REDACTED]";

    /// <summary>
    /// Creates a safe <see cref="HttpRequestLogEntry"/> from the given request.
    /// Sensitive headers are redacted; the original message is not mutated.
    /// </summary>
    /// <param name="request">The request to log. Must not be null.</param>
    /// <param name="policy">Logging policy. Defaults to <see cref="HttpLoggingPolicy.Default"/>.</param>
    public static HttpRequestLogEntry CreateRequestEntry(
        HttpRequestMessage request,
        HttpLoggingPolicy? policy = null)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        policy ??= HttpLoggingPolicy.Default;
        var headers = SanitizeHeaders(request.Headers, null, policy);

        return new HttpRequestLogEntry
        {
            Method = request.Method.Method,
            RequestUri = request.RequestUri?.ToString(),
            Version = request.Version,
            Headers = headers,
            Body = null,  // Bodies not read from request by default to avoid stream consumption
        };
    }

    /// <summary>
    /// Creates a safe <see cref="HttpResponseLogEntry"/> from the given response.
    /// Sensitive headers are redacted; the original message is not mutated.
    /// </summary>
    /// <param name="response">The response to log. Must not be null.</param>
    /// <param name="policy">Logging policy. Defaults to <see cref="HttpLoggingPolicy.Default"/>.</param>
    /// <param name="bodyText">
    ///     Optional pre-read body text. Only included in the log entry if
    ///     <see cref="HttpLoggingPolicy.LogResponseBody"/> is true.
    ///     Passing a value here does NOT consume the response stream.
    /// </param>
    public static HttpResponseLogEntry CreateResponseEntry(
        HttpResponseMessage response,
        HttpLoggingPolicy? policy = null,
        string? bodyText = null)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        policy ??= HttpLoggingPolicy.Default;
        var headers = SanitizeHeaders(response.Headers, response.Content?.Headers, policy);
        var body = policy.LogResponseBody ? TruncateBody(bodyText, policy.MaxBodyLogLength) : null;

        return new HttpResponseLogEntry
        {
            StatusCode = (int)response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
            Version = response.Version,
            Headers = headers,
            Body = body,
        };
    }

    /// <summary>
    /// Returns true if the given header name is sensitive under the specified policy.
    /// Always true for Authorization, Proxy-Authorization, Cookie, and Set-Cookie.
    /// </summary>
    public static bool IsSensitiveHeader(string headerName, HttpLoggingPolicy? policy = null)
    {
        policy ??= HttpLoggingPolicy.Default;
        return policy.IsSensitive(headerName);
    }

    // ── Private Helpers ──────────────────────────────────────────────────────────

    private static IReadOnlyList<(string Name, string Value)> SanitizeHeaders(
        System.Net.Http.Headers.HttpHeaders? primaryHeaders,
        System.Net.Http.Headers.HttpHeaders? contentHeaders,
        HttpLoggingPolicy policy)
    {
        var result = new List<(string Name, string Value)>();

        if (primaryHeaders is not null)
        {
            AppendHeaders(result, primaryHeaders, policy);
        }

        if (contentHeaders is not null)
        {
            AppendHeaders(result, contentHeaders, policy);
        }

        return result;
    }

    private static void AppendHeaders(
        List<(string Name, string Value)> result,
        System.Net.Http.Headers.HttpHeaders headers,
        HttpLoggingPolicy policy)
    {
        foreach (var header in headers)
        {
            var name = header.Key;
            if (policy.IsSensitive(name))
            {
                result.Add((name, RedactedValue));
            }
            else
            {
                var sb = new StringBuilder();
                var first = true;
                foreach (var v in header.Value)
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(v);
                    first = false;
                }

                result.Add((name, sb.ToString()));
            }
        }
    }

    private static string? TruncateBody(string? body, int maxLength)
    {
        if (body is null)
        {
            return null;
        }

        if (maxLength <= 0)
        {
            return body;
        }

        if (body.Length <= maxLength)
        {
            return body;
        }

        return body[..maxLength] + $"...[truncated, {body.Length - maxLength} chars omitted]";
    }
}
