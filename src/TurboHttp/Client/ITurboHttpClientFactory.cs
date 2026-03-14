using System;

namespace TurboHttp.Client;

public interface ITurboHttpClientFactory
{
    ITurboHttpClient CreateClient(Action<TurboClientOptions>? configure = null);
}