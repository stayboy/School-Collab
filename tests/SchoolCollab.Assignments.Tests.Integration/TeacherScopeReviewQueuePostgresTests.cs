using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// Round <c>teacher-scope-auth</c> D3 — the scope-aware review queue on REAL Postgres/Npgsql.
///
/// <para>The in-memory provider evaluates the whole expression tree locally, which is exactly how an
/// untranslatable read survives a unit suite. The scope-aware queue adds one construct that MUST
/// translate: the visible-assignment id set is filtered in memory (a null-subject-aware predicate
/// over the tenant's assignment keys) and then used as a parameterized <c>Contains</c> against
/// <c>assignment_submissions.assignment_id</c>. This test proves the translation and the
/// own-OR-taught / never-foreign semantics on the real provider.</para>
/// </summary>
[TestClass]
public sealed class TeacherScopeReviewQueuePostgresTests
{
    private static readonly Guid TenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TeacherA = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid TeacherB = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    private static readonly Guid GradeA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid SubjectArt = Guid.Parse("00000000-0000-0000-0000-000000000042");
    private static readonly Guid Ward = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    [TestMethod]
    public async Task ScopeAwareReviewQueue_TranslatesAndGates_OnRealPostgres()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));

        Guid ownId, taughtId, foreignId;
        await using (var seed = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId))
        {
            var own = NewAssignment("own", TeacherA, GradeA);
            var taught = NewAssignment("taught-grade", TeacherB, GradeA);
            var foreign = NewAssignment("foreign-grade", TeacherB, null);
            seed.Assignments.AddRange(own, taught, foreign);
            await seed.SaveChangesAsync();

            seed.AssignmentSubmissions.AddRange(
                AssignmentSubmission.Create(TenantId, own.Id, Ward, null),
                AssignmentSubmission.Create(TenantId, taught.Id, Ward, null),
                AssignmentSubmission.Create(TenantId, foreign.Id, Ward, null));
            await seed.SaveChangesAsync();

            ownId = own.Id;
            taughtId = taught.Id;
            foreignId = foreign.Id;
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId);
        var repository = new SubmissionRepository(read);

        var scope = TeacherScope.ForTeacher(TeacherA, [new TeacherSubjectGrade(GradeA, TopicId: null, RoleCodedValueId: null)]);

        var scoped = await repository.ListSubmissionsForReviewAsync(TeacherA, scope);
        scoped.Select(r => r.AssignmentId).Should().BeEquivalentTo(
            new[] { ownId, taughtId },
            "on real Postgres the queue covers the caller's own assignment AND the grade they teach, "
            + "and never the untaught foreign one");

        var unrestricted = await repository.ListSubmissionsForReviewAsync(TeacherA, TeacherScope.Unrestricted);
        unrestricted.Select(r => r.AssignmentId).Should().BeEquivalentTo(
            new[] { ownId },
            "the unrestricted posture keeps the owner-only read (the dev/role-less/staff/admin shape)");
    }

    /// <summary>The tenant-scoped context: the read is filtered by the global query filter as well as
    /// by the explicit tenant id, matching the runtime path. Round <c>drop-primary-grade</c>: the row's
    /// grade scope is its authored grade TARGETS, so each grade is attached as a <c>GradeLevel</c>
    /// target row (a null grade = no grade target).</summary>
    private static Assignment NewAssignment(string title, Guid creator, Guid? gradeLevelId)
    {
        var assignment = Assignment.Create(title, null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, SubjectArt, null, null, creator)
            .WithTenant(TenantId);

        if (gradeLevelId is Guid gradeId)
        {
            assignment.SetTargets([(TargetKind.GradeLevel, (Guid?)gradeId)], TenantId);
        }

        return assignment;
    }
}
