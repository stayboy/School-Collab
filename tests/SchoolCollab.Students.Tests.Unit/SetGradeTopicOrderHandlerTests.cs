using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.SetGradeTopicOrder;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// AC2 (swap semantics on the subject bridge row's <c>DisplayOrder</c>), AC3
/// (duplicate orders normalised before the swap), AC4 (out-of-range → throws),
/// AC5 (unknown, other-tenant, activity-group, or non-effective assignment →
/// throws) and AC6 (row-version conflict → <see cref="ConcurrencyException"/>)
/// for the subjects reorder command.
/// </summary>
/// <remarks>
/// The command carries no grade id (the route is id-only), so the grade scope is
/// derived from the loaded, tenant-filtered row — which is exactly what the AC5
/// cases pin.
/// </remarks>
[TestClass]
public class SetGradeTopicOrderHandlerTests
{
    private static readonly Guid OtherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static SetGradeTopicOrderHandler NewHandler(StudentsTestScope s) =>
        new(s.GradeTopicAssignments, s.Cache, NullLogger<SetGradeTopicOrderHandler>.Instance);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Guid> SeedGradeAsync(StudentsTestScope s, string name)
    {
        var grade = GradeLevel.Create(Guid.NewGuid(), 1, name, 1);
        s.Db.GradeLevels.Add(grade);
        await s.Db.SaveChangesAsync();
        return grade.Id;
    }

    private static async Task<Guid> SeedTopicAsync(StudentsTestScope s, string code, string name, int topicOrder = 1)
    {
        var topic = Topic.Create(Guid.NewGuid(), code, name, topicOrder);
        s.Db.Topics.Add(topic);
        await s.Db.SaveChangesAsync();
        return topic.Id;
    }

    private static async Task<GradeTopicAssignment> SeedAssignmentAsync(
        StudentsTestScope s, Guid gradeId, Guid topicId, int order)
    {
        var assignment = GradeTopicAssignment.Create(
            gradeId, topicId, Today(), displayOrder: order);
        s.Db.GradeTopicAssignments.Add(assignment);
        await s.Db.SaveChangesAsync();
        return assignment;
    }

    private static async Task<Dictionary<Guid, int>> OrdersByAssignmentAsync(
        StudentsTestScope s, Guid gradeId) =>
        await s.Db.GradeTopicAssignments
            .Where(x => x.GradeLevelId == gradeId)
            .ToDictionaryAsync(x => x.Id, x => x.DisplayOrder);

    // ── AC2: swap ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Swap_MovesAssignmentToTargetPosition_DisplacedTakesMoverOldOrder()
    {
        using var s = new StudentsTestScope("stgo-swap");
        var gradeId = await SeedGradeAsync(s, "Grade 4");
        var mathId = await SeedTopicAsync(s, "MATH", "Mathematics", 1);
        var engId = await SeedTopicAsync(s, "ENG", "English", 2);
        var sciId = await SeedTopicAsync(s, "SCI", "Science", 3);
        var math = await SeedAssignmentAsync(s, gradeId, mathId, 0);
        var eng = await SeedAssignmentAsync(s, gradeId, engId, 1);
        var sci = await SeedAssignmentAsync(s, gradeId, sciId, 2);

        // Move the LAST subject to position 0 (the dialog's move-up path).
        await NewHandler(s).HandleAsync(new SetGradeTopicOrder(sci.Id, 0));

        var orders = await OrdersByAssignmentAsync(s, gradeId);
        orders[sci.Id].Should().Be(0, "the mover takes the requested position");
        orders[math.Id].Should().Be(2, "the displaced subject takes the mover's old position");
        orders[eng.Id].Should().Be(1, "no other row's order changes");
    }

    // ── AC3: normalisation before the swap ──────────────────────────────────

    [TestMethod]
    public async Task DuplicateOrders_AreNormalisedBeforeTheSwap_AndEndContiguous()
    {
        using var s = new StudentsTestScope("stgo-duplicates");
        var gradeId = await SeedGradeAsync(s, "Grade 5");
        var a = await SeedAssignmentAsync(s, gradeId, await SeedTopicAsync(s, "A", "Alpha"), 0);
        var b = await SeedAssignmentAsync(s, gradeId, await SeedTopicAsync(s, "B", "Beta"), 0);
        var c = await SeedAssignmentAsync(s, gradeId, await SeedTopicAsync(s, "C", "Gamma"), 0);

        await NewHandler(s).HandleAsync(new SetGradeTopicOrder(b.Id, 2));

        var orders = await OrdersByAssignmentAsync(s, gradeId);
        orders.Values.Should().BeEquivalentTo(
            new[] { 0, 1, 2 },
            "every duplicate order is normalised to a unique contiguous position");
        orders[b.Id].Should().Be(2, "the mover lands at the requested position");
        orders.Keys.Should().BeEquivalentTo(new[] { a.Id, b.Id, c.Id });
    }

    // ── AC4: out-of-range ───────────────────────────────────────────────────

