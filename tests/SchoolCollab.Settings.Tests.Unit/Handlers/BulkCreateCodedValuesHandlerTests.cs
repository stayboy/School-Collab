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
public class BulkCreateCodedValuesHandlerTests
{
    private Mock<ICodedValueRepository> _repository = default!;
    private Mock<ITenantProvider> _tenantProvider = default!;
    private Mock<ITenantContextAccessor> _tenantContextAccessor = default!;
    private Mock<HybridCache> _cache = default!;
    private Mock<SchoolCollab.Core.Messaging.IIntegrationEventPublisher> _publisher = default!;
    private Mock<ILogger<BulkCreateCodedValuesHandler>> _logger = default!;
    private BulkCreateCodedValuesHandler _handler = default!;

    private static readonly Guid ParentId = Guid.NewGuid();

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<ICodedValueRepository>();
        _tenantProvider = new Mock<ITenantProvider>();
        _tenantContextAccessor = new Mock<ITenantContextAccessor>();
        _cache = new Mock<HybridCache>();
        _publisher = new Mock<SchoolCollab.Core.Messaging.IIntegrationEventPublisher>();
        _logger = new Mock<ILogger<BulkCreateCodedValuesHandler>>();

        // Default: real tenant — so tests verify the tenant-owned path
        _tenantProvider.Setup(p => p.GetTenantContext())
            .Returns(new TenantContext(Guid.NewGuid(), "Test", TenantType.School));

        // SuppressTenantGuard returns a disposable
        _tenantContextAccessor.Setup(a => a.SuppressTenantGuard())
            .Returns(new NoOpDisposable());

