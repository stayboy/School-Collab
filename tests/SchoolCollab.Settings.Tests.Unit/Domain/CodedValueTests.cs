using FluentAssertions;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Events;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Tests.Unit.Domain;

[TestClass]
public class CodedValueTests
{
    [TestMethod]
    public void Create_WithValidData_SetsProperties()
    {
        var cv = CodedValue.Create("GENDER", "Gender", "Biological gender", null, 0);

        cv.Code.Should().Be("GENDER");
        cv.Name.Should().Be("Gender");
        cv.Description.Should().Be("Biological gender");
        cv.ParentId.Should().BeNull();
        cv.IsDisabled.Should().BeFalse();
        cv.DisplayOrder.Should().Be(0);
        cv.Id.Should().NotBeEmpty();
        cv.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void Create_NormalizesCodeToUppercase()
    {
        var cv = CodedValue.Create("gender", "Gender", null, null, 0);
        cv.Code.Should().Be("GENDER");
    }

    [TestMethod]
    public void Create_RaisesCodedValueCreatedEvent()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);

        cv.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<CodedValueCreatedEvent>();
    }

    [TestMethod]
    public void Create_WithParent_SetsParentId()
    {
        var parentId = Guid.NewGuid();
        var cv = CodedValue.Create("MALE", "Male", null, parentId, 1);
        cv.ParentId.Should().Be(parentId);
    }

    [TestMethod]
    public void Update_ChangesNameAndDescription()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.ClearDomainEvents();

        cv.Update("Gender Updated", "New description", 5);

        cv.Name.Should().Be("Gender Updated");
        cv.Description.Should().Be("New description");
        cv.DisplayOrder.Should().Be(5);
        cv.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void Update_RaisesCodedValueUpdatedEvent()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.ClearDomainEvents();

        cv.Update("Gender Updated", null, 0);

        cv.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<CodedValueUpdatedEvent>();
    }

    [TestMethod]
    public void Disable_SetsIsDisabledTrue()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.ClearDomainEvents();

        cv.Disable();

        cv.IsDisabled.Should().BeTrue();
        cv.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<CodedValueDisabledEvent>();
    }

    [TestMethod]
    public void Disable_WhenAlreadyDisabled_IsIdempotent()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.Disable();
        cv.ClearDomainEvents();

        cv.Disable();

        cv.DomainEvents.Should().BeEmpty();
    }

    [TestMethod]
    public void Enable_WhenDisabled_SetsIsDisabledFalse()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.Disable();
        cv.ClearDomainEvents();

        cv.Enable();

        cv.IsDisabled.Should().BeFalse();
        cv.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<CodedValueEnabledEvent>();
    }

    [TestMethod]
    public void Enable_WhenAlreadyEnabled_IsIdempotent()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.ClearDomainEvents();

        cv.Enable();

        cv.DomainEvents.Should().BeEmpty();
    }

    [TestMethod]
    public void SetAttribute_AddsNewAttribute()
    {
        var cv = CodedValue.Create("STATE", "State", null, null, 0);

        cv.SetAttribute("country", "US");

        cv.Attributes.Should().ContainSingle(a => a.Key == "country" && a.Value == "US");
    }

    [TestMethod]
    public void SetAttribute_OverwritesExistingKey()
    {
        var cv = CodedValue.Create("STATE", "State", null, null, 0);
        cv.SetAttribute("country", "US");

        cv.SetAttribute("country", "UK");

        cv.Attributes.Should().ContainSingle(a => a.Key == "country")
            .Which.Value.Should().Be("UK");
    }

    [TestMethod]
    public void SetAttributeDefinition_AddsDefinitionWithMetadata()
    {
        var cv = CodedValue.Create("HSPTLS", "Hospitals", null, null, 0);

        cv.SetAttributeDefinition("HTYPE", AttributeDataType.CodedValue, "HOSPITAL_TYPES", isRequired: true, displayName: "Hospital Type");

        var def = cv.AttributeDefinitions.Should().ContainSingle().Subject;
        def.Key.Should().Be("HTYPE");
        def.DataType.Should().Be(AttributeDataType.CodedValue);
        def.SourceCode.Should().Be("HOSPITAL_TYPES");
        def.IsRequired.Should().BeTrue();
        def.DisplayName.Should().Be("Hospital Type");
    }

    [TestMethod]
    public void SetAttributeDefinition_OverwritesExistingKey()
    {
        var cv = CodedValue.Create("HSPTLS", "Hospitals", null, null, 0);
        cv.SetAttributeDefinition("HTYPE", AttributeDataType.Text);

        cv.SetAttributeDefinition("HTYPE", AttributeDataType.CodedValue, "HTYPES");

        cv.AttributeDefinitions.Should().ContainSingle().Which.DataType.Should().Be(AttributeDataType.CodedValue);
    }

    [TestMethod]
    public void RemoveAttributeDefinition_RemovesExistingKey()
    {
        var cv = CodedValue.Create("HSPTLS", "Hospitals", null, null, 0);
        cv.SetAttributeDefinition("HTYPE", AttributeDataType.Text);

        cv.RemoveAttributeDefinition("HTYPE");

        cv.AttributeDefinitions.Should().BeEmpty();
    }

    [TestMethod]
    public void RemoveAttributeDefinition_NonExistentKey_DoesNotThrow()
    {
        var cv = CodedValue.Create("HSPTLS", "Hospitals", null, null, 0);

        var act = () => cv.RemoveAttributeDefinition("nonexistent");

        act.Should().NotThrow();
    }

    [TestMethod]
    public void RemoveAttribute_RemovesExistingKey()
    {
        var cv = CodedValue.Create("STATE", "State", null, null, 0);
        cv.SetAttribute("country", "US");

        cv.RemoveAttribute("country");

        cv.Attributes.Should().BeEmpty();
    }

    [TestMethod]
    public void RemoveAttribute_NonExistentKey_DoesNotThrow()
    {
        var cv = CodedValue.Create("STATE", "State", null, null, 0);

        var act = () => cv.RemoveAttribute("nonexistent");

        act.Should().NotThrow();
    }

    [TestMethod]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var cv = CodedValue.Create("GENDER", "Gender", null, null, 0);
        cv.DomainEvents.Should().NotBeEmpty();

        cv.ClearDomainEvents();

        cv.DomainEvents.Should().BeEmpty();
    }
}
