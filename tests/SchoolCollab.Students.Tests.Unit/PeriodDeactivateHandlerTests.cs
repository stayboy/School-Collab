using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.DeactivatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Period deactivation (documents/specs/period-edit-parity-deactivate.md). Covers
/// FR-X1/X2/X3/X6/X10, NFR-E1/E2 and AC-E4/E5/E6/E8/E9 — Active-only guard, cascade
/// to Active sub-periods, the no-overlap relief (Deactivated excluded), the domain
/// event, and tenant isolation.
/// </summary>
[TestClass]
public class PeriodDeactivateHandlerTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static DeactivatePeriodHandler NewDeactivate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<DeactivatePeriodHandler>.Instance);

    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static Guid CurrentTenantId(StudentsTestScope s) => s.Tenants.GetTenantContext().TenantId;

    private static Period Seed(StudentsTestScope s, string name, AcademicYearDivision division, Guid? parent = null)
    {
        var p = Period.Create(name, D(2026, 9, 1), D(2027, 8, 31), division, parent);
        ((ITenantEntity)p).TenantId = CurrentTenantId(s);
        s.Db.Periods.Add(p);
        return p;
    }

    // AC-E4: an Active period → Deactivated.
    [TestMethod]
    public async Task Deactivate_ActivePeriod_BecomesDeactivated()
    {
        using var s = new StudentsTestScope("deact-active");
        var year = Seed(s, "AY2026", AcademicYearDivision.None);
        await s.Db.SaveChangesAsync();
        year.Activate();
        await s.Db.SaveChangesAsync();

        await NewDeactivate(s).HandleAsync(new DeactivatePeriod(year.Id));

        (await s.Db.Periods.SingleAsync(p => p.Id == year.Id)).Status.Should().Be(PeriodStatus.Deactivated);
    }

    // AC-E4: a non-Active (Draft) period → throws, row unchanged.
    [TestMethod]
    public async Task Deactivate_DraftPeriod_ThrowsPeriodNotDeactivatable_RowUnchanged()
    {
        using var s = new StudentsTestScope("deact-draft");
        var year = Seed(s, "AY2026", AcademicYearDivision.None);
        await s.Db.SaveChangesAsync();

        var act = async () => await NewDeactivate(s).HandleAsync(new DeactivatePeriod(year.Id));
        var ex = await act.Should().ThrowAsync<PeriodNotDeactivatableException>();
        ex.And.Message.Should().Contain("Only Active periods can be deactivated");

        (await s.Db.Periods.SingleAsync(p => p.Id == year.Id)).Status.Should().Be(PeriodStatus.Draft,
            "the row is unchanged (AC-E4)");
    }

    // AC-E8: deactivating an already-Deactivated period is a 422 (not a no-op).
    [TestMethod]
    public async Task Deactivate_AlreadyDeactivated_ThrowsPeriodNotDeactivatable()
    {
        using var s = new StudentsTestScope("deact-twice");
        var year = Seed(s, "AY2026", AcademicYearDivision.None);
        await s.Db.SaveChangesAsync();
        year.Activate();
        await s.Db.SaveChangesAsync();
        await NewDeactivate(s).HandleAsync(new DeactivatePeriod(year.Id));

        var act = async () => await NewDeactivate(s).HandleAsync(new DeactivatePeriod(year.Id));
        await act.Should().ThrowAsync<PeriodNotDeactivatableException>();
    }

    // AC-E5: deactivating an Active year cascades to its Active sub-periods.
    [TestMethod]
    public async Task Deactivate_YearWithActiveSubPeriods_Cascades()
    {
        using var s = new StudentsTestScope("deact-cascade");
        var year = Seed(s, "AY2026", AcademicYearDivision.Terms);
        var t1 = Seed(s, "T1", AcademicYearDivision.Terms, year.Id);
        var t2 = Seed(s, "T2", AcademicYearDivision.Terms, year.Id);
        await s.Db.SaveChangesAsync();
        year.Activate();
        t1.Activate();
        t2.Activate();
        await s.Db.SaveChangesAsync();

        await NewDeactivate(s).HandleAsync(new DeactivatePeriod(year.Id));

        var rows = await s.Db.Periods.OrderBy(p => p.Name).ToArrayAsync();
        rows.Select(p => p.Status).All(sp => sp == PeriodStatus.Deactivated).Should().BeTrue(
            "the year and its Active sub-periods deactivate together (AC-E5)");
    }

    // AC-E6: once Deactivated, a period's date range no longer blocks a new period.
    [TestMethod]
    public async Task DeactivatedPeriod_DoesNotBlockOverlap_ForNewPeriod()
    {
        using var s = new StudentsTestScope("deact-overlap-freed");
        var year = Seed(s, "AY2026", AcademicYearDivision.None);
        await s.Db.SaveChangesAsync();
        year.Activate();
        await s.Db.SaveChangesAsync();
        await NewDeactivate(s).HandleAsync(new DeactivatePeriod(year.Id));

        // Creating a new period in the same range must now succeed (Deactivated excluded).
        var create = NewCreate(s);
        var result = await create.HandleAsync(new CreatePeriod(
            "AY2027", D(2026, 9, 1), D(2027, 8, 31), AcademicYearDivision.None));
        result.YearId.Should().NotBeEmpty();
    }

    // AC-E9: another tenant's period id → PeriodNotFoundException (404).
    [TestMethod]
    public async Task Deactivate_OtherTenantsPeriod_ThrowsPeriodNotFound()
    {
        using var s = new StudentsTestScope("deact-other-tenant");
        var otherTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var otherYear = await s.TenantAccessor.RunWithExplicitTenantAsync(
            otherTenant,
            async _ =>
            {
                var p = Period.Create("Foreign", D(2026, 9, 1), D(2027, 8, 31), AcademicYearDivision.None);
                ((ITenantEntity)p).TenantId = otherTenant;
                s.Db.Periods.Add(p);
                await s.Db.SaveChangesAsync();
                return p;
            });

        // The handler's GetAsync is tenant-filtered, so the foreign row is invisible → 404.
        var act = async () => await NewDeactivate(s).HandleAsync(new DeactivatePeriod(otherYear.Id));
        await act.Should().ThrowAsync<PeriodNotFoundException>(
            "the tenant query filter hides the other tenant's row -> 404 (AC-E9)");

        var stillThere = await s.TenantAccessor.RunWithExplicitTenantAsync(
            otherTenant,
            ct => s.Db.Periods.CountAsync(p => p.Id == otherYear.Id, ct));
        stillThere.Should().Be(1, "the other tenant's row is untouched");
    }

    // FR-X6: Period.Deactivate() records the domain event at the aggregate level.
    [TestMethod]
    public void Deactivate_RaisesPeriodDeactivatedEvent()
    {
        var period = Period.Create("AY2026", D(2026, 9, 1), D(2027, 8, 31), AcademicYearDivision.None);
        period.Activate();

        period.Deactivate();

        period.Status.Should().Be(PeriodStatus.Deactivated);
        period.DomainEvents.OfType<PeriodDeactivatedEvent>().Should().ContainSingle(
            e => e.PeriodId == period.Id);
    }
}
