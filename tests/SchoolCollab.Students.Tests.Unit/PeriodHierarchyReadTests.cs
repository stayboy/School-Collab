using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.GetActiveAcademicYear;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.GetActiveSubPeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.GetSubPeriodCount;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.ListSubPeriods;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Hierarchy read endpoints/queries (period-hierarchy-terms-semesters.md FR-H12):
/// active academic year, active sub-period, and sub-periods-of-a-year.
/// </summary>
[TestClass]
public class PeriodHierarchyReadTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, new StubAcademicYearDivisionProvider("Terms"),
            NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
            NullLogger<ActivatePeriodHandler>.Instance);

    private static GetActiveAcademicYearHandler NewActiveYear(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static GetActiveSubPeriodHandler NewActiveSub(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static ListSubPeriodsHandler NewSubPeriods(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static GetSubPeriodCountHandler NewCount(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    [TestMethod]
    public async Task ActiveAcademicYear_ReturnsActivatedYear()
    {
        using var s = new StudentsTestScope("read-active-year");
        var create = NewCreate(s);
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        await NewActivate(s).HandleAsync(new ActivatePeriod(ay));

        var dto = await NewActiveYear(s).HandleAsync(new GetActiveAcademicYear());
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(ay);
        dto.PeriodType.Should().Be("AcademicYear");
        dto.Status.Should().Be("Active");
    }

    [TestMethod]
    public async Task ActiveAcademicYear_WhenNoActiveYear_ReturnsNull()
    {
        using var s = new StudentsTestScope("active-year-none");
        // Create but never activate.
        var create = NewCreate(s);
        await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));

        (await NewActiveYear(s).HandleAsync(new GetActiveAcademicYear())).Should().BeNull();
    }

    [TestMethod]
    public async Task ActiveSubPeriod_ReturnsActivatedSubPeriod()
    {
        using var s = new StudentsTestScope("active-sub-term");
        var create = NewCreate(s);
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        await NewActivate(s).HandleAsync(new ActivatePeriod(ay));
        var t1 = await create.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), PeriodType.Term, ParentPeriodId: ay));
        await NewActivate(s).HandleAsync(new ActivatePeriod(t1));

        var dto = await NewActiveSub(s).HandleAsync(new GetActiveSubPeriod());
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(t1);
        dto.PeriodType.Should().Be("Term");
    }

    [TestMethod]
    public async Task ListSubPeriods_ReturnsChildrenOfYear()
    {
        using var s = new StudentsTestScope("list-sub-periods");
        var create = NewCreate(s);
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        var t1 = await create.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), PeriodType.Term, ParentPeriodId: ay));
        var t2 = await create.HandleAsync(new CreatePeriod(
            "T2", new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 30), PeriodType.Term, ParentPeriodId: ay));

        var result = await NewSubPeriods(s).HandleAsync(new ListSubPeriods(ay));
        result.Select(x => x.Id).Should().BeEquivalentTo([t1, t2]);
        result.Should().BeInAscendingOrder(x => x.StartDate);
    }

    [TestMethod]
    public async Task SubPeriodCount_CountsNonCompletedSubPeriods()
    {
        using var s = new StudentsTestScope("count-sub-periods");
        var create = NewCreate(s);
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        await create.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), PeriodType.Term, ParentPeriodId: ay));
        await create.HandleAsync(new CreatePeriod(
            "T2", new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 30), PeriodType.Term, ParentPeriodId: ay));

        (await NewCount(s).HandleAsync(new GetSubPeriodCount())).Count.Should().Be(2);
    }

    [TestMethod]
    public async Task SubPeriodCount_WithOnlyAcademicYear_IsZero()
    {
        using var s = new StudentsTestScope("count-no-subperiods");
        var create = NewCreate(s);
        await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));

        (await NewCount(s).HandleAsync(new GetSubPeriodCount())).Count.Should().Be(0);
    }
}