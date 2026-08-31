using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Period hierarchy invariants (plan-drop-periodtype.md): a top-level academic
/// year (ParentPeriodId == null) may carry any division; a sub-period must share
/// its parent year's division; sibling overlap is rejected.
/// </summary>
[TestClass]
public class PeriodHierarchyTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    [TestMethod]
    public async Task Create_TopLevelYear_WithTermsDivision_NoParent()
    {
        using var s = new StudentsTestScope("ph-ay-terms");
        var id = await NewCreate(s).HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Division: AcademicYearDivision.Terms));

        var p = await s.Db.Periods.SingleAsync(x => x.Id == id);
        p.Division.Should().Be(AcademicYearDivision.Terms);
        p.ParentPeriodId.Should().BeNull();
    }

    [TestMethod]
    public async Task Create_SubPeriod_WithoutParent_Throws()
    {
        using var s = new StudentsTestScope("ph-term-noparent");
        var act = async () => await NewCreate(s).HandleAsync(
            new CreatePeriod("T1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
                AcademicYearDivision.Terms, ParentPeriodId: Guid.NewGuid()));

        await act.Should().ThrowAsync<PeriodNotFoundException>();
    }

    [TestMethod]
    public async Task Create_NoneDivision_WithParent_Throws()
    {
        using var s = new StudentsTestScope("ph-none-parent");
        var act = async () => await NewCreate(s).HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
                AcademicYearDivision.None, ParentPeriodId: Guid.NewGuid()));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Create_Term_InsideMatchingParent_Succeeds()
    {
        using var s = new StudentsTestScope("ph-term-inside");
        var h = NewCreate(s);
        var ayId = await h.HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Division: AcademicYearDivision.Terms));

        // A sub-period is contained within its parent year's range → the
        // parent is excluded from the no-overlap check (FR-H3).
        var termId = await h.HandleAsync(
            new CreatePeriod("T1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
                AcademicYearDivision.Terms, ParentPeriodId: ayId));

        var t = await s.Db.Periods.SingleAsync(x => x.Id == termId);
        t.Division.Should().Be(AcademicYearDivision.Terms);
        t.ParentPeriodId.Should().Be(ayId);
    }

    [TestMethod]
    public async Task Create_SubPeriod_ParentIsSubPeriod_Throws()
    {
        using var s = new StudentsTestScope("ph-term-parent-not-year");
        var h = NewCreate(s);
        var ayId = await h.HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Division: AcademicYearDivision.Terms));
        var t1Id = await h.HandleAsync(
            new CreatePeriod("T1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
                AcademicYearDivision.Terms, ParentPeriodId: ayId));

        // Parent must be a top-level year; T1 is a sub-period → rejected.
        var act = async () => await h.HandleAsync(
            new CreatePeriod("T2", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31),
                AcademicYearDivision.Terms, ParentPeriodId: t1Id));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Create_SubPeriod_ParentDivisionMismatch_Throws()
    {
        using var s = new StudentsTestScope("ph-division-mismatch");
        var h = NewCreate(s);
        var ayId = await h.HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Division: AcademicYearDivision.Terms));

        // A Semesters sub-period under a Terms year is rejected (one-kind rule).
        var act = async () => await h.HandleAsync(
            new CreatePeriod("S1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
                AcademicYearDivision.Semesters, ParentPeriodId: ayId));

        await act.Should().ThrowAsync<PeriodFrameworkMismatchException>();
    }

    [TestMethod]
    public async Task Create_SiblingTerms_OverlapRejected()
    {
        using var s = new StudentsTestScope("ph-sibling-overlap");
        var h = NewCreate(s);
        var ayId = await h.HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Division: AcademicYearDivision.Terms));
        await h.HandleAsync(
            new CreatePeriod("T1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
                AcademicYearDivision.Terms, ParentPeriodId: ayId));

        // T2 shares June with T1 → sibling overlap rejected (parent AY excluded,
        // but T1 is not).
        var act = async () => await h.HandleAsync(
            new CreatePeriod("T2", new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31),
                AcademicYearDivision.Terms, ParentPeriodId: ayId));

        await act.Should().ThrowAsync<PeriodOverlapException>();
    }
}
