using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.CodedValues.Tests.Unit;

[TestClass]
public class MigrationGuardTests
{
    [TestMethod]
    public void NoUncommittedModelChanges()
    {
        // Must match DesignTimeCodedValuesDbContextFactory configuration exactly,
        // including UseSnakeCaseNamingConvention() and the CodedValues-specific
        // outbox flags (jsonb/500/0/partial index), otherwise HasPendingModelChanges()
        // will report false positives due to annotation differences.
        var tenantProvider = new DesignTimeTenantProvider();
        OutboxMapping.SetFlagsFor<CodedValuesDbContext>(OutboxConfigurationFlags.FromConfiguration(b => b
            .SetTypeMaxLength(500)
            .UseJsonbPayload()
            .UseAttemptsDefaultZero()
            .UsePartialIndexOnOccurredAt()));

        using var context = new CodedValuesDbContext(
            new DbContextOptionsBuilder<CodedValuesDbContext>()
                .UseNpgsql("Host=localhost;Database=guard")
                .UseSnakeCaseNamingConvention()
                .Options,
            tenantProvider);

        Assert.IsFalse(
            context.Database.HasPendingModelChanges(),
            "Model has changes not reflected in a migration. " +
            "Run 'dotnet ef migrations add <Name> --project src/CodedValues/SchoolCollab.CodedValues.Core'");
    }
}