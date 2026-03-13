using TurboHttp.Client;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Tests.Integration;

/// <summary>
/// Tests for <see cref="TurboClientOptions"/> pipeline feature flags (TASK-009).
/// </summary>
public sealed class TurboClientOptionsTests
{
    [Fact(DisplayName = "OPT-002: All policy properties default to null")]
    public void All_Policy_Properties_Default_To_Null()
    {
        var options = new TurboClientOptions();

        Assert.Null(options.RedirectPolicy);
        Assert.Null(options.RetryPolicy);
        Assert.Null(options.CachePolicy);
        Assert.Null(options.ConnectionPolicy);
    }

    [Fact(DisplayName = "OPT-004: Policy objects can be set alongside feature flags")]
    public void Policy_Objects_Can_Be_Set()
    {
        var redirect = new RedirectPolicy();
        var retry = new RetryPolicy();
        var cache = new CachePolicy();
        var connection = new ConnectionPolicy();

        var options = new TurboClientOptions
        {
            RedirectPolicy = redirect,
            RetryPolicy = retry,
            CachePolicy = cache,
            ConnectionPolicy = connection,
        };

        Assert.Same(redirect, options.RedirectPolicy);
        Assert.Same(retry, options.RetryPolicy);
        Assert.Same(cache, options.CachePolicy);
        Assert.Same(connection, options.ConnectionPolicy);
    }
}