using TurboHttp.Client;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.Integration;

/// <summary>
/// Tests for <see cref="TurboClientOptions"/> pipeline feature flags (TASK-009).
/// </summary>
public sealed class TurboClientOptionsTests
{
    [Fact(DisplayName = "OPT-001: All pipeline feature flags default to false")]
    public void All_Feature_Flags_Default_To_False()
    {
        var options = new TurboClientOptions();

        Assert.False(options.EnableRedirectHandling);
        Assert.False(options.EnableCookies);
        Assert.False(options.EnableRetry);
        Assert.False(options.EnableCaching);
        Assert.False(options.EnableDecompression);
    }

    [Fact(DisplayName = "OPT-002: All policy properties default to null")]
    public void All_Policy_Properties_Default_To_Null()
    {
        var options = new TurboClientOptions();

        Assert.Null(options.RedirectPolicy);
        Assert.Null(options.RetryPolicy);
        Assert.Null(options.CachePolicy);
        Assert.Null(options.ConnectionPolicy);
    }

    [Fact(DisplayName = "OPT-003: Feature flags can be enabled independently")]
    public void Feature_Flags_Can_Be_Enabled_Independently()
    {
        var options = new TurboClientOptions
        {
            EnableRedirectHandling = true,
            EnableCookies = true,
            EnableRetry = true,
            EnableCaching = true,
            EnableDecompression = true,
        };

        Assert.True(options.EnableRedirectHandling);
        Assert.True(options.EnableCookies);
        Assert.True(options.EnableRetry);
        Assert.True(options.EnableCaching);
        Assert.True(options.EnableDecompression);
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
            EnableRedirectHandling = true,
            RedirectPolicy = redirect,
            EnableRetry = true,
            RetryPolicy = retry,
            EnableCaching = true,
            CachePolicy = cache,
            ConnectionPolicy = connection,
        };

        Assert.Same(redirect, options.RedirectPolicy);
        Assert.Same(retry, options.RetryPolicy);
        Assert.Same(cache, options.CachePolicy);
        Assert.Same(connection, options.ConnectionPolicy);
    }

    [Fact(DisplayName = "OPT-005: Enabling one flag does not affect others")]
    public void Enabling_One_Flag_Does_Not_Affect_Others()
    {
        var options = new TurboClientOptions { EnableRedirectHandling = true };

        Assert.True(options.EnableRedirectHandling);
        Assert.False(options.EnableCookies);
        Assert.False(options.EnableRetry);
        Assert.False(options.EnableCaching);
        Assert.False(options.EnableDecompression);
    }

    [Fact(DisplayName = "OPT-006: Record with-expression preserves unchanged flags")]
    public void With_Expression_Preserves_Unchanged_Flags()
    {
        var original = new TurboClientOptions();
        var modified = original with { EnableRetry = true };

        Assert.False(original.EnableRetry);
        Assert.True(modified.EnableRetry);
        Assert.False(modified.EnableRedirectHandling);
        Assert.False(modified.EnableCookies);
        Assert.False(modified.EnableCaching);
        Assert.False(modified.EnableDecompression);
    }
}
