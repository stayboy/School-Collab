using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Period hierarchy invariants (period-hierarchy-terms-semesters.md FR-H1/H2,
/// and the parent-aware no-overlap rule that permits a sub-period inside its
/// AcademicYear while forbidding sibling overlap).
/// </summary>
[TestClass]
public class PeriodHierarchyTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, new StubAcademicYearDivisionProvider("Terms"), NullLogger<CreatePeriodHandler>.Instance);

    [TestMethod]
    public async Task Create_AcademicYear_DefaultsToAcademicYearNoParent()
    {
        using var s = new StudentsTestScope("ph-ay-default");
        var id = await NewCreate(s).HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));

        var p = await s.Db.Periods.SingleAsync(x => x.Id == id);
        p.PeriodType.Should().Be(PeriodType.AcademicYear);
        p.ParentPeriodId.Should().BeNull();
    }

    [TestMethod]
    public async Task Create_TermWithoutParent_Throws()
    {
        using var s = new StudentsTestScope("ph-term-noparent");
        var act = async () => await NewCreate(s).HandleAsync(
            new CreatePeriod("T1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
                PeriodType.Term));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Create_AcademicYearWithParent_Throws()
    {
        using var s = new StudentsTestScope("ph-ay-parent");
        var act = async () => await NewCreate(s).HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
                PeriodType.AcademicYear, ParentPeriodId: Guid.NewGuid()));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Create_Term_InsideAcademicYearParent_Succeeds()
    {
        using var s = new StudentsTestScope("ph-term-inside");
        var h = NewCreate(s);
        var ayId = await h.HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));

        // A sub-period is contained within its parent year's range → the
        // parent is excluded from the no-overlap check (FR-H3).
        var termId = await h.HandleAsync(
            new CreatePeriod("T1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
                PeriodType.Term, ParentPeriodId: ayId));

        var t = await s.Db.Periods.SingleAsync(x => x.Id == termId);
        t.PeriodType.Should().Be(PeriodType.Term);
        t.ParentPeriodId.Should().Be(ayId);
    }

    [TestMethod]
    public async Task Create_Term_ParentNotAcademicYear_Throws()
    {
        using var s = new StudentsTestScope("ph-term-parent-not-year");
        var h = NewCreate(s);
        var ayId = await h.HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        var t1Id = await h.HandleAsync(
            new CreatePeriod("T1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
                PeriodType.Term, ParentPeriodId: ayId));

        // Parent must be an AcademicYear; T1 is a Term → rejected.
        var act = async () => await h.HandleAsync(
            new CreatePeriod("T2", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31),
                PeriodType.Term, ParentPeriodId: t1Id));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Create_SiblingTerms_OverlapRejected()
    {
        using var s = new StudentsTestScope("ph-sibling-overlap");
        var h = NewCreate(s);
        var ayId = await h.HandleAsync(
            new CreatePeriod("AY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        await h.HandleAsync(
            new CreatePeriod("T1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30),
                PeriodType.Term, ParentPeriodId: ayId));

        // T2 shares June with T1 → sibling overlap rejected (parent AY excluded,
        // but T1 is not).
        var act = async () => await h.HandleAsync(
            new CreatePeriod("T2", new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31),
                PeriodType.Term, ParentPeriodId: ayId));

        await act.Should().ThrowAsync<PeriodOverlapException>();
    }
}
