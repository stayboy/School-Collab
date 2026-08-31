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
/// Sub-period containment within the parent academic year
/// (period-hierarchy-terms-semesters.md FR-H3, AC-H6, EC-H3).
/// </summary>
[TestClass]
public class PeriodHierarchyContainmentTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static UpdatePeriodHandler NewUpdate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<UpdatePeriodHandler>.Instance);

    [TestMethod]
    public async Task Create_Term_WithinParentYear_Succeeds()
    {
        using var s = new StudentsTestScope("cont-mid");
        var h = NewCreate(s);
        var ay = (await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t = (await h.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;
        (await s.Db.Periods.SingleAsync(p => p.Id == t)).Division.Should().Be(AcademicYearDivision.Terms);
    }

    [TestMethod]
    public async Task Create_Term_OutsideParentYear_Throws()
    {
        using var s = new StudentsTestScope("cont-outside");
        var h = NewCreate(s);
        var ay = (await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;

        // Starts after the parent year ends — not contained.
        var act = async () => (await h.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2028, 9, 1), new DateOnly(2028, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;
        await act.Should().ThrowAsync<PeriodContainmentException>();
    }

    [TestMethod]
    public async Task Create_Term_CrossesYearBoundary_Throws()
    {
        using var s = new StudentsTestScope("cont-boundary");
        var h = NewCreate(s);
        var ay = (await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;

        // End date spills past the parent year end — EC-H3.
        var act = async () => (await h.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2027, 6, 1), new DateOnly(2027, 9, 15), AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;
        await act.Should().ThrowAsync<PeriodContainmentException>();
    }

    [TestMethod]
    public async Task Update_Term_OutsideParentYear_Throws()
    {
        using var s = new StudentsTestScope("cont-update");
        var create = NewCreate(s);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;

        var update = NewUpdate(s);
        var act = async () => await update.HandleAsync(new UpdatePeriod(
            t1, "T1", new DateOnly(2026, 9, 1), new DateOnly(2027, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay));
        await act.Should().ThrowAsync<PeriodContainmentException>();
    }
}