using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.Tenants.Queries.ListTenants;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit.Tenants;

[TestClass]
public class ListTenantsHandlerTests
{
    private static SettingsDbContext CreateDb(string name)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<SettingsDbContext>(o => o.UseInMemoryDatabase(name));
        return services.BuildServiceProvider().GetRequiredService<SettingsDbContext>();
    }

    [TestMethod]
    public async Task HandleAsync_ReturnsTenantsOrderedByName()
    {
        using var db = CreateDb("tenants-ordered");
        db.Tenants.Add(Tenant.Create("Zeta School", TenantType.School));
        db.Tenants.Add(Tenant.Create("Alpha School", TenantType.School));
        await db.SaveChangesAsync();

        var handler = new ListTenantsHandler(db);
        var result = await handler.HandleAsync(new ListTenants());

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Alpha School");
        result[1].Name.Should().Be("Zeta School");
        result[0].Type.Should().Be("School");
        result[0].Id.Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task HandleAsync_WhenNoTenants_ReturnsEmpty()
    {
        using var db = CreateDb("tenants-empty");
        var handler = new ListTenantsHandler(db);
        var result = await handler.HandleAsync(new ListTenants());
        result.Should().BeEmpty();
    }
}