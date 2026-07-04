using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Moq;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.DisableCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.EnableCodedValue;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class DisableEnableCodedValueHandlerTests
{
    private Mock<ICodedValueRepository> _repository = default!;
    private Mock<IIntegrationEventPublisher> _publisher = default!;
    private Mock<HybridCache> _cache = default!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<ICodedValueRepository>();
        _publisher = new Mock<IIntegrationEventPublisher>();
        _cache = new Mock<HybridCache>();
    }

    [TestMethod]
    public async Task DisableHandler_WhenFound_DisablesAndEnqueues()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new DisableCodedValueHandler(_repository.Object, _publisher.Object, _cache.Object);

        await handler.HandleAsync(new DisableCodedValue(cv.Id));

        cv.IsDisabled.Should().BeTrue();
        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
        _publisher.Verify(p => p.EnqueueAsync(It.IsAny<SchoolCollab.Settings.Contracts.Events.CodedValueDisabled>(), default), Times.Once);
    }

    [TestMethod]
    public async Task DisableHandler_WhenAlreadyDisabled_IsIdempotent_NoEnqueue()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.Disable();
        cv.ClearDomainEvents();
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new DisableCodedValueHandler(_repository.Object, _publisher.Object, _cache.Object);

        await handler.HandleAsync(new DisableCodedValue(cv.Id));

        _publisher.Verify(p => p.EnqueueAsync(It.IsAny<object>(), default), Times.Never);
    }

    [TestMethod]
    public async Task DisableHandler_WhenNotFound_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync((CodedValue?)null);
        var handler = new DisableCodedValueHandler(_repository.Object, _publisher.Object, _cache.Object);

        var act = async () => await handler.HandleAsync(new DisableCodedValue(Guid.NewGuid()));

        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }

    [TestMethod]
    public async Task EnableHandler_WhenDisabled_EnablesAndEnqueues()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.Disable();
        cv.ClearDomainEvents();
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new EnableCodedValueHandler(_repository.Object, _publisher.Object, _cache.Object);

        await handler.HandleAsync(new EnableCodedValue(cv.Id));

        cv.IsDisabled.Should().BeFalse();
        _publisher.Verify(p => p.EnqueueAsync(It.IsAny<SchoolCollab.Settings.Contracts.Events.CodedValueEnabled>(), default), Times.Once);
    }
}
