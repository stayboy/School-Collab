using FluentAssertions;
using MassTransit;
using Moq;
using SchoolCollab.CodedValues.Core.Commands.DisableCodedValue;
using SchoolCollab.CodedValues.Core.Commands.EnableCodedValue;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Tests.Unit.Handlers;

[TestClass]
public class DisableEnableCodedValueHandlerTests
{
    private Mock<ICodedValueRepository> _repository = default!;
    private Mock<IPublishEndpoint> _publishEndpoint = default!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<ICodedValueRepository>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
    }

    [TestMethod]
    public async Task DisableHandler_WhenFound_DisablesAndPublishes()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new DisableCodedValueHandler(_repository.Object, _publishEndpoint.Object);

        await handler.HandleAsync(new DisableCodedValue(cv.Id));

        cv.IsDisabled.Should().BeTrue();
        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<SchoolCollab.CodedValues.Contracts.Events.CodedValueDisabled>(), default), Times.Once);
    }

    [TestMethod]
    public async Task DisableHandler_WhenAlreadyDisabled_IsIdempotent_NoPublish()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.Disable();
        cv.ClearDomainEvents();
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new DisableCodedValueHandler(_repository.Object, _publishEndpoint.Object);

        await handler.HandleAsync(new DisableCodedValue(cv.Id));

        _publishEndpoint.Verify(p => p.Publish(It.IsAny<object>(), default), Times.Never);
    }

    [TestMethod]
    public async Task DisableHandler_WhenNotFound_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync((CodedValue?)null);
        var handler = new DisableCodedValueHandler(_repository.Object, _publishEndpoint.Object);

        var act = async () => await handler.HandleAsync(new DisableCodedValue(Guid.NewGuid()));

        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }

    [TestMethod]
    public async Task EnableHandler_WhenDisabled_EnablesAndPublishes()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.Disable();
        cv.ClearDomainEvents();
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new EnableCodedValueHandler(_repository.Object, _publishEndpoint.Object);

        await handler.HandleAsync(new EnableCodedValue(cv.Id));

        cv.IsDisabled.Should().BeFalse();
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<SchoolCollab.CodedValues.Contracts.Events.CodedValueEnabled>(), default), Times.Once);
    }
}
