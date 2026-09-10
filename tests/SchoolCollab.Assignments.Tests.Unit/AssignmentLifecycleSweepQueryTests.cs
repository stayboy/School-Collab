using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A2 / spec §3.5 step 8 + §7 Q6 — the sanctioned cross-tenant
/// candidate queries on <see cref="AssignmentRepository"/>:
/// <list type="bullet">
///   <item><see cref="AssignmentRepository.ListScheduledForAutoPublishAsync"/> — Scheduled + <c>AvailableFromUtc &lt;= now</c>.</item>
///   <item><see cref="AssignmentRepository.ListDueForArchiveAsync"/> — Published/Closed + <c>DueDate + ArchiveGraceDays &lt;= now</c>.</item>
/// </list>
/// Covers the rule matrix: not-due, no <c>AvailableFromUtc</c>, wrong
/// status, multi-tenant read correctness, and the per-row
/// <c>ArchiveGraceDays</c> override. Uses
/// <c>IgnoreQueryFilters(["Tenant"])</c> so a query that the sweeps
/// require to cross tenants verifies correctly here in-memory.
/// </summary>
[TestClass]
public class AssignmentLifecycleSweepQueryTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static (AssignmentsDbContext db, IAssignmentRepository repo, IServiceProvider sp) BuildScope(string name)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(name));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AssignmentsDbContext>();
        var tenants = sp.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(TenantA, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();
        return (db, new AssignmentRepository(db), sp);
    }

    private static Assignment NewAssignment(Guid tenantId,
        DateTimeOffset? dueDate = null, int archiveGraceDays = 30) =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, dueDate, null, TeacherId,
            archiveGraceDays: archiveGraceDays)
            .WithTenant(tenantId);

    /// <summary>Test-only reach: bypass the Scheduled-state future-date guard
    /// so the sweep query tests can place rows in any past <c>AvailableFromUtc</c>
    /// without depending on the wall clock to advance. Mirrors the
    /// "sweep tests set up rows in non-Draft states directly" pattern.</summary>
    private static Assignment ForceStatus(Assignment assignment, AssignmentStatus status, DateTimeOffset? availableFromUtc = null, DateTimeOffset? dueDate = null)
    {
        var statusProp = typeof(Assignment).GetProperty("Status", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        statusProp!.SetValue(assignment, status);
        if (availableFromUtc.HasValue)
        {
            var availableFromProp = typeof(Assignment).GetProperty("AvailableFromUtc", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            availableFromProp!.SetValue(assignment, availableFromUtc);
        }
        if (dueDate.HasValue)
        {
            var dueDateProp = typeof(Assignment).GetProperty("DueDate", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            dueDateProp!.SetValue(assignment, dueDate);
        }
        return assignment;
    }

    // ── ListScheduledForAutoPublishAsync ────────────────────────────────

    [TestMethod]
    public async Task ListScheduledForAutoPublishAsync_Due_ReturnsCandidate()
    {
        var (db, repo, _) = BuildScope("sweep-due");
        var now = DateTimeOffset.UtcNow;
        var asg = ForceStatus(NewAssignment(TenantA), AssignmentStatus.Scheduled, availableFromUtc: now.AddMinutes(-5));
        db.Assignments.Add(asg);
        await db.SaveChangesAsync();

        var candidates = await repo.ListScheduledForAutoPublishAsync(now);

        candidates.Should().ContainSingle();
        candidates[0].Id.Should().Be(asg.Id);
        candidates[0].TenantId.Should().Be(TenantA);
    }

    [TestMethod]
    public async Task ListScheduledForAutoPublishAsync_NotYetDue_Excluded()
    {
        var (db, repo, _) = BuildScope("sweep-not-due");
        var now = DateTimeOffset.UtcNow;
        var asg = ForceStatus(NewAssignment(TenantA), AssignmentStatus.Scheduled, availableFromUtc: now.AddDays(1));
        db.Assignments.Add(asg);
        await db.SaveChangesAsync();

        var candidates = await repo.ListScheduledForAutoPublishAsync(now);

        candidates.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ListScheduledForAutoPublishAsync_NonScheduled_Excluded()
    {
        var (db, repo, _) = BuildScope("sweep-non-scheduled");
        var now = DateTimeOffset.UtcNow;
        var draft = NewAssignment(TenantA);
        var published = NewAssignment(TenantA); published.Publish();
        var closed = NewAssignment(TenantA); closed.Publish(); closed.Close();
        var archived = NewAssignment(TenantA); archived.Publish(); archived.Archive();
        // Set the published+archived+closed ForArchive has the side effect of stamping PublishedAt.
        // Filter only checks status — these will all be filtered as non-Scheduled regardless.
        db.Assignments.AddRange(draft, published, closed, archived);
        await db.SaveChangesAsync();

        var candidates = await repo.ListScheduledForAutoPublishAsync(now);

        candidates.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ListScheduledForAutoPublishAsync_MultipleTenants_BothReturnedWithOwnTenantId()
    {
        var (db, repo, sp) = BuildScope("sweep-multi-tenant");
        var now = DateTimeOffset.UtcNow;
        var a = ForceStatus(NewAssignment(TenantA), AssignmentStatus.Scheduled, availableFromUtc: now.AddMinutes(-5));
        var b = ForceStatus(NewAssignment(TenantB), AssignmentStatus.Scheduled, availableFromUtc: now.AddMinutes(-1));
        var tenantAccessor = sp.GetRequiredService<ITenantContextAccessor>();
        using (tenantAccessor.SuppressTenantGuard())
        {
            db.Assignments.AddRange(a, b);
            await db.SaveChangesAsync();
        }

        var candidates = await repo.ListScheduledForAutoPublishAsync(now);

        candidates.Should().HaveCount(2);
        candidates.Should().ContainSingle(c => c.Id == a.Id && c.TenantId == TenantA);
        candidates.Should().ContainSingle(c => c.Id == b.Id && c.TenantId == TenantB);
    }

    // ── ListDueForArchiveAsync ──────────────────────────────────────────

    [TestMethod]
    public async Task ListDueForArchiveAsync_PublishedPastGrace_ReturnsCandidate()
    {
        var (db, repo, _) = BuildScope("archive-pub-past");
        var now = DateTimeOffset.UtcNow;
        var dueDate = now.AddDays(-40); // > 30 day grace
        var asg = NewAssignment(TenantA, dueDate: dueDate);
        asg.Publish();
        db.Assignments.Add(asg);
        await db.SaveChangesAsync();

        var candidates = await repo.ListDueForArchiveAsync(now);

        candidates.Should().ContainSingle();
        candidates[0].Id.Should().Be(asg.Id);
        candidates[0].TenantId.Should().Be(TenantA);
    }

    [TestMethod]
    public async Task ListDueForArchiveAsync_ClosedPastGrace_ReturnsCandidate()
    {
        var (db, repo, _) = BuildScope("archive-closed-past");
        var now = DateTimeOffset.UtcNow;
        var dueDate = now.AddDays(-31); // just over 30 day grace
        var asg = NewAssignment(TenantA, dueDate: dueDate);
        asg.Publish();
        asg.Close();
        db.Assignments.Add(asg);
        await db.SaveChangesAsync();

        var candidates = await repo.ListDueForArchiveAsync(now);

        candidates.Should().ContainSingle();
        candidates[0].Id.Should().Be(asg.Id);
    }

    [TestMethod]
    public async Task ListDueForArchiveAsync_InsideGrace_Excluded()
    {
        var (db, repo, _) = BuildScope("archive-inside");
        var now = DateTimeOffset.UtcNow;
        var dueDate = now.AddDays(-5); // well inside 30-day grace
        var asg = NewAssignment(TenantA, dueDate: dueDate);
        asg.Publish();
        db.Assignments.Add(asg);
        await db.SaveChangesAsync();

        var candidates = await repo.ListDueForArchiveAsync(now);

        candidates.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ListDueForArchiveAsync_NullDueDate_Excluded()
    {
        var (db, repo, _) = BuildScope("archive-null-duedate");
        var now = DateTimeOffset.UtcNow;
        var asg = NewAssignment(TenantA, dueDate: null);
        asg.Publish();
        db.Assignments.Add(asg);
        await db.SaveChangesAsync();

        var candidates = await repo.ListDueForArchiveAsync(now);

        candidates.Should().BeEmpty("a null DueDate means the assignment was never time-bounded → never archives");
    }

    [TestMethod]
    public async Task ListDueForArchiveAsync_GraceDaysHonoredPerRow()
    {
        var (db, repo, _) = BuildScope("archive-grace-honored");
        var now = DateTimeOffset.UtcNow;
        // Due date 25 days ago — only the 0-grace row has elapsed its grace window.
        // 25d + 30d grace ends in the future, so the 30-grace row is NOT due.
        var dueDate = now.AddDays(-25);
        var zeroGrace = NewAssignment(TenantA, dueDate: dueDate, archiveGraceDays: 0);
        zeroGrace.Publish();
        var thirtyGrace = NewAssignment(TenantA, dueDate: dueDate, archiveGraceDays: 30);
        thirtyGrace.Publish();
        db.Assignments.AddRange(zeroGrace, thirtyGrace);
        await db.SaveChangesAsync();

        var candidates = await repo.ListDueForArchiveAsync(now);

        candidates.Should().ContainSingle();
        candidates[0].Id.Should().Be(zeroGrace.Id, "only the 0-grace row has elapsed DueDate + 0 ≤ now");
    }

    [TestMethod]
    public async Task ListDueForArchiveAsync_NonArchivableStates_Excluded()
    {
        var (db, repo, _) = BuildScope("archive-non-archivable");
        var now = DateTimeOffset.UtcNow;
        var dueDate = now.AddDays(-40);
        var draft = NewAssignment(TenantA, dueDate: dueDate);
        var scheduled = NewAssignment(TenantA, dueDate: dueDate);
        scheduled.Schedule(DateTimeOffset.UtcNow.AddDays(1)); // future schedule still keeps status Scheduled for the test setup
        var archived = NewAssignment(TenantA, dueDate: dueDate);
        archived.Publish();
        archived.Archive();
        db.Assignments.AddRange(draft, scheduled, archived);
        await db.SaveChangesAsync();

        var candidates = await repo.ListDueForArchiveAsync(now);

        candidates.Should().BeEmpty();
    }
}
