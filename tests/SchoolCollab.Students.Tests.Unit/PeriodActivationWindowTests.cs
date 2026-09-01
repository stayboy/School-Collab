using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Activation-window guard (documents/specs/period-activation-window-auto-activation.md
/// FR-W1..W7): a period cannot be activated when today is outside
/// <c>[StartDate − tol, EndDate + tol]</c>. Covers the guard, the per-period override,
/// the config default, all-or-nothing ordering, and the FR-H4a cascade window filter.
/// </summary>
[TestClass]
public class PeriodActivationWindowTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s, int toleranceDays) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
            NullLogger<ActivatePeriodHandler>.Instance, StudentsTestScope.Config(toleranceDays));

    private static UpdatePeriodHandler NewUpdate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<UpdatePeriodHandler>.Instance);

    // FR-W1: today before StartDate − tol → rejected.
    [TestMethod]
    public async Task Activate_StartDateFarInFuture_ThrowsWindowException()
    {
        using var s = new StudentsTestScope("window-future");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(30), Today.AddDays(300), Division: AcademicYearDivision.None))).YearId;

        var act = async () => await NewActivate(s, 10).HandleAsync(new ActivatePeriod(year));
        await act.Should().ThrowAsync<PeriodActivationWindowException>();
    }

    // FR-W1: today after EndDate + tol → rejected.
    [TestMethod]
    public async Task Activate_EndDateFarInPast_ThrowsWindowException()
    {
        using var s = new StudentsTestScope("window-past");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(-300), Today.AddDays(-30), Division: AcademicYearDivision.None))).YearId;

        var act = async () => await NewActivate(s, 10).HandleAsync(new ActivatePeriod(year));
        await act.Should().ThrowAsync<PeriodActivationWindowException>();
    }

    // FR-W1: boundaries inclusive — exactly StartDate − tol is allowed.
    [TestMethod]
    public async Task Activate_AtStartBoundary_Allowed()
    {
        using var s = new StudentsTestScope("window-start-boundary");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(10), Today.AddDays(300), Division: AcademicYearDivision.None))).YearId;

        await NewActivate(s, 10).HandleAsync(new ActivatePeriod(year));

        (await s.Db.Periods.SingleAsync(p => p.Id == year)).Status.Should().Be(PeriodStatus.Active);
    }

    // FR-W1: boundaries inclusive — exactly EndDate + tol is allowed.
    [TestMethod]
    public async Task Activate_AtEndBoundary_Allowed()
    {
        using var s = new StudentsTestScope("window-end-boundary");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(-300), Today.AddDays(-10), Division: AcademicYearDivision.None))).YearId;

        await NewActivate(s, 10).HandleAsync(new ActivatePeriod(year));

        (await s.Db.Periods.SingleAsync(p => p.Id == year)).Status.Should().Be(PeriodStatus.Active);
    }

    // FR-W3: a per-period override widens the window beyond the global default.
    [TestMethod]
    public async Task Activate_OverrideWidensWindow_Allowed()
    {
        using var s = new StudentsTestScope("window-override-widen");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(30), Today.AddDays(300), Division: AcademicYearDivision.None,
            ActivationToleranceDays: 30))).YearId;

        // Default 10 would reject (today < StartDate − 10); override 30 allows it.
        await NewActivate(s, 10).HandleAsync(new ActivatePeriod(year));

        (await s.Db.Periods.SingleAsync(p => p.Id == year)).Status.Should().Be(PeriodStatus.Active);
    }

    // FR-W3: a per-period override narrows the window below the global default.
    [TestMethod]
    public async Task Activate_OverrideNarrowsWindow_Throws()
    {
        using var s = new StudentsTestScope("window-override-narrow");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(5), Today.AddDays(300), Division: AcademicYearDivision.None,
            ActivationToleranceDays: 0))).YearId;

        // Default 10 would allow (today >= StartDate − 10); override 0 rejects it.
        var act = async () => await NewActivate(s, 10).HandleAsync(new ActivatePeriod(year));
        await act.Should().ThrowAsync<PeriodActivationWindowException>();
    }

    // FR-W2: the global default is read from Students:PeriodActivationToleranceDays.
    [TestMethod]
    public async Task Activate_ConfigDefaultHonored()
    {
        using var s = new StudentsTestScope("window-config-default");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(1), Today.AddDays(300), Division: AcademicYearDivision.None))).YearId;

        // With default 10 this would be allowed; config 0 rejects it.
        var act = async () => await NewActivate(s, 0).HandleAsync(new ActivatePeriod(year));
        await act.Should().ThrowAsync<PeriodActivationWindowException>();
    }

    // FR-W4: the guard fires before any mutation — a prior active year is NOT closed.
    [TestMethod]
    public async Task Activate_GuardFiresBeforeMutations_PriorYearStaysActive()
    {
        using var s = new StudentsTestScope("window-before-mutation");
        var tenantId = s.Tenants.GetTenantContext().TenantId;

        var prior = Period.Create("AY2025", Today.AddDays(-300), Today.AddDays(-30), AcademicYearDivision.None);
        ((ITenantEntity)prior).TenantId = tenantId;
        prior.Activate();
        s.Db.Periods.Add(prior);
        await s.Db.SaveChangesAsync();

        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2026", Today.AddDays(30), Today.AddDays(300), Division: AcademicYearDivision.None))).YearId;

        var act = async () => await NewActivate(s, 10).HandleAsync(new ActivatePeriod(year));
        await act.Should().ThrowAsync<PeriodActivationWindowException>();

        (await s.Db.Periods.SingleAsync(p => p.Id == prior.Id)).Status.Should().Be(PeriodStatus.Active,
            "the window guard fails before any prior-year close runs (all-or-nothing)");
    }

    // FR-W5: the FR-H4a cascade only activates sub-periods inside their own window.
    [TestMethod]
    public async Task Activate_Year_CascadeSkipsOutOfWindowSubPeriod()
    {
        using var s = new StudentsTestScope("window-cascade-skip");
        var create = NewCreate(s);
        var year = (await create.HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(5), Today.AddDays(300), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod(
            "T1", Today.AddDays(30), Today.AddDays(60), AcademicYearDivision.Terms, ParentPeriodId: year))).YearId;

        // Year is in window (tolerance 10); T1 (StartDate +30) is out of window → cascade skips it.
        await NewActivate(s, 10).HandleAsync(new ActivatePeriod(year));

        (await s.Db.Periods.SingleAsync(p => p.Id == year)).Status.Should().Be(PeriodStatus.Active);
        (await s.Db.Periods.SingleAsync(p => p.Id == t1)).Status.Should().Be(PeriodStatus.Draft,
            "the cascade skips a sub-period outside its activation window (FR-W5)");
    }

    // FR-W1: the exception message names the period, the window, and the tolerance source.
    [TestMethod]
    public async Task Activate_WindowException_MessageNamesPeriodWindowAndToleranceSource()
    {
        using var s = new StudentsTestScope("window-message");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2026", Today.AddDays(30), Today.AddDays(300), Division: AcademicYearDivision.None))).YearId;

        var act = async () => await NewActivate(s, 10).HandleAsync(new ActivatePeriod(year));
        var ex = await act.Should().ThrowAsync<PeriodActivationWindowException>();
        ex.And.Message.Should().Contain("AY2026");
        ex.And.Message.Should().Contain("activation window");
        ex.And.Message.Should().ContainEquivalentOf("global default (10 days)");
    }

    // FR-W3: override is settable at create and persists.
    [TestMethod]
    public async Task Create_WithOverride_Persists()
    {
        using var s = new StudentsTestScope("window-create-override");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(5), Today.AddDays(300), Division: AcademicYearDivision.None,
            ActivationToleranceDays: 30))).YearId;

        (await s.Db.Periods.SingleAsync(p => p.Id == year)).ActivationToleranceDays.Should().Be(30);
    }

    // FR-W3: update sets the override; a later update with null clears it.
    [TestMethod]
    public async Task Update_SetsThenClearsOverride()
    {
        using var s = new StudentsTestScope("window-update-override");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(5), Today.AddDays(300), Division: AcademicYearDivision.None))).YearId;

        var update = NewUpdate(s);
        await update.HandleAsync(new UpdatePeriod(year, "AY", Today.AddDays(5), Today.AddDays(300), ActivationToleranceDays: 30));
        (await s.Db.Periods.SingleAsync(p => p.Id == year)).ActivationToleranceDays.Should().Be(30);

        await update.HandleAsync(new UpdatePeriod(year, "AY", Today.AddDays(5), Today.AddDays(300), ActivationToleranceDays: null));
        (await s.Db.Periods.SingleAsync(p => p.Id == year)).ActivationToleranceDays.Should().BeNull("null clears the override");
    }

    // FR-W3: a negative override is rejected at create and update.
    [TestMethod]
    public async Task Create_NegativeOverride_ThrowsArgumentException()
    {
        using var s = new StudentsTestScope("window-create-negative");
        var act = async () => await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(5), Today.AddDays(300), Division: AcademicYearDivision.None,
            ActivationToleranceDays: -1));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Update_NegativeOverride_ThrowsArgumentException()
    {
        using var s = new StudentsTestScope("window-update-negative");
        var year = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY", Today.AddDays(5), Today.AddDays(300), Division: AcademicYearDivision.None))).YearId;

        var act = async () => await NewUpdate(s).HandleAsync(new UpdatePeriod(
            year, "AY", Today.AddDays(5), Today.AddDays(300), ActivationToleranceDays: -1));

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
