using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Data.Configurations;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Data;

public sealed class AssignmentsDbContext(DbContextOptions<AssignmentsDbContext> options, ITenantProvider tenantProvider)
    : ModuleDbContext(options, tenantProvider)
{
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentRecipient> AssignmentRecipients => Set<AssignmentRecipient>();
    public DbSet<GuardianSubmissionGate> GuardianSubmissionGates => Set<GuardianSubmissionGate>();
    public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
    public DbSet<AssignmentSubmissionVersion> AssignmentSubmissionVersions => Set<AssignmentSubmissionVersion>();
    public DbSet<SubmissionReview> SubmissionReviews => Set<SubmissionReview>();
    public DbSet<AssignmentActivityGroup> AssignmentActivityGroups => Set<AssignmentActivityGroup>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Global (non-tenant-scoped) entities in this context: <see cref="OutboxMessage/>
    /// carries an optional tenant_id for dispatch routing (Step 5) but is not filtered —
    /// legacy events are global. See global-tenant-filter.md §3.2 / FR-14.
    /// </summary>
    protected override Type[] GlobalEntityAllowList => [typeof(OutboxMessage)];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations explicitly so constructor-injected dependencies are
        // available to tenant-aware configuration base classes. Do not use
        // ApplyConfigurationsFromAssembly here because it cannot inject arguments.
        modelBuilder.ApplyConfiguration(new AssignmentConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new AssignmentRecipientConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new GuardianSubmissionGateConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new AssignmentSubmissionConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new AssignmentSubmissionVersionConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new SubmissionReviewConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new AssignmentActivityGroupConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(OutboxMapping.FlagsFor<AssignmentsDbContext>()));

        // FR-14 / AC-17: fail fast at model build if any non-owned, non-allow-listed
        // entity lacks a "Tenant" query filter.
        ValidateTenantFilters(modelBuilder);
    }
}