using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class MigrationGuardTests
{
    [TestMethod]
    public void NoUncommittedModelChanges()
    {
        var tenantProvider = new DesignTimeTenantProvider();
        using var context = new AssignmentsDbContext(
            new DbContextOptionsBuilder<AssignmentsDbContext>()
                .UseNpgsql("Host=localhost;Database=guard")
                .UseSnakeCaseNamingConvention()
                .Options,
            tenantProvider);

        Assert.IsFalse(
            context.Database.HasPendingModelChanges(),
            "Model has changes not reflected in a migration. " +
            "Run 'dotnet ef migrations add <Name> --project src/Assignments/SchoolCollab.Assignments.Core'");
    }
}
