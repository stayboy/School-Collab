using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Domain-level coverage for <see cref="ContentModule"/> and
/// <see cref="AssignmentResource"/> + the aggregate gateway methods
/// (<see cref="Assignment.AddModule"/>, <see cref="Assignment.ReorderModules"/>,
/// <see cref="Assignment.AddResource"/>, etc.) introduced in WS-A1.
/// Mirrors the conventions of <c>AssignmentTests</c> / <c>AssignmentQuestionAttachmentTests</c>.
/// </summary>
[TestClass]
public class AssignmentModuleResourceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssignmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Assignment NewDraft() =>
        Assignment.Create(
            "Algebra HW", null, AssignmentType.Digital,
            GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
            Guid.NewGuid(), null, null, null,
            createdByTeacherId: Guid.Empty)
            .WithTenant(TenantId);

    [TestMethod]
    public void AddModule_AppendsWithContiguousDisplayOrder_AndStampsTenant()
    {
        var assignment = NewDraft();
        var first = assignment.AddModule(ModuleType.Video, "https://example.com/intro.mp4", "Intro");
        var second = assignment.AddModule(ModuleType.Guide, "https://example.com/handout.pdf", "Handout", isRequired: true);

        assignment.Modules.Should().HaveCount(2);
        first.DisplayOrder.Should().Be(0, "the aggregate re-indexes DisplayOrder 0..n by list position (EC-7)");
        second.DisplayOrder.Should().Be(1);
        first.TenantId.Should().Be(TenantId, "the aggregate's tenant id is stamped on every child");
        second.TenantId.Should().Be(TenantId);
        first.IsRequired.Should().BeFalse();
        second.IsRequired.Should().BeTrue();
        assignment.Modules[0].Id.Should().Be(first.Id);
        assignment.Modules[1].Id.Should().Be(second.Id);
    }

    [TestMethod]
    public void RemoveModule_RemovesFromCollection()
    {
        var assignment = NewDraft();
        var m = assignment.AddModule(ModuleType.Video, "https://example.com/v.mp4");

        assignment.RemoveModule(m.Id);

        assignment.Modules.Should().BeEmpty();
    }

    [TestMethod]
    public void RemoveModule_UnknownId_IsNoOp()
    {
        var assignment = NewDraft();
        var m = assignment.AddModule(ModuleType.Video, "https://example.com/v.mp4");

        assignment.RemoveModule(Guid.NewGuid());

        assignment.Modules.Should().HaveCount(1, "RemoveModule silently ignores unknown ids (mirrors RemoveQuestion)");
        assignment.Modules[0].Id.Should().Be(m.Id);
    }

    [TestMethod]
    public void ReorderModules_ReindexesByGivenOrder()
    {
        var assignment = NewDraft();
        var a = assignment.AddModule(ModuleType.Video, "https://example.com/a.mp4");
        var b = assignment.AddModule(ModuleType.Video, "https://example.com/b.mp4");
        var c = assignment.AddModule(ModuleType.Video, "https://example.com/c.mp4");
        // Initial contiguous 0/1/2.

        assignment.ReorderModules(new[] { c.Id, a.Id, b.Id, Guid.NewGuid() /* unknown — must be ignored */ });

        var byId = assignment.Modules.ToDictionary(m => m.Id);
        byId[a.Id].DisplayOrder.Should().Be(1);
        byId[b.Id].DisplayOrder.Should().Be(2);
        byId[c.Id].DisplayOrder.Should().Be(0, "the reorder call set c to position 0 (first in the inbound list)");
    }

    [TestMethod]
    public void AddResource_AddsToCollection()
    {
        var assignment = NewDraft();

        var r = assignment.AddResource(ResourceKind.Url, url: "https://example.com/article");
        var f = assignment.AddResource(ResourceKind.File, storagePath: "tenants/t/staging/g/notes.pdf",
            displayName: "Notes");

        assignment.Resources.Should().HaveCount(2);
        r.ResourceKind.Should().Be(ResourceKind.Url);
        r.IncludedInGeneration.Should().BeTrue("the IncludedInGeneration default is true per WS-A1");
        f.ResourceKind.Should().Be(ResourceKind.File);
        f.StoragePath.Should().Be("tenants/t/staging/g/notes.pdf");
        f.Url.Should().BeNull("a File resource carries no Url");
    }

    [TestMethod]
    public void RemoveResource_UnknownId_IsNoOp()
    {
        var assignment = NewDraft();
        var r = assignment.AddResource(ResourceKind.Url, url: "https://example.com/article");

        assignment.RemoveResource(Guid.NewGuid());

        assignment.Resources.Should().HaveCount(1, "RemoveResource silently ignores unknown ids");
        assignment.Resources[0].Id.Should().Be(r.Id);
    }

    [TestMethod]
    public void ContentModule_Create_ThrowsOnEmptyTenantOrAssignmentOrUrl()
    {
        var actEmptyTenant = () => ContentModule.Create(
            Guid.Empty, AssignmentId, ModuleType.Video, "https://example.com/v.mp4", null, null, 0);
        actEmptyTenant.Should().Throw<ArgumentException>()
            .WithParameterName("tenantId")
            .WithMessage("Tenant id is required.*");

        var actEmptyAssignment = () => ContentModule.Create(
            TenantId, Guid.Empty, ModuleType.Video, "https://example.com/v.mp4", null, null, 0);
        actEmptyAssignment.Should().Throw<ArgumentException>()
            .WithParameterName("assignmentId")
            .WithMessage("Assignment id is required.*");

        var actNullUrl = () => ContentModule.Create(
            TenantId, AssignmentId, ModuleType.Video, "  ", null, null, 0);
        actNullUrl.Should().Throw<ArgumentException>()
            .WithParameterName("url")
            .WithMessage("Url is required.*");
    }

    [TestMethod]
    public void AssignmentResource_Create_ThrowsOnEmptyTenantOrAssignment()
    {
        var actEmptyTenant = () => AssignmentResource.Create(
            Guid.Empty, AssignmentId, ResourceKind.Url, "https://example.com", null, null);
        actEmptyTenant.Should().Throw<ArgumentException>()
            .WithParameterName("tenantId")
            .WithMessage("Tenant id is required.*");

        var actEmptyAssignment = () => AssignmentResource.Create(
            TenantId, Guid.Empty, ResourceKind.Url, "https://example.com", null, null);
        actEmptyAssignment.Should().Throw<ArgumentException>()
            .WithParameterName("assignmentId")
            .WithMessage("Assignment id is required.*");

        // Constructor hygiene only — the kind matrix is validator-owned (decision (d)).
        var ok = AssignmentResource.Create(
            TenantId, AssignmentId, ResourceKind.Url, null, null, null);
        ok.ResourceKind.Should().Be(ResourceKind.Url);
    }
}
