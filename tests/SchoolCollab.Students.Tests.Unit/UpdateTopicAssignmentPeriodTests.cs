using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignActivityGroupTopic;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.UpdateTopicAssignmentPeriod;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Rev. 6 topic-assignment PeriodId editing (FR-55/56/57) — the
/// <see cref="UpdateTopicAssignmentPeriodHandler"/> reuses the shared
/// <see cref="TopicAssignmentPeriodValidator"/> so create and update enforce the
/// identical period rules.
/// </summary>
[TestClass]
public class UpdateTopicAssignmentPeriodTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly Guid GradeId = Guid.Parse("aaaaaaa1-1111-1111-1111-111111111111");
    private static readonly Guid TopicId = Guid.Parse("aaaaaaa2-2222-2222-2222-222222222222");

    private static CreatePeriodHandler NewCreatePeriod(StudentsTestScope s) => new(
        s.Periods, s.Cache, s.Tenants,
        NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) => new(
        s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
        NullLogger<ActivatePeriodHandler>.Instance, StudentsTestScope.Config(10000));

    private static AssignGradeTopicHandler NewAssignGrade(StudentsTestScope s) => new(
        s.GradeTopicAssignments, s.Periods, s.Cache, NullLogger<AssignGradeTopicHandler>.Instance);

    private static AssignActivityGroupTopicHandler NewAssignGroup(StudentsTestScope s) => new(
        new ActivityGroupTopicAssignmentRepository(s.Db), s.ActivityGroups, s.Periods, s.Cache,
        NullLogger<AssignActivityGroupTopicHandler>.Instance);

    private static UpdateTopicAssignmentPeriodHandler NewUpdate(StudentsTestScope s) => new(
        s.Db, s.Periods, s.ActivityGroups, s.Cache, NullLogger<UpdateTopicAssignmentPeriodHandler>.Instance);

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

    // FR-57: grade assignment updated to an AcademicYear period → succeeds.
    [TestMethod]
    public async Task Update_GradeAssignment_ValidAcademicYear_Succeeds()
    {
        using var s = new StudentsTestScope("upd-grade-year-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);
        var id = await NewAssignGrade(s).HandleAsync(new AssignGradeTopic(
            GradeId, TopicId, D(2026, 9, 1)));

        var dto = await NewUpdate(s).HandleAsync(new UpdateTopicAssignmentPeriod(id, yearId));
        dto.PeriodId.Should().Be(yearId);
        (await s.GradeTopicAssignments.GetAsync(id))!.PeriodId.Should().Be(yearId);
    }

    // FR-57: grade assignment updated to a Term within the active year → succeeds.
    [TestMethod]
    public async Task Update_GradeAssignment_TermWithinActiveYear_Succeeds()
    {
        using var s = new StudentsTestScope("upd-grade-term-" + Guid.NewGuid());
        var (_, termId) = await SeedActiveYearAsync(s);
        var id = await NewAssignGrade(s).HandleAsync(new AssignGradeTopic(
            GradeId, TopicId, D(2026, 9, 1)));

        var dto = await NewUpdate(s).HandleAsync(new UpdateTopicAssignmentPeriod(id, termId));
        dto.PeriodId.Should().Be(termId);
    }

    // FR-57/EC-24: grade assignment updated to a Term outside the active year → rejected.
    [TestMethod]
    public async Task Update_GradeAssignment_TermOutsideActiveYear_Throws()
    {
        using var s = new StudentsTestScope("upd-grade-ec24-" + Guid.NewGuid());
        var (_, _) = await SeedActiveYearAsync(s);
        var create = NewCreatePeriod(s);
        var otherYear = (await create.HandleAsync(new CreatePeriod("AY2027", D(2027, 9, 1), D(2028, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var otherTerm = (await create.HandleAsync(new CreatePeriod(
            "T9", D(2027, 9, 1), D(2027, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: otherYear))).YearId;
        var id = await NewAssignGrade(s).HandleAsync(new AssignGradeTopic(
            GradeId, TopicId, D(2026, 9, 1)));

        await FluentActions.Awaiting(() => NewUpdate(s).HandleAsync(
            new UpdateTopicAssignmentPeriod(id, otherTerm)))
            .Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    // FR-57: setting PeriodId = null reverts a grade assignment to year-spanning.
    [TestMethod]
    public async Task Update_GradeAssignment_NullPeriod_RevertsToYearSpan()
    {
        using var s = new StudentsTestScope("upd-grade-null-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);
        var id = await NewAssignGrade(s).HandleAsync(new AssignGradeTopic(
            GradeId, TopicId, D(2026, 9, 1), PeriodId: yearId));

        var dto = await NewUpdate(s).HandleAsync(new UpdateTopicAssignmentPeriod(id, null));
        dto.PeriodId.Should().BeNull();
        (await s.GradeTopicAssignments.GetAsync(id))!.PeriodId.Should().BeNull();
    }

    // FR-56: Termly group assignment updated to a Term period → succeeds.
    [TestMethod]
    public async Task Update_GroupAssignment_TermlyGroup_TermPeriod_Succeeds()
    {
        using var s = new StudentsTestScope("upd-group-term-" + Guid.NewGuid());
        var (_, termId) = await SeedActiveYearAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var id = await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 9, 1)));

        var dto = await NewUpdate(s).HandleAsync(new UpdateTopicAssignmentPeriod(id, termId));
        dto.PeriodId.Should().Be(termId);
    }

    // FR-56/EC-23: OpenEnded group assignment updated to carry a period → rejected.
    [TestMethod]
    public async Task Update_GroupAssignment_OpenEndedGroup_WithPeriod_Throws()
    {
        using var s = new StudentsTestScope("upd-group-ec23-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);
        var group = ActivityGroup.Create("Open Club", span: EnrollmentSpan.OpenEnded);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var id = await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 9, 1)));

        await FluentActions.Awaiting(() => NewUpdate(s).HandleAsync(
            new UpdateTopicAssignmentPeriod(id, yearId)))
            .Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    // FR-56: Termly group assignment updated to an AcademicYear period → rejected.
    [TestMethod]
    public async Task Update_GroupAssignment_TermlyGroup_AcademicYearPeriod_Throws()
    {
        using var s = new StudentsTestScope("upd-group-mismatch-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var id = await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 9, 1)));

        await FluentActions.Awaiting(() => NewUpdate(s).HandleAsync(
            new UpdateTopicAssignmentPeriod(id, yearId)))
            .Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    // Unknown assignment id → KeyNotFound.
    [TestMethod]
    public async Task Update_UnknownAssignment_Throws()
    {
        using var s = new StudentsTestScope("upd-unknown-" + Guid.NewGuid());
        await SeedActiveYearAsync(s);

        await FluentActions.Awaiting(() => NewUpdate(s).HandleAsync(
            new UpdateTopicAssignmentPeriod(Guid.NewGuid(), null)))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    // The owner (grade/group) is unchanged by a period update.
    [TestMethod]
    public async Task Update_DoesNotChangeOwner()
    {
        using var s = new StudentsTestScope("upd-owner-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);
        var id = await NewAssignGrade(s).HandleAsync(new AssignGradeTopic(
            GradeId, TopicId, D(2026, 9, 1)));

        var dto = await NewUpdate(s).HandleAsync(new UpdateTopicAssignmentPeriod(id, yearId));
        dto.GradeLevelId.Should().Be(GradeId);
        dto.ActivityGroupId.Should().BeNull();
        dto.Audience.Should().Be("grade");
    }
}
