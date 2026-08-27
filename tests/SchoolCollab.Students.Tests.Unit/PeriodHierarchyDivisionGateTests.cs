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
/// Academic-year-division framework gate on sub-period creation
/// (period-hierarchy-terms-semesters.md FR-H7, AC-H7).
/// </summary>
[TestClass]
public class PeriodHierarchyDivisionGateTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s, string division) =>
        new(s.Periods, s.Cache, s.Tenants, new StubAcademicYearDivisionProvider(division),
            NullLogger<CreatePeriodHandler>.Instance);

    private static UpdatePeriodHandler NewUpdate(StudentsTestScope s, string division) =>
        new(s.Periods, s.Cache, new StubAcademicYearDivisionProvider(division),
            NullLogger<UpdatePeriodHandler>.Instance);

    [TestMethod]
    public async Task Create_Term_WhenDivisionNone_Throws()
    {
        using var s = new StudentsTestScope("gate-term-none");
        var h = NewCreate(s, "None");
        var ay = await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));

        var act = async () => await h.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), PeriodType.Term, ParentPeriodId: ay));
        await act.Should().ThrowAsync<PeriodFrameworkMismatchException>();
    }

    [TestMethod]
    public async Task Create_Term_WhenDivisionTerms_Succeeds()
    {
        using var s = new StudentsTestScope("gate-term-terms");
        var h = NewCreate(s, "Terms");
        var ay = await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        var t1 = await h.HandleAsync(new CreatePeriod(
            "T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), PeriodType.Term, ParentPeriodId: ay));
        (await s.Db.Periods.SingleAsync(p => p.Id == t1)).PeriodType.Should().Be(PeriodType.Term);
    }

    [TestMethod]
    public async Task Create_Semester_WhenDivisionSemesters_Succeeds()
    {
        using var s = new StudentsTestScope("gate-sem-semesters");
        var h = NewCreate(s, "Semesters");
        var ay = await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        var sem = await h.HandleAsync(new CreatePeriod(
            "S1", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31), PeriodType.Semester, ParentPeriodId: ay));
        (await s.Db.Periods.SingleAsync(p => p.Id == sem)).PeriodType.Should().Be(PeriodType.Semester);
    }

    [TestMethod]
    public async Task Create_Semester_WhenDivisionTerms_Throws()
    {
        using var s = new StudentsTestScope("gate-sem-terms");
        var h = NewCreate(s, "Terms");
        var ay = await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));

        var act = async () => await h.HandleAsync(new CreatePeriod(
            "S1", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31), PeriodType.Semester, ParentPeriodId: ay));
        await act.Should().ThrowAsync<PeriodFrameworkMismatchException>();
    }

    [TestMethod]
    public async Task Update_AcademicYear_ToTerm_WhenDivisionNone_Throws()
    {
        using var s = new StudentsTestScope("gate-update-term-none");
        var create = NewCreate(s, "None");
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));

        var update = NewUpdate(s, "None");
        var act = async () => await update.HandleAsync(new UpdatePeriod(
            ay, "AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), PeriodType.Term, ParentPeriodId: ay));
        await act.Should().ThrowAsync<PeriodFrameworkMismatchException>();
    }
}
