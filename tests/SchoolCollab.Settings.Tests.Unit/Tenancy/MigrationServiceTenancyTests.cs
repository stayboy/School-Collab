using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Tests.Unit.Tenancy;

/// <summary>
/// Regression tests for the unified migration service scenario where DbContexts are
/// registered directly (not via AddCodedValuesCore) and must still resolve
/// <see cref="ITenantProvider"/>.
/// </summary>
[TestClass]
public class MigrationServiceTenancyTests
{
    [TestMethod]
    public void AddDbContext_CodedValuesDbContext_WithAddTenancy_ResolvesDbContext()
    {
        // Arrange: simulate how MigrationService registers the context
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<SettingsDbContext>(opts =>
            opts.UseInMemoryDatabase("migration-tenancy-test"));

        using var provider = services.BuildServiceProvider();

        // Act
        var context = provider.GetRequiredService<SettingsDbContext>();

        // Assert
        Assert.IsNotNull(context);
        Assert.AreEqual(Guid.Empty, context.CurrentTenantId);
    }

    [TestMethod]
    public void AddTenancy_Registers_ITenantProvider()
    {
        var services = new ServiceCollection();
        services.AddTenancy();

        using var provider = services.BuildServiceProvider();

        var tenantProvider = provider.GetRequiredService<ITenantProvider>();

        Assert.IsNotNull(tenantProvider);
        Assert.IsInstanceOfType<TenantProvider>(tenantProvider);
    }
}
