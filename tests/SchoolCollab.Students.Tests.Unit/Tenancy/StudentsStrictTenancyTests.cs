using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.GetOrCreateGradeLevel;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit.Tenancy;

/// <summary>
/// Acceptance criteria AC-11 (global-tenant-filter.md §6.3): GradeLevel/Subject/Period
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
        await h.HandleAsync(new CreatePeriod("H1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));
        (await s.Db.Periods.CountAsync()).Should().Be(1);

        // Tenant B creates a period with the SAME date range — succeeds (the overlap
        // check is scoped per-tenant by the Tenant filter; B has no periods yet).
        AsTenant(s, TenantB);
        await h.HandleAsync(new CreatePeriod("H1-B", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));
        (await s.Db.Periods.CountAsync()).Should().Be(1, "B sees only its own period");

        // Tenant A creates an overlapping period — throws (overlap within A only).
        AsTenant(s, TenantA);
        var act = async () => await h.HandleAsync(
            new CreatePeriod("H2", new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)));
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
            new CreatePeriod("H1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)));
        await act.Should().ThrowAsync<TenantContextRequiredException>();
        (await s.Db.Periods.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(0);
    }
}
