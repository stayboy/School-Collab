using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpdateCodedValue;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class UpdateCodedValueHandlerTests
{
    private Mock<ICodedValueRepository> _repository = default!;
    private Mock<IIntegrationEventPublisher> _publisher = default!;
    private Mock<HybridCache> _cache = default!;
    private Mock<ILogger<UpdateCodedValueHandler>> _logger = default!;
    private UpdateCodedValueHandler _handler = default!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<ICodedValueRepository>();
        _publisher = new Mock<IIntegrationEventPublisher>();
        _cache = new Mock<HybridCache>();
        _logger = new Mock<ILogger<UpdateCodedValueHandler>>();
        _handler = new UpdateCodedValueHandler(_repository.Object, _publisher.Object, _cache.Object, _logger.Object);
    }

    [TestMethod]
    public async Task HandleAsync_WhenFound_UpdatesAndEnqueues()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);

        await _handler.HandleAsync(new UpdateCodedValue(cv.Id, "Gender Updated", "desc", 1));

        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
        _publisher.Verify(p => p.EnqueueAsync(It.IsAny<SchoolCollab.Settings.Contracts.Events.CodedValueUpdated>(), default), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_WhenNotFound_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync((CodedValue?)null);

        var act = async () => await _handler.HandleAsync(new UpdateCodedValue(Guid.NewGuid(), "Name", null, 0));

        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }
}
