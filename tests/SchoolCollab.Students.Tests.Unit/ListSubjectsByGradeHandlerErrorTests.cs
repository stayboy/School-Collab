using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjectsByGrade;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Error-path coverage for <see cref="ListSubjectsByGradeHandler"/>. The
/// happy-path cases live in the same file's parent class
/// (<c>ListSubjectsByGradeHandlerTests</c>) — these are the boundary /
/// failure-mode pins the parent intentionally didn't pin.
///
/// Each test names the specific failure mode it locks down:
/// <list type="bullet">
///   <item><c>Cancellation_AlreadyCancelled_Throws_OperationCanceledException</c> —
///         the handler's only I/O is <c>ToArrayAsync(cancellationToken)</c>;
///         we pin that the token is actually wired through. Without this test
///         a future refactor could swallow the token and silently ignore
///         client disconnects.</item>
///   <item><c>DisposedDbContext_Throws_ObjectDisposedException</c> —
///         EF failures must propagate raw so the endpoint layer can map them
///         to a typed 500 response. If someone wraps the handler in a
///         swallowing try/catch, this test will catch it.</item>
///   <item><c>NonExistentGradeLevel_ReturnsEmpty_NotThrows</c> — pin the
///         0-row behaviour for an unknown <c>GradeLevelId</c>. The Subjects
///         landing depends on this returning <c>[]</c>, not throwing, when
///         a tenant filter masks the grade or the id has a typo.</item>
///   <item><c>PeriodIdSpecified_NoMatchingAssignment_ReturnsEmpty</c> — pin
///         the 0-row behaviour when an explicit <c>PeriodId</c> has no
///         assignments. Distinguishes "no assignments for this period" from
///         "no assignments ever".</item>
/// </list>
/// </summary>
[TestClass]
public class ListSubjectsByGradeHandlerErrorTests
{
    private static ListSubjectsByGradeHandler NewHandler(StudentsTestScope s) =>
        new(s.Db);

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s)
    {
        var gl = GradeLevel.Create(Guid.NewGuid(), 1, "Grade 1", 1);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    [TestMethod]
    public async Task Cancellation_AlreadyCancelled_Throws_OperationCanceledException()
    {
        // Arrange: an in-memory scope with a valid grade so the handler
        // *would* return rows if the token were ignored.
        using var s = new StudentsTestScope("subjects-cancel");
        var glId = await SeedGradeLevelAsync(s);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert: the pre-cancelled token must surface as
        // OperationCanceledException (or its TaskCanceledException subclass),
        // not be silently ignored.
        var act = async () =>
            await NewHandler(s).HandleAsync(new ListSubjectsByGrade(glId), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task DisposedDbContext_Throws_ObjectDisposedException()
    {
        // Arrange: build a scope, seed one row, then dispose the context.
        // The handler should not swallow the resulting ObjectDisposedException;
        // callers depend on that to return a typed 500 response.
        StudentsDbContext disposedDb;
        Guid glId;
        {
            using var s = new StudentsTestScope("subjects-disposed");
            glId = await SeedGradeLevelAsync(s);
            disposedDb = s.Db;
            // leaving 'using' disposes the context
        }

        // Act + Assert
        var act = async () =>
            await new ListSubjectsByGradeHandler(disposedDb)
                .HandleAsync(new ListSubjectsByGrade(glId), CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>(
            "EF errors must propagate raw so the endpoint layer can map them");
    }

    [TestMethod]
    public async Task NonExistentGradeLevel_ReturnsEmpty_NotThrows()
    {
        // Arrange: a scope with NO assignments for the requested grade id.
        // The handler must return [] instead of throwing — the Subjects
        // landing depends on this for "grade selected but no subjects yet".
        using var s = new StudentsTestScope("subjects-nonexistent-grade");
        var unknownGradeId = Guid.NewGuid();

        // Act
        var result = await NewHandler(s).HandleAsync(
            new ListSubjectsByGrade(unknownGradeId),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull("the handler must return a non-null array even for an unknown grade");
        result.Should().BeEmpty("no assignments exist for this grade id");
    }

    [TestMethod]
    public async Task PeriodIdSpecified_NoMatchingAssignment_ReturnsEmpty()
    {
        // Arrange: assignments exist for the grade but NOT for the explicit
        // periodId. The handler must distinguish "no assignment for THIS
        // period" from "no assignment ever" by returning [].
        using var s = new StudentsTestScope("subjects-period-mismatch");
        var glId = await SeedGradeLevelAsync(s);
        await SeedSubjectAndAssignmentAsync(s, glId);

        var explicitPeriodId = Guid.NewGuid(); // different from the seeded assignment's period

        // Act
        var result = await NewHandler(s).HandleAsync(
            new ListSubjectsByGrade(glId, explicitPeriodId),
            CancellationToken.None);

        // Assert
        result.Should().BeEmpty(
            "an explicit PeriodId must filter to ONLY assignments in that period");
    }

    // Helper: seed a current-period subject assignment so the harness has
    // something to filter against.
    private static async Task<Guid> SeedSubjectAndAssignmentAsync(StudentsTestScope s, Guid glId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var period = Period.Create("Term 1", today.AddDays(-1), today.AddDays(1));
        s.Db.Periods.Add(period);
        await s.Db.SaveChangesAsync();

        var subject = Subject.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        s.Db.Subjects.Add(subject);
        await s.Db.SaveChangesAsync();

        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(glId, subject.Id, period.Id));
        await s.Db.SaveChangesAsync();
        return subject.Id;
    }
}