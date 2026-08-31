using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.GetOrCreateGradeLevel;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit.Tenancy;

/// <summary>
/// Acceptance criteria AC-11 (global-tenant-filter.md §6.3): GradeLevel/Topic/Period
/// are strict tenant-scoped entities — created rows are tenant-stamped and isolated
/// from other tenants; the coded_value_id uniqueness is per-tenant; the period
/// no-overlap invariant is per-tenant; and FR-4 rejects creation under the default
/// (Guid.Empty) tenant. Uses a real in-memory <see cref="StudentsTestScope"/> with a
/// controllable <see cref="TenantProvider"/> so the named "Tenant" filter and the
/// save-guard exercise end-to-end.
/// </summary>
[TestClass]
public class StudentsStrictTenancyTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static void AsTenant(StudentsTestScope s, Guid tenantId) =>
        ((TenantProvider)s.Tenants).SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    private static void AsDefault(StudentsTestScope s) =>
        ((TenantProvider)s.Tenants).Clear();

    [TestMethod]
    public async Task AC11_GradeLevel_IsTenantScoped_AndPerTenantUnique()
    {
        using var s = new StudentsTestScope("strict-gl");
        var cv = Guid.NewGuid();
        var h = new GetOrCreateGradeLevelHandler(
            s.GradeLevels, s.Cache, s.Tenants, NullLogger<GetOrCreateGradeLevelHandler>.Instance);

        // Tenant A creates a grade level for cv.
        AsTenant(s, TenantA);
        var aDto = await h.HandleAsync(new GetOrCreateGradeLevel(cv, 1, "Grade 1", 1));
        aDto.Id.Should().NotBeEmpty();
        (await s.Db.GradeLevels.CountAsync(x => x.CodedValueId == cv)).Should().Be(1, "A sees its own");

        // Tenant B does NOT see tenant A's grade level (filter isolation).
        AsTenant(s, TenantB);
        (await s.Db.GradeLevels.AnyAsync(x => x.CodedValueId == cv)).Should().BeFalse(
            "tenant-owned rows are isolated from other tenants by the Tenant filter");

        // Tenant B can create its own grade level with the SAME coded_value_id (per-tenant uniqueness).
        var bDto = await h.HandleAsync(new GetOrCreateGradeLevel(cv, 1, "Grade 1", 1));
        bDto.Id.Should().NotBeEmpty();
        (await s.Db.GradeLevels.CountAsync(x => x.CodedValueId == cv)).Should().Be(1, "B sees only its own");

        // Two rows exist total (one per tenant) — verify with the filter bypassed.
        (await s.Db.GradeLevels.IgnoreQueryFilters(["Tenant"]).CountAsync(x => x.CodedValueId == cv))
            .Should().Be(2, "one GradeLevel per (tenant, coded value)");
    }

    [TestMethod]
    public async Task AC11_PeriodOverlapInvariant_IsPerTenant()
    {
        using var s = new StudentsTestScope("strict-period");
        var h = new CreatePeriodHandler(
            s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

        // Tenant A creates H1 (Jan–Jun 2026).
        AsTenant(s, TenantA);
        await h.HandleAsync(new CreatePeriod("H1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Division: AcademicYearDivision.Terms));
        (await s.Db.Periods.CountAsync()).Should().Be(1);

        // Tenant B creates a period with the SAME date range — succeeds (the overlap
        // check is scoped per-tenant by the Tenant filter; B has no periods yet).
        AsTenant(s, TenantB);
        await h.HandleAsync(new CreatePeriod("H1-B", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Division: AcademicYearDivision.Terms));
        (await s.Db.Periods.CountAsync()).Should().Be(1, "B sees only its own period");

        // Tenant A creates an overlapping period — throws (overlap within A only).
        AsTenant(s, TenantA);
        var act = async () => await h.HandleAsync(
            new CreatePeriod("H2", new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31), Division: AcademicYearDivision.Terms));
        await act.Should().ThrowAsync<PeriodOverlapException>(
            "the no-overlap invariant is enforced per-tenant, not globally");
    }

    [TestMethod]
    public async Task FR4_GetOrCreateGradeLevel_UnderDefaultTenant_ThrowsBeforeAnyWrite()
    {
        using var s = new StudentsTestScope("strict-fr4");
        AsDefault(s); // Guid.Empty — no real tenant context
        var h = new GetOrCreateGradeLevelHandler(
            s.GradeLevels, s.Cache, s.Tenants, NullLogger<GetOrCreateGradeLevelHandler>.Instance);

        var act = async () => await h.HandleAsync(new GetOrCreateGradeLevel(Guid.NewGuid(), 1, "Grade 1", 1));
        await act.Should().ThrowAsync<TenantContextRequiredException>(
            "FR-4: no strict entity may be created with an empty tenant");

        // Nothing was written.
        (await s.Db.GradeLevels.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task FR4_CreatePeriod_UnderDefaultTenant_ThrowsBeforeAnyWrite()
    {
        using var s = new StudentsTestScope("strict-fr4-period");
        AsDefault(s);
        var h = new CreatePeriodHandler(
            s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

        var act = async () => await h.HandleAsync(
            new CreatePeriod("H1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Division: AcademicYearDivision.Terms));
        await act.Should().ThrowAsync<TenantContextRequiredException>();
        (await s.Db.Periods.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(0);
    }

    // NFR-H2 (period-hierarchy-terms-semesters.md): sub-periods are strict
    // tenant-scoped — created rows are isolated per tenant and per-tenant
    // creatable with the same names/dates.
    [TestMethod]
    public async Task AC_H2_SubPeriod_IsTenantScoped_AndPerTenantCreatable()
    {
        using var s = new StudentsTestScope("strict-subperiod");
        var h = new CreatePeriodHandler(
            s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

        // Tenant A creates a year + term.
        AsTenant(s, TenantA);
        var ayA = await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms));
        await h.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ayA));

        // Tenant B sees no periods (filter isolation).
        AsTenant(s, TenantB);
        (await s.Db.Periods.CountAsync()).Should().Be(0, "B sees no A-owned periods");

        // Tenant B creates its own year + term with the same names/dates.
        var ayB = await h.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms));
        await h.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ayB));
        (await s.Db.Periods.CountAsync()).Should().Be(2, "B sees only its own year + term");

        // Two years + two terms exist total.
        (await s.Db.Periods.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(4,
            "one year + one term per tenant");
    }

    // NFR-H2: activating a sub-period owned by another tenant is rejected — the
    // tenant-filtered GetAsync cannot see the foreign row.
    [TestMethod]
    public async Task AC_H2_SubPeriod_Activation_IsTenantScoped()
    {
        using var s = new StudentsTestScope("strict-subperiod-act");
        var create = new CreatePeriodHandler(
            s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);
        var activate = new ActivatePeriodHandler(
            s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache, NullLogger<ActivatePeriodHandler>.Instance);

        // Tenant A creates + activates a year + term.
        AsTenant(s, TenantA);
        var ayA = await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms));
        await activate.HandleAsync(new ActivatePeriod(ayA));
        var termA = await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ayA));
        await activate.HandleAsync(new ActivatePeriod(termA));

        // Tenant B cannot activate A's term (not visible through the filter).
        AsTenant(s, TenantB);
        var act = async () => await activate.HandleAsync(new ActivatePeriod(termA));
        await act.Should().ThrowAsync<PeriodNotFoundException>(
            "B cannot see or activate A's sub-period");

        // A's term remains Active.
        AsTenant(s, TenantA);
        (await s.Db.Periods.SingleAsync(p => p.Id == termA)).Status.Should().Be(PeriodStatus.Active);
    }

    // FR-4: creating a sub-period under the default (Guid.Empty) tenant is rejected
    // before any write.
    [TestMethod]
    public async Task FR4_CreateSubPeriod_UnderDefaultTenant_ThrowsBeforeAnyWrite()
    {
        using var s = new StudentsTestScope("strict-fr4-subperiod");
        AsDefault(s);
        var h = new CreatePeriodHandler(
            s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

        var act = async () => await h.HandleAsync(
            new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
                AcademicYearDivision.Terms, ParentPeriodId: Guid.NewGuid()));
        await act.Should().ThrowAsync<TenantContextRequiredException>();
        (await s.Db.Periods.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(0);
    }
}
