using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Auth;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Constants;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round <c>teacher-scope-auth</c> D6.3 / acceptance criterion 3 — <b>fail-closed proven at the
/// LIST level</b>, end to end across the real seams: a failing / malformed / non-2xx
/// <c>students-api</c> hop resolved through the REAL <see cref="TeacherScopeHttpClient"/> and
/// <see cref="AssignmentScopeResolver"/>, fed to the REAL
/// <see cref="ListAssignmentsQueryHandler"/> over seeded rows.
///
/// <para>Discriminating: the seed deliberately contains a row the caller did NOT create. An
/// implementation that falls back to tenant-wide — or that fails open anywhere on the path —
/// returns it and fails these assertions; only the empty-scope posture returns none. The control
/// case (same seed, a 200 that teaches the seeded grade) returns both rows, so the empty result
/// is caused by the failed hop and not by a broken harness.</para>
/// </summary>
[TestClass]
public class ListAssignmentsFailClosedTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CallerTeacherId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid ForeignTeacherId = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    private static readonly Guid GradeId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid SubjectId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private sealed class SeedRepository : IAssignmentRepository
    {
        public List<AssignmentSummary> Rows { get; } = [];

        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? status, CancellationToken ct = default) =>
            Task.FromResult(Rows);

        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Assignment?>(null);
        public Task AddAsync(Assignment assignment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Assignment assignment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Assignment assignment, CancellationToken ct = default) => throw new NotSupportedException();
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
    }

    private sealed class StubTenantProvider : ITenantProvider
    {
        public TenantContext GetTenantContext() => new(TenantId, "SchoolA", TenantType.School);
    }

    /// <summary>The teacher-role principal's id, as the endpoint resolves it (never the wire).</summary>
    private sealed class StubCurrentUser(Guid? teacherId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? TeacherId => teacherId;
        public TenantContext CurrentTenant => new(TenantId, "SchoolA", TenantType.School);
    }

    private sealed class StubPolicyResolver : IAssignmentPolicyResolver
    {
        public Task<EffectiveAssignmentPolicy> ResolveAsync(Guid? gradeLevelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EffectiveAssignmentPolicyResolver().Resolve(tenantDefault: null, gradeOverride: null));
    }

    private sealed class OffFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => false;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    /// <summary>Pass-through cache: this test is about the fail-closed path, not about TTLs.</summary>
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

    private static ClaimsPrincipal TeacherPrincipal() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, RealmRoleNames.Teacher)], "TestBearer"));

    private static AssignmentSummary Row(string title, Guid creator) =>
        new(
            Guid.NewGuid(), title, null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, SubjectId, GradeId, AssignmentStatus.Draft,
            DueDate: null, MaxScore: null, MandatoryReview: false,
            CreatedByTeacherId: creator, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);

    private static async Task<string[]> TitlesAsTeacherAsync(MockHttpMessageHandler students)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("students-api"))
            .Returns(new HttpClient(students) { BaseAddress = new Uri("http://students-api") });
        var client = new TeacherScopeHttpClient(factory.Object, NullLogger<TeacherScopeHttpClient>.Instance);

        var repo = new SeedRepository();
        // The caller's own draft, plus a foreign row in the grade the caller would teach — the
        // fail-OPEN hazard.
        repo.Rows.Add(Row("own-draft", CallerTeacherId));
        repo.Rows.Add(Row("foreign-in-grade", ForeignTeacherId));

        var scope = await AssignmentScopeResolver.ResolveAsync(
            TeacherPrincipal(), new StubCurrentUser(CallerTeacherId), client, CancellationToken.None);

        var handler = new ListAssignmentsQueryHandler(
            repo, new PassThroughHybridCache(), new StubTenantProvider(),
            new StubPolicyResolver(), new OffFlags(), NullLogger<ListAssignmentsQueryHandler>.Instance);

        var rows = await handler.HandleAsync(new ListAssignmentsQuery(null, scope));
        return rows.Select(r => r.Title).ToArray();
    }

    [TestMethod]
    public async Task List_FailingStudentsHop_ReturnsNoRows_EvenWithForeignSeeds()
    {
        var students = new MockHttpMessageHandler();
        students.When("*/teachers/*").Throw(new HttpRequestException("students-api unreachable"));

        (await TitlesAsTeacherAsync(students)).Should().BeEmpty(
            "a failed taught-set read must fail CLOSED: the seeded foreign row (and, under a "
            + "tenant-wide fallback, the whole tenant list) must not be returned");
    }

    [TestMethod]
    public async Task List_MalformedStudentsBody_ReturnsNoRows_EvenWithForeignSeeds()
    {
        var students = new MockHttpMessageHandler();
        students.When("*/teachers/*").Respond("application/json", "{ not json ]");

        (await TitlesAsTeacherAsync(students)).Should().BeEmpty(
            "a malformed taught-set body is an unresolved scope, not a licence to read the tenant");
    }

    [TestMethod]
    public async Task List_StudentsError_ReturnsNoRows_EvenWithForeignSeeds()
    {
        var students = new MockHttpMessageHandler();
        students.When("*/teachers/*").Respond(HttpStatusCode.ServiceUnavailable);

        (await TitlesAsTeacherAsync(students)).Should().BeEmpty(
            "a non-2xx taught-set read is an unresolved scope");
    }

    [TestMethod]
    public async Task List_Control_ResolvedTaughtGrade_ReturnsTheTaughtRowAndTheOwnRow()
    {
        // Control for the three cases above: the SAME seed, but the hop resolves and teaches the
        // seeded grade — so the empty results above are caused by the failed hop, not the harness.
        var students = new MockHttpMessageHandler();
        students.When("*/teachers/*").Respond("application/json",
            $$"""[{ "gradeLevelId": "{{GradeId}}", "subjectId": "{{SubjectId}}", "roleCodedValueId": null }]""");

        (await TitlesAsTeacherAsync(students)).Should().BeEquivalentTo(
            new[] { "own-draft", "foreign-in-grade" },
            "a resolved grade-wide teaching row matches both the caller's own row and the row in "
            + "that grade (the same subject) — the control that makes the empty cases above meaningful");
    }
}
