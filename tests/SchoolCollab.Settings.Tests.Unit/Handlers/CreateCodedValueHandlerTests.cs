using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateCodedValue;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class CreateCodedValueHandlerTests
{
    private Mock<ICodedValueRepository> _repository = default!;
    private Mock<IIntegrationEventPublisher> _publisher = default!;
    private Mock<HybridCache> _cache = default!;
    private Mock<ILogger<CreateCodedValueHandler>> _logger = default!;
    private Mock<ITenantProvider> _tenantProvider = default!;
    private Mock<ITenantContextAccessor> _tenantContextAccessor = default!;
    private CreateCodedValueHandler _handler = default!;

    private static readonly Guid RealTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<ICodedValueRepository>();
        _publisher = new Mock<IIntegrationEventPublisher>();
        _cache = new Mock<HybridCache>();
        _logger = new Mock<ILogger<CreateCodedValueHandler>>();
        _tenantProvider = new Mock<ITenantProvider>();
        _tenantContextAccessor = new Mock<ITenantContextAccessor>();
        // Default to a real tenant; individual tests override via SetTenant.
        SetTenant(RealTenant);
        // SuppressTenantGuard returns a no-op disposable by default.
        _tenantContextAccessor
            .Setup(a => a.SuppressTenantGuard())
            .Returns(NoOpDisposable.Instance);
        _handler = new CreateCodedValueHandler(
            _repository.Object, _publisher.Object, _cache.Object,
            _tenantProvider.Object, _tenantContextAccessor.Object, _logger.Object);
    }

    private void SetTenant(Guid tenantId) =>
        _tenantProvider.Setup(p => p.GetTenantContext())
            .Returns(new TenantContext(tenantId, "Test", TenantType.School));

    [TestMethod]
    public async Task HandleAsync_RealTenant_NewCode_StampsTenantOwnedRow()
    {
        _repository.Setup(r => r.FindConflictingByCodeAndParentAsync("GENDER", null, RealTenant, default))
            .ReturnsAsync((CodedValue?)null);

        await _handler.HandleAsync(new CreateCodedValue("gender", "Gender", null, null, 0));

        _repository.Verify(r => r.AddAsync(It.Is<CodedValue>(
            cv => cv.Code == "GENDER" && cv.TenantId == RealTenant), default), Times.Once);
        _publisher.Verify(p => p.EnqueueAsync(It.IsAny<SchoolCollab.Settings.Contracts.Events.CodedValueCreated>(), default), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_DefaultTenant_NewCode_WritesNullBlueprintRow()
    {
        // FR-5 / AC-7: default/dev tenant (Guid.Empty) writes a NULL shared-blueprint row.
        SetTenant(Guid.Empty);

        _repository.Setup(r => r.FindConflictingByCodeAndParentAsync("GENDER", null, null, default))
            .ReturnsAsync((CodedValue?)null);

        await _handler.HandleAsync(new CreateCodedValue("gender", "Gender", null, null, 0));

        _repository.Verify(r => r.AddAsync(It.Is<CodedValue>(
            cv => cv.Code == "GENDER" && cv.TenantId == null), default), Times.Once);
        // Guard is suppressed on the default-tenant blueprint path (FR-5).
        _tenantContextAccessor.Verify(a => a.SuppressTenantGuard(), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_RealTenant_ConflictWithSharedBlueprint_ThrowsDirectingToOverride()
    {
        // FR-6 / AC-9: a shared GRADE_1 exists; real tenant submitting GRADE_1 is rejected.
        var sharedId = Guid.NewGuid();
        var shared = CodedValue.Create("GRADE_1", "Grade 1", null, null, 1);
        typeof(CodedValue).GetProperty("Id")!.SetValue(shared, sharedId);
        // Simulate a shared (NULL) row by leaving TenantId null (default from Create).
        _repository.Setup(r => r.FindConflictingByCodeAndParentAsync("GRADE_1", null, RealTenant, default))
            .ReturnsAsync(shared);

        var act = async () => await _handler.HandleAsync(new CreateCodedValue("grade_1", "My Grade 1", null, null, 1));

        var ex = await act.Should().ThrowAsync<CodedValueCodeConflictException>();
        ex.Which.ExistingIsSharedBlueprint.Should().BeTrue("the conflict is a shared blueprint row");
        ex.Which.ExistingCodedValueId.Should().Be(sharedId);
        _repository.Verify(r => r.AddAsync(It.IsAny<CodedValue>(), default), Times.Never);
    }

    [TestMethod]
    public async Task HandleAsync_RealTenant_ConflictWithOwnOwnedRow_ThrowsDirectingToUpdate()
    {
        // The tenant's own owned MATH row exists → directed to update, not duplicate.
        var ownedId = Guid.NewGuid();
        var owned = CodedValue.Create("MATH", "Mathematics", null, Guid.NewGuid(), 0);
        typeof(CodedValue).GetProperty("Id")!.SetValue(owned, ownedId);
        owned.SetTenant(RealTenant);
        _repository.Setup(r => r.FindConflictingByCodeAndParentAsync("MATH", owned.ParentId, RealTenant, default))
            .ReturnsAsync(owned);

        var act = async () => await _handler.HandleAsync(new CreateCodedValue("math", "Math", null, owned.ParentId, 0));

        var ex = await act.Should().ThrowAsync<CodedValueCodeConflictException>();
        ex.Which.ExistingIsSharedBlueprint.Should().BeFalse("the conflict is the tenant's own owned row");
        _repository.Verify(r => r.AddAsync(It.IsAny<CodedValue>(), default), Times.Never);
    }

    [TestMethod]
    public async Task HandleAsync_SameCodeUnderDifferentParent_IsAllowed()
    {
        // Code "GENDER" exists as a root (shared), but not under parent "HSPTL".
        var parentId = Guid.NewGuid();
        _repository.Setup(r => r.FindConflictingByCodeAndParentAsync("GENDER", parentId, RealTenant, default))
            .ReturnsAsync((CodedValue?)null);

        await _handler.HandleAsync(new CreateCodedValue("gender", "Gender", null, parentId, 0));

        _repository.Verify(r => r.AddAsync(It.Is<CodedValue>(
            cv => cv.Code == "GENDER" && cv.ParentId == parentId && cv.TenantId == RealTenant), default), Times.Once);
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }
}
