using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CompletePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Tenancy;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Covers FR-A5 / §4.2 and the §4.6 follow-up: <see cref="ActivePeriodProvider"/>
/// resolves the tenant's active period via the tenant-filtered repository and
/// caches it per tenant under the "students" tag. The cache key is derived from
/// the current tenant, so lookups never leak across tenants (including workers
/// running under <c>RunWithExplicitTenantAsync</c>).
/// </summary>
[TestClass]
public class ActivePeriodProviderTests
{
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [TestMethod]
    public async Task GetActivePeriod_ReturnsActivePeriodForCurrentTenant()
    {
        using var s = new StudentsTestScope("active-period-provider");
        var tenantId = s.Tenants.GetTenantContext().TenantId;

        var period = Period.Create("Term 1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), AcademicYearDivision.None);
        period.Activate();
        ((ITenantEntity)period).TenantId = tenantId;
        s.Db.Periods.Add(period);
        await s.Db.SaveChangesAsync();

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);

        var active = await provider.GetActivePeriodAsync();

        active.Should().NotBeNull();
        active!.Id.Should().Be(period.Id);
        active.Name.Should().Be("Term 1");
        active.Status.Should().Be("Active");
    }

    [TestMethod]
    public async Task GetActivePeriod_ReturnsNullWhenNoActivePeriod()
    {
        using var s = new StudentsTestScope("active-period-provider-none");

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);

