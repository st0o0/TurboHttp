using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TurboHttp.Client;

public record TurboClientOptions
{
    public Uri?    BaseAddress           { get; init; }
    public Version DefaultRequestVersion { get; init; } = HttpVersion.Version11;

    public TimeSpan ConnectTimeout        { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReconnectInterval     { get; init; } = TimeSpan.FromSeconds(5);
    public int      MaxReconnectAttempts  { get; init; } = 10;
    public int      MaxFrameSize          { get; init; } = 128 * 1024;

    // TLS overrides — null means "decide from URI scheme"
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }
    public X509CertificateCollection?           ClientCertificates                  { get; init; }
    public SslProtocols                         EnabledSslProtocols                 { get; init; } = SslProtocols.None;
}

public interface ITurboHttpClient
{
    Uri? BaseAddress { get; set; }
    HttpRequestHeaders DefaultRequestHeaders { get; }
    Version DefaultRequestVersion { get; set; }
    HttpVersionPolicy DefaultVersionPolicy { get; set; }
    TimeSpan Timeout { get; set; }
    long MaxResponseContentBufferSize { get; set; }
    ChannelWriter<HttpRequestMessage> Requests { get; }
    ChannelReader<HttpResponseMessage> Responses { get; }
    void CancelPendingRequests();
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}


public class TurboHttpClient : ITurboHttpClient
{
    private readonly Channel<HttpRequestMessage> _requests = Channel.CreateUnbounded<HttpRequestMessage>();
    private readonly Channel<HttpResponseMessage> _responses = Channel.CreateUnbounded<HttpResponseMessage>();
    
    public Uri? BaseAddress { get; set; }
    public HttpRequestHeaders DefaultRequestHeaders { get; }
    public Version DefaultRequestVersion { get; set; }
    public HttpVersionPolicy DefaultVersionPolicy { get; set; }
    public TimeSpan Timeout { get; set; }
    public long MaxResponseContentBufferSize { get; set; }
    public ChannelWriter<HttpRequestMessage> Requests => _requests.Writer;
    public ChannelReader<HttpResponseMessage> Responses => _responses.Reader;
    public void CancelPendingRequests()
    {
        
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

public interface ITurboHttpClientFactory
{
    ITurboHttpClient CreateClient();
}