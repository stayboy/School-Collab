using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.CreateEntityCodeRule;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class CreateEntityCodeRuleHandlerTests
{
    private static readonly Guid RealTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Mock<ITenantProvider> NewTenantProvider(Guid tenantId) =>
        new(MockBehavior.Strict);

    [TestInitialize]
    public void Setup()
    {
        // Default to a real tenant; CreateCommand handler reads current tenant.
    }

    private static CreateEntityCodeRuleHandler NewHandler(
        Mock<IEntityCodeRuleRepository> repository,
        Mock<ITenantProvider> tenants,
        Mock<IIntegrationEventPublisher> publisher)
    {
        tenants.Setup(p => p.GetTenantContext())
               .Returns(new TenantContext(RealTenant, "Test", TenantType.School));
        return new CreateEntityCodeRuleHandler(
            repository.Object,
            publisher.Object,
            tenants.Object,
            NullLogger<CreateEntityCodeRuleHandler>.Instance);
    }

    private static List<EntityCodeSegmentInput> SampleSegments() =>
    [
        new(0, "stamp", SegmentType.Fixed, FixedText: "STU", Prefix: null, Suffix: "",
             ResetPeriod: ResetPeriod.None, MinWidth: 0, UpperLimit: null),
        new(1, null, SegmentType.AlphanumericSequence, FixedText: null, Prefix: "A", Suffix: "",
             ResetPeriod: ResetPeriod.None, MinWidth: 2, UpperLimit: "09"),
    ];

    [TestMethod]
    public async Task HandleAsync_NewCode_PersistsRuleAndSegments_AndStampsTenant()
    {
        var repository = new Mock<IEntityCodeRuleRepository>();
        repository.Setup(r => r.GetActiveByCodeAsync("STUDENT_CODE", default)).ReturnsAsync((EntityCodeRule?)null);

        var tenants = NewTenantProvider(RealTenant);
        var publisher = new Mock<IIntegrationEventPublisher>();
        var handler = NewHandler(repository, tenants, publisher);

        var id = await handler.HandleAsync(new CreateEntityCodeRule(
            "STUDENT_CODE", "Student Code Template", null, true, SampleSegments()));

        id.Should().NotBe(Guid.Empty);
        repository.Verify(r => r.AddAsync(
            It.Is<EntityCodeRule>(rule =>
                rule.Code == "STUDENT_CODE" &&
                rule.IsActive &&
                rule.TenantId == RealTenant &&
                rule.Segments.Count == 2 &&
                rule.Segments.OrderBy(s => s.Index).First().Type == SegmentType.Fixed &&
                rule.Segments.OrderBy(s => s.Index).ElementAt(1).Type == SegmentType.AlphanumericSequence),
            default), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_DuplicateCode_ThrowsConflict()
    {
        var existingId = Guid.NewGuid();
        var existing = EntityCodeRule.Create("STUDENT_CODE", "Existing", null, true);
        typeof(EntityCodeRule).GetProperty(nameof(EntityCodeRule.Id))!.SetValue(existing, existingId);

        var repository = new Mock<IEntityCodeRuleRepository>();
        repository.Setup(r => r.GetActiveByCodeAsync("STUDENT_CODE", default)).ReturnsAsync(existing);

        var tenants = NewTenantProvider(RealTenant);
        var publisher = new Mock<IIntegrationEventPublisher>();
        var handler = NewHandler(repository, tenants, publisher);

        var act = async () => await handler.HandleAsync(new CreateEntityCodeRule(
            "STUDENT_CODE", "Duplicate", null, true, SampleSegments()));

        var ex = await act.Should().ThrowAsync<EntityCodeRuleCodeConflictException>();
        ex.Which.RuleCode.Should().Be("STUDENT_CODE");
        ex.Which.ExistingRuleId.Should().Be(existingId);
        repository.Verify(r => r.AddAsync(It.IsAny<EntityCodeRule>(), default), Times.Never);
    }
}