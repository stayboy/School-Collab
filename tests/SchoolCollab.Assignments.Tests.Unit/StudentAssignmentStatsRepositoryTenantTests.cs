using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Issue #444 (layer L2a) — <see cref="StudentAssignmentStatsRepository"/> at the EF layer, where the
/// global <c>"Tenant"</c> query filter lives (C7), and where the read's <b>status-independent</b> WHERE
/// clause is observable (decision B: the stats read must see a <c>Draft</c> and a not-yet-active
/// <c>Scheduled</c> row, or the handler's status rules pass vacuously).
/// <para>
/// The filter is EF-global and model-build validated (<c>AssignmentsDbContext.ValidateTenantFilters</c>),
/// so the isolation assertion here pins that the read does not escape it — not that the filter exists.
/// </para>
/// </summary>
[TestClass]
public class StudentAssignmentStatsRepositoryTenantTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid ContactId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    /// <summary>The SAME student id in both tenants: the worst case for a leaking read.</summary>
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-0000000000c2");

    private static async Task<ServiceProvider> BuildProviderAsync(string dbName)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();

        using (var bootScope = provider.CreateScope())
        {
            var bootCtx = bootScope.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            await bootCtx.Database.EnsureCreatedAsync();
        }

        return provider;
    }

    private static void AsTenant(ServiceProvider provider, Guid tenantId)
    {
        var tenants = provider.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));
    }

    /// <summary>Adds one assignment of the caller's tenant plus the recipient link that makes it
    /// recipient-linked to <see cref="StudentId"/>.</summary>
    private static async Task AddLinkedAssignmentAsync(ServiceProvider provider, Guid tenantId, Assignment assignment, bool link)
    {
        AsTenant(provider, tenantId);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssignmentsDbContext>();

        db.Assignments.Add(assignment);
        if (link)
        {
            db.AssignmentRecipients.Add(AssignmentRecipient.Create(
                tenantId, assignment.Id, ContactOwnerType.Guardian, StudentId, StudentId,
                ContactId, ContactChannel.Email, role: null,
                notifyOnBroadcast: true, subscriptionActive: true));
        }

        await db.SaveChangesAsync();
    }

    private static Assignment NewAssignment(Guid tenantId) =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId)
            .WithTenant(tenantId);

    private static async Task<List<StudentAssignmentStatRow>> ReadAsAsync(
        ServiceProvider provider, Guid tenantId, Guid studentId)
    {
        AsTenant(provider, tenantId);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
        return await new StudentAssignmentStatsRepository(db).ListStudentAssignmentStatRowsAsync(studentId);
    }

    [TestMethod]
    public async Task ListStudentAssignmentStatRowsAsync_ExcludesAnotherTenantsRows()
    {
        using var provider = await BuildProviderAsync(nameof(ListStudentAssignmentStatRowsAsync_ExcludesAnotherTenantsRows));

        await AddLinkedAssignmentAsync(provider, TenantA, NewAssignment(TenantA), link: true);
        await AddLinkedAssignmentAsync(provider, TenantB, NewAssignment(TenantB), link: true);
        await AddLinkedAssignmentAsync(provider, TenantB, NewAssignment(TenantB), link: true);

        var rowsA = await ReadAsAsync(provider, TenantA, StudentId);
        var rowsB = await ReadAsAsync(provider, TenantB, StudentId);

        rowsA.Should().HaveCount(1, "tenant A is linked to exactly one assignment — tenant B's two never appear");
        rowsB.Should().HaveCount(2, "tenant B sees its own two, never tenant A's");
    }

    [TestMethod]
    public async Task ListStudentAssignmentStatRowsAsync_AppliesTheRecipientLinkAndNoStatusPredicate()
    {
        using var provider = await BuildProviderAsync(
            nameof(ListStudentAssignmentStatRowsAsync_AppliesTheRecipientLinkAndNoStatusPredicate));

        var published = NewAssignment(TenantA);
        published.Publish(approvalRequired: false);
        var draft = NewAssignment(TenantA);
        var scheduledAhead = NewAssignment(TenantA);
        scheduledAhead.Schedule(DateTimeOffset.UtcNow.AddDays(7), approvalRequired: false);

        await AddLinkedAssignmentAsync(provider, TenantA, published, link: true);
        await AddLinkedAssignmentAsync(provider, TenantA, draft, link: true);
        await AddLinkedAssignmentAsync(provider, TenantA, scheduledAhead, link: true);
        // Published but NOT recipient-linked: the one predicate the WHERE clause does carry.
        await AddLinkedAssignmentAsync(provider, TenantA, NewAssignment(TenantA), link: false);

        var rows = await ReadAsAsync(provider, TenantA, StudentId);

        rows.Should().HaveCount(3,
            "a Draft and a not-yet-active Scheduled row MUST reach the fold — filtering statuses in SQL would "
            + "collapse Assigned into SentOut and make the handler's status rules untestable");
        rows.Select(r => r.Status).Should().BeEquivalentTo(
            [AssignmentStatus.Published, AssignmentStatus.Draft, AssignmentStatus.Scheduled],
            "all three linked statuses arrive, unsent ones included");
        rows.Should().OnlyContain(r => !r.HasFinalizedSubmission && r.ReviewState == null && !r.HasReviewedGate,
            "no submission or gate was created, so every flag reads as absent rather than defaulting to a count");
    }
}
