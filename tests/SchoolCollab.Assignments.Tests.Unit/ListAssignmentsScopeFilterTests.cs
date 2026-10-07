using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>teacher-scope-auth</c> D3 / D6.2 + D6.4 + D6.6 — the assignment-list scope filter,
/// exercised through the REAL <see cref="ListAssignmentsQueryHandler"/> over the real repository
/// (EF InMemory) so the assertion is on the <b>list</b>, not on the scope object.
///
/// <para>Discriminating: the scope cases fail against the pre-fix code, which had no scope
/// concept at all (the seeded foreign rows would be returned). The two-caller case is the
/// [P1-3] guard — with the filter applied inside the cached delegate, caller B observes caller
/// A's filtered list. <b>One case is a posture guard, not a discriminator</b> —
/// <see cref="List_NoRecognisedRole_IsTenantWide"/>, which passes pre-fix by construction because
/// there was no scope concept to fail against; it is labelled as such, per the acceptance-honesty
/// rule the plan gate applied.</para>
/// </summary>
[TestClass]
public class ListAssignmentsScopeFilterTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherA = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid TeacherB = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    private static readonly Guid GradeA = Guid.Parse("00000000-0000-0000-0000-00000000a001");
    private static readonly Guid GradeB = Guid.Parse("00000000-0000-0000-0000-00000000b001");
    private static readonly Guid SubjectMath = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid SubjectArt = Guid.Parse("00000000-0000-0000-0000-000000000042");

    private sealed class Scope : IDisposable
    {
        public required AssignmentsDbContext Db { get; init; }
        public required AssignmentRepository Repository { get; init; }
        public required ITenantProvider Tenants { get; init; }

        public ListAssignmentsQueryHandler Handler(HybridCache cache) =>
            new(Repository, cache, Tenants, new NoPolicyResolver(), new OffFlags(),
                NullLogger<ListAssignmentsQueryHandler>.Instance);

        public void Dispose() => Db.Dispose();
    }

    /// <summary>Only the scope paths are under test — approval is off for every row.</summary>
    private sealed class NoPolicyResolver : IAssignmentPolicyResolver
    {
        public Task<EffectiveAssignmentPolicy> ResolveAsync(
            Guid? gradeLevelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FakeAssignmentPolicyResolver.BuiltInDefault);
    }

    private sealed class OffFlags : SchoolCollab.Core.Features.IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => false;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    /// <summary>A cache that really caches and records every key it was asked for: the observable
    /// for "the cached value stayed tenant-wide" and for "no bleed between callers".</summary>
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
            {
                return typed;
            }

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

    private static Scope Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(TenantId, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();

        return new Scope { Db = db, Repository = new AssignmentRepository(db), Tenants = tenants };
    }

    /// <summary>Round <c>drop-primary-grade</c>: the grade half of the row is its authored grade
    /// TARGETS (a null grade = no grade target ⇒ visible through the creator leg only).</summary>
    private static Assignment NewAssignment(string title, Guid creator, Guid? grade, Guid subject)
    {
        var assignment = Assignment.Create(title, null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, subject, null, null, creator)
            .WithTenant(TenantId);

        if (grade is Guid gradeId)
        {
            assignment.WithGradeTarget(gradeId);
        }

        return assignment;
    }

    // ── D6.2 — both directions: own creation OR taught grade/subject ──────

    [TestMethod]
    public async Task List_TeacherScope_KeepsOwnCreationAndTaughtRows_DropsForeignRows()
    {
        using var s = Build(nameof(List_TeacherScope_KeepsOwnCreationAndTaughtRows_DropsForeignRows));

        // A's own creation in a grade A does NOT teach.
        s.Db.Assignments.Add(NewAssignment("own", TeacherA, GradeB, SubjectArt));
        // A's own creation with no grade at all (the null-grade leg).
        s.Db.Assignments.Add(NewAssignment("own-gradeless", TeacherA, null, SubjectArt));
        // A teaches grade A grade-wide (null subject — [P1-5]) → the subject is irrelevant.
        s.Db.Assignments.Add(NewAssignment("grade-wide", TeacherB, GradeA, SubjectArt));
        // A teaches grade B + subject Art exactly.
        s.Db.Assignments.Add(NewAssignment("subject-exact", TeacherB, GradeB, SubjectArt));
        // B's row in grade B with a DIFFERENT subject: neither creator nor taught.
        s.Db.Assignments.Add(NewAssignment("foreign-other-subject", TeacherB, GradeB, SubjectMath));
        // B's row in a grade A does not teach.
        s.Db.Assignments.Add(NewAssignment("foreign-other-grade", TeacherB, null, SubjectArt));
        await s.Db.SaveChangesAsync();

        var scope = TeacherScope.ForTeacher(TeacherA,
            [new TeacherSubjectGrade(GradeA, TopicId: null, RoleCodedValueId: null),
             new TeacherSubjectGrade(GradeB, TopicId: SubjectArt, RoleCodedValueId: null)]);

        var rows = await s.Handler(new RecordingHybridCache())
            .HandleAsync(new ListAssignmentsQuery(null, scope));

        rows.Select(r => r.Title).Should().BeEquivalentTo(
            new[] { "own", "own-gradeless", "grade-wide", "subject-exact" },
            "the caller's own creations stay visible (including a grade-less one), a grade-wide "
            + "teaching row matches any subject of that grade ([P1-5]), an exact (subject, grade) "
            + "row matches its own subject only, and another teacher's untaught rows are dropped");
    }

    [TestMethod]
    public async Task List_TeacherWithoutTaughtRows_StillSeesOwnCreations()
    {
        using var s = Build(nameof(List_TeacherWithoutTaughtRows_StillSeesOwnCreations));
        s.Db.Assignments.Add(NewAssignment("mine", TeacherA, GradeA, SubjectArt));
        s.Db.Assignments.Add(NewAssignment("not-mine", TeacherB, GradeA, SubjectArt));
        await s.Db.SaveChangesAsync();

        var rows = await s.Handler(new RecordingHybridCache())
            .HandleAsync(new ListAssignmentsQuery(null, TeacherScope.ForTeacher(TeacherA, [])));

        rows.Select(r => r.Title).Should().BeEquivalentTo(new[] { "mine" },
            "a resolved scope with no taught rows still carries the caller's own creations");
    }

    [TestMethod]
    public async Task List_EmptyScope_ReturnsNothing()
    {
        using var s = Build(nameof(List_EmptyScope_ReturnsNothing));
        s.Db.Assignments.Add(NewAssignment("mine", TeacherA, GradeA, SubjectArt));
        s.Db.Assignments.Add(NewAssignment("not-mine", TeacherB, GradeB, SubjectArt));
        await s.Db.SaveChangesAsync();

        var rows = await s.Handler(new RecordingHybridCache())
            .HandleAsync(new ListAssignmentsQuery(null, TeacherScope.Empty));

        rows.Should().BeEmpty(
            "[P1-4] a teacher-only principal whose scope could not be established sees nothing — "
            + "tenant-wide would have returned both rows");
    }

    /// <summary>Posture guard, <b>NOT a discriminator</b>: the tenant-wide posture for a role-less or
    /// scope-less caller passes against the pre-fix code by construction, because there was no scope
    /// concept to fail against. Labelled so a reader cannot mistake it for coverage of the filter.</summary>
    [TestMethod]
    public async Task List_NoRecognisedRole_IsTenantWide()
    {
        using var s = Build(nameof(List_NoRecognisedRole_IsTenantWide));
        s.Db.Assignments.AddRange(
            NewAssignment("mine", TeacherA, GradeA, SubjectArt),
            NewAssignment("not-mine", TeacherB, GradeB, SubjectArt));
        await s.Db.SaveChangesAsync();

        var cache = new RecordingHybridCache();
        var unrestricted = await s.Handler(cache).HandleAsync(new ListAssignmentsQuery(null, TeacherScope.Unrestricted));
        var legacyShape = await s.Handler(cache).HandleAsync(new ListAssignmentsQuery(null, Scope: null));

        unrestricted.Should().HaveCount(2, "[P1-4] staff/admin/no-role posture is tenant-wide");
        legacyShape.Should().HaveCount(2,
            "the scope-less query shape pre-existing callers use stays tenant-wide");
    }

    // ── D6.4 / [P1-3] — the two-caller cache-bleed test ───────────────────

    [TestMethod]
    public async Task List_TwoCallers_ShareTheTenantWideCacheEntry_WithoutBleeding()
    {
        using var s = Build(nameof(List_TwoCallers_ShareTheTenantWideCacheEntry_WithoutBleeding));
        s.Db.Assignments.AddRange(
            NewAssignment("a-only", TeacherA, GradeA, SubjectArt),
            NewAssignment("b-only", TeacherB, GradeB, SubjectArt));
        await s.Db.SaveChangesAsync();

        var cache = new RecordingHybridCache();
        var handler = s.Handler(cache);
        var tenantKey = $"assignments:list:{TenantId}:all";

        var scopeA = TeacherScope.ForTeacher(TeacherA, []);
        var scopeB = TeacherScope.ForTeacher(TeacherB, []);

        var forA = await handler.HandleAsync(new ListAssignmentsQuery(null, scopeA));
        // Caller B reads the SAME cache entry. A filter applied inside the cached delegate would
        // hand B the list cached for A.
        var forB = await handler.HandleAsync(new ListAssignmentsQuery(null, scopeB));
        var tenantWide = await handler.HandleAsync(new ListAssignmentsQuery(null, TeacherScope.Unrestricted));

        forA.Select(r => r.Title).Should().BeEquivalentTo(new[] { "a-only" });
        forB.Select(r => r.Title).Should().BeEquivalentTo(new[] { "b-only" },
            "[P1-3] caller B must never observe caller A's filtered list — the filter is applied "
            + "AFTER the tenant-wide cache read, not inside the cached delegate");
        tenantWide.Should().HaveCount(2,
            "the cached value itself stays the tenant-wide projection, so a later unrestricted "
            + "caller still sees every row");

        cache.RequestedKeys.Count(k => k == tenantKey).Should().Be(3,
            "all three callers read the one tenant-wide list key (no scope hash in the key)");
    }
}
