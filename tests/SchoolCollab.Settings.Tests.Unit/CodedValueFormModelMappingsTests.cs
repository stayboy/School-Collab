using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Application.Components.Pages.CodedValues;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Unit tests for the DTO → form-model projection on <see cref="CodedValueEditModel"/>
/// (<see cref="CodedValueEditModel.LoadFrom"/> / <see cref="CodedValueEditModel.From"/>)
/// used by the coded-value edit page. Keeping the projection in a named, tested method
/// (rather than inline field-by-field assignments in the razor) makes the mapping easy
/// to verify and keeps it in lockstep with both types.
/// </summary>
[TestClass]
public class CodedValueFormModelMappingsTests
{
    private static CodedValueDto MakeCodedValue() => new(
        Id: Guid.NewGuid(),
        Code: "STATUS_ACTIVE",
        Name: "Active",
        Description: "An active status",
        ParentId: null,
        ParentCode: null,
        IsDisabled: false,
        DisplayOrder: 3,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        Attributes: [],
        AttributeDefinitions: []);

    [TestMethod]
    public void LoadFrom_MapsAllFields()
    {
        var cv = MakeCodedValue();
        var model = new CodedValueEditModel();

        model.LoadFrom(cv);

        model.Name.Should().Be(cv.Name);
        model.Description.Should().Be(cv.Description);
        model.DisplayOrder.Should().Be(cv.DisplayOrder);
    }

    [TestMethod]
    public void From_ReturnsNewPopulatedModel()
    {
        var cv = MakeCodedValue();

        var model = CodedValueEditModel.From(cv);

        model.Should().NotBeNull();
        model.Name.Should().Be(cv.Name);
        model.Description.Should().Be(cv.Description);
        model.DisplayOrder.Should().Be(cv.DisplayOrder);
    }

    [TestMethod]
    public void LoadFrom_OverwritesPriorValues()
    {
        var cv = MakeCodedValue();
        var model = new CodedValueEditModel
        {
            Name = "Old",
            Description = "Stale",
            DisplayOrder = 99,
        };

        model.LoadFrom(cv);

        model.Name.Should().Be(cv.Name);
        model.Description.Should().Be(cv.Description);
        model.DisplayOrder.Should().Be(cv.DisplayOrder);
    }
}