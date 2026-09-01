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
/// Academic-year-division framework gate on sub-period creation/update
/// (period-hierarchy-terms-semesters.md FR-H6/H7/H7a, AC-H7/AC-H8, EC-H2).
/// The division is a property of the parent AcademicYear period (Rev. 2) — a
/// Term requires parent.Division == Terms, a Semester requires Semesters.
/// </summary>
[TestClass]
public class PeriodHierarchyDivisionGateTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static UpdatePeriodHandler NewUpdate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<UpdatePeriodHandler>.Instance);

    // AC-H7: a Term under a None-division year is rejected.
    [TestMethod]
    public async Task Create_Term_WhenParentDivisionNone_Throws()
    {
        using var s = new StudentsTestScope("gate-term-none");
        var h = NewCreate(s);
        var ay = (await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31),
            Division: AcademicYearDivision.None))).YearId;

        var act = async () => (await h.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;
        await act.Should().ThrowAsync<PeriodFrameworkMismatchException>();
    }

    // AC-H7: a Term under a Terms-division year is allowed.
    [TestMethod]
    public async Task Create_Term_WhenParentDivisionTerms_Succeeds()
    {
        using var s = new StudentsTestScope("gate-term-terms");
        var h = NewCreate(s);
        var ay = (await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31),
            Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await h.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;
        (await s.Db.Periods.SingleAsync(p => p.Id == t1)).Division.Should().Be(AcademicYearDivision.Terms);
    }

    // AC-H7: a Semester under a Semesters-division year is allowed.
    [TestMethod]
    public async Task Create_Semester_WhenParentDivisionSemesters_Succeeds()
    {
        using var s = new StudentsTestScope("gate-sem-semesters");
        var h = NewCreate(s);
        var ay = (await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31),
            Division: AcademicYearDivision.Semesters))).YearId;
        var sem = (await h.HandleAsync(new CreatePeriod(
            "S1", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31), AcademicYearDivision.Semesters, ParentPeriodId: ay))).YearId;
        (await s.Db.Periods.SingleAsync(p => p.Id == sem)).Division.Should().Be(AcademicYearDivision.Semesters);
    }

    // AC-H7: a Semester under a Terms-division year is rejected (framework mismatch).
    [TestMethod]
    public async Task Create_Semester_WhenParentDivisionTerms_Throws()
    {
        using var s = new StudentsTestScope("gate-sem-terms");
        var h = NewCreate(s);
        var ay = (await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31),
            Division: AcademicYearDivision.Terms))).YearId;

        var act = async () => (await h.HandleAsync(new CreatePeriod(
            "S1", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31), AcademicYearDivision.Semesters, ParentPeriodId: ay))).YearId;
        await act.Should().ThrowAsync<PeriodFrameworkMismatchException>();
    }

    // FR-H6: division is REQUIRED on every period (non-nullable). A sub-period
    // carries its own division (its kind), so there is no "forbidden division"
    // case — the one-kind rule is enforced by the parent-division match.
    [TestMethod]
    public async Task Create_SubPeriod_WithMismatchedParentDivision_Throws()
    {
        using var s = new StudentsTestScope("gate-term-div");
        var h = NewCreate(s);
        var ay = (await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31),
            Division: AcademicYearDivision.Terms))).YearId;

        var act = async () => (await h.HandleAsync(new CreatePeriod(
            "S1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Semesters, ParentPeriodId: ay))).YearId;
        await act.Should().ThrowAsync<PeriodFrameworkMismatchException>();
    }

    // FR-H7: update-bypass — an AcademicYear cannot be updated to a Term under a
    // None-division year.
    [TestMethod]
    public async Task Update_AcademicYear_ToTerm_WhenParentDivisionNone_Throws()
    {
        using var s = new StudentsTestScope("gate-update-term-none");
        var create = NewCreate(s);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31),
            Division: AcademicYearDivision.None))).YearId;

        var update = NewUpdate(s);
        var act = async () => await update.HandleAsync(new UpdatePeriod(
            ay, "AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), ParentPeriodId: ay));
        await act.Should().ThrowAsync<PeriodFrameworkMismatchException>();
    }

    // AC-H8: per-year division independence — Y1 (Terms) allows a Term, Y2 (None) rejects it.
    [TestMethod]
    public async Task PerYearDivision_Independence()
    {
        using var s = new StudentsTestScope("gate-per-year");
        var h = NewCreate(s);
        var y1 = (await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31),
            Division: AcademicYearDivision.Terms))).YearId;
        var y2 = (await h.HandleAsync(new CreatePeriod("AY2027", new DateOnly(2027, 9, 1), new DateOnly(2028, 8, 31),
            Division: AcademicYearDivision.None))).YearId;

        // Term under Y1 (Terms) succeeds.
        var t1 = (await h.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: y1))).YearId;
        (await s.Db.Periods.SingleAsync(p => p.Id == t1)).Division.Should().Be(AcademicYearDivision.Terms);

        // Term under Y2 (None) is rejected.
        var act = async () => (await h.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2027, 9, 1), new DateOnly(2027, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: y2))).YearId;
        await act.Should().ThrowAsync<PeriodFrameworkMismatchException>();
    }
}
