using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
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
        new(s.Periods, s.Cache, s.Tenants, new StubAcademicYearDivisionProvider("Terms"), NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache, NullLogger<ActivatePeriodHandler>.Instance);

    private static CompletePeriodHandler NewComplete(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache, NullLogger<CompletePeriodHandler>.Instance);

    [TestMethod]
    public async Task Activate_YearAndTerm_BothActive()
    {
        using var s = new StudentsTestScope("h2-year-term-both");
        var create = NewCreate(s);
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        var t1 = await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31),
            PeriodType.Term, ParentPeriodId: ay));

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
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        var t1 = await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31),
            PeriodType.Term, ParentPeriodId: ay));

        // AY is still Draft → activating its Term must be rejected.
        var act = async () => await NewActivate(s).HandleAsync(new ActivatePeriod(t1));
        await act.Should().ThrowAsync<PeriodNotOpenException>();
    }

    [TestMethod]
    public async Task Activate_SecondYear_ClosesFirstYearAndCascadesItsTerms()
    {
        using var s = new StudentsTestScope("h2-second-year-cascade");
        var create = NewCreate(s);
        var ay2025 = await create.HandleAsync(new CreatePeriod("AY2025", new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31)));
        var ay2026 = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        var t1 = await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 31),
            PeriodType.Term, ParentPeriodId: ay2025));

        var activate = NewActivate(s);
        await activate.HandleAsync(new ActivatePeriod(ay2025));
        await activate.HandleAsync(new ActivatePeriod(t1));

        await activate.HandleAsync(new ActivatePeriod(ay2026)); // closes AY2025 + cascades T1

        var ay2025Row = await s.Db.Periods.SingleAsync(p => p.Id == ay2025);
        var t1Row = await s.Db.Periods.SingleAsync(p => p.Id == t1);
        var ay2026Row = await s.Db.Periods.SingleAsync(p => p.Id == ay2026);
        ay2025Row.Status.Should().Be(PeriodStatus.Completed);
        t1Row.Status.Should().Be(PeriodStatus.Completed);
        ay2026Row.Status.Should().Be(PeriodStatus.Active);
    }

    [TestMethod]
    public async Task Activate_SecondTerm_SameType_ClosesPriorSibling()
    {
        using var s = new StudentsTestScope("h2-sibling-close");
        var create = NewCreate(s);
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        var t1 = await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            PeriodType.Term, ParentPeriodId: ay));
        var t2 = await create.HandleAsync(new CreatePeriod("T2", new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 30),
            PeriodType.Term, ParentPeriodId: ay));

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
        var ay = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));
        var t1 = await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            PeriodType.Term, ParentPeriodId: ay));
        var t2 = await create.HandleAsync(new CreatePeriod("T2", new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 30),
            PeriodType.Term, ParentPeriodId: ay));

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

    [TestMethod]
    public void SetNextPeriod_OnTerm_Throws()
    {
        var term = Period.Create("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            PeriodType.Term, parentPeriodId: Guid.NewGuid());

        var act = () => term.SetNextPeriod(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void SetNextPeriod_OnSelf_Throws()
    {
        var year = Period.Create("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31));

        var act = () => year.SetNextPeriod(year.Id);
        act.Should().Throw<InvalidOperationException>("a period cannot be its own next period");
    }
}
