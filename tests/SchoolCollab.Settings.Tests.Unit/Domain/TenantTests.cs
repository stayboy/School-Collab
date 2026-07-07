using FluentAssertions;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit.Domain;

[TestClass]
public class TenantTests
{
    [TestMethod]
    public void Create_WithValidData_SetsPropertiesAndFreshId()
    {
        var tenant = Tenant.Create("Hydeson School", TenantType.School);

        tenant.Id.Should().NotBeEmpty();
        tenant.Name.Should().Be("Hydeson School");
        tenant.Type.Should().Be(TenantType.School);
        tenant.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        tenant.UpdatedAt.Should().Be(tenant.CreatedAt);
    }

    [TestMethod]
    public void Create_TrimsName()
    {
        var tenant = Tenant.Create("  Little Legends  ", TenantType.School);
        tenant.Name.Should().Be("Little Legends");
    }

    [TestMethod]
    public void Create_GeneratesDistinctIds()
    {
        // No hardcoded Guids: every Create call mints a fresh id (spec §0 decision 2).
        var a = Tenant.Create("A", TenantType.School);
        var b = Tenant.Create("B", TenantType.School);
        a.Id.Should().NotBe(b.Id);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_RejectsEmptyName(string? name)
    {
        var act = () => Tenant.Create(name!, TenantType.School);
        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public void Create_RejectsOverlongName()
    {
        var act = () => Tenant.Create(new string('x', 201), TenantType.School);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Update_ChangesNameAndTypeAndStampsUpdatedAt()
    {
        var tenant = Tenant.Create("Old Name", TenantType.School);
        var originalUpdatedAt = tenant.UpdatedAt;

        // Ensure the UpdatedAt clock can tick.
        Thread.Sleep(20);

        tenant.Update("New Name", TenantType.Organization);

        tenant.Name.Should().Be("New Name");
        tenant.Type.Should().Be(TenantType.Organization);
        tenant.UpdatedAt.Should().BeAfter(originalUpdatedAt);
        tenant.CreatedAt.Should().Be(originalUpdatedAt); // never overwritten
    }
}