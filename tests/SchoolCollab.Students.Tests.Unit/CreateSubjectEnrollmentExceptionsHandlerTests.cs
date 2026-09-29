using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.CreateSubjectEnrollmentExceptions;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// <see cref="CreateSubjectEnrollmentExceptionsHandler"/> — the v6 bulk write
/// (subject-period-exception-model.md §11.3, decision 18): ONE ROW PER ITEM, written in ONE
/// transaction, with the same rule vocabulary the single-create path uses (422 for a bad shape or
/// ordinal, 404 for an unknown owner or topic, 409 for a duplicate span).
///
/// <para>What this class exists to prove, beyond "it writes two rows":</para>
/// <list type="bullet">
/// <item><b>The batch is atomic.</b> A rejection anywhere — a later item's missing bound, a later
/// item breaching a group's window — leaves the table EMPTY. That is what <c>AddRangeAsync</c>'s
/// single save buys, and it is asserted rather than assumed.</item>
/// <item><b>The intra-batch duplicate check is load-bearing.</b> Two items covering the same span
/// must be a 409 rather than a raw <c>23505</c>: the database pre-check cannot see rows this
/// transaction has not saved, so without the in-memory check both would pass it and collide at
/// save time. <see cref="Bulk_IntraBatchDuplicateSpan_Throws_AndWritesNothing"/> is the test that
/// would fail if that check were removed.</item>
/// <item><b>The ordinal is descriptive, not part of the key.</b> Two items with the SAME span and
/// DIFFERENT ordinals are still one span, so they are a duplicate (§0 decision 15).</item>
/// </list>
///
/// <para>The seed helpers mirror the single-create suite's
/// (<c>CreateSubjectEnrollmentExceptionHandlerTests</c>) deliberately: the two classes test
/// sibling handlers over the same aggregate, and a shared seeding type was not worth introducing
/// for three small methods.</para>
/// </summary>
[TestClass]
public class CreateSubjectEnrollmentExceptionsHandlerTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    /// <summary>The topic row every successful create references.</summary>
    private Guid _topicId;

    private static CreateSubjectEnrollmentExceptionsHandler NewHandler(StudentsTestScope s) => new(
        new SubjectEnrollmentExceptionRepository(s.Db),
        s.GradeLevels,
        s.ActivityGroups,
        s.Topics,
        s.Tenants,
        s.Cache,
        NullLogger<CreateSubjectEnrollmentExceptionsHandler>.Instance);

    private async Task SeedCanonicalTopicAsync(StudentsTestScope s)
    {
        var topic = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        s.Db.Topics.Add(topic);
        await s.Db.SaveChangesAsync();
        _topicId = topic.Id;
    }

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s)
    {
        var gradeLevel = GradeLevel.Create(Guid.NewGuid(), 1, "Grade 1", 1);
        s.Db.GradeLevels.Add(gradeLevel);
        await s.Db.SaveChangesAsync();
        return gradeLevel.Id;
    }

    private static async Task<ActivityGroup> SeedGroupAsync(
        StudentsTestScope s, EnrollmentSpan span, DateOnly? windowStart = null, DateOnly? windowEnd = null)
    {
        var group = ActivityGroup.Create(
            span + " Club", span: span, enrollmentStartDate: windowStart, enrollmentEndDate: windowEnd);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        return group;
    }

    // ── the happy path: N items, N rows, one write ────────────────────────────

    [TestMethod]
    public async Task Bulk_WritesOneRowPerItem_AndReturnsTheirIds()
    {
        using var s = new StudentsTestScope("bulk-writes-per-item");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);

        var ids = await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, _topicId, AcademicYearDivision.Terms,
            [
                new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31), Ordinal: 1),
                new(StartDate: D(2027, 9, 1), EndDate: D(2027, 12, 20), Ordinal: 3),
            ]));

        ids.Should().HaveCount(2, "one row per chosen sequence");

        var rows = await s.Db.SubjectEnrollmentExceptions.OrderBy(e => e.Ordinal).ToListAsync();
        rows.Should().HaveCount(2, "and both of them are persisted");
        rows[0].Ordinal.Should().Be(1);
        rows[0].StartDate.Should().Be(D(2027, 1, 1), "each sequence keeps its OWN span, not a shared one");
        rows[1].Ordinal.Should().Be(3);
        rows[1].StartDate.Should().Be(D(2027, 9, 1));
    }

    /// <summary>
    /// The regression tie to v5: a one-item batch IS the single-create write, so a page that
    /// always posts to the bulk route (it does) has not changed what a lone sequence means.
    /// </summary>
    [TestMethod]
    public async Task Bulk_SingleItem_IsTheSameWriteAsTheSingleCreate()
    {
        using var s = new StudentsTestScope("bulk-single-item");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);

        var ids = await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, _topicId, AcademicYearDivision.Terms,
            [new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31), Ordinal: 2)],
            Reason: "Staffing"));

        ids.Should().ContainSingle();

        var row = await s.Db.SubjectEnrollmentExceptions.SingleAsync();
        row.Ordinal.Should().Be(2);
        row.Reason.Should().Be("Staffing", "the reason belongs to the action, so every row carries it");
    }

    // ── atomicity: a rejection anywhere writes NOTHING ────────────────────────

    [TestMethod]
    public async Task Bulk_IntraBatchDuplicateSpan_Throws_AndWritesNothing()
    {
        // The load-bearing one. Both items cover the same span, so the SECOND is a duplicate of the
        // first — and the database pre-check cannot see the first, because it is not saved yet. The
        // in-memory check is what turns a raw 23505 at save time into the 409 the caller can act on.
        using var s = new StudentsTestScope("bulk-intra-duplicate");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, _topicId, AcademicYearDivision.Terms,
            [
                new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31), Ordinal: 1),
                new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31), Ordinal: 3),
            ]));

        await act.Should().ThrowAsync<DuplicateSubjectEnrollmentException>();
        s.Db.SubjectEnrollmentExceptions.Should().BeEmpty("the batch is all-or-nothing");
    }

    [TestMethod]
    public async Task Bulk_DuplicateAgainstAnExistingRow_Throws_AndWritesNothing()
    {
        using var s = new StudentsTestScope("bulk-existing-duplicate");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);
        var handler = NewHandler(s);

        await handler.HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, _topicId, AcademicYearDivision.Terms,
            [new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31), Ordinal: 1)]));

        // The batch's second item collides with the row above, so the batch adds nothing.
        var act = async () => await handler.HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, _topicId, AcademicYearDivision.Terms,
            [
                new(StartDate: D(2027, 9, 1), EndDate: D(2027, 12, 20), Ordinal: 3),
                new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31), Ordinal: 2),
            ]));

        await act.Should().ThrowAsync<DuplicateSubjectEnrollmentException>();
        s.Db.SubjectEnrollmentExceptions.Should().HaveCount(1, "the first write survives; the batch adds none");
    }

    [TestMethod]
    public async Task Bulk_SameItemBoundaryOnlySpan_WithDifferentOrdinals_IsStillADuplicate()
    {
        // §0 decision 15's second half, at batch scale: the ordinal is DESCRIPTIVE and not part of
        // the duplicate key, so two sequences claiming different positions over the same span are
        // the same row — 409, not two rows.
        using var s = new StudentsTestScope("bulk-ordinal-not-in-key");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, _topicId, AcademicYearDivision.Terms,
            [
                new(StartDate: null, EndDate: D(2027, 3, 31), Ordinal: 1),
                new(StartDate: null, EndDate: D(2027, 3, 31), Ordinal: 4),
            ]));

        await act.Should().ThrowAsync<DuplicateSubjectEnrollmentException>();
        s.Db.SubjectEnrollmentExceptions.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Bulk_EmptyBatch_Throws422_AndWritesNothing()
    {
        // An empty batch would "succeed" while writing nothing, which reads as a successful write to
        // every caller. It is a caller error, so it is a 422 like the rest of the shape rules.
        using var s = new StudentsTestScope("bulk-empty");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, _topicId, AcademicYearDivision.Terms, []));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("at least one item");
        s.Db.SubjectEnrollmentExceptions.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Bulk_OneItemWithoutABound_Throws422_AndWritesNothing()
    {
        // The per-item shape rule, and the atomicity proof: the FIRST item is perfectly valid and
        // the second is not, so nothing at all may be written.
        using var s = new StudentsTestScope("bulk-item-no-bound");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, _topicId, AcademicYearDivision.Terms,
            [
                new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31), Ordinal: 1),
                new(Ordinal: 3),
            ]));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("at least one bound");
        s.Db.SubjectEnrollmentExceptions.Should().BeEmpty("a valid first item does not survive a bad second");
    }

    [TestMethod]
    public async Task Bulk_OrdinalOnAFreeWindow_Throws422()
    {
        // The ordinal is only meaningful on a real part; the server rejects one on a free window.
        using var s = new StudentsTestScope("bulk-ordinal-free-window");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, _topicId, AcademicYearDivision.None,
            [new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31), Ordinal: 1)]));

        await act.Should().ThrowAsync<TopicAssignmentPeriodException>();
        s.Db.SubjectEnrollmentExceptions.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Bulk_UndefinedDivision_Throws422()
    {
        using var s = new StudentsTestScope("bulk-undefined-division");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, _topicId, (AcademicYearDivision)42,
            [new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31))]));

        await act.Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    [TestMethod]
    public async Task Bulk_WithBothOwners_Throws422()
    {
        using var s = new StudentsTestScope("bulk-both-owners");
        var gradeId = await SeedGradeLevelAsync(s);
        var group = await SeedGroupAsync(s, EnrollmentSpan.WholeAcademicYear);
        await SeedCanonicalTopicAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, group.Id, _topicId, AcademicYearDivision.None,
            [new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31))]));

        await act.Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    [TestMethod]
    public async Task Bulk_UnknownTopic_ThrowsTopicNotFound()
    {
        using var s = new StudentsTestScope("bulk-unknown-topic");
        var gradeId = await SeedGradeLevelAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            gradeId, null, Guid.NewGuid(), AcademicYearDivision.None,
            [new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31))]));

        await act.Should().ThrowAsync<TopicNotFoundException>();
        s.Db.SubjectEnrollmentExceptions.Should().BeEmpty();
    }

    // ── the group-owned per-item window check ─────────────────────────────────

    [TestMethod]
    public async Task Bulk_GroupOwned_ChecksEveryItemsSpanAgainstTheWindow()
    {
        // ValidateGroupExceptionDivisionAsync is SPAN-dependent, so it has to run per item: the
        // first item sits inside the group's enrollment window and the second starts before it. A
        // handler that checked only the first item's span would let this through.
        using var s = new StudentsTestScope("bulk-group-window");
        var group = await SeedGroupAsync(
            s, EnrollmentSpan.DateRange, windowStart: D(2026, 9, 1), windowEnd: D(2027, 8, 31));
        await SeedCanonicalTopicAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            null, group.Id, _topicId, AcademicYearDivision.None,
            [
                new(StartDate: D(2027, 1, 1), EndDate: D(2027, 3, 31)),
                new(StartDate: D(2026, 1, 1), EndDate: D(2026, 3, 31)),
            ]));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("window");
        s.Db.SubjectEnrollmentExceptions.Should().BeEmpty("the batch is all-or-nothing");
    }

    [TestMethod]
    public async Task Bulk_GroupOwned_EverySpanInsideTheWindow_IsAllowed()
    {
        using var s = new StudentsTestScope("bulk-group-window-ok");
        var group = await SeedGroupAsync(
            s, EnrollmentSpan.DateRange, windowStart: D(2026, 9, 1), windowEnd: D(2027, 8, 31));
        await SeedCanonicalTopicAsync(s);

        var ids = await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentExceptions(
            null, group.Id, _topicId, AcademicYearDivision.None,
            [
                new(StartDate: D(2026, 9, 1), EndDate: D(2026, 12, 20)),
                new(StartDate: D(2027, 1, 10), EndDate: D(2027, 8, 31)),
            ]));

        ids.Should().HaveCount(2);
    }
}
