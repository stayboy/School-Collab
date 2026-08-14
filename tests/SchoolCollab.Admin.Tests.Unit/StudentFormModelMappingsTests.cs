using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Unit tests for the DTO → form-model projection on <see cref="StudentFormModel"/>
/// (<see cref="StudentFormModel.LoadFrom"/> / <see cref="StudentFormModel.From"/>)
/// used by the student edit dialog and edit page. Keeping the projection in a named,
/// tested method (rather than inline field-by-field assignments in the razor files)
/// makes the mapping easy to verify and keeps it in lockstep with both types.
/// </summary>
[TestClass]
public class StudentFormModelMappingsTests
{
    private static StudentDto MakeStudent() => new(
        Id: Guid.NewGuid(),
        StudentNumber: "STU001",
        FirstName: "Ada",
        LastName: "Lovelace",
        DateOfBirth: new DateOnly(1815, 12, 10),
        GenderCodedValueId: Guid.NewGuid(),
        IsDeleted: false,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        TitleCodedValueId: Guid.NewGuid());

    [TestMethod]
    public void LoadFrom_MapsAllProfileFields()
    {
        var student = MakeStudent();
        var model = new StudentFormModel();

        model.LoadFrom(student);

        model.StudentNumber.Should().Be(student.StudentNumber);
        model.FirstName.Should().Be(student.FirstName);
        model.LastName.Should().Be(student.LastName);
        model.DateOfBirth.Should().Be(student.DateOfBirth);
        model.GenderCodedValueId.Should().Be(student.GenderCodedValueId);
        model.TitleCodedValueId.Should().Be(student.TitleCodedValueId);
    }

    [TestMethod]
    public void From_ReturnsNewPopulatedModel()
    {
        var student = MakeStudent();

        var model = StudentFormModel.From(student);

        model.Should().NotBeNull();
        model.StudentNumber.Should().Be(student.StudentNumber);
        model.FirstName.Should().Be(student.FirstName);
        model.LastName.Should().Be(student.LastName);
        model.DateOfBirth.Should().Be(student.DateOfBirth);
        model.GenderCodedValueId.Should().Be(student.GenderCodedValueId);
        model.TitleCodedValueId.Should().Be(student.TitleCodedValueId);
        // Collection state is not copied from the DTO — it starts empty.
        model.GuardianLinks.Should().BeEmpty();
        model.Contacts.Should().BeEmpty();
    }

    [TestMethod]
    public void LoadFrom_OverwritesPriorValues()
    {
        var student = MakeStudent();
        var model = new StudentFormModel
        {
            StudentNumber = "OLD",
            FirstName = "Old",
            LastName = "Name",
            DateOfBirth = new DateOnly(2000, 1, 1),
        };

        model.LoadFrom(student);

        model.StudentNumber.Should().Be(student.StudentNumber);
        model.FirstName.Should().Be(student.FirstName);
        model.LastName.Should().Be(student.LastName);
        model.DateOfBirth.Should().Be(student.DateOfBirth);
    }
}