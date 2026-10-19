using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListSubscribedContacts;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Round notification-templates-b1, spec §3.4 (name enrichment): the
/// <c>GET /contacts/subscribed</c> projection carries <c>ContactName</c>
/// (<see cref="Guardian.DisplayName"/> for guardian-owned rows) and <c>StudentName</c> (the
/// ward's person-demographic name) so the notification renderer's tags carry real values with
/// no second hop — and each degrades to <see langword="null"/> (= empty at the consumer) when
/// the name cannot be resolved.
/// </summary>
[TestClass]
public sealed class ListSubscribedContactsNameEnrichmentTests
{
    private static async Task<Guid> SeedStudentAsync(StudentsTestScope s, string firstName, string lastName)
    {
        var student = Student.Create(
                Guid.NewGuid().ToString("N")[..8], firstName, lastName,
                new DateOnly(2015, 1, 1), Guid.NewGuid())
            .WithTenant(s.Tenants);
        s.Db.Students.Add(student);
        await s.Db.SaveChangesAsync();
        return student.Id;
    }

    private static async Task<Guid> SeedGuardianAsync(StudentsTestScope s, string? displayName)
    {
        var guardian = Guardian.Create(null, "Kwame", "Mensah", displayName, null, null)
            .WithTenant(s.Tenants);
        s.Db.Guardians.Add(guardian);
        await s.Db.SaveChangesAsync();
        return guardian.Id;
    }

    private static async Task SeedLinkAsync(
        StudentsTestScope s, Guid studentId, Guid guardianId, GuardianRole role = GuardianRole.Primary)
    {
        s.Db.StudentGuardians.Add(
            StudentGuardian.Create(studentId, guardianId, role, null, false, null).WithTenant(s.Tenants));
        await s.Db.SaveChangesAsync();
    }

    private static async Task SeedSubscribedEmailAsync(
        StudentsTestScope s, ContactOwnerType ownerType, Guid ownerId, string value)
    {
        var contact = Contact.Create(ownerType, ownerId, ContactChannel.Email, value, null, null)
            .WithTenant(s.Tenants);
        s.Db.Contacts.Add(contact);
        var subscription = ContactSubscription.Create(contact.Id, SubscriptionScope.AllAssignments, null)
            .WithTenant(s.Tenants);
        subscription.Subscribe();
        s.Db.ContactSubscriptions.Add(subscription);
        await s.Db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task GuardianOwnedRow_CarriesGuardianDisplayName_AndWardStudentName()
    {
        using var s = new StudentsTestScope(nameof(GuardianOwnedRow_CarriesGuardianDisplayName_AndWardStudentName));
        var studentId = await SeedStudentAsync(s, "Ada", "Lovelace");
        var guardianId = await SeedGuardianAsync(s, "Mrs Lovelace");
        await SeedLinkAsync(s, studentId, guardianId);
        await SeedSubscribedEmailAsync(s, ContactOwnerType.Guardian, guardianId, "lovelace@example.com");

        var result = await new ListSubscribedContactsHandler(s.Db, s.Cache)
            .HandleAsync(new ListSubscribedContacts(
                ContactOwnerType.Guardian, guardianId, SubscriptionScope.AllAssignments, studentId));

        result.Should().ContainSingle();
        result[0].ContactName.Should().Be("Mrs Lovelace", "{{GuardianName}} reads Guardian.DisplayName");
        result[0].StudentName.Should().Be("Ada Lovelace", "{{StudentName}} reads the ward's demographic name");
        result[0].Role.Should().Be(GuardianRole.Primary, "the existing role projection is unchanged");
    }

    [TestMethod]
    public async Task StudentOwnedRow_CarriesTheStudentsOwnName_AndNoContactName()
    {
        using var s = new StudentsTestScope(nameof(StudentOwnedRow_CarriesTheStudentsOwnName_AndNoContactName));
        var studentId = await SeedStudentAsync(s, "Grace", "Hopper");
        await SeedSubscribedEmailAsync(s, ContactOwnerType.Student, studentId, "grace@example.com");

        var result = await new ListSubscribedContactsHandler(s.Db, s.Cache)
            .HandleAsync(new ListSubscribedContacts(
                ContactOwnerType.Student, studentId, SubscriptionScope.AllAssignments));

        result.Should().ContainSingle();
        result[0].StudentName.Should().Be("Grace Hopper");
        result[0].ContactName.Should().BeNull(
            "a student-owned contact has no guardian display name (§3.2: {{GuardianName}} is empty there)");
    }

    [TestMethod]
    public async Task GuardianWithoutDisplayName_DegradesToNull_NotAFailure()
    {
        using var s = new StudentsTestScope(nameof(GuardianWithoutDisplayName_DegradesToNull_NotAFailure));
        var studentId = await SeedStudentAsync(s, "Alan", "Turing");
        var guardianId = await SeedGuardianAsync(s, displayName: null);
        await SeedLinkAsync(s, studentId, guardianId);
        await SeedSubscribedEmailAsync(s, ContactOwnerType.Guardian, guardianId, "turing@example.com");

        var result = await new ListSubscribedContactsHandler(s.Db, s.Cache)
            .HandleAsync(new ListSubscribedContacts(
                ContactOwnerType.Guardian, guardianId, SubscriptionScope.AllAssignments, studentId));

        result.Should().ContainSingle();
        result[0].ContactName.Should().BeNull("an absent name degrades, it never fails the resolution");
        result[0].StudentName.Should().Be("Alan Turing", "the ward name is independent of the guardian name");
    }

    [TestMethod]
    public async Task TeacherOwnedRow_HasNoWard_SoBothNamesDegradeToNull()
    {
        using var s = new StudentsTestScope(nameof(TeacherOwnedRow_HasNoWard_SoBothNamesDegradeToNull));
        var teacherId = await SeedTeacherAsync(s);
        await SeedSubscribedEmailAsync(s, ContactOwnerType.Teacher, teacherId, "teacher@school.edu");

        var result = await new ListSubscribedContactsHandler(s.Db, s.Cache)
            .HandleAsync(new ListSubscribedContacts(
                ContactOwnerType.Teacher, teacherId, SubscriptionScope.AllAssignments));

        result.Should().ContainSingle();
        result[0].ContactName.Should().BeNull();
        result[0].StudentName.Should().BeNull("a teacher contact has no ward student");
    }

    private static async Task<Guid> SeedTeacherAsync(StudentsTestScope s)
    {
        var teacher = Teacher.Create(null, "Ama", "Boateng", "Ms Boateng", "ama@school.edu")
            .WithTenant(s.Tenants);
        s.Db.Teachers.Add(teacher);
        await s.Db.SaveChangesAsync();
        return teacher.Id;
    }
}
