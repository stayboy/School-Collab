using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.Data;

namespace SchoolCollab.CodedValues.Tests.Unit;

[TestClass]
public class MigrationGuardTests
{
    [TestMethod]
    public void NoUncommittedModelChanges()
    {
        // Must match DesignTimeCodedValuesDbContextFactory configuration exactly,
        // including UseSnakeCaseNamingConvention(), otherwise HasPendingModelChanges()
        // will report false positives due to annotation differences.
        using var context = new CodedValuesDbContext(
            new DbContextOptionsBuilder<CodedValuesDbContext>()
                .UseNpgsql("Host=localhost;Database=guard")
                .UseSnakeCaseNamingConvention()
                .Options);

        Assert.IsFalse(
            context.Database.HasPendingModelChanges(),
            "Model has changes not reflected in a migration. " +
            "Run 'dotnet ef migrations add <Name> --project src/CodedValues/SchoolCollab.CodedValues.Core'");
    }
}