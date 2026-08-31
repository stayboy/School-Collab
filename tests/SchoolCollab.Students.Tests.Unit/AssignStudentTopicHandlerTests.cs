using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CompletePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.AssignStudentTopic;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Tenancy;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// FR-H13 / AC-H12 (period-hierarchy-terms-semesters.md Rev. 3): a
/// StudentTopicAssignment records the active AcademicYear (PeriodId) + the
/// active sub-period (SubPeriodId) at creation, server-resolved. The caller's
/// PeriodId is the topic's source scope and is validated against the active year.
/// </summary>
[TestClass]
public class AssignStudentTopicHandlerTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
            NullLogger<ActivatePeriodHandler>.Instance);

    private static CompletePeriodHandler NewComplete(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
            NullLogger<CompletePeriodHandler>.Instance);

    private static AssignStudentTopicHandler NewAssign(StudentsTestScope s) =>
        new(new StudentTopicAssignmentRepository(s.Db), s.Periods,
            new ActivePeriodProvider(s.Db, s.Tenants, s.Cache), s.Cache,
            NullLogger<AssignStudentTopicHandler>.Instance);

    private static async Task<Guid> SeedActiveYearAsync(StudentsTestScope s, AcademicYearDivision division)
    {
        var yearId = (await NewCreate(s).HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31),
            Division: division))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        return yearId;
    }

    private static async Task<Guid> SeedActiveTermAsync(StudentsTestScope s, Guid yearId)
    {
        var termId = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "T1", D(2026, 9, 1), D(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(termId));
        return termId;
    }

    private static AssignStudentTopic Command(Guid periodId) =>
        new(StudentId: Guid.NewGuid(), TopicId: Guid.NewGuid(), PeriodId: periodId,
            IsOverride: false, SourceType: SubjectAssignmentSource.GradeAssignment);

    // AC-H12: stamp = active year + active term.
    [TestMethod]
    public async Task Assign_ActiveYearAndTerm_StampsYearAndSubPeriod()
    {
        using var s = new StudentsTestScope("sta-stamp-" + Guid.NewGuid());
        // Guard (FR-G1): the Draft term is seeded before the Terms year activates;
        // activating the year auto-activates the earliest sub (FR-H4a).
        var yearId = (await NewCreate(s).HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31),
            Division: AcademicYearDivision.Terms))).YearId;
        var termId = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "T1", D(2026, 9, 1), D(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));

        var id = await NewAssign(s).HandleAsync(Command(yearId));

        var row = await s.Db.StudentTopicAssignments.SingleAsync(x => x.Id == id);
        row.PeriodId.Should().Be(yearId);
        row.SubPeriodId.Should().Be(termId);
    }

    // AC-H12: gap state (active year, no active sub-period) → SubPeriodId = null.
    // The guard (FR-G1) needs a Draft sub to activate the year; the auto-activated
    // sub is then completed to reach the post-activation gap state (FR-G3).
    [TestMethod]
    public async Task Assign_GapState_SubPeriodIdNull()
    {
        using var s = new StudentsTestScope("sta-gap-" + Guid.NewGuid());
        var yearId = (await NewCreate(s).HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31),
            Division: AcademicYearDivision.Terms))).YearId;
        var termId = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "T1", D(2026, 9, 1), D(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        await NewComplete(s).HandleAsync(new CompletePeriod(termId)); // now no Active sub

        var id = await NewAssign(s).HandleAsync(Command(yearId));

        var row = await s.Db.StudentTopicAssignments.SingleAsync(x => x.Id == id);
        row.PeriodId.Should().Be(yearId);
        row.SubPeriodId.Should().BeNull();
    }

    // AC-H12: a None-division year has no sub-period → SubPeriodId = null.
    [TestMethod]
    public async Task Assign_NoneDivisionYear_SubPeriodIdNull()
    {
        using var s = new StudentsTestScope("sta-none-" + Guid.NewGuid());
        var yearId = await SeedActiveYearAsync(s, AcademicYearDivision.None);

        var id = await NewAssign(s).HandleAsync(Command(yearId));

        var row = await s.Db.StudentTopicAssignments.SingleAsync(x => x.Id == id);
        row.PeriodId.Should().Be(yearId);
        row.SubPeriodId.Should().BeNull();
    }

    // AC-H12: a Term of the active year is a valid term/semester-scoped source.
    [TestMethod]
    public async Task Assign_ActiveYearTerm_AsScopedSource_Accepted()
    {
        using var s = new StudentsTestScope("sta-scoped-" + Guid.NewGuid());
        var yearId = (await NewCreate(s).HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31),
            Division: AcademicYearDivision.Terms))).YearId;
        var termId = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "T1", D(2026, 9, 1), D(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId)); // auto-activates T1 (FR-H4a)

        var id = await NewAssign(s).HandleAsync(Command(termId));

        var row = await s.Db.StudentTopicAssignments.SingleAsync(x => x.Id == id);
        row.PeriodId.Should().Be(yearId);
        row.SubPeriodId.Should().Be(termId);
    }

    // AC-H12 arm 2: a caller-provided AcademicYear other than the active year is rejected.
    // The active year is None-division here because only its Active status is needed
    // (hierarchy is incidental) — None years need no sub to activate (FR-G3).
    [TestMethod]
    public async Task Assign_ForeignAcademicYear_ThrowsPeriodNotOpen()
    {
        using var s = new StudentsTestScope("sta-foreign-" + Guid.NewGuid());
        var activeYearId = await SeedActiveYearAsync(s, AcademicYearDivision.None);
        var otherYearId = (await NewCreate(s).HandleAsync(new CreatePeriod("AY2027", D(2027, 9, 1), D(2028, 8, 31),
            Division: AcademicYearDivision.Terms))).YearId;

        await FluentActions.Awaiting(() => NewAssign(s).HandleAsync(Command(otherYearId)))
            .Should().ThrowAsync<PeriodNotOpenException>();
    }

    // AC-H12 arm 3: a Term outside the active year is rejected (422).
    [TestMethod]
    public async Task Assign_TermOutsideActiveYear_ThrowsTopicAssignmentPeriod()
    {
        using var s = new StudentsTestScope("sta-outside-" + Guid.NewGuid());
        var activeYearId = await SeedActiveYearAsync(s, AcademicYearDivision.None);
        var otherYearId = (await NewCreate(s).HandleAsync(new CreatePeriod("AY2027", D(2027, 9, 1), D(2028, 8, 31),
            Division: AcademicYearDivision.Terms))).YearId;
        var otherTermId = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "T1", D(2027, 9, 1), D(2027, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: otherYearId))).YearId;

        await FluentActions.Awaiting(() => NewAssign(s).HandleAsync(Command(otherTermId)))
            .Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    // AC-H12 arm 3: a nonexistent period is rejected (422).
    [TestMethod]
    public async Task Assign_NonexistentPeriod_ThrowsTopicAssignmentPeriod()
    {
        using var s = new StudentsTestScope("sta-missing-" + Guid.NewGuid());
        await SeedActiveYearAsync(s, AcademicYearDivision.None);

        await FluentActions.Awaiting(() => NewAssign(s).HandleAsync(Command(Guid.NewGuid())))
            .Should().ThrowAsync<TopicAssignmentPeriodException>();
    }
}
