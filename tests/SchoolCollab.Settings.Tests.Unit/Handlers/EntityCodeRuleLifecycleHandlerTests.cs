using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.ActivateEntityCodeRule;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.CreateEntityCodeRule;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.DeleteEntityCodeRule;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.UpdateEntityCodeRule;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

/// <summary>
/// Tests for <see cref="UpdateEntityCodeRuleHandler"/>, <see cref="DeleteEntityCodeRuleHandler"/>,
/// and <see cref="ActivateEntityCodeRuleHandler"/> (spec §4.8).
/// </summary>
[TestClass]
public class EntityCodeRuleLifecycleHandlerTests
{
    private static (EntityCodeRule rule, Mock<IEntityCodeRuleRepository> repo) NewRepoWithRule(Guid ruleId, string code, bool isActive = true)
    {
        var rule = EntityCodeRule.Create(code, code + " Template", null, isActive);
        typeof(EntityCodeRule).GetProperty(nameof(EntityCodeRule.Id))!.SetValue(rule, ruleId);
        rule.AddSegment(EntityCodeSegment.Fixed(0, "stamp", "STU"));
        rule.AddSegment(EntityCodeSegment.Sequence(1, null, SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "09"));
        var repo = new Mock<IEntityCodeRuleRepository>();
        repo.Setup(r => r.GetByIdAsync(ruleId, default)).ReturnsAsync(rule);
        return (rule, repo);
    }

    private static List<EntityCodeSegmentInput> UpdatedSegments() =>
    [
        new(0, "stamp", SegmentType.Fixed, FixedText: "STF", Prefix: null, Suffix: "",
             ResetPeriod: ResetPeriod.None, MinWidth: 0, UpperLimit: null),
        new(1, null, SegmentType.AlphanumericSequence, FixedText: null, Prefix: "Z", Suffix: "",
             ResetPeriod: ResetPeriod.Yearly, MinWidth: 3, UpperLimit: "999"),
    ];

    // ── Update ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task UpdateEntityCodeRuleHandler_ExistingRule_ReplacesSegmentsAndUpdates()
    {
        var ruleId = Guid.NewGuid();
        var (rule, repo) = NewRepoWithRule(ruleId, "STAFF_CODE");
        var handler = new UpdateEntityCodeRuleHandler(repo.Object, NullLogger<UpdateEntityCodeRuleHandler>.Instance);

        await handler.HandleAsync(new UpdateEntityCodeRule(ruleId, "Updated", "new desc", false, UpdatedSegments()));

        rule.Name.Should().Be("Updated");
        rule.Description.Should().Be("new desc");
        rule.IsActive.Should().BeFalse();
        rule.Segments.Count.Should().Be(2);
        rule.Segments.OrderBy(s => s.Index).First().FixedText.Should().Be("STF");
        rule.Segments.OrderBy(s => s.Index).ElementAt(1).Prefix.Should().Be("Z");
        rule.Segments.OrderBy(s => s.Index).ElementAt(1).ResetPeriod.Should().Be(ResetPeriod.Yearly);
        repo.Verify(r => r.UpdateAsync(rule, default), Times.Once);
    }

    [TestMethod]
    public async Task UpdateEntityCodeRuleHandler_NotFound_Throws()
    {
        var repo = new Mock<IEntityCodeRuleRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((EntityCodeRule?)null);

        var handler = new UpdateEntityCodeRuleHandler(repo.Object, NullLogger<UpdateEntityCodeRuleHandler>.Instance);

        var act = async () => await handler.HandleAsync(new UpdateEntityCodeRule(
            Guid.NewGuid(), "x", null, true, UpdatedSegments()));
        await act.Should().ThrowAsync<EntityCodeRuleNotFoundException>();
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task DeleteEntityCodeRuleHandler_ExistingRule_SoftDeletes()
    {
        var ruleId = Guid.NewGuid();
        var (rule, repo) = NewRepoWithRule(ruleId, "STUDENT_CODE");
        var handler = new DeleteEntityCodeRuleHandler(repo.Object, NullLogger<DeleteEntityCodeRuleHandler>.Instance);

        await handler.HandleAsync(new DeleteEntityCodeRule(ruleId));

        rule.IsDeleted.Should().BeTrue();
        rule.DeletedAt.Should().NotBeNull();
        repo.Verify(r => r.UpdateAsync(rule, default), Times.Once);
    }

    [TestMethod]
    public async Task DeleteEntityCodeRuleHandler_NotFound_Throws()
    {
        var repo = new Mock<IEntityCodeRuleRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((EntityCodeRule?)null);

        var handler = new DeleteEntityCodeRuleHandler(repo.Object, NullLogger<DeleteEntityCodeRuleHandler>.Instance);

        var act = async () => await handler.HandleAsync(new DeleteEntityCodeRule(Guid.NewGuid()));
        await act.Should().ThrowAsync<EntityCodeRuleNotFoundException>();
    }

    // ── Activate ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ActivateEntityCodeRuleHandler_ActivatesAndDeactivatesConflictingRule()
    {
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var (target, repo) = NewRepoWithRule(targetId, "STAFF_CODE", isActive: false);

        var other = EntityCodeRule.Create("STAFF_CODE", "Other active", null, true);
        typeof(EntityCodeRule).GetProperty(nameof(EntityCodeRule.Id))!.SetValue(other, otherId);
        var list = new List<EntityCodeRule> { target, other };
        repo.Setup(r => r.ListAsync(default)).ReturnsAsync(list);

        var handler = new ActivateEntityCodeRuleHandler(repo.Object, NullLogger<ActivateEntityCodeRuleHandler>.Instance);

        await handler.HandleAsync(new ActivateEntityCodeRule(targetId));

        target.IsActive.Should().BeTrue("the targeted rule must be activated");
        other.IsActive.Should().BeFalse("the other active rule with the same Code must be deactivated");
        // Both rules were updated (other deactivated + target activated).
        repo.Verify(r => r.UpdateAsync(It.IsAny<EntityCodeRule>(), default), Times.Exactly(2));
    }

    [TestMethod]
    public async Task ActivateEntityCodeRuleHandler_NotFound_Throws()
    {
        var repo = new Mock<IEntityCodeRuleRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((EntityCodeRule?)null);

        var handler = new ActivateEntityCodeRuleHandler(repo.Object, NullLogger<ActivateEntityCodeRuleHandler>.Instance);

        var act = async () => await handler.HandleAsync(new ActivateEntityCodeRule(Guid.NewGuid()));
        await act.Should().ThrowAsync<EntityCodeRuleNotFoundException>();
    }
}