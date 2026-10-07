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
/// Round B1 (<c>documents/rounds/round-assignment-policy-ui.md</c> Plan (f) AC5, owner decision
/// Q6) — the <b>server-derived</b> <see cref="AssignmentSummaryDto.RequiresApproval"/> projected by
/// both read handlers as the effective policy's
/// <see cref="EffectiveAssignmentPolicy.RequiresApprovalBeforePublish"/> OR'd with
/// <c>FEATURE:RequireAssignmentApproval</c> (the D3 OR stays live). No UI surface reads the flag.
///
/// <para>The discriminating cases: (a) policy true + flag OFF ⇒ true; (b) policy false + flag ON ⇒
/// true (deploy-window parity); (c) both off ⇒ false; (d) the resolver's fail-open result + flag OFF
/// ⇒ <b>false</b> (a failed resolve can never turn approval ON); (e) two assignments sharing a grade
/// resolve the policy <b>once</b>, through the per-(tenant, grade) cache; (f) a flag-service throw ⇒
/// false and both reads still return (plan-review P2-3 — a Config outage must not 500 the
/// list/detail read, which is what the replaced client-side read degraded around).</para>
/// </summary>
[TestClass]
public class AssignmentRequiresApprovalProjectionTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid GradeA = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid GradeB = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

    private sealed class ProjectionScope : IDisposable
    {
        public required AssignmentsDbContext Db { get; init; }
        public required AssignmentRepository Repository { get; init; }
        public required ITenantProvider Tenants { get; init; }

        public ListAssignmentsQueryHandler ListHandler(
            IAssignmentPolicyResolver resolver, IFeatureFlagService flags, HybridCache cache) =>
            new(Repository, cache, Tenants, resolver, flags, NullLogger<ListAssignmentsQueryHandler>.Instance);

        public GetAssignmentByIdQueryHandler DetailHandler(
            IAssignmentPolicyResolver resolver, IFeatureFlagService flags, HybridCache cache) =>
            new(Db, cache, resolver, flags, NullLogger<GetAssignmentByIdQueryHandler>.Instance);

        public void Dispose() => Db.Dispose();
    }

    private static ProjectionScope Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(TenantA, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();

        return new ProjectionScope
        {
            Db = db,
            Repository = new AssignmentRepository(db),
            Tenants = tenants,
        };
    }

    /// <summary>Round <c>drop-primary-grade</c>: a row's grade scope is its authored grade TARGETS,
    /// so the policy-scope grade the list read resolves is DERIVED from them — a null grade here
    /// means "no grade target" and derives the tenant-default policy.</summary>
    private static Assignment NewAssignment(Guid? gradeLevelId)
    {
        var assignment = Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId)
            .WithTenant(TenantA);

        if (gradeLevelId is Guid grade)
        {
            assignment.WithGradeTarget(grade);
        }

        return assignment;
    }

    private static EffectiveAssignmentPolicy PolicyWith(bool requiresApproval) =>
        FakeAssignmentPolicyResolver.BuiltInDefault with { RequiresApprovalBeforePublish = requiresApproval };

    /// <summary>
    /// A cache that actually caches (so a repeated per-grade read is a hit) and records every key
    /// it is asked for — the observable for "resolved once per distinct grade".
    /// </summary>
    private sealed class RecordingHybridCache : HybridCache
    {
        private readonly Dictionary<string, object?> _store = new();

        public List<string> RequestedKeys { get; } = [];

        public override async ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            RequestedKeys.Add(key);
            if (_store.TryGetValue(key, out var cached) && cached is T typed)
                return typed;

            var value = await factory(state, cancellationToken);
            _store[key] = value;
            return value;
        }

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            _store[key] = value;
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _store.Remove(key);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            _store.Clear();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records which grades the projection asked for, so "once per distinct grade" is
    /// observable rather than inferred.</summary>
    private sealed class RecordingAssignmentPolicyResolver(EffectiveAssignmentPolicy policy)
        : IAssignmentPolicyResolver
    {
        public List<Guid?> RequestedGradeLevelIds { get; } = [];

        public Task<EffectiveAssignmentPolicy> ResolveAsync(
            Guid? gradeLevelId, CancellationToken cancellationToken = default)
        {
            RequestedGradeLevelIds.Add(gradeLevelId);
            return Task.FromResult(policy);
        }
    }

    /// <summary>The failure posture of the real HTTP resolver: a failed fetch degrades to the
    /// built-in default, so it can only ever <b>suppress</b> the policy leg of the OR.</summary>
    private sealed class FailOpenAssignmentPolicyResolver : IAssignmentPolicyResolver
    {
        public Task<EffectiveAssignmentPolicy> ResolveAsync(
            Guid? gradeLevelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FakeAssignmentPolicyResolver.BuiltInDefault);
    }

    /// <summary>A Config outage: the flag read throws rather than answering.</summary>
    private sealed class ThrowingFeatureFlagService : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => throw new InvalidOperationException("config unavailable");

        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) =>
            Task.FromException<bool>(new InvalidOperationException("config unavailable"));

        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();

        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    // ── (a)/(b)/(c) the OR ───────────────────────────────────────────────

    [TestMethod]
    public async Task List_PolicyRequiresApproval_FlagOff_ProjectsTrue()
    {
        using var s = Build(nameof(List_PolicyRequiresApproval_FlagOff_ProjectsTrue));
        s.Db.Assignments.Add(NewAssignment(GradeA));
        await s.Db.SaveChangesAsync();

        var rows = await s.ListHandler(
                new FakeAssignmentPolicyResolver { Policy = PolicyWith(true) },
                new FakeFeatureFlagService(), new RecordingHybridCache())
            .HandleAsync(new ListAssignmentsQuery(null));

        rows.Should().ContainSingle();
        rows[0].RequiresApproval.Should().BeTrue(
            "the policy field is an independent trigger from the flag (D3)");
    }

    [TestMethod]
    public async Task List_PolicyOff_FlagOn_ProjectsTrue()
    {
        using var s = Build(nameof(List_PolicyOff_FlagOn_ProjectsTrue));
        s.Db.Assignments.Add(NewAssignment(GradeA));
        await s.Db.SaveChangesAsync();

        var rows = await s.ListHandler(
                new FakeAssignmentPolicyResolver { Policy = PolicyWith(false) },
                new FakeFeatureFlagService { IsEnabledValue = true }, new RecordingHybridCache())
            .HandleAsync(new ListAssignmentsQuery(null));

        rows[0].RequiresApproval.Should().BeTrue(
            "deploy-window parity: the flag alone still gates exactly as it did");
    }

    [TestMethod]
    public async Task List_PolicyOff_FlagOff_ProjectsFalse()
    {
        using var s = Build(nameof(List_PolicyOff_FlagOff_ProjectsFalse));
        s.Db.Assignments.Add(NewAssignment(GradeA));
        await s.Db.SaveChangesAsync();

        var rows = await s.ListHandler(
                new FakeAssignmentPolicyResolver { Policy = PolicyWith(false) },
                new FakeFeatureFlagService(), new RecordingHybridCache())
            .HandleAsync(new ListAssignmentsQuery(null));

        rows[0].RequiresApproval.Should().BeFalse();
    }

    [TestMethod]
    public async Task Detail_PolicyRequiresApproval_FlagOff_ProjectsTrue()
    {
        using var s = Build(nameof(Detail_PolicyRequiresApproval_FlagOff_ProjectsTrue));
        var assignment = NewAssignment(GradeA);
        s.Db.Assignments.Add(assignment);
        await s.Db.SaveChangesAsync();

        var row = await s.DetailHandler(
                new FakeAssignmentPolicyResolver { Policy = PolicyWith(true) },
                new FakeFeatureFlagService(), new RecordingHybridCache())
            .HandleAsync(new GetAssignmentByIdQuery(assignment.Id));

        row.Should().NotBeNull();
        row!.RequiresApproval.Should().BeTrue();
    }

    [TestMethod]
    public async Task Detail_PolicyOff_FlagOn_ProjectsTrue()
    {
        using var s = Build(nameof(Detail_PolicyOff_FlagOn_ProjectsTrue));
        var assignment = NewAssignment(GradeA);
        s.Db.Assignments.Add(assignment);
        await s.Db.SaveChangesAsync();

        var row = await s.DetailHandler(
                new FakeAssignmentPolicyResolver { Policy = PolicyWith(false) },
                new FakeFeatureFlagService { IsEnabledValue = true }, new RecordingHybridCache())
            .HandleAsync(new GetAssignmentByIdQuery(assignment.Id));

        row!.RequiresApproval.Should().BeTrue();
    }

    // ── (d) a failed resolve can never turn approval ON ──────────────────

    [TestMethod]
    public async Task List_ResolverFailsOpen_FlagOff_ProjectsFalse()
    {
        using var s = Build(nameof(List_ResolverFailsOpen_FlagOff_ProjectsFalse));
        s.Db.Assignments.Add(NewAssignment(GradeA));
        await s.Db.SaveChangesAsync();

        var rows = await s.ListHandler(
                new FailOpenAssignmentPolicyResolver(), new FakeFeatureFlagService(), new RecordingHybridCache())
            .HandleAsync(new ListAssignmentsQuery(null));

        rows[0].RequiresApproval.Should().BeFalse(
            "the built-in default is what a failed policy fetch yields — it must never gate");
    }

    [TestMethod]
    public async Task Detail_ResolverFailsOpen_FlagOff_ProjectsFalse()
    {
        using var s = Build(nameof(Detail_ResolverFailsOpen_FlagOff_ProjectsFalse));
        var assignment = NewAssignment(GradeA);
        s.Db.Assignments.Add(assignment);
        await s.Db.SaveChangesAsync();

        var row = await s.DetailHandler(
                new FailOpenAssignmentPolicyResolver(), new FakeFeatureFlagService(), new RecordingHybridCache())
            .HandleAsync(new GetAssignmentByIdQuery(assignment.Id));

        row!.RequiresApproval.Should().BeFalse();
    }

    // ── (e) once per DISTINCT grade, through the per-(tenant, grade) cache ──

    [TestMethod]
    public async Task List_TwoAssignmentsSharingAGrade_ResolveThePolicyOnce()
    {
        using var s = Build(nameof(List_TwoAssignmentsSharingAGrade_ResolveThePolicyOnce));
        s.Db.Assignments.AddRange(NewAssignment(GradeA), NewAssignment(GradeA), NewAssignment(GradeB));
        await s.Db.SaveChangesAsync();

        var resolver = new RecordingAssignmentPolicyResolver(PolicyWith(true));
        var cache = new RecordingHybridCache();

        var rows = await s.ListHandler(resolver, new FakeFeatureFlagService(), cache)
            .HandleAsync(new ListAssignmentsQuery(null));

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(r => r.RequiresApproval);

        resolver.RequestedGradeLevelIds.Should().BeEquivalentTo(new Guid?[] { GradeA, GradeB },
            "the cache key is the (tenant, grade) pair, so the two rows sharing GradeA resolve it once");

        var gradeAKey = $"assignment-policy:effective:{TenantA}:{GradeA}";
        cache.RequestedKeys.Count(k => k == gradeAKey).Should().Be(1,
            "the per-grade policy is read through the per-(tenant, grade) cache entry exactly once");
        cache.RequestedKeys.Should().NotContain($"assignment-policy:effective:{TenantA}:none",
            "every assignment in this page carries a grade");
    }

    [TestMethod]
    public async Task List_AssignmentsWithoutAGrade_ResolveThroughTheNoneSentinelKey()
    {
        using var s = Build(nameof(List_AssignmentsWithoutAGrade_ResolveThroughTheNoneSentinelKey));
        s.Db.Assignments.Add(NewAssignment(null));
        await s.Db.SaveChangesAsync();

        var resolver = new RecordingAssignmentPolicyResolver(PolicyWith(false));
        var cache = new RecordingHybridCache();

        var rows = await s.ListHandler(resolver, new FakeFeatureFlagService(), cache)
            .HandleAsync(new ListAssignmentsQuery(null));

        rows.Should().ContainSingle();
        resolver.RequestedGradeLevelIds.Should().Contain((Guid?)null,
            "a grade-less assignment resolves the tenant default");
        cache.RequestedKeys.Should().Contain($"assignment-policy:effective:{TenantA}:none",
            "the null grade resolves through the 'none' sentinel key");
    }

    // ── (f) a Config outage degrades, it does not 500 the read ───────────

    [TestMethod]
    public async Task List_FlagServiceThrows_ProjectsFalseAndStillReturns()
    {
        using var s = Build(nameof(List_FlagServiceThrows_ProjectsFalseAndStillReturns));
        s.Db.Assignments.Add(NewAssignment(GradeA));
        await s.Db.SaveChangesAsync();

        var rows = await s.ListHandler(
                new FakeAssignmentPolicyResolver { Policy = PolicyWith(false) },
                new ThrowingFeatureFlagService(), new RecordingHybridCache())
            .HandleAsync(new ListAssignmentsQuery(null));

        rows.Should().ContainSingle("the list read must not fail because the flag backend is down");
        rows[0].RequiresApproval.Should().BeFalse(
            "plan-review P2-3: the flag leg is guarded and defaults to OFF, the behaviour the " +
            "replaced client-side read degraded to");
    }

    [TestMethod]
    public async Task Detail_FlagServiceThrows_ProjectsFalseAndStillReturns()
    {
        using var s = Build(nameof(Detail_FlagServiceThrows_ProjectsFalseAndStillReturns));
        var assignment = NewAssignment(GradeA);
        s.Db.Assignments.Add(assignment);
        await s.Db.SaveChangesAsync();

        var row = await s.DetailHandler(
                new FakeAssignmentPolicyResolver { Policy = PolicyWith(false) },
                new ThrowingFeatureFlagService(), new RecordingHybridCache())
            .HandleAsync(new GetAssignmentByIdQuery(assignment.Id));

        row.Should().NotBeNull("the detail read must not fail because the flag backend is down");
        row!.RequiresApproval.Should().BeFalse();
    }

    [TestMethod]
    public async Task Detail_FlagServiceThrows_PolicyRequiresApproval_StillProjectsTrue()
    {
        // The guard is scoped to the flag leg only — a policy that requires approval keeps
        // gating even when the Config backend is unreachable.
        using var s = Build(nameof(Detail_FlagServiceThrows_PolicyRequiresApproval_StillProjectsTrue));
        var assignment = NewAssignment(GradeA);
        s.Db.Assignments.Add(assignment);
        await s.Db.SaveChangesAsync();

        var row = await s.DetailHandler(
                new FakeAssignmentPolicyResolver { Policy = PolicyWith(true) },
                new ThrowingFeatureFlagService(), new RecordingHybridCache())
            .HandleAsync(new GetAssignmentByIdQuery(assignment.Id));

        row!.RequiresApproval.Should().BeTrue(
            "the policy leg is resolved independently of the flag backend");
    }
}
