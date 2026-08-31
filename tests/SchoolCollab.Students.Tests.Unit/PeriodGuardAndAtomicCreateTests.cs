using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CompletePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Activation guard + atomic period create
/// (documents/specs/period-activation-guard-atomic-create.md). Covers FR-G1..G4,
/// FR-C1..C4 + AC-G1..G4 and AC-C1..C3 (NFR-C1 matrix).
/// </summary>
[TestClass]
public class PeriodGuardAndAtomicCreateTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache, NullLogger<ActivatePeriodHandler>.Instance);

    // ── Activation guard (FR-G1..G4 / AC-G1..G4) ─────────────────────────────

    // AC-G1: Terms year + 1 Draft term → activates; term auto-activated (FR-H4a).
    [TestMethod]
    public async Task Activate_TermsYear_WithDraftTerm_ActivatesAndAutoActivatesTerm()
    {
        using var s = new StudentsTestScope("guard-g1");
        var create = NewCreate(s);
        var year = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var term = (await create.HandleAsync(new CreatePeriod("T1", D(2026, 9, 1), D(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: year))).YearId;

        await NewActivate(s).HandleAsync(new ActivatePeriod(year));

        (await s.Db.Periods.SingleAsync(p => p.Id == year)).Status.Should().Be(PeriodStatus.Active);
        (await s.Db.Periods.SingleAsync(p => p.Id == term)).Status.Should().Be(PeriodStatus.Active,
            "FR-H4a auto-activates the earliest Draft sub-period");
    }

    // AC-G2: Terms year + 0 sub-periods → guard fails; year stays Draft; no prior year closed.
    [TestMethod]
    public async Task Activate_TermsYear_ZeroSubPeriods_ThrowsGuard_AndLeavesPriorYearActive()
    {
        using var s = new StudentsTestScope("guard-g2");
        var tenantId = s.Tenants.GetTenantContext().TenantId;

        // A currently-Active prior year must stay Active (partial-mutation-free).
        var prior = Period.Create("AY2025", D(2025, 9, 1), D(2026, 8, 31), AcademicYearDivision.Terms);
        ((ITenantEntity)prior).TenantId = tenantId;
        var priorTerm = Period.Create("T", D(2025, 9, 1), D(2026, 1, 31), AcademicYearDivision.Terms, parentPeriodId: prior.Id);
        ((ITenantEntity)priorTerm).TenantId = tenantId;
        priorTerm.Activate();
        prior.Activate();
        s.Db.Periods.AddRange(prior, priorTerm);
        await s.Db.SaveChangesAsync();

        var year = (await NewCreate(s).HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;

        var act = async () => await NewActivate(s).HandleAsync(new ActivatePeriod(year));
        await act.Should().ThrowAsync<PeriodGuardException>();

        (await s.Db.Periods.SingleAsync(p => p.Id == year)).Status.Should().Be(PeriodStatus.Draft,
            "the guarded year stays Draft (no mutation)");
        (await s.Db.Periods.SingleAsync(p => p.Id == prior.Id)).Status.Should().Be(PeriodStatus.Active,
            "the guard fails before any prior-year close runs (AC-G2 partial-mutation-free)");
    }

    // AC-G3: Terms year with only Completed sub-periods → no *Draft* candidate → guard fails.
    [TestMethod]
    public async Task Activate_TermsYear_OnlyCompletedSubPeriods_ThrowsGuard()
    {
        using var s = new StudentsTestScope("guard-g3");
        var create = NewCreate(s);
        var year = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;

        var term = (await create.HandleAsync(new CreatePeriod("T1", D(2026, 9, 1), D(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: year))).YearId;
        var activate = NewActivate(s);
        await activate.HandleAsync(new ActivatePeriod(year));
        await activate.HandleAsync(new ActivatePeriod(term));

        // Complete the only sub → no Draft candidate remains.
        var complete = new CompletePeriodHandler(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
            NullLogger<CompletePeriodHandler>.Instance);
        await complete.HandleAsync(new CompletePeriod(term));

        var act = async () => await NewActivate(s).HandleAsync(new ActivatePeriod(year));
        await act.Should().ThrowAsync<PeriodGuardException>();
    }

    // AC-G4: None-division year, no sub-periods → activates unchanged.
    [TestMethod]
    public async Task Activate_NoneDivisionYear_NoSubPeriods_Activates()
    {
        using var s = new StudentsTestScope("guard-g4");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.None))).YearId;

        await NewActivate(s).HandleAsync(new ActivatePeriod(year));

        (await s.Db.Periods.SingleAsync(p => p.Id == year)).Status.Should().Be(PeriodStatus.Active);
        (await s.Db.Periods.CountAsync(p => p.ParentPeriodId == year && p.Status == PeriodStatus.Active)).Should().Be(0);
    }

    // FR-G1 evidence: the guard message names the year and the required action.
    [TestMethod]
    public async Task Guard_Message_NamesYearAndRequiredAction()
    {
        using var s = new StudentsTestScope("guard-msg");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;

        var act = async () => await NewActivate(s).HandleAsync(new ActivatePeriod(year));
        var ex = await act.Should().ThrowAsync<PeriodGuardException>();
        ex.And.Message.Should().Contain("AY2026");
        ex.And.Message.Should().ContainEquivalentOf("create and activate at least one");
    }

    // FR-G4: sub-period activation unchanged — term under Draft year still throws PeriodNotOpen.
    [TestMethod]
    public async Task Activate_Term_UnderDraftYear_StillThrowsPeriodNotOpen()
    {
        using var s = new StudentsTestScope("guard-g4-term");
        var create = NewCreate(s);
        var year = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var term = (await create.HandleAsync(new CreatePeriod("T1", D(2026, 9, 1), D(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: year))).YearId;

        var act = async () => await NewActivate(s).HandleAsync(new ActivatePeriod(term));
        await act.Should().ThrowAsync<PeriodNotOpenException>();
    }

    // ── Atomic create (FR-C1..C4 / AC-C1..C3) ────────────────────────────────

    // AC-C1: Terms year + 2 definitions → 3 rows Draft in one save; result carries ids.
    [TestMethod]
    public async Task Create_TermsYear_WithTwoSubPeriods_PersistsAllDraftAtomically()
    {
        using var s = new StudentsTestScope("create-c1");
        var result = await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms,
            SubPeriods:
            [
                new SubPeriodDefinition("T1", D(2026, 9, 1), D(2026, 12, 31)),
                new SubPeriodDefinition("T2", D(2027, 1, 1), D(2027, 4, 30)),
            ]));

        (await s.Db.Periods.CountAsync(p => p.ParentPeriodId == result.YearId)).Should().Be(2);
        (await s.Db.Periods.CountAsync()).Should().Be(3);
        (await s.Db.Periods.AllAsync(p => p.Status == PeriodStatus.Draft)).Should().BeTrue();
        result.SubPeriodIds.Should().HaveCount(2);
        result.SubPeriodIds.Should().BeEquivalentTo(
            await s.Db.Periods.Where(p => p.ParentPeriodId == result.YearId).Select(p => p.Id).ToListAsync());
    }

    // AC-C2: one definition overlapping a sibling → PeriodOverlapException; zero rows persisted.
    [TestMethod]
    public async Task Create_OverlappingSiblingDefinitions_ThrowsOverlap_ZeroRows()
    {
        using var s = new StudentsTestScope("create-c2-overlap");
        var act = async () => await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms,
            SubPeriods:
            [
                new SubPeriodDefinition("T1", D(2026, 9, 1), D(2026, 12, 31)),
                new SubPeriodDefinition("T2", D(2026, 10, 1), D(2027, 1, 31)), // overlaps T1
            ]));

        await act.Should().ThrowAsync<PeriodOverlapException>();
        (await s.Db.Periods.CountAsync()).Should().Be(0, "zero rows on whole-request rejection (FR-C3)");
    }

    // AC-C2: containment violation → PeriodContainmentException; zero rows.
    [TestMethod]
    public async Task Create_SubPeriodOutsideYearRange_ThrowsContainment_ZeroRows()
    {
        using var s = new StudentsTestScope("create-c2-contain");
        var act = async () => await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms,
            SubPeriods:
            [
                new SubPeriodDefinition("T1", D(2026, 9, 1), D(2027, 9, 30)), // crosses year end
            ]));

        await act.Should().ThrowAsync<PeriodContainmentException>();
        (await s.Db.Periods.CountAsync()).Should().Be(0);
    }

    // AC-C3: None-division year + sub-period list → ArgumentException.
    [TestMethod]
    public async Task Create_NoneDivisionYear_WithSubPeriods_ThrowsArgumentException()
    {
        using var s = new StudentsTestScope("create-c3-none");
        var act = async () => await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.None,
            SubPeriods: [new SubPeriodDefinition("T1", D(2026, 9, 1), D(2026, 12, 31))]));

        await act.Should().ThrowAsync<ArgumentException>();
        (await s.Db.Periods.CountAsync()).Should().Be(0);
    }

    // FR-C1: sub-period create (parent set) + a list → ArgumentException.
    [TestMethod]
    public async Task Create_SubPeriodWithList_ThrowsArgumentException()
    {
        using var s = new StudentsTestScope("create-c3-sub");
        var create = NewCreate(s);
        var year = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;

        var act = async () => await create.HandleAsync(new CreatePeriod(
            "T1", D(2026, 9, 1), D(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: year,
            SubPeriods: [new SubPeriodDefinition("X", D(2026, 1, 1), D(2026, 1, 31))]));

        await act.Should().ThrowAsync<ArgumentException>();
        (await s.Db.Periods.CountAsync()).Should().Be(1, "only the year exists — the sub create was rejected");
    }

    // FR-C2: end < start definition → rejected, zero rows.
    [TestMethod]
    public async Task Create_SubPeriodEndBeforeStart_ThrowsArgumentException_ZeroRows()
    {
        using var s = new StudentsTestScope("create-c2-end-start");
        var act = async () => await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms,
            SubPeriods: [new SubPeriodDefinition("T1", D(2026, 12, 31), D(2026, 9, 1))]));

        await act.Should().ThrowAsync<ArgumentException>();
        (await s.Db.Periods.CountAsync()).Should().Be(0);
    }

    // Back-compat: null/empty list on top-level Terms year → plain single-period create.
    [TestMethod]
    public async Task Create_TermsYear_NoSubPeriodList_PlainSingleCreate()
    {
        using var s = new StudentsTestScope("create-backcompat");
        var result = await NewCreate(s).HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms));

        (await s.Db.Periods.CountAsync()).Should().Be(1);
        result.SubPeriodIds.Should().BeEmpty();
        result.YearId.Should().NotBeEmpty();
    }

    // Semesters variant carries its division through to the sub-periods.
    [TestMethod]
    public async Task Create_SemestersYear_WithSubPeriods_PersistsSemesters()
    {
        using var s = new StudentsTestScope("create-semesters");
        var result = await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Semesters,
            SubPeriods:
            [
                new SubPeriodDefinition("S1", D(2026, 9, 1), D(2027, 1, 31)),
                new SubPeriodDefinition("S2", D(2027, 2, 1), D(2027, 8, 31)),
            ]));

        (await s.Db.Periods.AllAsync(p => p.Division == AcademicYearDivision.Semesters)).Should().BeTrue();
        result.SubPeriodIds.Should().HaveCount(2);
    }
}
