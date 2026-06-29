using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.CodedValues.Core.Data;

public sealed class DesignTimeCodedValuesDbContextFactory : IDesignTimeDbContextFactory<CodedValuesDbContext>
{
    public CodedValuesDbContext CreateDbContext(string[] args)
    {
        // The design-time factory is invoked outside of DI, so
        // AddOutbox<CodedValuesDbContext> is never called. We seed the
        // per-context outbox flag registry with the CodedValues-specific
        // flags here so the EF Core model that the migrations and
        // snapshot reflect matches the runtime shape produced by
        // AddOutbox<CodedValuesDbContext>(...) in Extensions.cs.
        OutboxMapping.SetFlagsFor<CodedValuesDbContext>(OutboxConfigurationFlags.FromConfiguration(b => b
            .SetTypeMaxLength(500)
            .UseJsonbPayload()
            .UseAttemptsDefaultZero()
            .UsePartialIndexOnOccurredAt()));

        var optionsBuilder = new DbContextOptionsBuilder<CodedValuesDbContext>();
        optionsBuilder
            .UseNpgsql("Host=localhost;Port=5432;Database=schoolcollab_coded_values;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        var tenantProvider = new DesignTimeTenantProvider();
        return new CodedValuesDbContext(optionsBuilder.Options, tenantProvider);
    }
}