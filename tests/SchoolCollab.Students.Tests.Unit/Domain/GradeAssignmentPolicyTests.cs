using FluentAssertions;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit.Domain;

[TestClass]
public class GradeAssignmentPolicyTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GradeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [TestMethod]
    public void Create_DefaultsToInheritNull()
    {
        var p = GradeAssignmentPolicy.Create(TenantId, GradeId);
        p.TenantId.Should().Be(TenantId);
        p.GradeLevelId.Should().Be(GradeId);
        p.RequiresSignatureDefault.Should().BeNull();
    }

    [TestMethod]
    public void Create_WithExplicitOverride_StoresFlag()
    {
        var p = GradeAssignmentPolicy.Create(TenantId, GradeId, requiresSignatureDefault: true);
        p.RequiresSignatureDefault.Should().BeTrue();
    }

    [TestMethod]
    public void SetOverride_ToNull_RestoresInherit()
    {
        var p = GradeAssignmentPolicy.Create(TenantId, GradeId, requiresSignatureDefault: true);
        p.SetOverride(null);
        p.RequiresSignatureDefault.Should().BeNull();
    }

    [TestMethod]
    public void SetOverride_StampsUpdatedAt()
    {
        var p = GradeAssignmentPolicy.Create(TenantId, GradeId);
        var before = p.UpdatedAt;

        p.SetOverride(true);

        p.RequiresSignatureDefault.Should().BeTrue();
        p.UpdatedAt.Should().BeOnOrAfter(before);
    }
}