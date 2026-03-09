using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TurboHttp.Client;

public record TurboClientOptions();

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