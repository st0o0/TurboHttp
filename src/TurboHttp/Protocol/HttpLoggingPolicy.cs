#nullable enable

using System.Collections.Generic;

namespace TurboHttp.Protocol;

/// <summary>
/// Configuration for safe HTTP logging behavior.
/// Controls which headers are redacted and whether bodies are included in log entries.
/// </summary>
/// <remarks>
/// By default: sensitive headers are redacted, bodies are not logged.
/// Altering request/response objects is never permitted regardless of policy.
/// </remarks>
public sealed record HttpLoggingPolicy
{
    /// <summary>
    /// Default logging policy: sensitive headers redacted, bodies not logged.
    /// </summary>
    public static readonly HttpLoggingPolicy Default = new();

    /// <summary>
    /// Headers that must always be redacted regardless of policy configuration.
    /// RFC 7235 §4.2 (Authorization), RFC 6265 §5.4 (Cookie), RFC 7235 §4.4 (Proxy-Authorization).
    /// </summary>
    public static readonly IReadOnlySet<string> AlwaysRedacted = new HashSet<string>(
        System.StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "proxy-authorization",
        "cookie",
        "set-cookie",
    };

    /// <summary>
    /// If true, request bodies are included in log entries up to <see cref="MaxBodyLogLength"/> bytes.
    /// Default is false.
    /// </summary>
    public bool LogRequestBody { get; init; } = false;

    /// <summary>
    /// If true, response bodies are included in log entries up to <see cref="MaxBodyLogLength"/> bytes.
    /// Default is false.
    /// </summary>
    public bool LogResponseBody { get; init; } = false;

    /// <summary>
    /// Maximum number of characters from a body to include in a log entry when body logging is enabled.
    /// 0 means no limit (not recommended for production).
    /// Default is 1024.
    /// </summary>
    public int MaxBodyLogLength { get; init; } = 1024;

    /// <summary>
    /// Additional header names to redact beyond <see cref="AlwaysRedacted"/>.
    /// Header name comparison is case-insensitive.
    /// </summary>
    public IReadOnlySet<string>? AdditionalSensitiveHeaders { get; init; } = null;

    /// <summary>
    /// Returns true if the given header name should be redacted under this policy.
    /// </summary>
    public bool IsSensitive(string headerName)
    {
        if (AlwaysRedacted.Contains(headerName))
        {
            return true;
        }

        if (AdditionalSensitiveHeaders is not null && AdditionalSensitiveHeaders.Contains(headerName))
        {
            return true;
        }

        return false;
    }
}