        _handler = new BulkCreateCodedValuesHandler(
            _repository.Object,
            _publisher.Object,
            _tenantProvider.Object,
            _tenantContextAccessor.Object,
            _cache.Object,
            _logger.Object);
    }

    [TestMethod]
    public async Task HandleAsync_AllNewCodes_CreatesAll()
    {
        // Arrange
        var parent = CodedValue.Create("PKTYPES", "Packaging Types", null, null, 0);
        _repository.Setup(r => r.GetAsync(ParentId, default)).ReturnsAsync(parent);
        _repository.Setup(r => r.ExistsByCodeInParentAsync(It.IsAny<string>(), ParentId, default)).ReturnsAsync(false);

        var children = new List<BulkCreateChildItem>
        {
            new("BOX", "Box", "Cardboard box", 1),
            new("CRATE", "Crate", "Wooden crate", 2),
        };

        var command = new BulkCreateCodedValues(ParentId, children);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.CreatedCount.Should().Be(2);
        result.SkippedCodes.Should().BeEmpty();
        result.ParentId.Should().Be(ParentId);
        _repository.Verify(r => r.AddRangeAsync(It.Is<List<CodedValue>>(list => list.Count == 2), default), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_SomeExistingCodes_SkipsExistingAndCreatesNew()
    {
        // Arrange
        var parent = CodedValue.Create("PKTYPES", "Packaging Types", null, null, 0);
        _repository.Setup(r => r.GetAsync(ParentId, default)).ReturnsAsync(parent);
        // "BOX" already exists, "CRATE" does not
        _repository.Setup(r => r.ExistsByCodeInParentAsync("BOX", ParentId, default)).ReturnsAsync(true);
        _repository.Setup(r => r.ExistsByCodeInParentAsync("CRATE", ParentId, default)).ReturnsAsync(false);

        var children = new List<BulkCreateChildItem>
        {
            new("BOX", "Box", "Cardboard box", 1),
            new("CRATE", "Crate", "Wooden crate", 2),
        };

        var command = new BulkCreateCodedValues(ParentId, children);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.CreatedCount.Should().Be(1);
        result.SkippedCodes.Should().ContainSingle().Which.Should().Be("BOX");
        result.ParentId.Should().Be(ParentId);
        _repository.Verify(r => r.AddRangeAsync(It.Is<List<CodedValue>>(list => list.Count == 1), default), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_AllCodesAlreadyExist_CreatesNone()
    {
        // Arrange
        var parent = CodedValue.Create("PKTYPES", "Packaging Types", null, null, 0);
        _repository.Setup(r => r.GetAsync(ParentId, default)).ReturnsAsync(parent);
        _repository.Setup(r => r.ExistsByCodeInParentAsync(It.IsAny<string>(), ParentId, default)).ReturnsAsync(true);

        var children = new List<BulkCreateChildItem>
        {
            new("BOX", "Box", null, 1),
            new("CRATE", "Crate", null, 2),
        };

        var command = new BulkCreateCodedValues(ParentId, children);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.CreatedCount.Should().Be(0);
        result.SkippedCodes.Should().HaveCount(2);
        result.SkippedCodes.Should().Contain("BOX", "CRATE");
        _repository.Verify(r => r.AddRangeAsync(It.IsAny<List<CodedValue>>(), default), Times.Never);
    }

    [TestMethod]
    public async Task HandleAsync_IntraBatchDuplicate_ThrowsDuplicateCodeException()
    {
        // Arrange
        var parent = CodedValue.Create("PKTYPES", "Packaging Types", null, null, 0);
        _repository.Setup(r => r.GetAsync(ParentId, default)).ReturnsAsync(parent);
        _repository.Setup(r => r.ExistsByCodeInParentAsync(It.IsAny<string>(), ParentId, default)).ReturnsAsync(false);

        // Same code "BOX" appears twice in the batch
        var children = new List<BulkCreateChildItem>
        {
            new("BOX", "Box", null, 1),
            new("box", "Box Duplicate", null, 2),
        };

        var command = new BulkCreateCodedValues(ParentId, children);

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<DuplicateCodeException>();
    }

    [TestMethod]
    public async Task HandleAsync_ParentNotFound_ThrowsCodedValueNotFoundException()
    {
        // Arrange
        _repository.Setup(r => r.GetAsync(ParentId, default)).ReturnsAsync((CodedValue?)null);

        var children = new List<BulkCreateChildItem>
        {
            new("BOX", "Box", null, 1),
        };

        var command = new BulkCreateCodedValues(ParentId, children);

        // Act
        var act = async () => await _handler.HandleAsync(command);

        // Assert
        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }

    [TestMethod]
    public async Task HandleAsync_SkippedCodesIncludesDescriptions()
    {
        // Arrange
        var parent = CodedValue.Create("DISEASES", "Diseases", "Medical conditions", null, 0);
        _repository.Setup(r => r.GetAsync(ParentId, default)).ReturnsAsync(parent);
        // "MALARIA" already exists, "TYPHOID" does not
        _repository.Setup(r => r.ExistsByCodeInParentAsync("MALARIA", ParentId, default)).ReturnsAsync(true);
        _repository.Setup(r => r.ExistsByCodeInParentAsync("TYPHOID", ParentId, default)).ReturnsAsync(false);

        var children = new List<BulkCreateChildItem>
        {
            new("MALARIA", "Malaria", "Mosquito-borne disease", 1),
            new("TYPHOID", "Typhoid", "Waterborne disease", 2),
        };

        var command = new BulkCreateCodedValues(ParentId, children);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.CreatedCount.Should().Be(1);
        result.SkippedCodes.Should().ContainSingle().Which.Should().Be("MALARIA");
        _repository.Verify(r => r.AddRangeAsync(It.Is<List<CodedValue>>(list => list.Count == 1), default), Times.Once);
    }

    // AC-14: real tenant → tenant-owned rows stamped with current tenant
    [TestMethod]
    public async Task HandleAsync_RealTenant_StampsTenantIdOnCreatedRows()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantProvider.Setup(p => p.GetTenantContext())
            .Returns(new TenantContext(tenantId, "Test", TenantType.School));

        var parent = CodedValue.Create("PKTYPES", "Packaging Types", null, null, 0);
        _repository.Setup(r => r.GetAsync(ParentId, default)).ReturnsAsync(parent);
        _repository.Setup(r => r.ExistsByCodeInParentAsync(It.IsAny<string>(), ParentId, default)).ReturnsAsync(false);

        var children = new List<BulkCreateChildItem>
        {
            new("BOX", "Box", null, 1),
            new("CRATE", "Crate", null, 2),
        };

        var command = new BulkCreateCodedValues(ParentId, children);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.CreatedCount.Should().Be(2);
        _repository.Verify(r => r.AddRangeAsync(
            It.Is<List<CodedValue>>(list => list.All(c => c.TenantId == tenantId)),
            default), Times.Once);
    }

    // AC-15: default/dev tenant (Guid.Empty) → NULL blueprint rows, guard suppressed
    [TestMethod]
    public async Task HandleAsync_DefaultTenant_CreatesNullBlueprints_AndSuppressesGuard()
    {
        // Arrange
        _tenantProvider.Setup(p => p.GetTenantContext())
            .Returns(new TenantContext(Guid.Empty, "(default)", TenantType.School));

        var parent = CodedValue.Create("PKTYPES", "Packaging Types", null, null, 0);
        _repository.Setup(r => r.GetAsync(ParentId, default)).ReturnsAsync(parent);
        _repository.Setup(r => r.ExistsByCodeInParentAsync(It.IsAny<string>(), ParentId, default)).ReturnsAsync(false);

        var children = new List<BulkCreateChildItem>
        {
            new("BOX", "Box", null, 1),
        };

        var command = new BulkCreateCodedValues(ParentId, children);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.CreatedCount.Should().Be(1);
        _repository.Verify(r => r.AddRangeAsync(
            It.Is<List<CodedValue>>(list => list.All(c => c.TenantId == null)),
            default), Times.Once);
        _tenantContextAccessor.Verify(a => a.SuppressTenantGuard(), Times.Once);
    }

    /// <summary>No-op disposable for SuppressTenantGuard mock returns.</summary>
    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
