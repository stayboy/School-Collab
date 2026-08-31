using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ArchivePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CompletePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Relaxed active-period invariant + hierarchy-aware activation/completion
/// (period-hierarchy-terms-semesters.md FR-H4/H5/H10, AC-H2..H5, EC-H4).
/// </summary>
[TestClass]
public class PeriodHierarchyActivationTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache, NullLogger<ActivatePeriodHandler>.Instance);

    private static CompletePeriodHandler NewComplete(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache, NullLogger<CompletePeriodHandler>.Instance);

    private static ArchivePeriodHandler NewArchive(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<ArchivePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivateRecording(StudentsTestScope s, RecordingPublisher publisher) =>
        new(s.Periods, publisher, s.Cache, NullLogger<ActivatePeriodHandler>.Instance);

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<object> Enqueued { get; } = new();
        public Task EnqueueAsync<T>(T message, CancellationToken ct = default) where T : class
        {
            Enqueued.Add(message);
            return Task.CompletedTask;
        }
        public Task EnqueueAsync<T>(T message, Guid? tenantStamp, CancellationToken ct = default) where T : class
            => EnqueueAsync(message, ct);
    }

    [TestMethod]
    public async Task Activate_YearAndTerm_BothActive()
    {
        using var s = new StudentsTestScope("h2-year-term-both");
        var create = NewCreate(s);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;

        var activate = NewActivate(s);
        await activate.HandleAsync(new ActivatePeriod(ay));
        await activate.HandleAsync(new ActivatePeriod(t1));

        var ayRow = await s.Db.Periods.SingleAsync(p => p.Id == ay);
        var t1Row = await s.Db.Periods.SingleAsync(p => p.Id == t1);
        ayRow.Status.Should().Be(PeriodStatus.Active);
        t1Row.Status.Should().Be(PeriodStatus.Active);
    }

    [TestMethod]
    public async Task Activate_Term_WithoutActiveYear_ThrowsPeriodNotOpen()
    {
        using var s = new StudentsTestScope("h2-term-no-year");
        var create = NewCreate(s);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;

        // AY is still Draft → activating its Term must be rejected.
        var act = async () => await NewActivate(s).HandleAsync(new ActivatePeriod(t1));
        await act.Should().ThrowAsync<PeriodNotOpenException>();
    }

    [TestMethod]
    public async Task Activate_SecondYear_ClosesFirstYearAndCascadesItsTerms()
    {
        using var s = new StudentsTestScope("h2-second-year-cascade");
        var create = NewCreate(s);
        var ay2025 = (await create.HandleAsync(new CreatePeriod("AY2025", new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var ay2026 = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ay2025))).YearId;
        // Guard (FR-G1): AY2026 also needs a Draft sub before it can activate.
        var t3 = (await create.HandleAsync(new CreatePeriod("T3", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ay2026))).YearId;

        var activate = NewActivate(s);
        await activate.HandleAsync(new ActivatePeriod(ay2025));
        await activate.HandleAsync(new ActivatePeriod(t1));

        await activate.HandleAsync(new ActivatePeriod(ay2026)); // closes AY2025 + cascades T1

        var ay2025Row = await s.Db.Periods.SingleAsync(p => p.Id == ay2025);
        var t1Row = await s.Db.Periods.SingleAsync(p => p.Id == t1);
        var ay2026Row = await s.Db.Periods.SingleAsync(p => p.Id == ay2026);
        var t3Row = await s.Db.Periods.SingleAsync(p => p.Id == t3);
        ay2025Row.Status.Should().Be(PeriodStatus.Completed);
        t1Row.Status.Should().Be(PeriodStatus.Completed);
        ay2026Row.Status.Should().Be(PeriodStatus.Active);
        t3Row.Status.Should().Be(PeriodStatus.Active, "AY2026's earliest Draft sub auto-activated (FR-H4a)");
    }

    [TestMethod]
    public async Task Activate_SecondTerm_SameType_ClosesPriorSibling()
    {
        using var s = new StudentsTestScope("h2-sibling-close");
        var create = NewCreate(s);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;
        var t2 = (await create.HandleAsync(new CreatePeriod("T2", new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 30),
            AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;

        var activate = NewActivate(s);
        await activate.HandleAsync(new ActivatePeriod(ay));
        await activate.HandleAsync(new ActivatePeriod(t1));
        await activate.HandleAsync(new ActivatePeriod(t2)); // closes T1

        var t1Row = await s.Db.Periods.SingleAsync(p => p.Id == t1);
        var t2Row = await s.Db.Periods.SingleAsync(p => p.Id == t2);
        t1Row.Status.Should().Be(PeriodStatus.Completed);
        t2Row.Status.Should().Be(PeriodStatus.Active);
    }

    [TestMethod]
    public async Task Complete_Year_CascadesToActiveSubPeriods_NotDraft()
    {
        using var s = new StudentsTestScope("h2-complete-cascade");
        var create = NewCreate(s);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;
        var t2 = (await create.HandleAsync(new CreatePeriod("T2", new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 30),
            AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;

        var activate = NewActivate(s);
        await activate.HandleAsync(new ActivatePeriod(ay));
        await activate.HandleAsync(new ActivatePeriod(t1)); // T2 stays Draft

        await NewComplete(s).HandleAsync(new CompletePeriod(ay));

        var ayRow = await s.Db.Periods.SingleAsync(p => p.Id == ay);
        var t1Row = await s.Db.Periods.SingleAsync(p => p.Id == t1);
        var t2Row = await s.Db.Periods.SingleAsync(p => p.Id == t2);
        ayRow.Status.Should().Be(PeriodStatus.Completed);
        t1Row.Status.Should().Be(PeriodStatus.Completed); // cascaded
        t2Row.Status.Should().Be(PeriodStatus.Draft);    // not active → untouched
    }

    // FR-H4b / AC-H4b: archiving an AcademicYear cascade-completes its still-Active
    // sub-periods BEFORE the year is archived — no orphaned Active sub-period.
    [TestMethod]
    public async Task Archive_Year_CascadesToActiveSubPeriods()
    {
        using var s = new StudentsTestScope("h2-archive-cascade");
        var create = NewCreate(s);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;

        var activate = NewActivate(s);
        await activate.HandleAsync(new ActivatePeriod(ay));
        await activate.HandleAsync(new ActivatePeriod(t1));

        await NewArchive(s).HandleAsync(new ArchivePeriod(ay));

        var ayRow = await s.Db.Periods.SingleAsync(p => p.Id == ay);
        var t1Row = await s.Db.Periods.SingleAsync(p => p.Id == t1);
        ayRow.Status.Should().Be(PeriodStatus.Archived);
        t1Row.Status.Should().Be(PeriodStatus.Completed); // cascaded before archive
    }

    [TestMethod]
    public void SetNextPeriod_OnTerm_Throws()
    {
        var term = Period.Create("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, parentPeriodId: Guid.NewGuid());

        var act = () => term.SetNextPeriod(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void SetNextPeriod_OnSelf_Throws()
    {
        var year = Period.Create("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), AcademicYearDivision.None);

        var act = () => year.SetNextPeriod(year.Id);
        act.Should().Throw<InvalidOperationException>("a period cannot be its own next period");
    }

    // ── FR-H4a / AC-H2a (follow-up F1): activating a year auto-activates its
    //    earliest sub-period so the opened year's current window is available. ──

    [TestMethod]
    public async Task Activate_Year_AutoActivatesEarliestDraftSubPeriod()
    {
        using var s = new StudentsTestScope("fu1-f1-earliest");
        var create = NewCreate(s);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;
        var t2 = (await create.HandleAsync(new CreatePeriod("T2", new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 30), AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;

        await NewActivate(s).HandleAsync(new ActivatePeriod(ay));

        var ayRow = await s.Db.Periods.SingleAsync(p => p.Id == ay);
        var t1Row = await s.Db.Periods.SingleAsync(p => p.Id == t1);
        var t2Row = await s.Db.Periods.SingleAsync(p => p.Id == t2);
        ayRow.Status.Should().Be(PeriodStatus.Active);
        t1Row.Status.Should().Be(PeriodStatus.Active, "earliest StartDate sub-period is auto-activated");
        t2Row.Status.Should().Be(PeriodStatus.Draft, "later sub-period stays Draft");
    }

    [TestMethod]
    public async Task Activate_Year_AutoActivatedSubPeriod_EnqueuesPeriodActivatedEvent()
    {
        using var s = new StudentsTestScope("fu1-f1-event");
        var create = NewCreate(s);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;
        var t2 = (await create.HandleAsync(new CreatePeriod("T2", new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 30), AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;

        var publisher = new RecordingPublisher();
        await NewActivateRecording(s, publisher).HandleAsync(new ActivatePeriod(ay));

        var activated = publisher.Enqueued.OfType<PeriodActivated>().ToList();
        activated.Should().HaveCount(2, "the year and its auto-activated sub-period each enqueue a PeriodActivated event");
        activated.Select(e => e.Id).Should().Contain(new[] { ay, t1 });
        activated.Select(e => e.Id).Should().NotContain(t2);
    }

    [TestMethod]
    public async Task Activate_NoneDivisionYear_ActivatesNoSubPeriod()
    {
        using var s = new StudentsTestScope("fu1-f1-none");
        var create = NewCreate(s);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.None))).YearId;

        var publisher = new RecordingPublisher();
        await NewActivateRecording(s, publisher).HandleAsync(new ActivatePeriod(ay));

        var ayRow = await s.Db.Periods.SingleAsync(p => p.Id == ay);
        ayRow.Status.Should().Be(PeriodStatus.Active);
        (await s.Db.Periods.CountAsync(p => p.ParentPeriodId == ay && p.Status == PeriodStatus.Active)).Should().Be(0,
            "a None-division year activates no sub-period");
        publisher.Enqueued.OfType<PeriodActivated>().Should().ContainSingle(e => e.Id == ay, "only the year's own event");
    }

    [TestMethod]
    public async Task Activate_SecondYear_CascadesPriorYearAndAutoActivatesItsOwnEarliestSub()
    {
        using var s = new StudentsTestScope("fu1-f1-second-year");
        var create = NewCreate(s);
        var ay2025 = (await create.HandleAsync(new CreatePeriod("AY2025", new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var ay2026 = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 31), AcademicYearDivision.Terms, ParentPeriodId: ay2025))).YearId;
        var t3 = (await create.HandleAsync(new CreatePeriod("T3", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: ay2026))).YearId;

        var activate = NewActivate(s);
        await activate.HandleAsync(new ActivatePeriod(ay2025));
        await activate.HandleAsync(new ActivatePeriod(t1));

        await activate.HandleAsync(new ActivatePeriod(ay2026));

        var ay2025Row = await s.Db.Periods.SingleAsync(p => p.Id == ay2025);
        var t1Row = await s.Db.Periods.SingleAsync(p => p.Id == t1);
        var ay2026Row = await s.Db.Periods.SingleAsync(p => p.Id == ay2026);
        var t3Row = await s.Db.Periods.SingleAsync(p => p.Id == t3);
        ay2025Row.Status.Should().Be(PeriodStatus.Completed);
        t1Row.Status.Should().Be(PeriodStatus.Completed, "prior year's active sub-period cascade-completed");
        ay2026Row.Status.Should().Be(PeriodStatus.Active);
        t3Row.Status.Should().Be(PeriodStatus.Active, "new year's earliest sub-period auto-activated");
    }

    [TestMethod]
    public async Task Activate_Year_WithOnlyCompletedSubPeriods_ThrowsGuard()
    {
        using var s = new StudentsTestScope("fu1-f1-gap");
        var tenantId = s.Tenants.GetTenantContext().TenantId;

        var year = Period.Create("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), division: AcademicYearDivision.Terms);
        ((ITenantEntity)year).TenantId = tenantId;
        var term = Period.Create("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, parentPeriodId: year.Id);
        ((ITenantEntity)term).TenantId = tenantId;
        term.Activate();
        term.Complete(); // Completed before the year is activated — no Draft candidate (AC-G3)
        s.Db.Periods.AddRange(year, term);
        await s.Db.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var act = async () => await NewActivateRecording(s, publisher).HandleAsync(new ActivatePeriod(year.Id));
        await act.Should().ThrowAsync<PeriodGuardException>()
            .WithMessage("*create and activate at least one Terms first*");

        // Guard runs before any mutation: the year stays Draft and no event was enqueued.
        var yearRow = await s.Db.Periods.SingleAsync(p => p.Id == year.Id);
        yearRow.Status.Should().Be(PeriodStatus.Draft);
        publisher.Enqueued.Should().BeEmpty("guard failure happens before any mutation/events");
    }
}
