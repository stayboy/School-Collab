using FluentAssertions;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.CodedValues.Tests.Unit.Tenancy;

[TestClass]
public class TenantProviderTests
{
    [TestMethod]
    public void GetTenantContext_WhenNoTenantSet_ReturnsSystemContext()
    {
        var provider = new TenantProvider();

        provider.GetTenantContext().Should().Be(new TenantContext(Guid.Empty, "System", TenantType.Organization));
    }

    [TestMethod]
    public void SetTenant_UpdatesCurrentTenantContext()
    {
        var provider = new TenantProvider();
        var tenant = new TenantContext(Guid.Parse("11111111-1111-1111-1111-111111111111"), "North High", TenantType.School);

        provider.SetTenant(tenant);

        provider.GetTenantContext().Should().Be(tenant);
    }

    [TestMethod]
    public void Clear_RemovesCurrentTenantContext()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(Guid.NewGuid(), "North High", TenantType.School));

        provider.Clear();

        provider.GetTenantContext().Should().Be(new TenantContext(Guid.Empty, "System", TenantType.Organization));
    }
}
