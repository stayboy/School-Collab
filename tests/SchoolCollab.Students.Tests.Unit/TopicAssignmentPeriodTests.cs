using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignActivityGroupTopic;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Rev. 6 topic-assignment PeriodId rules
/// (spec activity-group-enrollment.md FR-55/56/57, AC-44..46, EC-23/24).
/// </summary>
[TestClass]
public class TopicAssignmentPeriodTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly Guid GradeId = Guid.Parse("aaaaaaa1-1111-1111-1111-111111111111");
    private static readonly Guid TopicId = Guid.Parse("aaaaaaa2-2222-2222-2222-222222222222");

    private static CreatePeriodHandler NewCreatePeriod(StudentsTestScope s) => new(
        s.Periods, s.Cache, s.Tenants,
        NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) => new(
        s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
        NullLogger<ActivatePeriodHandler>.Instance);

    private static AssignGradeTopicHandler NewAssignGrade(StudentsTestScope s) => new(
        s.GradeTopicAssignments, s.Periods, s.Cache, NullLogger<AssignGradeTopicHandler>.Instance);

    private static AssignActivityGroupTopicHandler NewAssignGroup(StudentsTestScope s) => new(
        new ActivityGroupTopicAssignmentRepository(s.Db), s.ActivityGroups, s.Periods, s.Cache,
        NullLogger<AssignActivityGroupTopicHandler>.Instance);

    /// <summary>Seeds an active academic year (and, when <paramref name="withTerm"/>
    /// is true, an active Term within it), plus a GradeLevel row for FK integrity.</summary>
    private static async Task<(Guid yearId, Guid? termId)> SeedActiveYearAsync(StudentsTestScope s, bool withTerm = true)
    {
        var create = NewCreatePeriod(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        Guid? termId = null;
        // Guard (FR-G1): seed a Draft term before the Terms year activates. The
        // year then auto-activates the earliest sub (FR-H4a); activating it again
        // is a harmless no-op.
        if (withTerm)
        {
            termId = (await create.HandleAsync(new CreatePeriod(
                "T1", D(2026, 9, 1), D(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        }
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        if (termId is not null)
        {
            await NewActivate(s).HandleAsync(new ActivatePeriod(termId.Value));
        }
        s.Db.GradeLevels.Add(GradeLevel.Create(GradeId, 1, "Grade 1", 1));
        await s.Db.SaveChangesAsync();
        return (yearId, termId);
    }

    // FR-57: grade topic with an AcademicYear period → allowed (year-spanning).
    [TestMethod]
    public async Task AssignGrade_AcademicYearPeriod_Succeeds()
    {
        using var s = new StudentsTestScope("tp-grade-year-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);
        var id = await NewAssignGrade(s).HandleAsync(new AssignGradeTopic(
            GradeId, TopicId, D(2026, 9, 1), PeriodId: yearId));
        (await s.GradeTopicAssignments.GetAsync(id))!.PeriodId.Should().Be(yearId);
    }

    // FR-57/AC-44: grade topic with a Term within the active year → allowed.
    [TestMethod]
    public async Task AssignGrade_TermWithinActiveYear_Succeeds()
    {
        using var s = new StudentsTestScope("tp-grade-term-" + Guid.NewGuid());
        var (_, termId) = await SeedActiveYearAsync(s);
        var id = await NewAssignGrade(s).HandleAsync(new AssignGradeTopic(
            GradeId, TopicId, D(2026, 9, 1), PeriodId: termId));
        (await s.GradeTopicAssignments.GetAsync(id))!.PeriodId.Should().Be(termId);
    }

    // FR-57/EC-24: grade topic with a Term outside the active year → rejected.
    [TestMethod]
    public async Task AssignGrade_TermOutsideActiveYear_Throws()
    {
        using var s = new StudentsTestScope("tp-grade-ec24-" + Guid.NewGuid());
        var (_, _) = await SeedActiveYearAsync(s);
        // A second, un-activated academic year + term (outside the active year).
        var create = NewCreatePeriod(s);
        var otherYear = (await create.HandleAsync(new CreatePeriod("AY2027", D(2027, 9, 1), D(2028, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var otherTerm = (await create.HandleAsync(new CreatePeriod(
            "T9", D(2027, 9, 1), D(2027, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: otherYear))).YearId;

        await FluentActions.Awaiting(() => NewAssignGrade(s).HandleAsync(
            new AssignGradeTopic(GradeId, TopicId, D(2027, 9, 1), PeriodId: otherTerm)))
            .Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    // FR-56: Termly group topic with a Term period → allowed.
    [TestMethod]
    public async Task AssignGroup_TermlyGroup_TermPeriod_Succeeds()
    {
        using var s = new StudentsTestScope("tp-group-term-" + Guid.NewGuid());
        var (_, termId) = await SeedActiveYearAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();

        var id = await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 9, 1), PeriodId: termId));
        (await new ActivityGroupTopicAssignmentRepository(s.Db).GetAsync(id))!.PeriodId.Should().Be(termId);
    }

    // FR-H14 / AC-H13: a Termly group topic with a Term of a NON-active year is rejected.
    [TestMethod]
    public async Task AssignGroup_TermlyGroup_TermOfNonActiveYear_Throws()
    {
        using var s = new StudentsTestScope("tp-group-fr14-" + Guid.NewGuid());
        var (_, _) = await SeedActiveYearAsync(s);
        // A second, un-activated academic year + term (outside the active year).
        var create = NewCreatePeriod(s);
        var otherYear = (await create.HandleAsync(new CreatePeriod("AY2027", D(2027, 9, 1), D(2028, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var otherTerm = (await create.HandleAsync(new CreatePeriod(
            "T9", D(2027, 9, 1), D(2027, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: otherYear))).YearId;
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => NewAssignGroup(s).HandleAsync(
            new AssignActivityGroupTopic(group.Id, TopicId, D(2027, 9, 1), PeriodId: otherTerm)))
            .Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    // FR-56/EC-23: OpenEnded group topic must not carry a PeriodId.
    [TestMethod]
    public async Task AssignGroup_OpenEndedGroup_WithPeriod_Throws()
    {
        using var s = new StudentsTestScope("tp-group-ec23-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);
        var group = ActivityGroup.Create("Open Club", span: EnrollmentSpan.OpenEnded);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => NewAssignGroup(s).HandleAsync(
            new AssignActivityGroupTopic(group.Id, TopicId, D(2026, 9, 1), PeriodId: yearId)))
            .Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    // FR-56: Termly group topic with the wrong period type → rejected.
    [TestMethod]
    public async Task AssignGroup_TermlyGroup_AcademicYearPeriod_Throws()
    {
        using var s = new StudentsTestScope("tp-group-mismatch-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => NewAssignGroup(s).HandleAsync(
            new AssignActivityGroupTopic(group.Id, TopicId, D(2026, 9, 1), PeriodId: yearId)))
            .Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    // Rev. 6: duplicate active (group, topic, period) assignment → rejected (409).
    [TestMethod]
    public async Task AssignGroup_DuplicateActiveSamePeriod_Throws()
    {
        using var s = new StudentsTestScope("tp-dup-period-" + Guid.NewGuid());
        var (_, termId) = await SeedActiveYearAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();

        // Start date in the past so the assignment is active on today (the guard
        // checks effectiveness on DateTime.UtcNow).
        await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 1, 1), PeriodId: termId));

        await FluentActions.Awaiting(() => NewAssignGroup(s).HandleAsync(
            new AssignActivityGroupTopic(group.Id, TopicId, D(2026, 1, 1), PeriodId: termId)))
            .Should().ThrowAsync<DuplicateTopicAssignmentException>();
    }

    // Rev. 6: duplicate active (group, topic) with null period → rejected (null == null).
    [TestMethod]
    public async Task AssignGroup_DuplicateActiveNullPeriod_Throws()
    {
        using var s = new StudentsTestScope("tp-dup-null-" + Guid.NewGuid());
        await SeedActiveYearAsync(s);
        var group = ActivityGroup.Create("Open Club", span: EnrollmentSpan.OpenEnded);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();

        await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 1, 1)));

        await FluentActions.Awaiting(() => NewAssignGroup(s).HandleAsync(
            new AssignActivityGroupTopic(group.Id, TopicId, D(2026, 1, 1))))
            .Should().ThrowAsync<DuplicateTopicAssignmentException>();
    }

    // Rev. 6: same (group, topic) with a different period → both succeed.
    [TestMethod]
    public async Task AssignGroup_DifferentPeriod_AllowsSecond()
    {
        using var s = new StudentsTestScope("tp-diff-period-" + Guid.NewGuid());
        var (yearId, termId) = await SeedActiveYearAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();

        // A second Term within the active year gives a distinct PeriodId.
        var create = NewCreatePeriod(s);
        var term2 = (await create.HandleAsync(new CreatePeriod(
            "T2", D(2027, 1, 1), D(2027, 4, 30), AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(term2));

        await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 1, 1), PeriodId: termId));
        var second = await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 1, 1), PeriodId: term2));
        second.Should().NotBeEmpty();
    }

    // Rev. 6: period validation runs before the duplicate guard (422 wins over 409).
    [TestMethod]
    public async Task AssignGroup_InvalidPeriod_Still422BeforeDuplicate()
    {
        using var s = new StudentsTestScope("tp-422-before-dup-" + Guid.NewGuid());
        var create = NewCreatePeriod(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        // Guard (FR-G1): seed a Draft sub so the Terms year can activate; this
        // auto-activated seed is separate from the term the test creates below.
        await create.HandleAsync(new CreatePeriod(
            "Seed", D(2026, 9, 1), D(2026, 9, 30), AcademicYearDivision.Terms, ParentPeriodId: yearId));
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();

        // First assign with a valid Term.
        var term = (await create.HandleAsync(new CreatePeriod(
            "T1", D(2026, 10, 1), D(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(term));
        await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 1, 1), PeriodId: term));

        // Second assign with the SAME (group, topic) but an INVALID period (year).
        // The invalid period must 422 (TopicAssignmentPeriodException), not 409.
        await FluentActions.Awaiting(() => NewAssignGroup(s).HandleAsync(
            new AssignActivityGroupTopic(group.Id, TopicId, D(2026, 1, 1), PeriodId: yearId)))
            .Should().ThrowAsync<TopicAssignmentPeriodException>();
    }
}