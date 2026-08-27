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
        s.Periods, s.Cache, s.Tenants, new StubAcademicYearDivisionProvider("Terms"),
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
        var yearId = await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31)));
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        Guid? termId = null;
        if (withTerm)
        {
            termId = await create.HandleAsync(new CreatePeriod(
                "T1", D(2026, 9, 1), D(2026, 12, 31), PeriodType.Term, ParentPeriodId: yearId));
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
        var otherYear = await create.HandleAsync(new CreatePeriod("AY2027", D(2027, 9, 1), D(2028, 8, 31)));
        var otherTerm = await create.HandleAsync(new CreatePeriod(
            "T9", D(2027, 9, 1), D(2027, 12, 31), PeriodType.Term, ParentPeriodId: otherYear));

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
}