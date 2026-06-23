using Moq;
using FluentAssertions;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.DTOs;
using SchoolCollab.CodedValues.Core.Services;

namespace SchoolCollab.CodedValues.Tests.Unit.Services;

[TestClass]
public class CodedValueResolverTests
{
    private Mock<ICodedValueRepository> _repoMock;
    private CodedValueResolver _resolver;

    [TestInitialize]
    public void Setup()
    {
        _repoMock = new Mock<ICodedValueRepository>();
        _resolver = new CodedValueResolver(_repoMock.Object);
    }

    [TestMethod]
    public async Task ResolveAsync_NoOverride_ReturnsGlobalValue()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var cv = CodedValue.Create("CODE1", "Name1", "Desc1", null, 0);
        cv.SetAttribute("Key1", "Val1");

        _repoMock.Setup(r => r.GetOverrideAsync(tenantId, cv.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantCodedValueOverride?)null);
        _repoMock.Setup(r => r.GetAttributeOverrideAsync(tenantId, cv.Id, "Key1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantCodedValueAttributeOverride?)null);

        // Act
        var result = await _resolver.ResolveAsync(cv, tenantId);

        // Assert
        result.Code.Should().Be("CODE1");
        result.Name.Should().Be("Name1");
        result.Attributes.Should().ContainSingle(a => a.Key == "Key1" && a.Value == "Val1");
    }

    [TestMethod]
    public async Task ResolveAsync_WithOverride_ReturnsOverriddenValues()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var cv = CodedValue.Create("CODE1", "Name1", "Desc1", null, 0);
        cv.SetAttribute("Key1", "Val1");

        var overrideValue = TenantCodedValueOverride.Create(tenantId, cv.Id, "Name Override", "Description Override");
        var attrOverride = new TenantCodedValueAttributeOverride(tenantId, cv.Id, "Key1", "Val Override");

        _repoMock.Setup(r => r.GetOverrideAsync(tenantId, cv.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overrideValue);
        _repoMock.Setup(r => r.GetAttributeOverrideAsync(tenantId, cv.Id, "Key1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(attrOverride);

        // Act
        var result = await _resolver.ResolveAsync(cv, tenantId);

        // Assert
        result.Code.Should().Be("CODE1");
        result.Name.Should().Be("Name Override");
        result.Description.Should().Be("Description Override");
        result.IsDisabled.Should().BeFalse();
        result.Attributes.Should().ContainSingle(a => a.Key == "Key1" && a.Value == "Val Override");
    }

    [TestMethod]
    public async Task ResolveAsync_PartialOverride_MergesCorrectly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var cv = CodedValue.Create("CODE1", "Name1", "Desc1", null, 0);
        
        var overrideValue = TenantCodedValueOverride.Create(tenantId, cv.Id, "Only Name Override", null);

        _repoMock.Setup(r => r.GetOverrideAsync(tenantId, cv.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overrideValue);

        // Act
        var result = await _resolver.ResolveAsync(cv, tenantId);

        // Assert
        result.Code.Should().Be("CODE1"); // Global
        result.Name.Should().Be("Only Name Override"); // Override
        result.IsDisabled.Should().BeFalse(); // Global
    }
}
