using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
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

        var period = Period.Create("Term 1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
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

        var period = Period.Create("Term A", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
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
}
