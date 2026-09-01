using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.DeletePeriod;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Draft-period delete (documents/specs/period-draft-delete.md). Covers FR-D1..D7,
/// NFR-D1/D2 and AC-D1..D7 plus the FR-D6 dangling-link housekeeping and the
/// repository's <see cref="PeriodRepository.GetDraftPeriodsLinkedToAsync"/>.
/// </summary>
[TestClass]
public class PeriodDeleteHandlerTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static DeletePeriodHandler NewDelete(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<DeletePeriodHandler>.Instance);

    private static Guid CurrentTenantId(StudentsTestScope s) => s.Tenants.GetTenantContext().TenantId;

    private static Period Seed(StudentsTestScope s, string name, AcademicYearDivision division, Guid? parent = null)
    {
        var p = Period.Create(name, D(2026, 9, 1), D(2027, 8, 31), division, parent);
        ((ITenantEntity)p).TenantId = CurrentTenantId(s);
        s.Db.Periods.Add(p);
        return p;
    }

    // AC-D1: deleting an Active year throws and leaves the row unchanged.
    [TestMethod]
    public async Task Delete_ActiveYear_ThrowsPeriodNotDeletable_RowUnchanged()
    {
        using var s = new StudentsTestScope("del-active-year");
        var year = Seed(s, "AY2026", AcademicYearDivision.None);
        year.Activate();
        await s.Db.SaveChangesAsync();

        var act = async () => await NewDelete(s).HandleAsync(new DeletePeriod(year.Id));
        var ex = await act.Should().ThrowAsync<PeriodNotDeletableException>();
        ex.And.Message.Should().Contain("Only Draft periods can be deleted");

        (await s.Db.Periods.SingleAsync(p => p.Id == year.Id)).Status.Should().Be(PeriodStatus.Active,
            "the row is unchanged (AC-D1)");
    }

    // AC-D1/FR-D4: deleting an Active sub-period throws and leaves the row unchanged.
    [TestMethod]
    public async Task Delete_ActiveSubPeriod_ThrowsPeriodNotDeletable_RowUnchanged()
    {
        using var s = new StudentsTestScope("del-active-sub");
        var year = Seed(s, "AY2026", AcademicYearDivision.Terms);
        var term = Seed(s, "T1", AcademicYearDivision.Terms, year.Id);
        await s.Db.SaveChangesAsync();
        term.Activate();
        await s.Db.SaveChangesAsync();

        var act = async () => await NewDelete(s).HandleAsync(new DeletePeriod(term.Id));
        await act.Should().ThrowAsync<PeriodNotDeletableException>();

        (await s.Db.Periods.SingleAsync(p => p.Id == term.Id)).Status.Should().Be(PeriodStatus.Active);
    }

    // AC-D2 / NFR-D1: deleting a Draft year with 2 Draft subs removes all 3 in one unit of work.
    [TestMethod]
    public async Task Delete_DraftYear_With2DraftSubPeriods_RemovesAll3_OneUnitOfWork()
    {
        using var s = new StudentsTestScope("del-draft-year-2subs");
        var year = Seed(s, "AY2026", AcademicYearDivision.Terms);
        var t1 = Seed(s, "T1", AcademicYearDivision.Terms, year.Id);
        var t2 = Seed(s, "T2", AcademicYearDivision.Terms, year.Id);
        await s.Db.SaveChangesAsync();

        await NewDelete(s).HandleAsync(new DeletePeriod(year.Id));

        (await s.Db.Periods.CountAsync(p => p.Id == year.Id || p.ParentPeriodId == year.Id)).Should().Be(0,
            "the year and both Draft sub-periods are gone in one transaction (AC-D2/NFR-D1)");
        (await s.Db.Periods.CountAsync()).Should().Be(0);
    }

    // AC-D3 / FR-D3: a Draft year with an Active sub aborts the whole delete and names the blocker.
    [TestMethod]
    public async Task Delete_DraftYear_WithActiveSub_BlockingSubNamed_NothingDeleted()
    {
        using var s = new StudentsTestScope("del-draft-year-activesub");
        var year = Seed(s, "AY2026", AcademicYearDivision.Terms);
        var draft = Seed(s, "T1", AcademicYearDivision.Terms, year.Id);
        var active = Seed(s, "T2", AcademicYearDivision.Terms, year.Id);
        await s.Db.SaveChangesAsync();
        active.Activate();
        await s.Db.SaveChangesAsync();

        var act = async () => await NewDelete(s).HandleAsync(new DeletePeriod(year.Id));
        var ex = await act.Should().ThrowAsync<PeriodNotDeletableException>();
        ex.And.Message.Should().Contain("AY2026");
        ex.And.Message.Should().Contain("T2");
        ex.And.Message.Should().Contain("Active");

        (await s.Db.Periods.CountAsync(p => p.Id == year.Id || p.ParentPeriodId == year.Id)).Should().Be(3,
            "zero partial deletions — the year and both subs remain (AC-D3/NFR-D1)");
    }

    // AC-D4 / FR-D4: deleting a Draft sub-period removes only that row; the parent year remains.
    [TestMethod]
    public async Task Delete_DraftSubPeriod_RemovesOnlyTheRow_ParentRemains()
    {
        using var s = new StudentsTestScope("del-draft-sub");
        var year = Seed(s, "AY2026", AcademicYearDivision.Terms);
        var t1 = Seed(s, "T1", AcademicYearDivision.Terms, year.Id);
        var t2 = Seed(s, "T2", AcademicYearDivision.Terms, year.Id);
        await s.Db.SaveChangesAsync();

        await NewDelete(s).HandleAsync(new DeletePeriod(t1.Id));

        (await s.Db.Periods.AnyAsync(p => p.Id == t1.Id)).Should().BeFalse("the deleted sub is gone");
        (await s.Db.Periods.SingleAsync(p => p.Id == year.Id)).Status.Should().Be(PeriodStatus.Draft,
            "the parent year remains (AC-D4)");
        (await s.Db.Periods.SingleAsync(p => p.Id == t2.Id)).Status.Should().Be(PeriodStatus.Draft,
            "the sibling sub remains");
    }

    // AC-D5 / FR-D5: another tenant's Draft period id resolves to not-found; no rows removed.
    [TestMethod]
    public async Task Delete_OtherTenantsPeriod_ThrowsPeriodNotFound()
    {
        using var s = new StudentsTestScope("del-other-tenant");
        var otherTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var otherYear = await s.TenantAccessor.RunWithExplicitTenantAsync(
            otherTenant,
            async _ =>
            {
                var p = Period.Create("OtherAY", D(2026, 9, 1), D(2027, 8, 31), AcademicYearDivision.None);
                ((ITenantEntity)p).TenantId = otherTenant;
                s.Db.Periods.Add(p);
                await s.Db.SaveChangesAsync();
                return p;
            });

        var act = async () => await NewDelete(s).HandleAsync(new DeletePeriod(otherYear.Id));
        await act.Should().ThrowAsync<PeriodNotFoundException>(
            "the tenant query filter hides the other tenant's row -> 404 (AC-D5)");

        var stillThere = await s.TenantAccessor.RunWithExplicitTenantAsync(
            otherTenant,
            ct => s.Db.Periods.CountAsync(p => p.Id == otherYear.Id, ct));
        stillThere.Should().Be(1, "the other tenant's row is untouched");
    }

    // NFR-D2: re-deleting an already-deleted period resolves to not-found, not an exception leak.
    [TestMethod]
    public async Task Delete_ReDeletedPeriod_ResolvesToNotFound()
    {
        using var s = new StudentsTestScope("del-redelete");
        var year = Seed(s, "AY2026", AcademicYearDivision.None);
        await s.Db.SaveChangesAsync();

        await NewDelete(s).HandleAsync(new DeletePeriod(year.Id));

        var act = async () => await NewDelete(s).HandleAsync(new DeletePeriod(year.Id));
        await act.Should().ThrowAsync<PeriodNotFoundException>(
            "a second delete of the same id is an idempotent 404 (NFR-D2)");
    }

    // AC-D6 / FR-D6 / EC-2: a surviving Draft link is nulled; a non-Draft link stays untouched.
    [TestMethod]
    public async Task Delete_DanglingDraftNextPeriodLink_IsNulled_NonDraftLinkUntouched()
    {
        using var s = new StudentsTestScope("del-dangling-link");
        var a = Seed(s, "AY2026", AcademicYearDivision.None);
        var b = Seed(s, "AY2027", AcademicYearDivision.None);
        var c = Seed(s, "AY2025", AcademicYearDivision.None);
        await s.Db.SaveChangesAsync();

        b.SetNextPeriod(a.Id);   // Draft B -> Draft A (should be nulled)
        c.SetNextPeriod(a.Id);   // Completed C -> A (historical record, stays)
        c.Activate();
        c.Complete();
        await s.Db.SaveChangesAsync();

        await NewDelete(s).HandleAsync(new DeletePeriod(a.Id));

        (await s.Db.Periods.SingleAsync(p => p.Id == b.Id)).NextPeriodId.Should().BeNull(
            "the surviving Draft link is nulled (AC-D6/FR-D6)");
        (await s.Db.Periods.SingleAsync(p => p.Id == c.Id)).NextPeriodId.Should().Be(a.Id,
            "a non-Draft link is a historical record and stays untouched (EC-2)");
    }

    // FR-D7: the domain Delete() on a Draft period raises exactly one PeriodDeletedEvent.
    [TestMethod]
    public void Period_Delete_OnDraft_AddsPeriodDeletedEvent()
    {
        var p = Period.Create("AY2026", D(2026, 9, 1), D(2027, 8, 31), AcademicYearDivision.None);

        p.Delete();

        p.DomainEvents.OfType<PeriodDeletedEvent>().Should().ContainSingle(e => e.PeriodId == p.Id && e.Name == p.Name);
    }

    // FR-D2: Delete() on Active/Completed/Archived throws a Draft-only message.
    [TestMethod]
    public void Period_Delete_OnActive_Completed_Archived_Throws()
    {
        var p = Period.Create("AY2026", D(2026, 9, 1), D(2027, 8, 31), AcademicYearDivision.None);

        p.Activate();
        var act = () => p.Delete();
        act.Should().Throw<PeriodNotDeletableException>().WithMessage("*Only Draft periods can be deleted*");

        p.Complete();
        act.Should().Throw<PeriodNotDeletableException>();

        p.Archive();
        act.Should().Throw<PeriodNotDeletableException>();
    }

    // FR-D6: ClearNextPeriod nulls a set link and bumps UpdatedAt; a null link is a no-op.
    [TestMethod]
    public void Period_ClearNextPeriod_NullsLink()
    {
        var a = Period.Create("AY2026", D(2026, 9, 1), D(2027, 8, 31), AcademicYearDivision.None);
        var b = Period.Create("AY2027", D(2027, 9, 1), D(2028, 8, 31), AcademicYearDivision.None);
        b.SetNextPeriod(a.Id);
        var before = b.UpdatedAt;

        b.ClearNextPeriod();

        b.NextPeriodId.Should().BeNull();
        b.UpdatedAt.Should().BeAfter(before);

        // Null link is a no-op (no exception, no timestamp bump).
        var ts = b.UpdatedAt;
        b.ClearNextPeriod();
        b.UpdatedAt.Should().Be(ts);
    }

    // Repository: GetDraftPeriodsLinkedToAsync returns only Draft rows linked to the target.
    [TestMethod]
    public async Task GetDraftPeriodsLinkedToAsync_ReturnsOnlyDraftLinkers()
    {
        using var s = new StudentsTestScope("del-repo-linked");
        var a = Seed(s, "AY2026", AcademicYearDivision.None);
        var draftLinker = Seed(s, "AY2027", AcademicYearDivision.None);
        var completedLinker = Seed(s, "AY2025", AcademicYearDivision.None);
        await s.Db.SaveChangesAsync();

        draftLinker.SetNextPeriod(a.Id);
        completedLinker.SetNextPeriod(a.Id);
        completedLinker.Activate();
        completedLinker.Complete();
        await s.Db.SaveChangesAsync();

        var linked = await s.Periods.GetDraftPeriodsLinkedToAsync(a.Id);

        linked.Should().ContainSingle(p => p.Id == draftLinker.Id);
        linked.Should().NotContain(p => p.Id == completedLinker.Id,
            "only Draft linkers are returned (FR-D6)");
    }
}
