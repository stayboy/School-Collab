using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Tenancy;

/// <summary>
/// Regression tests for the migration service scenario where AssignmentsDbContext is
/// registered directly (not via AddAssignmentsCore) and must still resolve
/// <see cref="ITenantProvider"/>.
/// </summary>
[TestClass]
public class MigrationServiceTenancyTests
{
    [TestMethod]
    public void AddDbContext_AssignmentsDbContext_WithAddTenancy_ResolvesDbContext()
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(opts =>
            opts.UseInMemoryDatabase("assignments-migration-tenancy-test"));

        using var provider = services.BuildServiceProvider();

        var context = provider.GetRequiredService<AssignmentsDbContext>();

        Assert.IsNotNull(context);
        Assert.AreEqual(Guid.Empty, context.CurrentTenantId);
    }
}
