using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Core.Tests.Unit.Auth;

[TestClass]
public class DevTenantSelectionTests
{
    private static IDevTenantSelection Create()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddLogging(); // Required for ILogger<DevTenantSelection>
        // DevTenantSelection is internal; resolve via the interface registered the
        // same way AddAuthAndTenancy registers it.
        services.AddAuthAndTenancy(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        return services.BuildServiceProvider().GetRequiredService<IDevTenantSelection>();
    }

    [TestMethod]
    public async Task Get_WhenNothingSet_ReturnsNull()
    {
        var selection = Create();
        var result = await selection.GetSelectedTenantIdAsync();
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task Set_ThenGet_ReturnsTheId()
    {
        var selection = Create();
        var id = Guid.NewGuid();

        await selection.SetSelectedTenantIdAsync(id);
        var result = await selection.GetSelectedTenantIdAsync();

        result.Should().Be(id);
    }

    [TestMethod]
    public async Task SetNull_ClearsTheSelection()
    {
        var selection = Create();
        var id = Guid.NewGuid();

        await selection.SetSelectedTenantIdAsync(id);
        (await selection.GetSelectedTenantIdAsync()).Should().Be(id);

        await selection.SetSelectedTenantIdAsync(null);
        (await selection.GetSelectedTenantIdAsync()).Should().BeNull();
    }

    [TestMethod]
    public async Task Get_WithCorruptedStoreValue_ReturnsNull()
    {
        // Write garbage directly into the cache key the implementation uses.
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddLogging(); // Required for ILogger<DevTenantSelection>
        services.AddAuthAndTenancy(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<IDistributedCache>();
        var selection = sp.GetRequiredService<IDevTenantSelection>();

        await cache.SetAsync("dev:tenant-selection", System.Text.Encoding.UTF8.GetBytes("not-a-guid"));
        (await selection.GetSelectedTenantIdAsync()).Should().BeNull();
    }

    /// <summary>
    /// Regression guard for the auth service's startup crash. That host calls
    /// <c>AddAuthAndTenancy</c> and registers NO <see cref="IDistributedCache"/> — it is not a
    /// tenant-scoped data host and never reads the dev tenant switcher — so a hard constructor
    /// dependency on this OPTIONAL service made <c>builder.Build()</c> throw
    /// <c>Unable to resolve service for type IDistributedCache while attempting to activate
    /// DevTenantSelection</c> under <c>ValidateOnBuild</c>, the Development default. The service
    /// could not start in Development at all, and the sibling data hosts hid it: they DO register
    /// a cache, as does <c>Create()</c> above — which is exactly why the previous tests were green.
    /// </summary>
    [TestMethod]
    public void AddAuthAndTenancy_WithoutADistributedCache_StillBuilds_UnderValidateOnBuild()
    {
        // A REAL host builder, not a bare ServiceCollection. AddAuthAndTenancy calls
        // AddAuthorization, whose AuthorizationPolicyCache singleton needs routing's
        // EndpointDataSource — supplied by the ASP.NET host, never by an empty collection.
        // Validating a harness the hosts do not build fails for an unrelated reason (it did),
        // which is exactly the kind of wrong-but-green guard to avoid.
        var builder = WebApplication.CreateBuilder();
        // The auth service's own appsettings.json value. The crash was flag-independent (the
        // registration sits outside the flag branch), so this pins the state that host runs in.
        builder.Configuration["FeatureFlags:FEATURE:DisableOIDCAuth"] = "true";
        builder.Logging.ClearProviders();
        builder.Services.AddAuthAndTenancy(builder.Configuration);

        var act = () => builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        act.Should().NotThrow(
            "a host that registers no IDistributedCache must still compose: the dev tenant switcher "
            + "is an optional dev convenience, never a startup requirement.");
    }

    /// <summary>
    /// The other half of the guard: with no cache the selection resolves to a NO-OP rather than a
    /// throwing proxy — reads report "nothing selected", writes are discarded, and the request
    /// outcome matches the service being absent.
    /// </summary>
    [TestMethod]
    public async Task WithoutADistributedCache_TheSelectionIsANoOp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthAndTenancy(new ConfigurationBuilder().Build());
        var sp = services.BuildServiceProvider();

        var selection = sp.GetRequiredService<IDevTenantSelection>();

        (await selection.GetSelectedTenantIdAsync()).Should().BeNull(
            "TestAuthHandler overrides its default only on HasValue, so null is the correct "
            + "'nothing selected' value — and never an exception.");

        await selection.SetSelectedTenantIdAsync(Guid.NewGuid()); // must not throw
        (await selection.GetSelectedTenantIdAsync()).Should().BeNull(
            "writes are discarded, not queued for a cache that does not exist.");
    }
}