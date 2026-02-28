using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.Stress;

/// <summary>
/// xUnit collection that shares a single <see cref="KestrelFixture"/> across all
/// Phase 17 HTTP/1.1 stress test classes.
/// </summary>
[CollectionDefinition("StressHttp11")]
public sealed class StressHttp11Collection : ICollectionFixture<KestrelFixture>;

/// <summary>
/// xUnit collection that shares a single <see cref="KestrelH2Fixture"/> across all
/// Phase 17 HTTP/2 stress test classes.
/// </summary>
[CollectionDefinition("StressHttp2")]
public sealed class StressHttp2Collection : ICollectionFixture<KestrelH2Fixture>;
