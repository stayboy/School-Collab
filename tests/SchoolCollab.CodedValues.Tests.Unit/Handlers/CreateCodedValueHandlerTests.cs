using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.CodedValues.Core.Commands.CreateCodedValue;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Tests.Unit.Handlers;

[TestClass]
public class CreateCodedValueHandlerTests
{
    private Mock<ICodedValueRepository> _repository = default!;
    private Mock<IPublishEndpoint> _publishEndpoint = default!;
    private Mock<HybridCache> _cache = default!;
    private Mock<ILogger<CreateCodedValueHandler>> _logger = default!;
    private CreateCodedValueHandler _handler = default!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<ICodedValueRepository>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _cache = new Mock<HybridCache>();
        _logger = new Mock<ILogger<CreateCodedValueHandler>>();
        _handler = new CreateCodedValueHandler(_repository.Object, _publishEndpoint.Object, _cache.Object, _logger.Object);
    }

    [TestMethod]
    public async Task HandleAsync_WithNewCode_CreatesAndPublishes()
    {
        _repository.Setup(r => r.ExistsByCodeAsync("GENDER", default)).ReturnsAsync(false);

        await _handler.HandleAsync(new CreateCodedValue("gender", "Gender", null, null, 0));

        _repository.Verify(r => r.AddAsync(It.Is<CodedValue>(cv => cv.Code == "GENDER"), default), Times.Once);
        _publishEndpoint.Verify(p => p.Publish(It.IsAny<SchoolCollab.CodedValues.Contracts.Events.CodedValueCreated>(), default), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_WithDuplicateCode_ThrowsDuplicateCodeException()
    {
        _repository.Setup(r => r.ExistsByCodeAsync("GENDER", default)).ReturnsAsync(true);

        var act = async () => await _handler.HandleAsync(new CreateCodedValue("gender", "Gender", null, null, 0));

        await act.Should().ThrowAsync<DuplicateCodeException>();
        _repository.Verify(r => r.AddAsync(It.IsAny<CodedValue>(), default), Times.Never);
    }
}
