using System;
using Microsoft.Extensions.DependencyInjection;

namespace TurboHttp.Client;

public interface ITurboHttpClientFactory
{
    ITurboHttpClient CreateClient();
}

public static class TurboClientServiceCollectionExtensions
{
    public static IServiceCollection AddTurboClient(
        this IServiceCollection services,
        Action<TurboClientOptions> configure)
    {
        services.Configure(configure);

        services.AddSingleton<ITurboHttpClientFactory, TurboHttpClientFactory>();
        return services;
    }
}