        (await provider.GetActivePeriodAsync()).Should().BeNull();
    }

    [TestMethod]
    public async Task GetActivePeriod_IsolatedPerTenant()
    {
        using var s = new StudentsTestScope("active-period-provider-iso");
        var tenantA = s.Tenants.GetTenantContext().TenantId;

        var period = Period.Create("Term A", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), AcademicYearDivision.None);
        period.Activate();
        ((ITenantEntity)period).TenantId = tenantA;
        s.Db.Periods.Add(period);
        await s.Db.SaveChangesAsync();

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);

        // Tenant A sees its active period.
        (await provider.GetActivePeriodAsync()).Should().NotBeNull();

        // Switching to a tenant with no periods must still resolve null — the cache
        // key is per-tenant (active-period:{tenantId}), so A's cached value is not leaked.
        ((TenantProvider)s.Tenants).SetTenant(new TenantContext(TenantB, "B", TenantType.School));
        (await provider.GetActivePeriodAsync()).Should().BeNull();
    }

    // ── §4.6/§10: active-sub-period + active-academic-year cache invalidation ──

    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache, NullLogger<ActivatePeriodHandler>.Instance);

    private static CompletePeriodHandler NewComplete(StudentsTestScope s) =>
        new(s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache, NullLogger<CompletePeriodHandler>.Instance);

    [TestMethod]
    public async Task GetActiveSubPeriod_ReturnsActiveSubPeriodForCurrentTenant()
    {
        using var s = new StudentsTestScope("active-sub-provider");
        var create = NewCreate(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var termId = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        // Guard (FR-G1) requires a Draft sub-period before a Terms year activates.
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        await NewActivate(s).HandleAsync(new ActivatePeriod(termId));

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        var sub = await provider.GetActiveSubPeriodAsync();

        sub.Should().NotBeNull();
        sub!.Id.Should().Be(termId);
        sub.PeriodType.Should().Be("Term");
    }

    [TestMethod]
    public async Task GetActiveSubPeriod_ReturnsNullWhenNoneActive()
    {
        using var s = new StudentsTestScope("active-sub-none");
        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        (await provider.GetActiveSubPeriodAsync()).Should().BeNull();
    }

    [TestMethod]
    public async Task GetActiveSubPeriod_IsolatedPerTenant()
    {
        using var s = new StudentsTestScope("active-sub-iso");
        var create = NewCreate(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var termId = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        // Guard (FR-G1) requires a Draft sub-period before a Terms year activates.
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        await NewActivate(s).HandleAsync(new ActivatePeriod(termId));

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        (await provider.GetActiveSubPeriodAsync()).Should().NotBeNull();

        ((TenantProvider)s.Tenants).SetTenant(new TenantContext(TenantB, "B", TenantType.School));
        (await provider.GetActiveSubPeriodAsync()).Should().BeNull(
            "the active-sub-period:{tenantId} key never leaks across tenants");
    }

    [TestMethod]
    public async Task Activate_SecondTerm_InvalidatesCachedActiveSubPeriod()
    {
        using var s = new StudentsTestScope("active-sub-invalidate");
        var create = NewCreate(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var t1 = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        var t2 = (await create.HandleAsync(new CreatePeriod("T2", new DateOnly(2027, 1, 1), new DateOnly(2027, 4, 30),
            AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        // Guard (FR-G1) requires a Draft sub-period before a Terms year activates.
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        await NewActivate(s).HandleAsync(new ActivatePeriod(t1));

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        (await provider.GetActiveSubPeriodAsync())!.Id.Should().Be(t1, "warm the cache on T1");

        // Activating T2 auto-closes T1 and invalidates the "students" tag.
        await NewActivate(s).HandleAsync(new ActivatePeriod(t2));

        (await provider.GetActiveSubPeriodAsync())!.Id.Should().Be(t2,
            "no stale sub-period lookup after Activate invalidates the tag");
    }

    [TestMethod]
    public async Task Activate_SecondYear_InvalidatesCachedActiveAcademicYear()
    {
        using var s = new StudentsTestScope("active-year-invalidate");
        var create = NewCreate(s);
        var ay2025 = (await create.HandleAsync(new CreatePeriod("AY2025", new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var ay2026 = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        // Guard (FR-G1): each Terms year needs a Draft sub before it can activate.
        await create.HandleAsync(new CreatePeriod("T", new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ay2025));
        await create.HandleAsync(new CreatePeriod("T", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ay2026));

        await NewActivate(s).HandleAsync(new ActivatePeriod(ay2025));

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        (await provider.GetActiveAcademicYearAsync())!.Id.Should().Be(ay2025, "warm the cache on AY2025");

        await NewActivate(s).HandleAsync(new ActivatePeriod(ay2026));

        (await provider.GetActiveAcademicYearAsync())!.Id.Should().Be(ay2026,
            "no stale active-academic-year lookup after Activate invalidates the tag");
    }

    [TestMethod]
    public async Task Complete_AcademicYear_InvalidatesCachedYearLookups()
    {
        using var s = new StudentsTestScope("active-year-complete");
        var create = NewCreate(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        await create.HandleAsync(new CreatePeriod("T", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31),
            AcademicYearDivision.Terms, ParentPeriodId: yearId));
        // Guard (FR-G1) requires a Draft sub-period before a Terms year activates.
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        (await provider.GetActiveAcademicYearAsync()).Should().NotBeNull("warm the cache");

        await NewComplete(s).HandleAsync(new CompletePeriod(yearId));

        (await provider.GetActiveAcademicYearAsync()).Should().BeNull(
            "Complete invalidates the active-academic-year key (tag 'students')");
    }

    // ── B1: deterministic active sub-period under the two-active-rows hierarchy ──

    [TestMethod]
    public async Task GetActiveSubPeriod_WithTermAndSemesterActive_ReturnsDeterministicOne()
    {
        using var s = new StudentsTestScope("active-sub-deterministic");
        var tenantId = s.Tenants.GetTenantContext().TenantId;

        var year = Period.Create("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), AcademicYearDivision.None);
        year.Activate();
        ((ITenantEntity)year).TenantId = tenantId;

        var term = Period.Create("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, parentPeriodId: year.Id);
        term.Activate();
        ((ITenantEntity)term).TenantId = tenantId;

        var semester = Period.Create("S1", new DateOnly(2027, 1, 1), new DateOnly(2027, 5, 31),
            AcademicYearDivision.Semesters, parentPeriodId: year.Id);
        semester.Activate();
        ((ITenantEntity)semester).TenantId = tenantId;

        s.Db.Periods.AddRange(year, term, semester);
        await s.Db.SaveChangesAsync();

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        var sub = await provider.GetActiveSubPeriodAsync();

        sub.Should().NotBeNull();
        sub!.Id.Should().Be(term.Id, "the earlier-starting sub-period is preferred");
        sub.PeriodType.Should().Be("Term");
    }

    // ── B2: GetCurrentPeriodAsync ignores Draft + prefers the sub-period ──

    [TestMethod]
    public async Task GetCurrentPeriod_IgnoresDraftPeriods()
    {
        using var s = new StudentsTestScope("current-period-draft");
        var tenantId = s.Tenants.GetTenantContext().TenantId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // A Draft period spanning today must NOT be returned as the current period.
        var draft = Period.Create("Draft AY", today.AddDays(-10), today.AddDays(10), AcademicYearDivision.None);
        ((ITenantEntity)draft).TenantId = tenantId;
        s.Db.Periods.Add(draft);
        await s.Db.SaveChangesAsync();

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        (await provider.GetCurrentPeriodAsync()).Should().BeNull(
            "a Draft period containing today is not the current period");
    }

    [TestMethod]
    public async Task GetCurrentPeriod_ReturnsActivePeriodContainingToday()
    {
        using var s = new StudentsTestScope("current-period-active");
        var tenantId = s.Tenants.GetTenantContext().TenantId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var active = Period.Create("AY2026", today.AddDays(-10), today.AddDays(10), AcademicYearDivision.None);
        active.Activate();
        ((ITenantEntity)active).TenantId = tenantId;
        s.Db.Periods.Add(active);
        await s.Db.SaveChangesAsync();

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        var current = await provider.GetCurrentPeriodAsync();
        current.Should().NotBeNull();
        current!.Id.Should().Be(active.Id);
    }

    [TestMethod]
    public async Task GetCurrentPeriod_PrefersSubPeriodOverYear()
    {
        using var s = new StudentsTestScope("current-period-sub-pref");
        var tenantId = s.Tenants.GetTenantContext().TenantId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var year = Period.Create("AY2026", today.AddDays(-10), today.AddDays(10), AcademicYearDivision.None);
        year.Activate();
        ((ITenantEntity)year).TenantId = tenantId;

        var term = Period.Create("T1", today.AddDays(-5), today.AddDays(5),
            AcademicYearDivision.Terms, parentPeriodId: year.Id);
        term.Activate();
        ((ITenantEntity)term).TenantId = tenantId;

        s.Db.Periods.AddRange(year, term);
        await s.Db.SaveChangesAsync();

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        var current = await provider.GetCurrentPeriodAsync();
        current.Should().NotBeNull();
        current!.Id.Should().Be(term.Id, "the more specific sub-period is preferred over the year");
    }

    // ── FR-H4a / AC-H2a (follow-up F1): the auto-activated sub-period must be
    //    observable through the provider right after the year activation (the
    //    "students"-tag invalidation covers the active-sub-period lookup). ──

    [TestMethod]
    public async Task Activate_Year_AutoActivatedSubPeriod_VisibleViaActiveSubPeriodProvider()
    {
        using var s = new StudentsTestScope("fu1-f1-provider");
        var create = NewCreate(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var termId = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;

        var provider = new ActivePeriodProvider(s.Db, s.Tenants, s.Cache);
        (await provider.GetActiveSubPeriodAsync()).Should().BeNull("warm the cache with no active sub-period");

        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));

        (await provider.GetActiveSubPeriodAsync())!.Id.Should().Be(termId,
            "the auto-activated sub-period is visible via the provider after the tag invalidation");
    }
}
