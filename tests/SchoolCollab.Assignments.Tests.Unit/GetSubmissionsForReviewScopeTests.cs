using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmissionsForReview;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListSubmissionsByAssignment;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>teacher-scope-auth</c> D3 / [P2-1] / [P1-4] — the review queue
/// (<see cref="GetSubmissionsForReview"/> + <see cref="GetSubmissionsForReviewHandler"/>) and the
/// per-assignment read (<c>ListSubmissionsByAssignment</c>).
///
/// <para>Discriminating: pre-fix the queue had no scope at all, so the empty-scope case returned
/// the caller's rows and the taught-grade case returned nothing.</para>
/// </summary>
[TestClass]
public class GetSubmissionsForReviewScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherA = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid TeacherB = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    private static readonly Guid GradeA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid SubjectArt = Guid.Parse("00000000-0000-0000-0000-000000000042");
    private static readonly Guid Ward = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    private sealed class Harness : IDisposable
    {
        public required AssignmentsDbContext Db { get; init; }
        public required SubmissionRepository Repository { get; init; }

        public void Dispose() => Db.Dispose();
    }

    private static Harness Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        ((TenantProvider)provider.GetRequiredService<ITenantProvider>())
            .SetTenant(new TenantContext(TenantId, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();

        return new Harness { Db = db, Repository = new SubmissionRepository(db) };
    }

    /// <summary>Round <c>drop-primary-grade</c>: a row's grade scope is its authored grade TARGETS, so
    /// the grade half of every scope fixture is attached as a <c>GradeLevel</c> target row (a null
    /// grade means "no grade target", which is visible through the creator leg only).</summary>
    private static Assignment NewAssignment(string title, Guid creator, Guid? grade)
    {
        var assignment = Assignment.Create(title, null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, SubjectArt, null, null, creator)
            .WithTenant(TenantId);

        if (grade is Guid gradeId)
        {
            assignment.WithGradeTarget(gradeId);
        }

        return assignment;
    }

    private static AssignmentSubmission SubmissionFor(Guid assignmentId) =>
        AssignmentSubmission.Create(TenantId, assignmentId, Ward, null);

    /// <summary>Records which overload the handler selected, and returns a sentinel row so a
    /// leak-through is observable.</summary>
    private sealed class RecordingSubmissionRepository : ISubmissionRepository
    {
        public bool LegacyCalled { get; private set; }
        public TeacherScope? ReceivedScope { get; private set; }

        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid teacherId, CancellationToken ct = default)
        {
            LegacyCalled = true;
            return Task.FromResult(new[] { Row("legacy") });
        }

        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid teacherId, TeacherScope? scope, CancellationToken ct = default)
        {
            // Mirrors the interface default + the real repository: an unrestricted (or scope-less)
            // caller keeps the owner-only read.
            if (scope is null || scope.IsUnrestricted)
            {
                return ListSubmissionsForReviewAsync(teacherId, ct);
            }

            ReceivedScope = scope;
            return Task.FromResult(new[] { Row("scoped") });
        }

        private static SubmissionForReviewDto Row(string title) =>
            new(Guid.NewGuid(), Guid.NewGuid(), title, Ward, 1, ReviewStateDto.Pending, DateTimeOffset.UtcNow);

        // Unused in this test — the handler touches only the review-queue read.
        public Task<AssignmentRecipient?> GetRecipientAsync(Guid a, Guid c, CancellationToken ct = default) => throw new NotSupportedException();
        public void Add(AssignmentRecipient r) => throw new NotSupportedException();
        public void Update(AssignmentRecipient r) => throw new NotSupportedException();
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid g, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => throw new NotSupportedException();
        public void Add(GuardianSubmissionGate g) => throw new NotSupportedException();
        public void Update(GuardianSubmissionGate g) => throw new NotSupportedException();
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => throw new NotSupportedException();
        public void Add(AssignmentSubmission s) => throw new NotSupportedException();
        public void Update(AssignmentSubmission s) => throw new NotSupportedException();
        public void Add(SignatureEvent e) => throw new NotSupportedException();
        public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) => throw new NotSupportedException();
        public void Add(AssignmentSubmissionVersion v) => throw new NotSupportedException();
        public void Add(SubmissionReview r) => throw new NotSupportedException();
        public void Add(SubmissionAnswer a) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => throw new NotSupportedException();
    }

    // ── [P1-4] the empty scope is the containment ─────────────────────────

    [TestMethod]
    public async Task ReviewQueue_EmptyScope_ReturnsNothing_AndDoesNotTouchTheRepository()
    {
        var repository = new RecordingSubmissionRepository();
        var handler = new GetSubmissionsForReviewHandler(repository, NullLogger<GetSubmissionsForReviewHandler>.Instance);

        // The route's dev fallback threads a teacherId through; the scope is EMPTY because the
        // teacher-only principal carries no teacher_id claim.
        var rows = await handler.HandleAsync(new GetSubmissionsForReview(TeacherB, TeacherScope.Empty));

        rows.Should().BeEmpty(
            "[P1-4] a teacher without a teacher_id claim gets an empty queue — never the dev "
            + "teacherId fallback the route resolved, and never tenant-wide");
        repository.LegacyCalled.Should().BeFalse();
        repository.ReceivedScope.Should().BeNull("the repository is not consulted at all");
    }

    [TestMethod]
    public async Task ReviewQueue_ScopedCaller_PassesTheScopeToTheScopeAwareRead()
    {
        var repository = new RecordingSubmissionRepository();
        var handler = new GetSubmissionsForReviewHandler(repository, NullLogger<GetSubmissionsForReviewHandler>.Instance);
        var scope = TeacherScope.ForTeacher(TeacherA, [new TeacherSubjectGrade(GradeA, null, null)]);

        var rows = await handler.HandleAsync(new GetSubmissionsForReview(TeacherA, scope));

        rows.Should().ContainSingle().Which.AssignmentTitle.Should().Be("scoped");
        repository.ReceivedScope.Should().BeSameAs(scope);
    }

    [TestMethod]
    public async Task ReviewQueue_UnrestrictedCaller_KeepsTheOwnerOnlyRead()
    {
        var repository = new RecordingSubmissionRepository();
        var handler = new GetSubmissionsForReviewHandler(repository, NullLogger<GetSubmissionsForReviewHandler>.Instance);

        var rows = await handler.HandleAsync(new GetSubmissionsForReview(TeacherB, TeacherScope.Unrestricted));

        rows.Should().ContainSingle().Which.AssignmentTitle.Should().Be("legacy",
            "the unrestricted (dev/role-less/staff/admin) posture keeps the per-teacher owner filter");
        repository.LegacyCalled.Should().BeTrue();
    }

    // ── the repository's scope-aware read (own OR taught, never foreign) ──

    [TestMethod]
    public async Task ReviewQueue_ScopedRepositoryRead_ReturnsOwnAndTaught_NotForeign()
    {
        using var h = Build(nameof(ReviewQueue_ScopedRepositoryRead_ReturnsOwnAndTaught_NotForeign));

        var own = NewAssignment("own", TeacherA, GradeA);
        var taught = NewAssignment("taught", TeacherB, GradeA);
        var foreign = NewAssignment("foreign", TeacherB, null);
        h.Db.Assignments.AddRange(own, taught, foreign);
        h.Db.AssignmentSubmissions.AddRange(SubmissionFor(own.Id), SubmissionFor(taught.Id), SubmissionFor(foreign.Id));
        await h.Db.SaveChangesAsync();

        var scope = TeacherScope.ForTeacher(TeacherA, [new TeacherSubjectGrade(GradeA, null, null)]);

        var scoped = await h.Repository.ListSubmissionsForReviewAsync(TeacherA, scope, CancellationToken.None);

        scoped.Select(r => r.AssignmentId).Should().BeEquivalentTo(
            new[] { own.Id, taught.Id },
            "the queue covers the caller's own assignments AND the grade they teach — the foreign "
            + "assignment (a different, untaught grade) stays out");
    }

    [TestMethod]
    public async Task ReviewQueue_UnrestrictedRepositoryRead_KeepsTheOwnerFilter()
    {
        using var h = Build(nameof(ReviewQueue_UnrestrictedRepositoryRead_KeepsTheOwnerFilter));

        var own = NewAssignment("own", TeacherA, GradeA);
        var other = NewAssignment("other", TeacherB, GradeA);
        h.Db.Assignments.AddRange(own, other);
        h.Db.AssignmentSubmissions.AddRange(SubmissionFor(own.Id), SubmissionFor(other.Id));
        await h.Db.SaveChangesAsync();

        var unrestricted = await h.Repository.ListSubmissionsForReviewAsync(TeacherA, TeacherScope.Unrestricted, CancellationToken.None);
        var legacyShape = await h.Repository.ListSubmissionsForReviewAsync(TeacherA, CancellationToken.None);

        unrestricted.Select(r => r.AssignmentId).Should().BeEquivalentTo(new[] { own.Id });
        legacyShape.Select(r => r.AssignmentId).Should().BeEquivalentTo(new[] { own.Id },
            "the 2-arg read (existing callers and fakes) is unchanged");
    }

    // ── [P2-1]: the per-assignment read keeps its own shape ───────────────

    [TestMethod]
    public async Task ListSubmissionsByAssignment_KeepsReturningTheAssignmentsRows()
    {
        using var h = Build(nameof(ListSubmissionsByAssignment_KeepsReturningTheAssignmentsRows));

        var assignment = NewAssignment("own", TeacherA, GradeA);
        h.Db.Assignments.Add(assignment);
        h.Db.AssignmentSubmissions.Add(SubmissionFor(assignment.Id));
        await h.Db.SaveChangesAsync();

        var handler = new ListSubmissionsByAssignmentHandler(
            h.Repository, NullLogger<ListSubmissionsByAssignmentHandler>.Instance);

        var rows = await handler.HandleAsync(new ListSubmissionsByAssignment(assignment.Id));

        rows.Should().ContainSingle().Which.AssignmentId.Should().Be(assignment.Id,
            "the per-assignment read is gated at the route by the scope-aware detail read ([P2-2]), "
            + "so its own contract is unchanged");
    }
}
