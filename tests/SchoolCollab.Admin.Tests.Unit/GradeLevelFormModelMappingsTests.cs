using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Unit tests for the DTO → form-model projection on <see cref="GradeLevelFormModel"/>
/// (<see cref="GradeLevelFormModel.LoadFrom"/> / <see cref="GradeLevelFormModel.From"/>)
/// used by the grade-level edit page. Keeping the projection in a named, tested method
/// (rather than inline field-by-field assignments in the razor) makes the mapping easy
/// to verify and keeps it in lockstep with both types.
/// </summary>
[TestClass]
public class GradeLevelFormModelMappingsTests
{
    private static GradeLevelDto MakeGrade() => new(
        Id: Guid.NewGuid(),
        CodedValueId: Guid.NewGuid(),
        Level: 5,
        Name: "Grade 5",
        DisplayOrder: 3,
        TopicCount: 0,
        StudentCount: 0,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        MinAge: 10,
        MaxAge: 12,
        AllowedGenderCodedValueId: Guid.NewGuid());

    [TestMethod]
    public void LoadFrom_MapsAllFields()
    {
        var grade = MakeGrade();
        var model = new GradeLevelFormModel();

        model.LoadFrom(grade);

        model.CodedValueId.Should().Be(grade.CodedValueId);
        model.Name.Should().Be(grade.Name);
        model.Level.Should().Be(grade.Level);
        model.DisplayOrder.Should().Be(grade.DisplayOrder);
        model.MinAge.Should().Be(grade.MinAge);
        model.MaxAge.Should().Be(grade.MaxAge);
        model.AllowedGenderCodedValueId.Should().Be(grade.AllowedGenderCodedValueId);
    }

    [TestMethod]
    public void From_ReturnsNewPopulatedModel()
    {
        var grade = MakeGrade();

        var model = GradeLevelFormModel.From(grade);

        model.Should().NotBeNull();
        model.Name.Should().Be(grade.Name);
        model.Level.Should().Be(grade.Level);
        model.AllowedGenderCodedValueId.Should().Be(grade.AllowedGenderCodedValueId);
    }

    [TestMethod]
    public void LoadFrom_OverwritesPriorValues()
    {
        var grade = MakeGrade();
        var model = new GradeLevelFormModel
        {
            Name = "Old",
            Level = 1,
            DisplayOrder = 99,
        };

        model.LoadFrom(grade);

        model.Name.Should().Be(grade.Name);
        model.Level.Should().Be(grade.Level);
        model.DisplayOrder.Should().Be(grade.DisplayOrder);
    }
}