using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.ReplaceEntityCodeRuleOverrides;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class ReplaceEntityCodeRuleOverridesHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RuleId = Guid.NewGuid();
    private static readonly Guid StampSegmentId = Guid.NewGuid();
    private static readonly Guid NumericSegmentId = Guid.NewGuid();

    private static ITenantProvider Tenants(Guid tenantId)
    {
        var mock = new Mock<ITenantProvider>(MockBehavior.Strict);
        mock.Setup(x => x.GetTenantContext())
            .Returns(new TenantContext(tenantId, $"tenant-{tenantId}", TenantType.Organization));
        return mock.Object;
    }

    private static EntityCodeRule ExistingRule()
    {
        var rule = EntityCodeRule.Create("STUDENT_CODE", "Student Template", null, isActive: true);
        rule.AddSegment(EntityCodeSegment.Fixed(0, "stamp", "STU"));
        rule.AddSegment(EntityCodeSegment.Sequence(1, null, SegmentType.NumericSequence, prefix: "", minWidth: 2, upperLimit: "99"));
        return rule;
    }

    /// <summary>Stub rule repository that returns the supplied rule by id.</summary>
    private static Mock<IEntityCodeRuleRepository> RuleRepo(EntityCodeRule rule)
    {
        var mock = new Mock<IEntityCodeRuleRepository>(MockBehavior.Strict);
        mock.Setup(r => r.GetByIdAsync(rule.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule);
        return mock;
    }

    /// <summary>Stub rule repository that always returns null (rule not found).</summary>
    private static Mock<IEntityCodeRuleRepository> MissingRuleRepo()
    {
        var mock = new Mock<IEntityCodeRuleRepository>(MockBehavior.Strict);
        mock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityCodeRule?)null);
        return mock;
    }

    [TestMethod]
    public async Task HandleAsync_UnknownRule_ThrowsNotFound()
    {
        var ruleMock = MissingRuleRepo();
        var overrideMock = new Mock<ITenantEntityCodeRuleOverrideRepository>(MockBehavior.Strict);

        var handler = new ReplaceEntityCodeRuleOverridesHandler(
            ruleMock.Object, overrideMock.Object, Tenants(TenantA),
            NullLogger<ReplaceEntityCodeRuleOverridesHandler>.Instance);

        var command = new ReplaceEntityCodeRuleOverrides(Guid.NewGuid(), []);

        var act = async () => await handler.HandleAsync(command);
        await act.Should().ThrowAsync<EntityCodeRuleNotFoundException>();
        overrideMock.Verify(r => r.ReplaceForRuleAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<TenantEntityCodeRuleOverride>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task HandleAsync_DefaultTenant_ThrowsInvalidOperation()
    {
        var rule = ExistingRule();
        var ruleMock = RuleRepo(rule);
        var overrideMock = new Mock<ITenantEntityCodeRuleOverrideRepository>(MockBehavior.Strict);

        var handler = new ReplaceEntityCodeRuleOverridesHandler(
            ruleMock.Object, overrideMock.Object, Tenants(Guid.Empty),
            NullLogger<ReplaceEntityCodeRuleOverridesHandler>.Instance);

        var command = new ReplaceEntityCodeRuleOverrides(rule.Id, []);

        var act = async () => await handler.HandleAsync(command);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*resolved tenant*");
    }

    [TestMethod]
    public async Task HandleAsync_ValidInput_BuildsEntitiesWithCorrectTenantIdAndCallsRepository()
    {
        var rule = ExistingRule();
        var ruleMock = RuleRepo(rule);
        var overrideMock = new Mock<ITenantEntityCodeRuleOverrideRepository>(MockBehavior.Strict);
        List<TenantEntityCodeRuleOverride>? captured = null;
        overrideMock.Setup(r => r.ReplaceForRuleAsync(rule.Id, It.IsAny<IReadOnlyList<TenantEntityCodeRuleOverride>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<TenantEntityCodeRuleOverride>, CancellationToken>((_, list, _) =>
                captured = list.ToList())
            .Returns(Task.CompletedTask);

        var handler = new ReplaceEntityCodeRuleOverridesHandler(
            ruleMock.Object, overrideMock.Object, Tenants(TenantA),
            NullLogger<ReplaceEntityCodeRuleOverridesHandler>.Instance);

        var command = new ReplaceEntityCodeRuleOverrides(rule.Id,
        [
            new EntityCodeRuleOverrideInput(Guid.Empty, StampSegmentId, (int)OverrideField.FixedText, "ABC"),
            new EntityCodeRuleOverrideInput(Guid.Empty, NumericSegmentId, (int)OverrideField.MinWidth, "4"),
        ]);

        await handler.HandleAsync(command);

        captured.Should().NotBeNull();
        captured!.Should().HaveCount(2);
        captured.Should().AllSatisfy(o => o.TenantId.Should().Be(TenantA));
        captured.Should().AllSatisfy(o => o.GenerationRuleId.Should().Be(rule.Id));
        captured.Select(o => (o.EntityCodeSegmentId, o.Field)).Should().BeEquivalentTo(new[]
        {
            (StampSegmentId, OverrideField.FixedText),
            (NumericSegmentId, OverrideField.MinWidth),
        });
    }

    [TestMethod]
    public async Task HandleAsync_UnknownFieldValue_ThrowsArgumentException()
    {
        var rule = ExistingRule();
        var ruleMock = RuleRepo(rule);
        var overrideMock = new Mock<ITenantEntityCodeRuleOverrideRepository>(MockBehavior.Strict);

        var handler = new ReplaceEntityCodeRuleOverridesHandler(
            ruleMock.Object, overrideMock.Object, Tenants(TenantA),
            NullLogger<ReplaceEntityCodeRuleOverridesHandler>.Instance);

        var command = new ReplaceEntityCodeRuleOverrides(rule.Id,
        [
            new EntityCodeRuleOverrideInput(Guid.Empty, StampSegmentId, 999, "whatever"),
        ]);

        var act = async () => await handler.HandleAsync(command);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unknown OverrideField*");
    }

    [TestMethod]
    public async Task HandleAsync_BlankValue_ThrowsArgumentException()
    {
        var rule = ExistingRule();
        var ruleMock = RuleRepo(rule);
        var overrideMock = new Mock<ITenantEntityCodeRuleOverrideRepository>(MockBehavior.Strict);

        var handler = new ReplaceEntityCodeRuleOverridesHandler(
            ruleMock.Object, overrideMock.Object, Tenants(TenantA),
            NullLogger<ReplaceEntityCodeRuleOverridesHandler>.Instance);

        var command = new ReplaceEntityCodeRuleOverrides(rule.Id,
        [
            new EntityCodeRuleOverrideInput(Guid.Empty, StampSegmentId, (int)OverrideField.FixedText, "   "),
        ]);

        var act = async () => await handler.HandleAsync(command);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*value is required*");
    }

    [TestMethod]
    public async Task HandleAsync_ExistingId_UsesRehydratePath()
    {
        var rule = ExistingRule();
        var ruleMock = RuleRepo(rule);
        var existingId = Guid.NewGuid();
        var overrideMock = new Mock<ITenantEntityCodeRuleOverrideRepository>(MockBehavior.Strict);
        List<TenantEntityCodeRuleOverride>? captured = null;
        overrideMock.Setup(r => r.ReplaceForRuleAsync(rule.Id, It.IsAny<IReadOnlyList<TenantEntityCodeRuleOverride>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<TenantEntityCodeRuleOverride>, CancellationToken>((_, list, _) =>
                captured = list.ToList())
            .Returns(Task.CompletedTask);

        var handler = new ReplaceEntityCodeRuleOverridesHandler(
            ruleMock.Object, overrideMock.Object, Tenants(TenantA),
            NullLogger<ReplaceEntityCodeRuleOverridesHandler>.Instance);

        var command = new ReplaceEntityCodeRuleOverrides(rule.Id,
        [
            new EntityCodeRuleOverrideInput(existingId, StampSegmentId, (int)OverrideField.FixedText, "ABC"),
        ]);

        await handler.HandleAsync(command);

        captured.Should().NotBeNull();
        captured!.Should().ContainSingle(o => o.Id == existingId,
            "the Rehydrate path preserves the existing id so the repository treats it as an update");
    }
}
