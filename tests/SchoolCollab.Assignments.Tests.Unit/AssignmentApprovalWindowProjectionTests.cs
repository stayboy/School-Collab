using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>assignment-approval-workflow</c> (spec §3.1/§3.3, WS5) — the three new read fields on
/// <see cref="AssignmentSummaryDto"/>: <c>ApprovalReason</c>, <c>SubmittedAt</c> and the DERIVED
/// <c>WindowExpiresAt</c>. Both read handlers are covered because a field mapped in only one of them
/// silently stays null in the other, and the derivation must go through the existing
/// per-(tenant, grade) effective-policy cache.
///
/// <para>The discriminating cases: mapped at all; a per-grade window applies per row; no window ⇒
/// null; the resolver's fail-open result ⇒ <b>null, never an invented deadline</b>; a returned row
/// (SubmittedAt cleared) ⇒ null even with a window configured.</para>
/// </summary>
[TestClass]
public class AssignmentApprovalWindowProjectionTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid GradeA = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid GradeB = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    private static readonly Guid ApproverId = Guid.Parse("00000000-0000-0000-0000-000000000099");

    private sealed class Scope : IDisposable
    {
        public required AssignmentsDbContext Db { get; init; }
        public required AssignmentRepository Repository { get; init; }
        public required ITenantProvider Tenants { get; init; }

        public ListAssignmentsQueryHandler List(IAssignmentPolicyResolver resolver, HybridCache cache) =>
            new(Repository, cache, Tenants, resolver, new FakeFeatureFlagService(),
                NullLogger<ListAssignmentsQueryHandler>.Instance);

        public GetAssignmentByIdQueryHandler Detail(IAssignmentPolicyResolver resolver, HybridCache cache) =>
            new(Db, cache, resolver, new FakeFeatureFlagService(),
                NullLogger<GetAssignmentByIdQueryHandler>.Instance);

        public void Dispose() => Db.Dispose();
    }

    private static Scope Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(TenantA, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();
        return new Scope { Db = db, Repository = new AssignmentRepository(db), Tenants = tenants };
    }

    private static HybridCache NewCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    private static Assignment Pending(Guid? gradeLevelId, DateTimeOffset? dueDate = null)
    {
        var assignment = Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, dueDate, null, TeacherId)
            .WithTenant(TenantA);
        if (gradeLevelId is { } grade)
        {
            assignment.WithGradeTarget(grade);
        }

        assignment.SubmitForApproval();
        return assignment;
    }

    /// <summary>A resolver whose answer depends on the grade it was asked about — the observable for
    /// "the row's own policy-scope grade drove the window".</summary>
    private sealed class PerGradePolicyResolver(int? gradeAWindowDays, int? tenantWindowDays) : IAssignmentPolicyResolver
    {
        public List<Guid?> RequestedGradeLevelIds { get; } = [];

        public Task<EffectiveAssignmentPolicy> ResolveAsync(Guid? gradeLevelId, CancellationToken cancellationToken = default)
        {
            RequestedGradeLevelIds.Add(gradeLevelId);
            var days = gradeLevelId == GradeA ? gradeAWindowDays : tenantWindowDays;
            return Task.FromResult(FakeAssignmentPolicyResolver.BuiltInDefault with { ApprovalWindowDays = days });
        }
    }

    // ── Detail read ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task Detail_MapsSubmittedAt_AndDerivesTheWindowExpiry()
    {
        using var s = Build(nameof(Detail_MapsSubmittedAt_AndDerivesTheWindowExpiry));
        var assignment = Pending(GradeA);
        s.Db.Assignments.Add(assignment);
        await s.Db.SaveChangesAsync();

        var row = await s.Detail(new PerGradePolicyResolver(3, null), NewCache())
            .HandleAsync(new GetAssignmentByIdQuery(assignment.Id));

        row.Should().NotBeNull();
        row!.SubmittedAt.Should().Be(assignment.SubmittedAt);
        row.WindowExpiresAt.Should().Be(assignment.SubmittedAt!.Value.AddDays(3),
            "the window is SubmittedAt + the ROW's effective ApprovalWindowDays");
    }

    [TestMethod]
    public async Task Detail_MapsTheApprovalReason_OnAReturnedRow()
    {
        using var s = Build(nameof(Detail_MapsTheApprovalReason_OnAReturnedRow));
        var assignment = Pending(GradeA);
        assignment.Reject(ApproverId, "Add the rubric");
        s.Db.Assignments.Add(assignment);
        await s.Db.SaveChangesAsync();

        var row = await s.Detail(new PerGradePolicyResolver(null, 5), NewCache())
            .HandleAsync(new GetAssignmentByIdQuery(assignment.Id));

        row!.ApprovalStatus.Should().Be(ApprovalStatusDto.Rejected);
        row.ApprovalReason.Should().Be("Add the rubric");
        row.SubmittedAt.Should().BeNull();
        row.WindowExpiresAt.Should().BeNull(
            "a returned row carries no submission stamp, so a stale countdown can never render");
    }

    [TestMethod]
    public async Task Detail_NoConfiguredWindow_ProjectsNull_SoFailOpenNeverInvents()
    {
        using var s = Build(nameof(Detail_NoConfiguredWindow_ProjectsNull_SoFailOpenNeverInvents));
        var assignment = Pending(GradeA);
        s.Db.Assignments.Add(assignment);
        await s.Db.SaveChangesAsync();

        // BuiltInDefault is exactly what the HTTP resolver yields when both fetches fail.
        var row = await s.Detail(new FakeAssignmentPolicyResolver(), NewCache())
            .HandleAsync(new GetAssignmentByIdQuery(assignment.Id));

        row!.SubmittedAt.Should().NotBeNull();
        row.WindowExpiresAt.Should().BeNull(
            "the built-in default is no window; a failed resolve must never produce a deadline");
    }

    [TestMethod]
    public async Task Detail_StoredNonPositiveWindow_ProjectsNull()
    {
        using var s = Build(nameof(Detail_StoredNonPositiveWindow_ProjectsNull));
        var assignment = Pending(GradeA);
        s.Db.Assignments.Add(assignment);
        await s.Db.SaveChangesAsync();

        var row = await s.Detail(new PerGradePolicyResolver(0, null), NewCache())
            .HandleAsync(new GetAssignmentByIdQuery(assignment.Id));

        row!.WindowExpiresAt.Should().BeNull("Q7's consumer half: a stored zero is no window");
    }

    // ── List read ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task List_DerivesTheWindowPerRow_FromEachRowsGradePolicy()
    {
        using var s = Build(nameof(List_DerivesTheWindowPerRow_FromEachRowsGradePolicy));
        var graded = Pending(GradeA);
        var ungraded = Pending(gradeLevelId: null);
        s.Db.Assignments.AddRange(graded, ungraded);
        await s.Db.SaveChangesAsync();

        var resolver = new PerGradePolicyResolver(gradeAWindowDays: 2, tenantWindowDays: 10);
        var rows = await s.List(resolver, NewCache()).HandleAsync(new ListAssignmentsQuery(null));

        rows.Should().HaveCount(2);
        rows.Single(r => r.Id == graded.Id).WindowExpiresAt
            .Should().Be(graded.SubmittedAt!.Value.AddDays(2), "the grade override applies to its row");
        rows.Single(r => r.Id == ungraded.Id).WindowExpiresAt
            .Should().Be(ungraded.SubmittedAt!.Value.AddDays(10), "an ungraded row resolves the tenant default");
        resolver.RequestedGradeLevelIds.Should().BeEquivalentTo([GradeA, (Guid?)null],
            "one resolve per DISTINCT derived grade — the existing per-(tenant, grade) cache");
    }

    [TestMethod]
    public async Task List_MapsTheReason_AndLeavesAnUnsubmittedRowNull()
    {
        using var s = Build(nameof(List_MapsTheReason_AndLeavesAnUnsubmittedRowNull));
        var rejected = Pending(GradeA);
        rejected.Reject(ApproverId, "Not this term");
        var neverSubmitted = Assignment.Create("Other", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId).WithTenant(TenantA);
        s.Db.Assignments.AddRange(rejected, neverSubmitted);
        await s.Db.SaveChangesAsync();

        var rows = await s.List(new PerGradePolicyResolver(4, 4), NewCache())
            .HandleAsync(new ListAssignmentsQuery(null));

        rows.Single(r => r.Id == rejected.Id).ApprovalReason.Should().Be("Not this term");
        var untouched = rows.Single(r => r.Id == neverSubmitted.Id);
        untouched.ApprovalStatus.Should().BeNull();
        untouched.WindowExpiresAt.Should().BeNull();
    }
}
