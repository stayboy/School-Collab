using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Data.Configurations;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Messaging;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Data;

public sealed class AssignmentsDbContext(DbContextOptions<AssignmentsDbContext> options, ITenantProvider tenantProvider)
    : ModuleDbContext(options, tenantProvider)
{
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations explicitly so constructor-injected dependencies are
        // available to tenant-aware configuration base classes. Do not use
        // ApplyConfigurationsFromAssembly here because it cannot inject arguments.
        modelBuilder.ApplyConfiguration(new AssignmentConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
