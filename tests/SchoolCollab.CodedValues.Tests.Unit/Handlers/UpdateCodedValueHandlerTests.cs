using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Caching.Hybrid;
using Moq;
using SchoolCollab.CodedValues.Core.Commands.UpdateCodedValue;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Tests.Unit.Handlers;

[TestClass]
public class UpdateCodedValueHandlerTests
{
    private Mock<ICodedValueRepository> _repository = default!;
    private Mock<IPublishEndpoint> _publishEndpoint = default!;
    private Mock<HybridCache> _cache = default!;
    private UpdateCodedValueHandler _handler = default!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<ICodedValueRepository>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _cache = new Mock<HybridCache>();
        _handler = new UpdateCodedValueHandler(_repository.Object, _publishEndpoint.Object, _cache.Object);
    }

    [TestMethod]
    public async Task HandleAsync_WhenFound_UpdatesAndPublishes()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);

        await _handler.HandleAsync(new UpdateCodedValue(cv.Id, "Gender Updated", "desc", 1));

        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<SchoolCollab.CodedValues.Contracts.Events.CodedValueUpdated>(), default), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_WhenNotFound_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync((CodedValue?)null);

        var act = async () => await _handler.HandleAsync(new UpdateCodedValue(Guid.NewGuid(), "Name", null, 0));

        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }
}
