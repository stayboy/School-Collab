using FluentAssertions;
using Moq;
using SchoolCollab.CodedValues.Core.Commands.RemoveCodedValueAttribute;
using SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttribute;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;

namespace SchoolCollab.CodedValues.Tests.Unit.Handlers;

[TestClass]
public class SetAttributeHandlerTests
{
    private Mock<ICodedValueRepository> _repository = default!;

    [TestInitialize]
    public void Setup() => _repository = new Mock<ICodedValueRepository>();

    [TestMethod]
    public async Task SetAttribute_AddsAttributeAndUpdates()
    {
        var cv = CodedValue.Create("STATE", "State", null, null, 0);
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new SetCodedValueAttributeHandler(_repository.Object);

        await handler.HandleAsync(new SetCodedValueAttribute(cv.Id, "country", "US"));

        cv.Attributes.Should().ContainSingle(a =>
            a.Key == "country" && a.Value == "US" &&
            a.DataType == AttributeDataType.Text && a.SourceCode == null);
        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
    }

    [TestMethod]
    public async Task SetAttribute_WithDataTypeAndSourceCode_PersistsMetadata()
    {
        var cv = CodedValue.Create("COUNTRY", "Country", null, null, 0);
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new SetCodedValueAttributeHandler(_repository.Object);

        await handler.HandleAsync(new SetCodedValueAttribute(
            cv.Id, "region", "NA", AttributeDataType.CodedValue, "REGIONS"));

        cv.Attributes.Should().ContainSingle(a =>
            a.Key == "region" && a.Value == "NA" &&
            a.DataType == AttributeDataType.CodedValue && a.SourceCode == "REGIONS");
        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
    }

    [TestMethod]
    public async Task SetAttribute_WithNumericDataType_PersistsDataType()
    {
        var cv = CodedValue.Create("SCORE", "Score", null, null, 0);
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new SetCodedValueAttributeHandler(_repository.Object);

        await handler.HandleAsync(new SetCodedValueAttribute(
            cv.Id, "min_value", "0", AttributeDataType.Integer));

        cv.Attributes.Should().ContainSingle(a =>
            a.Key == "min_value" && a.DataType == AttributeDataType.Integer && a.SourceCode == null);
        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
    }

    [TestMethod]
    public async Task RemoveAttribute_RemovesAttributeAndUpdates()
    {
        var cv = CodedValue.Create("STATE", "State", null, null, 0);
        cv.SetAttribute("country", "US");
        _repository.Setup(r => r.GetAsync(cv.Id, default)).ReturnsAsync(cv);
        var handler = new RemoveCodedValueAttributeHandler(_repository.Object);

        await handler.HandleAsync(new RemoveCodedValueAttribute(cv.Id, "country"));

        cv.Attributes.Should().BeEmpty();
        _repository.Verify(r => r.UpdateAsync(cv, default), Times.Once);
    }

    [TestMethod]
    public async Task SetAttribute_WhenNotFound_ThrowsNotFoundException()
    {
        _repository.Setup(r => r.GetAsync(It.IsAny<Guid>(), default)).ReturnsAsync((CodedValue?)null);
        var handler = new SetCodedValueAttributeHandler(_repository.Object);

        var act = async () => await handler.HandleAsync(new SetCodedValueAttribute(Guid.NewGuid(), "k", "v"));

        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }
}
