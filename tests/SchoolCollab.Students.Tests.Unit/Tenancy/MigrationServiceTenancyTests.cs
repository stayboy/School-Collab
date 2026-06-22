using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Tests.Unit.Tenancy;

/// <summary>
/// Regression tests for the migration service scenario where StudentsDbContext is
/// registered directly (not via AddStudentsCore) and must still resolve
/// <see cref="ITenantProvider"/>.
/// </summary>
[TestClass]
public class MigrationServiceTenancyTests
{
    [TestMethod]
    public void AddDbContext_StudentsDbContext_WithAddTenancy_ResolvesDbContext()
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<StudentsDbContext>(opts =>
            opts.UseInMemoryDatabase("students-migration-tenancy-test"));

        using var provider = services.BuildServiceProvider();

        var context = provider.GetRequiredService<StudentsDbContext>();

        Assert.IsNotNull(context);
        Assert.AreEqual(Guid.Empty, context.CurrentTenantId);
    }
}
