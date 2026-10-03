using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>teacher-scope-auth</c> [P2-2] / D6.7 — the <b>direct-id</b> read. The assignment is
/// reachable by id, so it must apply the same visibility rule as the list and answer null (which
/// the endpoint maps to 404) for an out-of-scope id; otherwise a teacher-only principal reads any
/// assignment in the tenant even with the list filtered.
///
/// <para>Discriminating: pre-fix the handler returned the row for every id.</para>
/// </summary>
[TestClass]
public class GetAssignmentByIdScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherA = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid TeacherB = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    private static readonly Guid GradeA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid GradeB = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid SubjectArt = Guid.Parse("00000000-0000-0000-0000-000000000042");
    private static readonly Guid SubjectMath = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private sealed class Scope : IDisposable
    {
        public required AssignmentsDbContext Db { get; init; }
        public required GetAssignmentByIdQueryHandler Handler { get; init; }

        public void Dispose() => Db.Dispose();
    }

    private sealed class PolicyResolver : IAssignmentPolicyResolver
    {
        public Task<EffectiveAssignmentPolicy> ResolveAsync(Guid? gradeLevelId, CancellationToken cancellationToken = default) =>
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

    private sealed class PassThroughHybridCache : HybridCache
    {
        public override async ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) =>
            await factory(state, cancellationToken);

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private static Scope Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        ((TenantProvider)provider.GetRequiredService<ITenantProvider>())
            .SetTenant(new TenantContext(TenantId, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();

        return new Scope
        {
            Db = db,
            Handler = new GetAssignmentByIdQueryHandler(
                db, new PassThroughHybridCache(), new PolicyResolver(), new OffFlags(),
                NullLogger<GetAssignmentByIdQueryHandler>.Instance),
        };
    }

    private static Assignment NewAssignment(string title, Guid creator, Guid? grade, Guid subject) =>
        Assignment.Create(title, null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, subject, grade, null, null, creator)
            .WithTenant(TenantId);

    [TestMethod]
    public async Task ById_OwnAndTaughtRows_Resolve_ForeignRowIsNull()
    {
        using var s = Build(nameof(ById_OwnAndTaughtRows_Resolve_ForeignRowIsNull));

        var own = NewAssignment("own", TeacherA, GradeB, SubjectArt);
        var taughtGradeWide = NewAssignment("taught-grade-wide", TeacherB, GradeA, SubjectArt);
        var taughtExact = NewAssignment("taught-exact", TeacherB, GradeB, SubjectArt);
        var foreignSubject = NewAssignment("foreign-subject", TeacherB, GradeB, SubjectMath);
        var foreignGrade = NewAssignment("foreign-grade", TeacherB, null, SubjectArt);
        s.Db.Assignments.AddRange(own, taughtGradeWide, taughtExact, foreignSubject, foreignGrade);
        await s.Db.SaveChangesAsync();

        var scope = TeacherScope.ForTeacher(TeacherA,
            [new TeacherSubjectGrade(GradeA, TopicId: null, RoleCodedValueId: null),
             new TeacherSubjectGrade(GradeB, TopicId: SubjectArt, RoleCodedValueId: null)]);

        (await s.Handler.HandleAsync(new GetAssignmentByIdQuery(own.Id, scope))).Should().NotBeNull(
            "the caller's own row stays readable by id");
        (await s.Handler.HandleAsync(new GetAssignmentByIdQuery(taughtGradeWide.Id, scope))).Should().NotBeNull(
            "[P1-5] a null-topic teaching row matches the whole grade");
        (await s.Handler.HandleAsync(new GetAssignmentByIdQuery(taughtExact.Id, scope))).Should().NotBeNull(
            "an exact (subject, grade) teaching row matches");

        (await s.Handler.HandleAsync(new GetAssignmentByIdQuery(foreignSubject.Id, scope))).Should().BeNull(
            "[P2-2] out-of-scope id ⇒ null ⇒ 404 — a different subject in a taught grade is still foreign");
        (await s.Handler.HandleAsync(new GetAssignmentByIdQuery(foreignGrade.Id, scope))).Should().BeNull(
            "[P2-2] out-of-scope id ⇒ null ⇒ 404");

        // The unrestricted postures keep today's behaviour.
        (await s.Handler.HandleAsync(new GetAssignmentByIdQuery(foreignSubject.Id, TeacherScope.Unrestricted)))
            .Should().NotBeNull("staff/admin read tenant-wide");
        (await s.Handler.HandleAsync(new GetAssignmentByIdQuery(foreignSubject.Id, Scope: null)))
            .Should().NotBeNull("the scope-less query shape pre-existing callers use stays unrestricted");
    }

    [TestMethod]
    public async Task ById_EmptyScope_ResolvesNothing_IncludingTheOwnRow()
    {
        using var s = Build(nameof(ById_EmptyScope_ResolvesNothing_IncludingTheOwnRow));
        var own = NewAssignment("own", TeacherA, GradeA, SubjectArt);
        s.Db.Assignments.Add(own);
        await s.Db.SaveChangesAsync();

        (await s.Handler.HandleAsync(new GetAssignmentByIdQuery(own.Id, TeacherScope.Empty)))
            .Should().BeNull("[P1-4] an empty scope grants nothing");
    }
}
