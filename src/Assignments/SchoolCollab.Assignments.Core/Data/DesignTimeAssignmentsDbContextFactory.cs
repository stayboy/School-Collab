using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Assignments.Core.Data;

public sealed class DesignTimeAssignmentsDbContextFactory : IDesignTimeDbContextFactory<AssignmentsDbContext>
{
    public AssignmentsDbContext CreateDbContext(string[] args)
    {
        // The design-time factory is invoked outside of DI, so
        // AddOutbox<AssignmentsDbContext> is never called. We seed
        // the per-context outbox flag registry with the same flags
        // that AddOutbox uses at runtime, so the EF Core model that
        // the migrations and snapshot reflect matches runtime
        // behaviour.
        //
        // Assignments keeps its existing partial index on
        // `dispatched_at WHERE dispatched_at IS NULL` (previously
        // on `processed_at`). The dispatcher reads with
        // `FOR UPDATE SKIP LOCKED` and the partial index keeps the
        // SELECT cheap as dispatched rows accumulate.
        OutboxMapping.SetFlagsFor<AssignmentsDbContext>(
            OutboxConfigurationFlags.FromConfiguration(b => b
                .UsePartialIndexOnOccurredAt()));

        var options = new DbContextOptionsBuilder<AssignmentsDbContext>()
            .UseNpgsql("Host=localhost;Database=schoolcollab_assignments_design")
            .UseSnakeCaseNamingConvention()
            .Options;

        var tenantProvider = new DesignTimeTenantProvider();
        return new AssignmentsDbContext(options, tenantProvider);
    }
}