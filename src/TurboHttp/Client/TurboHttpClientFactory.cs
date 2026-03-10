using Akka.Actor;
using Microsoft.Extensions.Options;

namespace TurboHttp.Client;

public sealed class TurboHttpClientFactory : ITurboHttpClientFactory
{
    private readonly IOptionsMonitor<TurboClientOptions> _options;
    private readonly ActorSystem _actorSystem;

    public TurboHttpClientFactory(IOptionsMonitor<TurboClientOptions> options, ActorSystem system)
    {
        _options = options;
        _actorSystem = system;
    }

    public ITurboHttpClient CreateClient()
    {
        var options = _options.CurrentValue;

        return new TurboHttpClient(options, _actorSystem);
    }
}