using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Pure unit tests for <see cref="TopicEditRouter"/>. The dialog's submit path
/// routes through this decision, so these tests cover requirement 5 (override-first,
/// in-place when no override) and the tcv/3 provisional fallback (code + description
/// both changed) without a bUnit render (bUnit dialog submit deadlocks on the async
/// HTTP path — see TopicEditDialogTests notes).
/// </summary>
[TestClass]
public class TopicEditRouterTests
{
    private static CodedValueDto Cv(
        string code = "MATH",
        string? description = null,
        bool isOverridden = false) => new(
            Id: System.Guid.NewGuid(),
            Code: code,
            Name: "Mathematics",
            Description: description,
            ParentId: null,
            ParentCode: null,
            IsDisabled: false,
            DisplayOrder: 0,
            CreatedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch,
            Attributes: [],
            AttributeDefinitions: [],
            IsOverridden: isOverridden);

    [TestMethod]
    public void Decide_NoCodedValue_ReturnsDirectNameOnly()
    {
        var plan = TopicEditRouter.Decide(null, "Physics", "PHY", "Physics subject", 2);

        plan.Action.Should().Be(TopicEditRouter.Action.DirectNameOnly);
        plan.CodedValueId.Should().BeNull();
        plan.Name.Should().Be("Physics");
    }

    [TestMethod]
    public void Decide_CodeAndDescriptionBothChanged_ReturnsCreateProvisional()
    {
        var plan = TopicEditRouter.Decide(Cv("MATH", "Math description"), "Biology", "BIO", "Life sciences", 1);

        plan.Action.Should().Be(TopicEditRouter.Action.CreateProvisional);
        plan.CodedValueId.Should().NotBeNull();
        plan.Code.Should().Be("BIO");
        plan.Description.Should().Be("Life sciences");
    }

    [TestMethod]
    public void Decide_OnlyCodeChanged_ReturnsOverrideEvenWhenNotOverridden()
    {
        // A code change always routes through the override mechanism (editing a code
        // in place is unsupported), regardless of whether an override already exists.
        var plan = TopicEditRouter.Decide(Cv("MATH"), "Mathematics", "CS01", null, 0);

        plan.Action.Should().Be(TopicEditRouter.Action.Override);
        plan.Code.Should().Be("CS01");
        plan.Description.Should().BeNull("description did not change");
    }

    [TestMethod]
    public void Decide_OverrideExists_NameChanged_ReturnsOverride()
    {
        var plan = TopicEditRouter.Decide(Cv("MATH", isOverridden: true), "Algebra", "MATH", null, 0);

        plan.Action.Should().Be(TopicEditRouter.Action.Override);
        plan.Code.Should().BeNull("code did not change");
        plan.Description.Should().BeNull("description did not change");
    }

    [TestMethod]
    public void Decide_OverrideExists_DescriptionChanged_ReturnsOverrideWithDescription()
    {
        var plan = TopicEditRouter.Decide(Cv("MATH", "Old desc", isOverridden: true), "Mathematics", "MATH", "New desc", 0);

        plan.Action.Should().Be(TopicEditRouter.Action.Override);
        plan.Description.Should().Be("New desc");
        plan.Code.Should().BeNull("code did not change");
    }

    [TestMethod]
    public void Decide_NoOverride_NameChanged_ReturnsEditInPlace()
    {
        var plan = TopicEditRouter.Decide(Cv("MATH"), "Algebra", "MATH", null, 0);

        plan.Action.Should().Be(TopicEditRouter.Action.EditInPlace);
        plan.CodedValueId.Should().NotBeNull();
    }

    [TestMethod]
    public void Decide_NoOverride_DescriptionChanged_ReturnsEditInPlace()
    {
        var plan = TopicEditRouter.Decide(Cv("MATH", "Old desc"), "Mathematics", "MATH", "New desc", 0);

        plan.Action.Should().Be(TopicEditRouter.Action.EditInPlace);
        plan.Description.Should().Be("New desc");
        plan.Code.Should().BeNull("code did not change");
    }

    [TestMethod]
    public void Decide_NoChange_ReturnsEditInPlace()
    {
        var plan = TopicEditRouter.Decide(Cv("MATH", "Math"), "Mathematics", "MATH", "Math", 0);

        plan.Action.Should().Be(TopicEditRouter.Action.EditInPlace);
    }
}
