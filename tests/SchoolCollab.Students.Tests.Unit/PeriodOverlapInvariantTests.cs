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

        await h.HandleAsync(new CreatePeriod("H1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));
        await h.HandleAsync(new CreatePeriod("H2", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)));

        (await s.Db.Periods.CountAsync()).Should().Be(2);
    }

    [TestMethod]
    public async Task Create_Overlapping_ThrowsPeriodOverlapException()
    {
        using var s = new StudentsTestScope("period-create-overlap");
        var h = NewCreate(s);
        await h.HandleAsync(new CreatePeriod("H1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));

        var act = async () => await h.HandleAsync(
            new CreatePeriod("H2", new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)));

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
        await h.HandleAsync(new CreatePeriod("H1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));

        var act = async () => await h.HandleAsync(
            new CreatePeriod("H2", new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31)));

        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Update_ToOverlappingRange_Throws()
    {
        using var s = new StudentsTestScope("period-update-overlap");
        var ch = NewCreate(s);
        var idA = await ch.HandleAsync(new CreatePeriod("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));
        var idB = await ch.HandleAsync(new CreatePeriod("B", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)));

        var uh = NewUpdate(s);
        var act = async () => await uh.HandleAsync(
            new UpdatePeriod(idB, "B2", new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31), false));

        await act.Should().ThrowAsync<PeriodOverlapException>();
    }

    [TestMethod]
    public async Task Update_ToItsOwnRange_DoesNotThrow()
    {
        // Excluding itself: updating a period to a range that only "overlaps" itself is fine.
        using var s = new StudentsTestScope("period-update-self");
        var ch = NewCreate(s);
        var idA = await ch.HandleAsync(new CreatePeriod("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));

        var uh = NewUpdate(s);
        await uh.HandleAsync(new UpdatePeriod(idA, "A2", new DateOnly(2026, 2, 1), new DateOnly(2026, 5, 15), false));

        var updated = await s.Db.Periods.SingleAsync();
        updated.Name.Should().Be("A2");
    }

    [TestMethod]
    public async Task Activate_WhenAnotherIsActive_Throws()
    {
        using var s = new StudentsTestScope("period-activate-overlap");
        var ch = NewCreate(s);
        var idA = await ch.HandleAsync(new CreatePeriod("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));
        var idB = await ch.HandleAsync(new CreatePeriod("B", new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)));

        await NewActivate(s).HandleAsync(new ActivatePeriod(idA)); // A → Active

        var act = async () => await NewActivate(s).HandleAsync(new ActivatePeriod(idB));
        await act.Should().ThrowAsync<PeriodOverlapException>();

        // A stays Active; B stays Draft.
        var a = await s.Db.Periods.SingleAsync(p => p.Id == idA);
        var b = await s.Db.Periods.SingleAsync(p => p.Id == idB);
        a.Status.Should().Be(PeriodStatus.Active);
        b.Status.Should().Be(PeriodStatus.Draft);
    }
}