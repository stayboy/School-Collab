using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Settings.Core.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations add</c>. Targets
/// <c>settings-db</c> and seeds the outbox flag registry with the same flags
/// the runtime <c>AddSettingsCore</c> applies so the migration snapshot matches
/// the runtime model. The migration folder is at the project root to match the
/// existing convention used by Assignments/Students/Code-Collab (see spec §7).
/// </summary>
public sealed class DesignTimeSettingsDbContextFactory : IDesignTimeDbContextFactory<SettingsDbContext>
{
    public SettingsDbContext CreateDbContext(string[] args)
    {
        // The design-time factory is invoked outside of DI, so AddOutbox<SettingsDbContext>
        // is never called. Seed the per-context outbox flag registry here so the EF Core
        // model the migrations and snapshot reflect matches the runtime shape produced by
        // AddOutbox<SettingsDbContext>(...) in AddSettingsCore.
        OutboxMapping.SetFlagsFor<SettingsDbContext>(OutboxConfigurationFlags.FromConfiguration(b => b
            .SetTypeMaxLength(500)
            .UseJsonbPayload()
            .UseAttemptsDefaultZero()
            .UsePartialIndexOnOccurredAt()));

        var optionsBuilder = new DbContextOptionsBuilder<SettingsDbContext>();
        optionsBuilder
            .UseNpgsql("Host=localhost;Port=5432;Database=schoolcollab_settings;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        var tenantProvider = new DesignTimeTenantProvider();
        return new SettingsDbContext(optionsBuilder.Options, tenantProvider);
    }
}
