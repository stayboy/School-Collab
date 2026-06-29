using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class MigrationGuardTests
{
    [TestMethod]
    public void NoUncommittedModelChanges()
    {
        // Must match DesignTimeAssignmentsDbContextFactory configuration exactly,
        // including UseSnakeCaseNamingConvention() and the Assignments-specific
        // outbox flags (partial index on OccurredAt), otherwise
        // HasPendingModelChanges() will report false positives due to annotation
        // differences.
        var tenantProvider = new DesignTimeTenantProvider();
        OutboxMapping.SetFlagsFor<AssignmentsDbContext>(
            OutboxConfigurationFlags.FromConfiguration(b => b
                .UsePartialIndexOnOccurredAt()));

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