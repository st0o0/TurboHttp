#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using TurboHttp.Protocol;
using Xunit;

namespace TurboHttp.Tests;

/// <summary>
/// Tests for <see cref="HttpSafeLogger"/> and <see cref="HttpLoggingPolicy"/>.
/// Phase 57 — Safe HTTP Logging.
/// </summary>
public sealed class HttpSafeLoggerTests
{
    // ── Sensitive header detection ───────────────────────────────────────────────

    [Fact(DisplayName = "SL-001: Should_RedactValue_For_Authorization_Header")]
    public void Should_RedactValue_For_Authorization_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token");

        var entry = HttpSafeLogger.CreateRequestEntry(request);

        var auth = entry.Headers.Single(h => h.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpSafeLogger.RedactedValue, auth.Value);
    }

    [Fact(DisplayName = "SL-002: Should_RedactValue_For_Cookie_Header")]
    public void Should_RedactValue_For_Cookie_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Cookie", "session=abc123");

        var entry = HttpSafeLogger.CreateRequestEntry(request);

        var cookie = entry.Headers.Single(h => h.Name.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpSafeLogger.RedactedValue, cookie.Value);
    }

    [Fact(DisplayName = "SL-003: Should_RedactValue_For_ProxyAuthorization_Header")]
    public void Should_RedactValue_For_ProxyAuthorization_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic cHJveHk6cGFzcw==");

        var entry = HttpSafeLogger.CreateRequestEntry(request);

        var proxyAuth = entry.Headers.Single(h => h.Name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpSafeLogger.RedactedValue, proxyAuth.Value);
    }

    [Fact(DisplayName = "SL-004: Should_RedactValue_For_SetCookie_Response_Header")]
    public void Should_RedactValue_For_SetCookie_Response_Header()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", "id=xyz; HttpOnly; Secure");

        var entry = HttpSafeLogger.CreateResponseEntry(response);

        var setCookie = entry.Headers.Single(h => h.Name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpSafeLogger.RedactedValue, setCookie.Value);
    }

    [Fact(DisplayName = "SL-005: Should_Redact_CaseInsensitive_Authorization_Mixed_Case")]
    public void Should_Redact_CaseInsensitive_Authorization_Mixed_Case()
    {
        var policy = HttpLoggingPolicy.Default;
        Assert.True(policy.IsSensitive("AUTHORIZATION"));
        Assert.True(policy.IsSensitive("authorization"));
        Assert.True(policy.IsSensitive("Authorization"));
    }

    // ── Non-sensitive headers are preserved ──────────────────────────────────────

    [Fact(DisplayName = "SL-006: Should_Preserve_Non_Sensitive_Headers")]
    public void Should_Preserve_Non_Sensitive_Headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "abc-123");

        var entry = HttpSafeLogger.CreateRequestEntry(request);

        var accept = entry.Headers.SingleOrDefault(h => h.Name.Equals("Accept", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(accept);
        Assert.Contains("application/json", accept.Value);

        var correlation = entry.Headers.SingleOrDefault(h => h.Name.Equals("X-Correlation-Id", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(correlation);
        Assert.Equal("abc-123", correlation.Value);
    }

    // ── Original message not mutated ─────────────────────────────────────────────

    [Fact(DisplayName = "SL-007: Should_Not_Mutate_Original_Request")]
    public void Should_Not_Mutate_Original_Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token123");
        request.Headers.TryAddWithoutValidation("X-Custom", "custom-value");

        _ = HttpSafeLogger.CreateRequestEntry(request);

        // Original request headers are unchanged
        Assert.True(request.Headers.TryGetValues("Authorization", out var authValues));
        Assert.Equal("Bearer token123", authValues!.Single());

        Assert.True(request.Headers.TryGetValues("X-Custom", out var customValues));
        Assert.Equal("custom-value", customValues!.Single());
    }

    [Fact(DisplayName = "SL-008: Should_Not_Mutate_Original_Response")]
    public void Should_Not_Mutate_Original_Response()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("Set-Cookie", "session=xyz");
        response.Headers.TryAddWithoutValidation("X-Request-Id", "req-456");

        _ = HttpSafeLogger.CreateResponseEntry(response);

        // Original response headers are unchanged
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieValues));
        Assert.Equal("session=xyz", cookieValues!.Single());

        Assert.True(response.Headers.TryGetValues("X-Request-Id", out var reqIdValues));
        Assert.Equal("req-456", reqIdValues!.Single());
    }

    // ── Body logging default behavior ────────────────────────────────────────────

    [Fact(DisplayName = "SL-009: Should_Not_Log_Response_Body_By_Default")]
    public void Should_Not_Log_Response_Body_By_Default()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var entry = HttpSafeLogger.CreateResponseEntry(response, bodyText: "sensitive body data");

        Assert.Null(entry.Body);
    }

    [Fact(DisplayName = "SL-010: Should_Not_Log_Request_Body_By_Default")]
    public void Should_Not_Log_Request_Body_By_Default()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/");
        request.Content = new StringContent("sensitive request body");

        var entry = HttpSafeLogger.CreateRequestEntry(request);

        Assert.Null(entry.Body);
    }

    [Fact(DisplayName = "SL-011: Should_Include_Response_Body_When_Policy_Enables_It")]
    public void Should_Include_Response_Body_When_Policy_Enables_It()
    {
        var policy = new HttpLoggingPolicy { LogResponseBody = true, MaxBodyLogLength = 0 };
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var entry = HttpSafeLogger.CreateResponseEntry(response, policy, bodyText: "hello world");

        Assert.Equal("hello world", entry.Body);
    }

    [Fact(DisplayName = "SL-012: Should_Truncate_Response_Body_When_Exceeds_MaxBodyLogLength")]
    public void Should_Truncate_Response_Body_When_Exceeds_MaxBodyLogLength()
    {
        var policy = new HttpLoggingPolicy { LogResponseBody = true, MaxBodyLogLength = 10 };
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var longBody = "abcdefghijklmnopqrstuvwxyz";

        var entry = HttpSafeLogger.CreateResponseEntry(response, policy, bodyText: longBody);

        Assert.NotNull(entry.Body);
        Assert.StartsWith("abcdefghij", entry.Body!);
        Assert.Contains("truncated", entry.Body!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "SL-013: Should_Not_Truncate_Body_When_Within_MaxBodyLogLength")]
    public void Should_Not_Truncate_Body_When_Within_MaxBodyLogLength()
    {
        var policy = new HttpLoggingPolicy { LogResponseBody = true, MaxBodyLogLength = 100 };
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var entry = HttpSafeLogger.CreateResponseEntry(response, policy, bodyText: "short body");

        Assert.Equal("short body", entry.Body);
    }

    // ── Request metadata captured correctly ──────────────────────────────────────

    [Fact(DisplayName = "SL-014: Should_Capture_Request_Method_And_Uri")]
    public void Should_Capture_Request_Method_And_Uri()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/v1/users");

        var entry = HttpSafeLogger.CreateRequestEntry(request);

        Assert.Equal("POST", entry.Method);
        Assert.Equal("https://api.example.com/v1/users", entry.RequestUri);
    }

    [Fact(DisplayName = "SL-015: Should_Capture_Response_StatusCode_And_ReasonPhrase")]
    public void Should_Capture_Response_StatusCode_And_ReasonPhrase()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            ReasonPhrase = "Not Found",
        };

        var entry = HttpSafeLogger.CreateResponseEntry(response);

        Assert.Equal(404, entry.StatusCode);
        Assert.Equal("Not Found", entry.ReasonPhrase);
    }

    // ── Additional sensitive headers via policy ───────────────────────────────────

    [Fact(DisplayName = "SL-016: Should_Redact_AdditionalSensitiveHeaders_In_Policy")]
    public void Should_Redact_AdditionalSensitiveHeaders_In_Policy()
    {
        var policy = new HttpLoggingPolicy
        {
            AdditionalSensitiveHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "X-Api-Key",
                "X-Secret-Token",
            },
        };

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "super-secret-key");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "trace-123");

        var entry = HttpSafeLogger.CreateRequestEntry(request, policy);

        var apiKey = entry.Headers.Single(h => h.Name.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpSafeLogger.RedactedValue, apiKey.Value);

        var correlationId = entry.Headers.Single(h => h.Name.Equals("X-Correlation-Id", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("trace-123", correlationId.Value);
    }

    [Fact(DisplayName = "SL-017: Should_Redact_AdditionalSensitiveHeader_CaseInsensitive")]
    public void Should_Redact_AdditionalSensitiveHeader_CaseInsensitive()
    {
        var policy = new HttpLoggingPolicy
        {
            AdditionalSensitiveHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "x-api-key",
            },
        };

        Assert.True(policy.IsSensitive("X-API-KEY"));
        Assert.True(policy.IsSensitive("x-api-key"));
        Assert.True(policy.IsSensitive("X-Api-Key"));
    }

    // ── Null / empty edge cases ───────────────────────────────────────────────────

    [Fact(DisplayName = "SL-018: Should_Throw_ArgumentNullException_For_Null_Request")]
    public void Should_Throw_ArgumentNullException_For_Null_Request()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HttpSafeLogger.CreateRequestEntry(null!));
    }

    [Fact(DisplayName = "SL-019: Should_Throw_ArgumentNullException_For_Null_Response")]
    public void Should_Throw_ArgumentNullException_For_Null_Response()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HttpSafeLogger.CreateResponseEntry(null!));
    }

    [Fact(DisplayName = "SL-020: Should_Handle_Request_With_No_Headers")]
    public void Should_Handle_Request_With_No_Headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        var entry = HttpSafeLogger.CreateRequestEntry(request);

        Assert.Empty(entry.Headers);
    }

    [Fact(DisplayName = "SL-021: Should_Handle_Response_With_No_Headers")]
    public void Should_Handle_Response_With_No_Headers()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NoContent);

        var entry = HttpSafeLogger.CreateResponseEntry(response);

        Assert.Empty(entry.Headers);
    }

    // ── IsSensitiveHeader static helper ──────────────────────────────────────────

    [Fact(DisplayName = "SL-022: Should_Return_True_For_All_AlwaysRedacted_Headers")]
    public void Should_Return_True_For_All_AlwaysRedacted_Headers()
    {
        foreach (var header in HttpLoggingPolicy.AlwaysRedacted)
        {
            Assert.True(
                HttpSafeLogger.IsSensitiveHeader(header),
                $"Expected '{header}' to be sensitive");
        }
    }

    [Fact(DisplayName = "SL-023: Should_Return_False_For_Non_Sensitive_Header")]
    public void Should_Return_False_For_Non_Sensitive_Header()
    {
        Assert.False(HttpSafeLogger.IsSensitiveHeader("Content-Type"));
        Assert.False(HttpSafeLogger.IsSensitiveHeader("Accept"));
        Assert.False(HttpSafeLogger.IsSensitiveHeader("X-Request-Id"));
    }

    // ── Default policy properties ─────────────────────────────────────────────────

    [Fact(DisplayName = "SL-024: Default_Policy_Should_Not_Log_Bodies")]
    public void Default_Policy_Should_Not_Log_Bodies()
    {
        var policy = HttpLoggingPolicy.Default;
        Assert.False(policy.LogRequestBody);
        Assert.False(policy.LogResponseBody);
    }

    [Fact(DisplayName = "SL-025: Default_Policy_MaxBodyLogLength_Should_Be_1024")]
    public void Default_Policy_MaxBodyLogLength_Should_Be_1024()
    {
        var policy = HttpLoggingPolicy.Default;
        Assert.Equal(1024, policy.MaxBodyLogLength);
    }

    // ── Mixed headers: sensitive + non-sensitive in same request ─────────────────

    [Fact(DisplayName = "SL-026: Should_Redact_Only_Sensitive_Headers_In_Mixed_Request")]
    public void Should_Redact_Only_Sensitive_Headers_In_Mixed_Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/data");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer abc");
        request.Headers.TryAddWithoutValidation("Cookie", "session=xyz");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "req-789");
        request.Headers.Accept.ParseAdd("application/json");

        var entry = HttpSafeLogger.CreateRequestEntry(request);

        var redacted = entry.Headers
            .Where(h => h.Value == HttpSafeLogger.RedactedValue)
            .Select(h => h.Name.ToLowerInvariant())
            .ToHashSet();

        Assert.Contains("authorization", redacted);
        Assert.Contains("cookie", redacted);

        var notRedacted = entry.Headers
            .Where(h => h.Value != HttpSafeLogger.RedactedValue)
            .Select(h => h.Name.ToLowerInvariant())
            .ToHashSet();

        Assert.Contains("x-request-id", notRedacted);
        Assert.Contains("accept", notRedacted);
    }

    [Fact(DisplayName = "SL-027: Should_Handle_Null_BodyText_When_Body_Logging_Enabled")]
    public void Should_Handle_Null_BodyText_When_Body_Logging_Enabled()
    {
        var policy = new HttpLoggingPolicy { LogResponseBody = true };
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        var entry = HttpSafeLogger.CreateResponseEntry(response, policy, bodyText: null);

        Assert.Null(entry.Body);
    }

    [Fact(DisplayName = "SL-028: Should_Capture_Response_Version")]
    public void Should_Capture_Response_Version()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = new Version(1, 1),
        };

        var entry = HttpSafeLogger.CreateResponseEntry(response);

        Assert.Equal(new Version(1, 1), entry.Version);
    }

    [Fact(DisplayName = "SL-029: Should_Not_Truncate_Body_When_MaxBodyLogLength_Is_Zero")]
    public void Should_Not_Truncate_Body_When_MaxBodyLogLength_Is_Zero()
    {
        // MaxBodyLogLength = 0 means no limit
        var policy = new HttpLoggingPolicy { LogResponseBody = true, MaxBodyLogLength = 0 };
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var longBody = new string('x', 10_000);

        var entry = HttpSafeLogger.CreateResponseEntry(response, policy, bodyText: longBody);

        Assert.Equal(longBody, entry.Body);
    }

    [Fact(DisplayName = "SL-030: AlwaysRedacted_Should_Include_All_Four_Sensitive_Headers")]
    public void AlwaysRedacted_Should_Include_All_Four_Sensitive_Headers()
    {
        var alwaysRedacted = HttpLoggingPolicy.AlwaysRedacted;
        Assert.True(alwaysRedacted.Contains("authorization"));
        Assert.True(alwaysRedacted.Contains("proxy-authorization"));
        Assert.True(alwaysRedacted.Contains("cookie"));
        Assert.True(alwaysRedacted.Contains("set-cookie"));
    }
}
