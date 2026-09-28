using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.RemoveSubjectEnrollmentException;
using SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Queries.ListSubjectEnrollmentExceptions;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListActivityGroupTopicAssignments;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicAssignments;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicCurriculumByGrade;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Availability semantics of the exception model (subject-period-exception-model.md v3
/// §2.3): <c>available = EXISTS bridge AND NOT EXISTS exception containing date</c> —
/// ONE date test, with a null bound meaning open on that side. No active period is
/// resolved anywhere (RISK-2 is dissolved, §0 decision 9), so nothing here seeds a
/// period.
///
/// <para>Mutation note (M1 → AC-1/AC-5/AC-8): deleting the
/// <c>!exceptedTopicIds.Contains(...)</c> filter from
/// <c>ListGradeTopicAssignmentsHandler</c> /
/// <c>ListActivityGroupTopicAssignmentsHandler</c>
/// (or the date predicate in <c>SubjectAvailability</c>) must fail the
/// <c>*_IsNotAvailable</c> tests below. <b>Not</b>
/// <c>ListGradeTopicCurriculumByGradeHandler</c>: that reader is the management
/// surface and deliberately carries no exception filter (v3 §5.2 rows 3-4/AC-9,
/// parent ruling 2026-09-26), so the curriculum assertions below assert the
/// <i>opposite</i> — an excepted subject stays listed there.</para>
///
/// <para><b>Retired v1 tests and their replacements.</b> The RISK-2 set —
/// <c>GradeTopic_BlockedOnActiveAcademicYear_IsNotAvailable</c>,
/// <c>GradeTopic_BlockedOnActiveSubPeriod_IsNotAvailable</c>,
/// <c>GradeTopic_BlockedOnANonActivePeriod_IsStillAvailable</c>,
/// <c>GroupTopic_BlockedOnActiveSubPeriod_IsNotAvailable</c> — is replaced by
/// <c>GradeTopic_SpanContainingTheDate_IsNotAvailable_WithNoActivePeriodAtAll</c>
/// (AC-5: the predicate no longer consults a period, so it excludes the subject even
/// when the tenant has no active period whatsoever — pre-round it did not).
/// <c>BlockOnArchivedPeriod_StaysListable_AndDeletable</c> retires with the period
/// reference (a span cannot be archived); its surviving intent — an exception stays
/// listable and removable — lives in <c>ListedException_CarriesItsDivisionAndSpan_AndIsRemovable</c>
/// here, plus the re-add case in the handler and integration suites.</para>
/// </summary>
[TestClass]
public class SubjectAvailabilityTests
{
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static ListGradeTopicAssignmentsHandler NewGradeHandler(StudentsTestScope s) => new(s.Db, s.Cache);

    private static ListActivityGroupTopicAssignmentsHandler NewGroupHandler(StudentsTestScope s) => new(s.Db, s.Cache);

    private static ListGradeTopicCurriculumByGradeHandler NewCurriculumHandler(StudentsTestScope s) => new(s.Db, s.Cache);

    private static async Task<(Guid gradeId, Guid topicId)> SeedGradeAndTopicAsync(StudentsTestScope s)
    {
        var gradeLevel = GradeLevel.Create(Guid.NewGuid(), 1, "Grade 1", 1);
        s.Db.GradeLevels.Add(gradeLevel);
        var topic = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        s.Db.Topics.Add(topic);
        await s.Db.SaveChangesAsync();
        return (gradeLevel.Id, topic.Id);
    }

    private static async Task SeedGradeBridgeAsync(StudentsTestScope s, Guid gradeId, Guid topicId, DateOnly start)
    {
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(gradeId, topicId, start));
        await s.Db.SaveChangesAsync();
    }

    private static async Task SeedGroupBridgeAsync(StudentsTestScope s, Guid groupId, Guid topicId, DateOnly start)
    {
        s.Db.ActivityGroupTopicAssignments.Add(ActivityGroupTopicAssignment.Create(groupId, topicId, start));
        await s.Db.SaveChangesAsync();
    }

    private static SubjectEnrollmentException SeedGradeException(
        StudentsTestScope s, Guid gradeId, Guid topicId, DateOnly? start, DateOnly? end,
        AcademicYearDivision division = AcademicYearDivision.None, string? reason = null)
    {
        var exception = SubjectEnrollmentException.Create(
            s.Tenants.GetTenantContext().TenantId, gradeId, null, topicId, division, start, end, reason);
        s.Db.SubjectEnrollmentExceptions.Add(exception);
        return exception;
    }

    private static SubjectEnrollmentException SeedGroupException(
        StudentsTestScope s, Guid groupId, Guid topicId, DateOnly? start, DateOnly? end)
    {
        var exception = SubjectEnrollmentException.Create(
            s.Tenants.GetTenantContext().TenantId, null, groupId, topicId, AcademicYearDivision.None, start, end);
        s.Db.SubjectEnrollmentExceptions.Add(exception);
        return exception;
    }

    private static async Task<Guid[]> GradeAvailabilityAsync(StudentsTestScope s, Guid gradeId) =>
        (await NewGradeHandler(s).HandleAsync(new ListGradeTopicAssignments(gradeId, Today())))
        .Select(a => a.TopicId)
        .ToArray();

    // ── The three availability cases (AC-1) ───────────────────────────────────

    [TestMethod]
    public async Task GradeTopic_BridgeExists_NoException_IsAvailable()
    {
        using var s = new StudentsTestScope("avail-bridge-only");
        var (gradeId, topicId) = await SeedGradeAndTopicAsync(s);
        await SeedGradeBridgeAsync(s, gradeId, topicId, Today());

        (await GradeAvailabilityAsync(s, gradeId)).Should().Contain(topicId);
    }

    [TestMethod]
    public async Task GradeTopic_SpanContainingTheDate_IsNotAvailable()
    {
        using var s = new StudentsTestScope("avail-containing");
        var (gradeId, topicId) = await SeedGradeAndTopicAsync(s);
        await SeedGradeBridgeAsync(s, gradeId, topicId, Today());
        SeedGradeException(s, gradeId, topicId, Today().AddDays(-10), Today().AddDays(10), reason: "teacher on leave");
        await s.Db.SaveChangesAsync();

        (await GradeAvailabilityAsync(s, gradeId)).Should().BeEmpty("M1: a span containing the date hides the subject");
        (await NewCurriculumHandler(s)
            .HandleAsync(new ListGradeTopicCurriculumByGrade(gradeId, Today())))
            .Select(x => x.TopicId).Should().Contain(topicId,
                "the curriculum reader is the management surface (spec §5.2 rows 3-4/AC-9) and is deliberately NOT filtered by exceptions — " +
                "the grade-detail card and the View-all dialog need the subject listed to show its badge and kebab");
    }

    [TestMethod]
    public async Task GradeTopic_SpanNotContainingTheDate_IsStillAvailable()
    {
        using var s = new StudentsTestScope("avail-not-containing");
        var (gradeId, topicId) = await SeedGradeAndTopicAsync(s);
        await SeedGradeBridgeAsync(s, gradeId, topicId, Today());
        SeedGradeException(s, gradeId, topicId, Today().AddDays(10), Today().AddDays(20));
        await s.Db.SaveChangesAsync();

        (await GradeAvailabilityAsync(s, gradeId)).Should().Contain(topicId,
            "a span that does not contain the effective date leaves the subject available");
    }

    [TestMethod]
    public async Task GradeTopic_OpenStart_MatchesAnyEarlierDate()
    {
        using var s = new StudentsTestScope("avail-open-start");
        var (gradeId, topicId) = await SeedGradeAndTopicAsync(s);
        await SeedGradeBridgeAsync(s, gradeId, topicId, Today());
        SeedGradeException(s, gradeId, topicId, start: null, end: Today().AddDays(5));
        await s.Db.SaveChangesAsync();

        (await GradeAvailabilityAsync(s, gradeId)).Should().BeEmpty("an open start excepts every earlier date");
    }

    [TestMethod]
    public async Task GradeTopic_OpenEnd_MatchesAnyLaterDate()
    {
        using var s = new StudentsTestScope("avail-open-end");
        var (gradeId, topicId) = await SeedGradeAndTopicAsync(s);
        await SeedGradeBridgeAsync(s, gradeId, topicId, Today());
        SeedGradeException(s, gradeId, topicId, Today().AddDays(-5), end: null);
        await s.Db.SaveChangesAsync();

        (await GradeAvailabilityAsync(s, gradeId)).Should().BeEmpty("an open end excepts every later date");
    }

    [TestMethod]
    public async Task GradeTopic_NoBridge_IsNotAvailable_RegardlessOfExceptions()
    {
        using var s = new StudentsTestScope("avail-no-bridge");
        var (gradeId, topicId) = await SeedGradeAndTopicAsync(s);

        (await GradeAvailabilityAsync(s, gradeId)).Should().BeEmpty("no bridge = not available");

        SeedGradeException(s, gradeId, topicId, Today().AddDays(-1), Today().AddDays(1));
        await s.Db.SaveChangesAsync();
        await s.Cache.RemoveByTagAsync("students");

        (await GradeAvailabilityAsync(s, gradeId)).Should().BeEmpty("still not available, with or without an exception");
    }

    [TestMethod]
    public async Task GroupTopic_SpanContainingTheDate_IsNotAvailable()
    {
        using var s = new StudentsTestScope("avail-group-containing");
        var (_, topicId) = await SeedGradeAndTopicAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();

        await SeedGroupBridgeAsync(s, group.Id, topicId, Today());
        SeedGroupException(s, group.Id, topicId, Today().AddDays(-3), Today().AddDays(3));
        await s.Db.SaveChangesAsync();

        var result = await NewGroupHandler(s)
            .HandleAsync(new ListActivityGroupTopicAssignments(group.Id, Today()));

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GradeTopic_AfterRemove_IsAvailableAgain()
    {
        using var s = new StudentsTestScope("avail-remove");
        var (gradeId, topicId) = await SeedGradeAndTopicAsync(s);
        await SeedGradeBridgeAsync(s, gradeId, topicId, Today());
        var exception = SeedGradeException(s, gradeId, topicId, Today().AddDays(-1), Today().AddDays(1));
        await s.Db.SaveChangesAsync();

        (await GradeAvailabilityAsync(s, gradeId)).Should().BeEmpty();

        await new RemoveSubjectEnrollmentExceptionHandler(
                new SubjectEnrollmentExceptionRepository(s.Db), s.Cache,
                NullLogger<RemoveSubjectEnrollmentExceptionHandler>.Instance)
            .HandleAsync(new RemoveSubjectEnrollmentException(exception.Id));

        (await GradeAvailabilityAsync(s, gradeId)).Should().Contain(topicId);
    }

    // ── AC-5: RISK-2 dissolved — no active period is consulted at all ─────────

    [TestMethod]
    public async Task GradeTopic_SpanContainingTheDate_IsNotAvailable_WithNoActivePeriodAtAll()
    {
        // AC-5's replacement for the four retired RISK-2 tests. The tenant has NO
        // periods at all — no active academic year, no active sub-period — and the
        // subject is still excluded, because the predicate is a date test and never
        // resolves a period id. Pre-round v1's predicate resolved an active period id
        // set, returned [] without one, and therefore did NOT exclude the subject.
        using var s = new StudentsTestScope("avail-no-active-period");
        var (gradeId, topicId) = await SeedGradeAndTopicAsync(s);
        await SeedGradeBridgeAsync(s, gradeId, topicId, Today());
        SeedGradeException(s, gradeId, topicId, Today().AddDays(-10), Today().AddDays(10));
        await s.Db.SaveChangesAsync();

        s.Db.Periods.Should().BeEmpty("this test's whole point: there is no period state to resolve");

        (await GradeAvailabilityAsync(s, gradeId)).Should().BeEmpty(
            "M1/AC-5: a span-shaped exception excludes the subject with no active period at all");
    }

    [TestMethod]
    public async Task GroupTopic_SpanContainingTheDate_IsNotAvailable_WithNoActivePeriodAtAll()
    {
        using var s = new StudentsTestScope("avail-group-no-active-period");
        var (_, topicId) = await SeedGradeAndTopicAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        await SeedGroupBridgeAsync(s, group.Id, topicId, Today());
        SeedGroupException(s, group.Id, topicId, Today().AddDays(-10), Today().AddDays(10));
        await s.Db.SaveChangesAsync();

        (await NewGroupHandler(s).HandleAsync(new ListActivityGroupTopicAssignments(group.Id, Today())))
            .Should().BeEmpty("the group path applies the same date test with no period lookup");
    }

    // ── AC-19: the v1 rollover regression guard, re-expressed ────────────────

    [TestMethod]
    public async Task GradeTopic_WithNoExceptionAfterYearRollover_StaysAvailable()
    {
        // GUARD (AC-19 — honestly labelled): it passes pre-round too, and is now
        // structurally trivial because no period instance is attached to anything. It is
        // pinned anyway, because the rewrite must not lose §9's intent: year rollover is
        // a no-op for availability.
        using var s = new StudentsTestScope("avail-rollover");
        var (gradeId, topicId) = await SeedGradeAndTopicAsync(s);
        await SeedGradeBridgeAsync(s, gradeId, topicId, Today());

        var oldYear = Period.Create("AY2025", D(2025, 9, 1), D(2026, 8, 31), AcademicYearDivision.Terms);
        var oldTerm = Period.Create("T1", D(2025, 9, 1), D(2025, 12, 31), AcademicYearDivision.Terms, oldYear.Id);
        s.Db.Periods.Add(oldYear);
        s.Db.Periods.Add(oldTerm);
        await s.Db.SaveChangesAsync();
        oldYear.Activate();
        oldTerm.Activate();
        oldTerm.Archive();
        oldYear.Archive();
        await s.Db.SaveChangesAsync();

        (await GradeAvailabilityAsync(s, gradeId)).Should().Contain(topicId,
            "AC-19: rollover is a no-op — nothing on the subject↔grade link names a period");
        (await NewCurriculumHandler(s)
            .HandleAsync(new ListGradeTopicCurriculumByGrade(gradeId, Today())))
            .Select(x => x.TopicId).Should().Contain(topicId);
    }

    // ── the retired AC-2 assertions, re-expressed without a period ───────────

    [TestMethod]
    public async Task ListedException_CarriesItsDivisionAndSpan_AndIsRemovable()
    {
        // The surviving half of v1's BlockOnArchivedPeriod_StaysListable_AndDeletable:
        // an exception is listable with its shape rendered from the exception itself (no
        // period join), and removing it hides the row. The period half retires — a span
        // cannot be archived, and there is no period status to mark.
        using var s = new StudentsTestScope("exc-list-shape");
        var (gradeId, topicId) = await SeedGradeAndTopicAsync(s);
        var exception = SeedGradeException(
            s, gradeId, topicId, D(2027, 1, 1), D(2027, 3, 31), AcademicYearDivision.Terms, "teacher on leave");
        await s.Db.SaveChangesAsync();

        var listed = await new ListSubjectEnrollmentExceptionsHandler(new SubjectEnrollmentExceptionRepository(s.Db))
            .HandleAsync(new ListSubjectEnrollmentExceptions(gradeId, null));

        var row = listed.Should().ContainSingle(x => x.Id == exception.Id).Which;
        row.Division.Should().Be("Terms", "the DTO carries the period part, not a period id");
        row.StartDate.Should().Be(D(2027, 1, 1));
        row.EndDate.Should().Be(D(2027, 3, 31));
        row.Reason.Should().Be("teacher on leave");

        await new RemoveSubjectEnrollmentExceptionHandler(
                new SubjectEnrollmentExceptionRepository(s.Db), s.Cache,
                NullLogger<RemoveSubjectEnrollmentExceptionHandler>.Instance)
            .HandleAsync(new RemoveSubjectEnrollmentException(exception.Id));

        (await new ListSubjectEnrollmentExceptionsHandler(new SubjectEnrollmentExceptionRepository(s.Db))
            .HandleAsync(new ListSubjectEnrollmentExceptions(gradeId, null)))
            .Should().BeEmpty("a removed exception is hidden by the soft-delete filter");
    }
}
