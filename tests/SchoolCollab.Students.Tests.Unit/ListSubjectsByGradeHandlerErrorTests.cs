using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicsByGrade;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Error-path coverage for <see cref="ListTopicsByGradeHandler"/>. The
/// happy-path cases live in the same file's parent class
/// (<c>ListTopicsByGradeHandlerTests</c>) — these are the boundary /
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
///         0-row behaviour for an unknown <c>GradeLevelId</c>. The Topics
///         landing depends on this returning <c>[]</c>, not throwing, when
///         a tenant filter masks the grade or the id has a typo.</item>
///   <item><c>PeriodIdSpecified_NoMatchingAssignment_ReturnsEmpty</c> — pin
///         the 0-row behaviour when an explicit <c>PeriodId</c> has no
///         assignments. Distinguishes "no assignments for this period" from
///         "no assignments ever".</item>
/// </list>
/// </summary>
[TestClass]
public class ListTopicsByGradeHandlerErrorTests
{
    private static ListTopicsByGradeHandler NewHandler(StudentsTestScope s) =>
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
            await NewHandler(s).HandleAsync(new ListTopicsByGrade(glId), cts.Token);
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
            await new ListTopicsByGradeHandler(disposedDb)
                .HandleAsync(new ListTopicsByGrade(glId), CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>(
            "EF errors must propagate raw so the endpoint layer can map them");
    }

    [TestMethod]
    public async Task NonExistentGradeLevel_ReturnsEmpty_NotThrows()
    {
        // Arrange: a scope with NO assignments for the requested grade id.
        // The handler must return [] instead of throwing — the Topics
        // landing depends on this for "grade selected but no subjects yet".
        using var s = new StudentsTestScope("subjects-nonexistent-grade");
        var unknownGradeId = Guid.NewGuid();

        // Act
        var result = await NewHandler(s).HandleAsync(
            new ListTopicsByGrade(unknownGradeId),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull("the handler must return a non-null array even for an unknown grade");
        result.Should().BeEmpty("no assignments exist for this grade id");
    }

    [TestMethod]
    public async Task EffectiveDateSpecified_NoMatchingAssignment_ReturnsEmpty()
    {
        // Arrange: assignments exist for the grade but NOT for the explicit
        // effectiveDate. The handler must distinguish "no assignment for THIS
        // date" from "no assignment ever" by returning [].
        using var s = new StudentsTestScope("subjects-period-mismatch");
        var glId = await SeedGradeLevelAsync(s);
        await SeedBoundedTopicAndAssignmentAsync(s, glId);

        // A bounded assignment (EndDate set = blocked/archived) is NOT effective
        // on a date after its end.
        var explicitDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(100);

        // Act
        var result = await NewHandler(s).HandleAsync(
            new ListTopicsByGrade(glId, explicitDate),
            CancellationToken.None);

        // Assert
        result.Should().BeEmpty(
            "an explicit effectiveDate must filter to ONLY assignments effective on that date");
    }

    // Helper: seed a bounded (blocked/archived) date-based subject assignment so
    // the harness has something the far-future effectiveDate excludes.
    private static async Task<Guid> SeedBoundedTopicAndAssignmentAsync(StudentsTestScope s, Guid glId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var topic = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        s.Db.Topics.Add(topic);
        await s.Db.SaveChangesAsync();

        s.Db.GradeTopicAssignments.Add(
            GradeTopicAssignment.Create(glId, topic.Id, today.AddDays(-30), today));
        await s.Db.SaveChangesAsync();
        return topic.Id;
    }
}