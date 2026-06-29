using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.CreateCodedValue;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.CodedValues.Tests.Unit.Handlers;

[TestClass]
public class CreateCodedValueHandlerTests
{
    private Mock<ICodedValueRepository> _repository = default!;
    private Mock<IIntegrationEventPublisher> _publisher = default!;
    private Mock<HybridCache> _cache = default!;
    private Mock<ILogger<CreateCodedValueHandler>> _logger = default!;
    private CreateCodedValueHandler _handler = default!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<ICodedValueRepository>();
        _publisher = new Mock<IIntegrationEventPublisher>();
        _cache = new Mock<HybridCache>();
        _logger = new Mock<ILogger<CreateCodedValueHandler>>();
        _handler = new CreateCodedValueHandler(_repository.Object, _publisher.Object, _cache.Object, _logger.Object);
    }

    [TestMethod]
    public async Task HandleAsync_WithNewCode_CreatesAndEnqueues()
    {
        _repository.Setup(r => r.ExistsByCodeInParentAsync("GENDER", null, default)).ReturnsAsync(false);

        await _handler.HandleAsync(new CreateCodedValue("gender", "Gender", null, null, 0));

        _repository.Verify(r => r.AddAsync(It.Is<CodedValue>(cv => cv.Code == "GENDER"), default), Times.Once);
        _publisher.Verify(p => p.EnqueueAsync(It.IsAny<SchoolCollab.CodedValues.Contracts.Events.CodedValueCreated>(), default), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_WithDuplicateCode_ThrowsDuplicateCodeException()
    {
        _repository.Setup(r => r.ExistsByCodeInParentAsync("GENDER", null, default)).ReturnsAsync(true);

        var act = async () => await _handler.HandleAsync(new CreateCodedValue("gender", "Gender", null, null, 0));

        await act.Should().ThrowAsync<DuplicateCodeException>();
        _repository.Verify(r => r.AddAsync(It.IsAny<CodedValue>(), default), Times.Never);
    }

    [TestMethod]
    public async Task HandleAsync_SameCodeUnderDifferentParent_IsAllowed()
    {
        // Code "GENDER" exists as a root, but not under parent "HSPTL"
        _repository.Setup(r => r.ExistsByCodeInParentAsync("GENDER", null, default)).ReturnsAsync(true);
        var parentId = Guid.NewGuid();
        _repository.Setup(r => r.ExistsByCodeInParentAsync("GENDER", parentId, default)).ReturnsAsync(false);

        // Creating "GENDER" under a parent should succeed
        await _handler.HandleAsync(new CreateCodedValue("gender", "Gender", null, parentId, 0));

        _repository.Verify(r => r.AddAsync(It.Is<CodedValue>(cv => cv.Code == "GENDER" && cv.ParentId == parentId), default), Times.Once);
    }
}
