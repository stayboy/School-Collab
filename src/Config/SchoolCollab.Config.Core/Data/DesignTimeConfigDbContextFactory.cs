using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Config.Core.Data;

public sealed class DesignTimeConfigDbContextFactory : IDesignTimeDbContextFactory<ConfigDbContext>
{
    public ConfigDbContext CreateDbContext(string[] args)
    {
        // The design-time factory is invoked outside of DI, so AddOutbox<ConfigDbContext>
        // is never called. Seed the per-context outbox flag registry here so the EF Core
        // model the migrations and snapshot reflect matches the runtime shape produced by
        // AddOutbox<ConfigDbContext>(...) in Extensions.AddConfigCore. Mirrors the
        // CodedValues design-time factory.
        OutboxMapping.SetFlagsFor<ConfigDbContext>(OutboxConfigurationFlags.FromConfiguration(b => b
            .SetTypeMaxLength(500)
            .UseJsonbPayload()
            .UseAttemptsDefaultZero()
            .UsePartialIndexOnOccurredAt()));

        var optionsBuilder = new DbContextOptionsBuilder<ConfigDbContext>();
        optionsBuilder
            .UseNpgsql("Host=localhost;Port=5432;Database=schoolcollab_config;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        var tenantProvider = new DesignTimeTenantProvider();
        return new ConfigDbContext(optionsBuilder.Options, tenantProvider);
    }
}