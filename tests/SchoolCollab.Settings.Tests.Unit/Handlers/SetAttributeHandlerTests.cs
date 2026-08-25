using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Moq;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueAttribute;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueAttributeDefinition;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.SetCodedValueAttribute;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.SetCodedValueAttributeDefinition;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class SetAttributeHandlerTests
{
    private Mock<ICodedValueRepository> _repository = default!;
    private Mock<HybridCache> _cache = default!;
    private Mock<SchoolCollab.Core.Messaging.IIntegrationEventPublisher> _publisher = default!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<ICodedValueRepository>();
        _cache = new Mock<HybridCache>();
        _publisher = new Mock<SchoolCollab.Core.Messaging.IIntegrationEventPublisher>();
    }

    [TestMethod]
    public async Task SetAttribute_AddsAttributeAndUpdates()
    {
        var cv = CodedValue.Create("STATE", "State", null, null, 0);
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new SetCodedValueAttributeHandler(_repository.Object, _publisher.Object, _cache.Object);

        await handler.HandleAsync(new SetCodedValueAttribute(cv.Id, "country", "US"));

        cv.Attributes.Should().ContainSingle(a => a.Key == "country" && a.Value == "US");
        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
    }

    [TestMethod]
    public async Task RemoveAttribute_RemovesAttributeAndUpdates()
    {
        var cv = CodedValue.Create("STATE", "State", null, null, 0);
        cv.SetAttribute("country", "US");
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new RemoveCodedValueAttributeHandler(_repository.Object, _publisher.Object, _cache.Object);

        await handler.HandleAsync(new RemoveCodedValueAttribute(cv.Id, "country"));

        cv.Attributes.Should().BeEmpty();
        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
    }

    [TestMethod]
    public async Task SetAttribute_WhenNotFound_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync((CodedValue?)null);
        var handler = new SetCodedValueAttributeHandler(_repository.Object, _publisher.Object, _cache.Object);

        var act = async () => await handler.HandleAsync(new SetCodedValueAttribute(Guid.NewGuid(), "k", "v"));

        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }

    [TestMethod]
    public async Task SetAttributeDefinition_AddsDefinitionAndUpdates()
    {
        var cv = CodedValue.Create("HSPTLS", "Hospitals", null, null, 0);
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new SetCodedValueAttributeDefinitionHandler(_repository.Object, _cache.Object);

        await handler.HandleAsync(new SetCodedValueAttributeDefinition(
            cv.Id, "HTYPE", "Hospital Type", AttributeDataType.CodedValue, "HTYPES", true));

        var def = cv.AttributeDefinitions.Should().ContainSingle().Subject;
        def.Key.Should().Be("HTYPE");
        def.DataType.Should().Be(AttributeDataType.CodedValue);
        def.SourceCode.Should().Be("HTYPES");
        def.IsRequired.Should().BeTrue();
        def.AllowMultiple.Should().BeFalse();
        def.MinLength.Should().BeNull();
        def.MaxLength.Should().BeNull();
        def.RegexPattern.Should().BeNull();
        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
    }

    [TestMethod]
    public async Task SetAttributeDefinition_WhenNotFound_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync((CodedValue?)null);
        var handler = new SetCodedValueAttributeDefinitionHandler(_repository.Object, _cache.Object);

        var act = async () => await handler.HandleAsync(
            new SetCodedValueAttributeDefinition(Guid.NewGuid(), "KEY", null, AttributeDataType.Text, null, false));

        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }

    [TestMethod]
    public async Task RemoveAttributeDefinition_RemovesDefinitionAndUpdates()
    {
        var cv = CodedValue.Create("HSPTLS", "Hospitals", null, null, 0);
        cv.SetAttributeDefinition("HTYPE", AttributeDataType.Text);
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new RemoveCodedValueAttributeDefinitionHandler(_repository.Object, _cache.Object);

        await handler.HandleAsync(new RemoveCodedValueAttributeDefinition(cv.Id, "HTYPE"));

        cv.AttributeDefinitions.Should().BeEmpty();
        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
    }

    [TestMethod]
    public async Task RemoveAttributeDefinition_WhenNotFound_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync((CodedValue?)null);
        var handler = new RemoveCodedValueAttributeDefinitionHandler(_repository.Object, _cache.Object);

        var act = async () => await handler.HandleAsync(
            new RemoveCodedValueAttributeDefinition(Guid.NewGuid(), "KEY"));

        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }
}