    [TestMethod]
    public async Task OutOfRangeOrder_Throws_AndPersistsNothing()
    {
        using var s = new StudentsTestScope("stgo-out-of-range");
        var gradeId = await SeedGradeAsync(s, "Grade 6");
        var a = await SeedAssignmentAsync(s, gradeId, await SeedTopicAsync(s, "A", "Alpha"), 0);
        var b = await SeedAssignmentAsync(s, gradeId, await SeedTopicAsync(s, "B", "Beta"), 1);

        var tooHigh = () => NewHandler(s).HandleAsync(new SetGradeTopicOrder(a.Id, 2));
        await tooHigh.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var negative = () => NewHandler(s).HandleAsync(new SetGradeTopicOrder(a.Id, -1));
        await negative.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var orders = await OrdersByAssignmentAsync(s, gradeId);
        orders[a.Id].Should().Be(0, "a rejected position must not be persisted");
        orders[b.Id].Should().Be(1);
    }

    // ── AC5: unknown / other-tenant / activity-group / non-effective ────────

    [TestMethod]
    public async Task UnknownAssignmentId_ThrowsNotFound()
    {
        using var s = new StudentsTestScope("stgo-unknown");

        var act = () => NewHandler(s).HandleAsync(new SetGradeTopicOrder(Guid.NewGuid(), 0));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [TestMethod]
    public async Task AnotherTenantsAssignment_IsInvisible_AndThrowsNotFound()
    {
        using var s = new StudentsTestScope("stgo-cross-tenant");
        var gradeId = await SeedGradeAsync(s, "Grade 5");
        var topicId = await SeedTopicAsync(s, "MATH", "Mathematics");
        Guid foreignId;

        using (s.TenantAccessor.SuppressTenantGuard())
        {
            var foreign = GradeTopicAssignment.Create(gradeId, topicId, Today(), displayOrder: 0)
                .WithTenant(OtherTenantId);
            s.Db.GradeTopicAssignments.Add(foreign);
            await s.Db.SaveChangesAsync();
            foreignId = foreign.Id;
        }

        var act = () => NewHandler(s).HandleAsync(new SetGradeTopicOrder(foreignId, 0));

        await act.Should().ThrowAsync<InvalidOperationException>(
            "the id-only route derives its scope from the tenant-filtered row");
    }

    [TestMethod]
    public async Task ActivityGroupAssignmentId_ThrowsNotFound()
    {
        using var s = new StudentsTestScope("stgo-activity-group");
        var topicId = await SeedTopicAsync(s, "MATH", "Mathematics");
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        var groupAssignment = ActivityGroupTopicAssignment.Create(group.Id, topicId, Today());
        s.Db.ActivityGroupTopicAssignments.Add(groupAssignment);
        await s.Db.SaveChangesAsync();

        var act = () => NewHandler(s).HandleAsync(new SetGradeTopicOrder(groupAssignment.Id, 0));

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an activity-group assignment is not a grade subject assignment");
    }

    [TestMethod]
    public async Task AssignmentOutsideItsEffectiveWindow_ThrowsNotFound()
    {
        using var s = new StudentsTestScope("stgo-not-effective");
        var gradeId = await SeedGradeAsync(s, "Grade 5");
        var topicId = await SeedTopicAsync(s, "MATH", "Mathematics");
        // An assignment whose window closed yesterday: still a row, but not part of
        // the effective set the curriculum read lists.
        var ended = GradeTopicAssignment.Create(
            gradeId, topicId, Today().AddDays(-10), endDate: Today().AddDays(-1), displayOrder: 0);
        s.Db.GradeTopicAssignments.Add(ended);
        await s.Db.SaveChangesAsync();

        var act = () => NewHandler(s).HandleAsync(new SetGradeTopicOrder(ended.Id, 0));

        await act.Should().ThrowAsync<InvalidOperationException>(
            "the reorder bound is the effective set the grid lists, so an ended assignment is not reorderable");
    }

    // ── AC6: row-version conflict ───────────────────────────────────────────

    [TestMethod]
    public async Task RowVersionConflict_ThrowsConcurrencyException()
    {
        using var s = new StudentsTestScope("stgo-conflict");
        var gradeId = Guid.NewGuid();
        var assignment = GradeTopicAssignment.Create(gradeId, Guid.NewGuid(), Today(), displayOrder: 0);

        var repository = new Mock<IGradeTopicAssignmentRepository>();
        repository
            .Setup(r => r.GetAsync(assignment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignment);
        repository
            .Setup(r => r.ListByGradeLevelForUpdateAsync(
                gradeId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { assignment });
        repository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("row version mismatch"));

        var handler = new SetGradeTopicOrderHandler(
            repository.Object, s.Cache, NullLogger<SetGradeTopicOrderHandler>.Instance);

        var act = () => handler.HandleAsync(new SetGradeTopicOrder(assignment.Id, 0));

        await act.Should().ThrowAsync<ConcurrencyException>();
    }
}
