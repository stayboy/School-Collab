using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.Data;

namespace SchoolCollab.CodedValues.Tests.Unit;

[TestClass]
public class MigrationGuardTests
{
    [TestMethod]
    public void NoUncommittedModelChanges()
    {
        using var context = new CodedValuesDbContext(
            new DbContextOptionsBuilder<CodedValuesDbContext>()
                .UseNpgsql("Host=localhost;Database=guard") // DSN irrelevant — snapshot-only check
                .Options);

        Assert.IsFalse(
            context.Database.HasPendingModelChanges(),
            "Model has changes not reflected in a migration. " +
            "Run 'dotnet ef migrations add <Name> --project src/CodedValues/SchoolCollab.CodedValues.Core'");
    }
}