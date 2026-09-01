using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class PeriodOverlapInvariantTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static UpdatePeriodHandler NewUpdate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<UpdatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache, NullLogger<ActivatePeriodHandler>.Instance);

    [TestMethod]
    public async Task Create_NonOverlapping_Succeeds()
    {
        using var s = new StudentsTestScope("period-no-overlap");
        var h = NewCreate(s);

        await h.HandleAsync(new CreatePeriod("H1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Division: AcademicYearDivision.Terms));
        await h.HandleAsync(new CreatePeriod("H2", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31), Division: AcademicYearDivision.Terms));

        (await s.Db.Periods.CountAsync()).Should().Be(2);
    }

    [TestMethod]
    public async Task Create_Overlapping_ThrowsPeriodOverlapException()
    {
        using var s = new StudentsTestScope("period-create-overlap");
        var h = NewCreate(s);
        await h.HandleAsync(new CreatePeriod("H1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Division: AcademicYearDivision.Terms));

        var act = async () => (await h.HandleAsync(
            new CreatePeriod("H2", new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31), Division: AcademicYearDivision.Terms))).YearId;

        await act.Should().ThrowAsync<PeriodOverlapException>();
        (await s.Db.Periods.CountAsync()).Should().Be(1); // second was rejected
    }

    [TestMethod]
    public async Task Create_AdjacentDoesNotOverlap()
    {
        // [Jan 1–Jun 30] and [Jun 30–Aug 31] share a boundary day → overlap.
        // [Jan 1–Jun 30] and [Jul 1–Aug 31] touch but don't share a day → no overlap.
        using var s = new StudentsTestScope("period-adjacent");
        var h = NewCreate(s);
        await h.HandleAsync(new CreatePeriod("H1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Division: AcademicYearDivision.Terms));

        var act = async () => (await h.HandleAsync(
            new CreatePeriod("H2", new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31), Division: AcademicYearDivision.Terms))).YearId;

        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Update_ToOverlappingRange_Throws()
    {
        using var s = new StudentsTestScope("period-update-overlap");
        var ch = NewCreate(s);
        var idA = (await ch.HandleAsync(new CreatePeriod("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Division: AcademicYearDivision.Terms))).YearId;
        var idB = (await ch.HandleAsync(new CreatePeriod("B", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31), Division: AcademicYearDivision.Terms))).YearId;

        var uh = NewUpdate(s);
        var act = async () => await uh.HandleAsync(
            new UpdatePeriod(idB, "B2", new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)));

        await act.Should().ThrowAsync<PeriodOverlapException>();
    }

    [TestMethod]
    public async Task Update_ToItsOwnRange_DoesNotThrow()
    {
        // Excluding itself: updating a period to a range that only "overlaps" itself is fine.
        using var s = new StudentsTestScope("period-update-self");
        var ch = NewCreate(s);
        var idA = (await ch.HandleAsync(new CreatePeriod("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Division: AcademicYearDivision.Terms))).YearId;

        var uh = NewUpdate(s);
        await uh.HandleAsync(new UpdatePeriod(idA, "A2", new DateOnly(2026, 2, 1), new DateOnly(2026, 5, 15)));

        var updated = await s.Db.Periods.SingleAsync();
        updated.Name.Should().Be("A2");
    }

    [TestMethod]
    public async Task Activate_WhenAnotherIsActive_ClosesPriorAndActivatesNew()
    {
        using var s = new StudentsTestScope("period-activate-overlap");
        var ch = NewCreate(s);
        var idA = (await ch.HandleAsync(new CreatePeriod("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Division: AcademicYearDivision.Terms))).YearId;
        var idB = (await ch.HandleAsync(new CreatePeriod("B", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31), Division: AcademicYearDivision.Terms))).YearId;
        // Hierarchy is incidental here — the invariant tested is overlap/close,
        // so each year gets a Draft sub to satisfy the activation guard (FR-G1).
        await ch.HandleAsync(new CreatePeriod("A1", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31),
            AcademicYearDivision.Terms, ParentPeriodId: idA));
        await ch.HandleAsync(new CreatePeriod("B1", new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30),
            AcademicYearDivision.Terms, ParentPeriodId: idB));

        await NewActivate(s).HandleAsync(new ActivatePeriod(idA)); // A → Active

        // Opening B must auto-close A (FR-A1) rather than reject.
        await NewActivate(s).HandleAsync(new ActivatePeriod(idB));

        // A is closed (Completed); B is now the single Active period.
        var a = await s.Db.Periods.SingleAsync(p => p.Id == idA);
        var b = await s.Db.Periods.SingleAsync(p => p.Id == idB);
        a.Status.Should().Be(PeriodStatus.Completed);
        b.Status.Should().Be(PeriodStatus.Active);
    }
}