using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Division immutability + overlap-interaction on update
/// (documents/specs/period-edit-parity-deactivate.md FR-E1, FR-X3). The
/// <c>UpdatePeriod</c> command carries no <c>Division</c>, so a period's framework
/// can never change; and <c>Deactivated</c> periods are excluded from the no-overlap
/// check while <c>Completed</c>/<c>Active</c> still block.
/// </summary>
[TestClass]
public class PeriodUpdateImmutabilityTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static UpdatePeriodHandler NewUpdate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<UpdatePeriodHandler>.Instance);

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

    // FR-E1: updating a top-level Terms year preserves its Division (immutable).
    [TestMethod]
    public async Task Update_Year_PreservesItsDivision()
    {
        using var s = new StudentsTestScope("immut-year-division");
        var year = Seed(s, "AY2026", AcademicYearDivision.Terms);
        var t1 = Seed(s, "T1", AcademicYearDivision.Terms, year.Id);
        await s.Db.SaveChangesAsync();

        await NewUpdate(s).HandleAsync(new UpdatePeriod(
            year.Id, "AY2026 renamed", year.StartDate, year.EndDate));

        (await s.Db.Periods.SingleAsync(p => p.Id == year.Id))
            .Division.Should().Be(AcademicYearDivision.Terms, "Division is immutable (FR-E1)");
    }

    // FR-E1: updating a sub-period preserves its division and leaves it a sub-period.
    [TestMethod]
    public async Task Update_SubPeriod_PreservesDivisionAndParent()
    {
        using var s = new StudentsTestScope("immut-sub-division");
        var year = Seed(s, "AY2026", AcademicYearDivision.Terms);
        var t1 = Seed(s, "T1", AcademicYearDivision.Terms, year.Id);
        await s.Db.SaveChangesAsync();

        await NewUpdate(s).HandleAsync(new UpdatePeriod(
            t1.Id, "T1 renamed", t1.StartDate, D(2026, 12, 31), year.Id));

        var row = await s.Db.Periods.SingleAsync(p => p.Id == t1.Id);
        row.Name.Should().Be("T1 renamed");
        row.Division.Should().Be(AcademicYearDivision.Terms);
        row.ParentPeriodId.Should().Be(year.Id);
    }

    // FR-X3: a Completed period (NOT Deactivated) still blocks overlap on create.
    [TestMethod]
    public async Task CompletedYear_StillBlocksOverlap()
    {
        using var s = new StudentsTestScope("immut-completed-blocks");
        var year = Seed(s, "AY2026", AcademicYearDivision.Terms);
        var t1 = Seed(s, "T1", AcademicYearDivision.Terms, year.Id);
        await s.Db.SaveChangesAsync();
        year.Activate();
        t1.Activate();
        await s.Db.SaveChangesAsync();
        year.Complete();
        t1.Complete();
        await s.Db.SaveChangesAsync();

        var act = async () => await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2027", D(2026, 9, 1), D(2027, 8, 31), AcademicYearDivision.None));
        await act.Should().ThrowAsync<PeriodOverlapException>(
            "Completed periods still occupy the timeline (only Deactivated is excluded — FR-X3)");
    }
}
