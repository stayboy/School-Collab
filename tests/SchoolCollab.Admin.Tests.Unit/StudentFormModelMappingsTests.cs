using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Domain;
using StudentGuardianViewDto = SchoolCollab.Students.Core.DTOs.StudentGuardianViewDto;
using ContactDto = SchoolCollab.Students.Core.DTOs.ContactDto;

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
        TitleCodedValueId: Guid.NewGuid(),
        RowVersion: 42);

    private static StudentGuardianViewDto MakeGuardian() => new(
        GuardianId: Guid.NewGuid(),
        StudentId: Guid.NewGuid(),
        Role: GuardianRole.CC,
        RelationshipCodedValueId: Guid.NewGuid(),
        IsEmergencyContact: true,
        FirstName: "Alice",
        LastName: "Existing",
        DisplayName: "Alice Existing",
        TitleCodedValueId: Guid.NewGuid());

    private static ContactDto MakeContact() => new(
        Id: Guid.NewGuid(),
        OwnerType: ContactOwnerType.Student,
        OwnerId: Guid.NewGuid(),
        Channel: ContactChannel.Email,
        Value: "ada@example.com",
        Label: "Work",
        IsVerified: false,
        IsDeleted: false,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow)
    {
        DisplayOrder = 2
    };

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

    [TestMethod]
    public void LoadFrom_AllInclusive_PopulatesProfileGuardiansContactsAndConcurrencySnapshot()
    {
        var student = MakeStudent();
        var guardian = MakeGuardian();
        var contact = MakeContact();

        var model = new StudentFormModel();
        model.LoadFrom(student, new[] { guardian }, new[] { contact });

        // Profile
        model.FirstName.Should().Be(student.FirstName);
        model.LastName.Should().Be(student.LastName);
        // Concurrency snapshot
        model.RowVersion.Should().Be(student.RowVersion);
        model.LoadedGuardianIds.Should().Equal(guardian.GuardianId);
        model.LoadedContactIds.Should().Equal(contact.Id);
        // Guardians
        model.GuardianLinks.Should().ContainSingle();
        var g = model.GuardianLinks[0];
        g.ExistingGuardianId.Should().Be(guardian.GuardianId);
        g.FirstName.Should().Be("Alice");
        g.Role.Should().Be(GuardianRole.CC);
        g.IsEmergencyContact.Should().BeTrue();
        // Contacts
        model.Contacts.Should().ContainSingle();
        var c = model.Contacts[0];
        c.PersistedId.Should().Be(contact.Id);
        c.Value.Should().Be("ada@example.com");
        c.Order.Should().Be(2);
    }

    [TestMethod]
    public void ToUpdateRequest_RoundTripsProfileGuardiansContactsAndConcurrency()
    {
        var student = MakeStudent();
        var guardian = MakeGuardian();
        var contact = MakeContact();
        var model = new StudentFormModel();
        model.LoadFrom(student, new[] { guardian }, new[] { contact });

        var req = model.ToUpdateRequest();

        req.FirstName.Should().Be(student.FirstName);
        req.LastName.Should().Be(student.LastName);
        req.ExpectedRowVersion.Should().Be(student.RowVersion);
        req.LoadedGuardianIds.Should().Equal(guardian.GuardianId);
        req.LoadedContactIds.Should().Equal(contact.Id);
        req.Guardians.Should().ContainSingle();
        req.Guardians![0].ExistingGuardianId.Should().Be(guardian.GuardianId);
        req.Guardians![0].IsEmergencyContact.Should().BeTrue();
        req.Contacts.Should().ContainSingle();
        req.Contacts![0].Id.Should().Be(contact.Id);
        req.Contacts![0].Value.Should().Be("ada@example.com");
    }
}