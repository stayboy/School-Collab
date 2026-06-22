using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class MigrationGuardTests
{
    [TestMethod]
    public void NoUncommittedModelChanges()
    {
        // Must match DesignTimeStudentsDbContextFactory configuration exactly,
        // including UseSnakeCaseNamingConvention(), otherwise HasPendingModelChanges()
        // will report false positives due to annotation differences.
        var tenantProvider = new DesignTimeTenantProvider();
        using var context = new StudentsDbContext(
            new DbContextOptionsBuilder<StudentsDbContext>()
                .UseNpgsql("Host=localhost;Database=guard")
                .UseSnakeCaseNamingConvention()
                .Options,
            tenantProvider);

        Assert.IsFalse(
            context.Database.HasPendingModelChanges(),
            "Model has changes not reflected in a migration. " +
            "Run 'dotnet ef migrations add <Name> --project src/Students/SchoolCollab.Students.Core'");
    }
}
