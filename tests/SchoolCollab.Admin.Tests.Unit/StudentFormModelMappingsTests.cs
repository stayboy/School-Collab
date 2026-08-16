using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Domain;
using StudentGuardianViewDto = SchoolCollab.Students.Core.DTOs.StudentGuardianViewDto;
using ContactDto = SchoolCollab.Students.Core.DTOs.ContactDto;
using GuardianContactViewDto = SchoolCollab.Students.Core.DTOs.GuardianContactViewDto;

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
    public void LoadFrom_AllInclusive_MapsGuardianContacts()
    {
        // The student edit dialog's guardian card list renders each
        // guardian's top-3 contacts as chips (per the card-redesign plan).
        // ToGuardianAssignment must therefore map the per-link
        // StudentGuardianViewDto.Contacts (Channel/Value/CountryCode) into
        // GuardianAssignment.Contacts (ContactModel) with stable Order.
        var student = MakeStudent();
        var emailContact = new GuardianContactViewDto(ContactChannel.Email, "alice@example.com");
        var phoneContact = new GuardianContactViewDto(ContactChannel.SMS, "0241234567", CountryCode: "+233");
        var whatsAppContact = new GuardianContactViewDto(ContactChannel.WhatsApp, "0247654321", CountryCode: "+233");
        var guardian = MakeGuardian() with { Contacts = new[] { emailContact, phoneContact, whatsAppContact } };
        var model = new StudentFormModel();
        model.LoadFrom(student, new[] { guardian }, Array.Empty<ContactDto>());

        model.GuardianLinks.Should().ContainSingle();
        var g = model.GuardianLinks[0];
        g.Contacts.Should().NotBeNull("the card list needs each guardian's contacts");
        g.Contacts!.Count.Should().Be(3);
        g.Contacts[0].Channel.Should().Be(ContactChannel.Email);
        g.Contacts[0].Value.Should().Be("alice@example.com");
        g.Contacts[0].Order.Should().Be(0, "Order reflects the top-3 source index for stable display order");
        g.Contacts[1].Channel.Should().Be(ContactChannel.SMS);
        g.Contacts[1].CountryCode.Should().Be("+233");
        g.Contacts[1].Order.Should().Be(1);
        g.Contacts[2].Channel.Should().Be(ContactChannel.WhatsApp);
        g.Contacts[2].Order.Should().Be(2);
    }

    [TestMethod]
    public void LoadFrom_AllInclusive_EmptyGuardianContacts_RendersAsEmptyChipList()
    {
        // A guardian with no contacts should still produce a GuardianAssignment
        // with an empty (not null) Contacts list, so the card list renders the
        // muted "— no contacts —" placeholder rather than crashing.
        var student = MakeStudent();
        var guardian = MakeGuardian(); // no Contacts init → empty array default
        var model = new StudentFormModel();
        model.LoadFrom(student, new[] { guardian }, Array.Empty<ContactDto>());

        var g = model.GuardianLinks[0];
        g.Contacts.Should().NotBeNull();
        g.Contacts.Should().BeEmpty();
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

    [TestMethod]
    public void ToCreateRequest_RoundTripsProfileGuardiansContactsAndEnrollment()
    {
        // Create drafts the model (no DTO load); ToCreateRequest projects it back for the
        // atomic create. Notably the guardian emergency flag must round-trip — the previous
        // inline flush hardcoded IsEmergencyContact: false (a latent bug this fixes).
        var gradeId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var guardianId = Guid.NewGuid();
        var model = new StudentFormModel
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            DateOfBirth = new DateOnly(1815, 12, 10),
            GenderCodedValueId = Guid.NewGuid(),
            TitleCodedValueId = Guid.NewGuid(),
            GuardianLinks = new()
            {
                new GuardianAssignment(
                    guardianId, "Alice", "Existing", Guid.NewGuid(),
                    null, null, Guid.NewGuid(),
                    Role: GuardianRole.CC, IsEmergencyContact: true)
            },
            Contacts = new()
            {
                new ContactModel { Channel = ContactChannel.Email, Value = "ada@example.com", Label = "Work", Order = 0 }
            }
        };

        var req = model.ToCreateRequest(gradeId, periodId);

        req.FirstName.Should().Be("Ada");
        req.LastName.Should().Be("Lovelace");
        req.Guardians.Should().ContainSingle();
        req.Guardians![0].ExistingGuardianId.Should().Be(guardianId);
        req.Guardians![0].Role.Should().Be(GuardianRole.CC);
        req.Guardians![0].IsEmergencyContact.Should().BeTrue(
            "the emergency flag must round-trip (the inline flush used to hardcode false)");
        req.Contacts.Should().ContainSingle();
        req.Contacts![0].Id.Should().BeNull("create contacts are new (no persisted id)");
        req.Contacts![0].Value.Should().Be("ada@example.com");
        req.EnrollmentGradeLevelId.Should().Be(gradeId);
        req.EnrollmentPeriodId.Should().Be(periodId);
    }
}