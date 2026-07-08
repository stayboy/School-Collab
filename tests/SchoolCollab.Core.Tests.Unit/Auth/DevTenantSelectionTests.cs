using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
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
}