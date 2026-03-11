using System;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TurboHttp.Protocol;

namespace TurboHttp.Client;

public record TurboClientOptions
{
    public Uri? BaseAddress { get; init; }
    public Version DefaultRequestVersion { get; init; } = HttpVersion.Version11;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxReconnectAttempts { get; init; } = 10;
    public int MaxFrameSize { get; init; } = 128 * 1024;

    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; } =
        static (_, _, _, sslPolicyErrors) => sslPolicyErrors is SslPolicyErrors.None;

    public X509CertificateCollection? ClientCertificates { get; init; }
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;

    // Pipeline feature flags — all default to false for backward compatibility
    public bool EnableRedirectHandling { get; init; } = false;
    public RedirectPolicy? RedirectPolicy { get; init; }

    public bool EnableCookies { get; init; } = false;

    public bool EnableRetry { get; init; } = false;
    public RetryPolicy? RetryPolicy { get; init; }

    public bool EnableCaching { get; init; } = false;
    public CachePolicy? CachePolicy { get; init; }

    public bool EnableDecompression { get; init; } = false;

    public ConnectionPolicy? ConnectionPolicy { get; init; }
}