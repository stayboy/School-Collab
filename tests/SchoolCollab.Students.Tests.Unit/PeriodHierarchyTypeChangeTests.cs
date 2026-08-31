using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Hierarchy-change orphan guard on update (plan-drop-periodtype.md). A top-level
/// year → sub-period flip must not orphan sub-periods. Unchanged updates and
/// orphan-free changes still succeed.
/// </summary>
[TestClass]
public class PeriodHierarchyTypeChangeTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static UpdatePeriodHandler NewUpdate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<UpdatePeriodHandler>.Instance);

    // A year with sub-periods cannot be flipped to a sub-period (orphaning children).
    [TestMethod]
    public async Task Update_Year_WithSubPeriods_FlipToSubPeriod_Throws()
    {
        using var s = new StudentsTestScope("fu1-f2-year-to-term");
        var create = NewCreate(s);
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms));
        await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay));

        var update = NewUpdate(s);
        var act = async () => await update.HandleAsync(new UpdatePeriod(
            ay, "AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), AcademicYearDivision.Terms, ParentPeriodId: ay));
        await act.Should().ThrowAsync<PeriodFrameworkMismatchException>();
    }

    // Guard non-regression: an unchanged update still succeeds.
    [TestMethod]
    public async Task Update_TypeUnchanged_StillSucceeds()
    {
        using var s = new StudentsTestScope("fu1-f2-unchanged");
        var create = NewCreate(s);
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms));
        var t1 = await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay));

        var update = NewUpdate(s);
        await update.HandleAsync(new UpdatePeriod(
            t1, "T1 renamed", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay));

        (await s.Db.Periods.SingleAsync(p => p.Id == t1)).Name.Should().Be("T1 renamed");
    }

    // Guard is narrowly scoped to orphaning: a childless year can be flipped to a sub-period.
    [TestMethod]
    public async Task Update_ChildlessYear_FlipToSubPeriod_Succeeds()
    {
        using var s = new StudentsTestScope("fu1-f2-childless");
        var create = NewCreate(s);
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.None));
        var otherAy = await create.HandleAsync(new CreatePeriod("AY2027", new DateOnly(2027, 9, 1), new DateOnly(2028, 8, 31), Division: AcademicYearDivision.Terms));

        var update = NewUpdate(s);
        await update.HandleAsync(new UpdatePeriod(
            ay, "AY2026", new DateOnly(2027, 9, 1), new DateOnly(2027, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: otherAy));

        var row = await s.Db.Periods.SingleAsync(p => p.Id == ay);
        row.Division.Should().Be(AcademicYearDivision.Terms);
        row.ParentPeriodId.Should().Be(otherAy);
    }
}
