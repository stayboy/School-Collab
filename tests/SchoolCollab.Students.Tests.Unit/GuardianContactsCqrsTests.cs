using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.AddContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.DeleteContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.SetPrimaryContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.Subscribe;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.Unsubscribe;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.UpdateContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.VerifyContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListContacts;
using SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListSubscribedContacts;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.CreateGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.DeleteGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.LinkGuardianToStudent;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.UnlinkGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.UpdateGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.GetGuardianNameHistory;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardians;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class GuardianContactsCqrsTests
{
    private static GuardianRepository GuardianRepo(StudentsTestScope s) => new(s.Db);
    private static ContactRepository ContactRepo(StudentsTestScope s) => new(s.Db);
    private static StudentRepository StudentRepo(StudentsTestScope s) => new(s.Db);

    private static CreateGuardianHandler NewCreateGuardian(StudentsTestScope s) =>
        new(GuardianRepo(s), s.Cache, s.Tenants, NullLogger<CreateGuardianHandler>.Instance);
    private static UpdateGuardianHandler NewUpdateGuardian(StudentsTestScope s) =>
        new(GuardianRepo(s), s.Cache, NullLogger<UpdateGuardianHandler>.Instance);
    private static DeleteGuardianHandler NewDeleteGuardian(StudentsTestScope s) =>
        new(GuardianRepo(s), s.Cache, NullLogger<DeleteGuardianHandler>.Instance);
    private static LinkGuardianToStudentHandler NewLink(StudentsTestScope s) =>
        new(StudentRepo(s), GuardianRepo(s), s.Cache, s.Tenants, NullLogger<LinkGuardianToStudentHandler>.Instance);
    private static UnlinkGuardianHandler NewUnlink(StudentsTestScope s) =>
        new(GuardianRepo(s), s.Cache, NullLogger<UnlinkGuardianHandler>.Instance);

    private static AddContactHandler NewAddContact(StudentsTestScope s) =>
        new(ContactRepo(s), s.Cache, s.Tenants, NullLogger<AddContactHandler>.Instance);
    private static UpdateContactHandler NewUpdateContact(StudentsTestScope s) =>
        new(ContactRepo(s), s.Cache, NullLogger<UpdateContactHandler>.Instance);
    private static DeleteContactHandler NewDeleteContact(StudentsTestScope s) =>
        new(ContactRepo(s), s.Cache, NullLogger<DeleteContactHandler>.Instance);
    private static VerifyContactHandler NewVerifyContact(StudentsTestScope s) =>
        new(ContactRepo(s), s.Cache, NullLogger<VerifyContactHandler>.Instance);
    private static SetPrimaryContactHandler NewSetPrimary(StudentsTestScope s) =>
        new(ContactRepo(s), s.Cache, NullLogger<SetPrimaryContactHandler>.Instance);
    private static SubscribeHandler NewSubscribe(StudentsTestScope s) =>
        new(ContactRepo(s), s.Cache, s.Tenants, NullLogger<SubscribeHandler>.Instance);
    private static UnsubscribeHandler NewUnsubscribe(StudentsTestScope s) =>
        new(ContactRepo(s), s.Cache, s.Tenants, NullLogger<UnsubscribeHandler>.Instance);

    private static async Task<Guid> SeedStudentAsync(StudentsTestScope s, string number)
    {
        var student = Student.Create(number, "Ward", "Pupil", new DateOnly(2015, 1, 1), Guid.NewGuid()).WithTenant(s.Tenants);
        await StudentRepo(s).AddAsync(student, default);
        return student.Id;
    }

    // ── Guardian CRUD ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateGuardian_CreatesGuardian_AndInitialNameHistory()
    {
        using var s = new StudentsTestScope("g-create");
        var id = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));

        var guardian = s.Db.Guardians.IgnoreQueryFilters().Single(g => g.Id == id);
        guardian.FirstName.Should().Be("Jane");
        guardian.TenantId.Should().Be(s.Tenants.GetTenantContext().TenantId);

        var history = s.Db.GuardianNameHistories.IgnoreQueryFilters().Where(h => h.GuardianId == id).ToList();
        history.Should().ContainSingle();
        history[0].FirstName.Should().Be("Jane");
        history[0].LastName.Should().Be("Doe");
    }

    [TestMethod]
    public async Task UpdateGuardian_NameChange_AppendsOneHistoryRow()
    {
        using var s = new StudentsTestScope("g-update-name");
        var id = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));

        await NewUpdateGuardian(s).HandleAsync(new UpdateGuardian(id, null, "Janet", "Doeman", null, "Addr", null));

        var guardian = s.Db.Guardians.IgnoreQueryFilters().Single(g => g.Id == id);
        guardian.FirstName.Should().Be("Janet");

        var history = s.Db.GuardianNameHistories.IgnoreQueryFilters()
            .Where(h => h.GuardianId == id).OrderBy(h => h.CreatedAt).ToList();
        history.Should().HaveCount(2, "initial snapshot + one name change");
        history[1].FirstName.Should().Be("Janet");
    }

    [TestMethod]
    public async Task UpdateGuardian_ProfileOnly_AppendsNoHistoryRow()
    {
        using var s = new StudentsTestScope("g-update-profile");
        var id = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));

        await NewUpdateGuardian(s).HandleAsync(new UpdateGuardian(id, null, "Jane", "Doe", null, "New Address", null));

        var history = s.Db.GuardianNameHistories.IgnoreQueryFilters().Where(h => h.GuardianId == id).ToList();
        history.Should().ContainSingle("profile-only update never appends name history");
    }

    [TestMethod]
    public async Task DeleteGuardian_SoftDeletes_KeepsHistoryLinksAndContacts()
    {
        using var s = new StudentsTestScope("g-delete");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentId = await SeedStudentAsync(s, "S1");

        // Link + guardian-owned contact (so they can be checked for retention).
        await NewLink(s).HandleAsync(new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.Primary, false, null));
        var contactId = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Guardian, guardianId, ContactChannel.Email, "jane@example.com", null, true));

        await NewDeleteGuardian(s).HandleAsync(new DeleteGuardian(guardianId));

        var guardian = s.Db.Guardians.IgnoreQueryFilters().Single(g => g.Id == guardianId);
        guardian.IsDeleted.Should().BeTrue();

        s.Db.GuardianNameHistories.IgnoreQueryFilters().Count(h => h.GuardianId == guardianId).Should().Be(1, "history retained");
        s.Db.StudentGuardians.IgnoreQueryFilters().Count(l => l.GuardianId == guardianId).Should().Be(1, "link retained (no cascade)");
        s.Db.Contacts.IgnoreQueryFilters().Count(c => c.Id == contactId).Should().Be(1, "contacts retained");

        // And the guardian is filtered out of the active list.
        var listed = await new ListGuardiansHandler(s.Db, s.Cache).HandleAsync(new ListGuardians());
        listed.Should().NotContain(g => g.Id == guardianId);
    }

    // ── Links ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LinkGuardianToStudent_CreatesLink_AndRecordsActingGuardian()
    {
        using var s = new StudentsTestScope("g-link");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentId = await SeedStudentAsync(s, "S1");
        var actingId = Guid.NewGuid();

        var linkId = await NewLink(s).HandleAsync(
            new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.CC, true, actingId));

        var link = s.Db.StudentGuardians.IgnoreQueryFilters().Single(l => l.Id == linkId);
        link.GuardianId.Should().Be(guardianId);
        link.StudentId.Should().Be(studentId);
        link.Role.Should().Be(GuardianRole.CC);
        link.IsEmergencyContact.Should().BeTrue();
        link.CreatedByGuardianId.Should().Be(actingId, "Primary-adds-CC: acting guardian is recorded");
    }

    [TestMethod]
    public async Task LinkGuardianToStudent_Duplicate_Throws()
    {
        using var s = new StudentsTestScope("g-link-dup");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentId = await SeedStudentAsync(s, "S1");
        await NewLink(s).HandleAsync(new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.Primary, false, null));

        var act = () => NewLink(s).HandleAsync(new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.CC, false, null));
        await act.Should().ThrowAsync<GuardianLinkAlreadyExistsException>();
    }

    [TestMethod]
    public async Task UnlinkGuardian_RemovesLink()
    {
        using var s = new StudentsTestScope("g-unlink");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentId = await SeedStudentAsync(s, "S1");
        await NewLink(s).HandleAsync(new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.Primary, false, null));

        await NewUnlink(s).HandleAsync(new UnlinkGuardian(studentId, guardianId));

        s.Db.StudentGuardians.IgnoreQueryFilters().Count(l => l.GuardianId == guardianId).Should().Be(0);
    }

    // ── Contacts ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AddContact_CreatesMultiChannelContact_PerOwner()
    {
        using var s = new StudentsTestScope("c-add");
        var studentId = await SeedStudentAsync(s, "S1");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));

        var studentContact = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "kid@example.com", null, true));
        var guardianContact = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Guardian, guardianId, ContactChannel.SMS, "+12345", "Mobile", false));

        var sc = s.Db.Contacts.IgnoreQueryFilters().Single(c => c.Id == studentContact);
        sc.OwnerType.Should().Be(ContactOwnerType.Student);
        sc.Channel.Should().Be(ContactChannel.Email);
        sc.IsVerified.Should().BeFalse("new contacts default unverified");
        sc.TenantId.Should().Be(s.Tenants.GetTenantContext().TenantId);

        var gc = s.Db.Contacts.IgnoreQueryFilters().Single(c => c.Id == guardianContact);
        gc.OwnerType.Should().Be(ContactOwnerType.Guardian);
        gc.Channel.Should().Be(ContactChannel.SMS);
    }

    [TestMethod]
    public async Task UpdateContact_ChangesValueAndLabel()
    {
        using var s = new StudentsTestScope("c-update");
        var studentId = await SeedStudentAsync(s, "S1");
        var id = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "old@example.com", "Old", true));

        await NewUpdateContact(s).HandleAsync(new UpdateContact(id, "new@example.com", "New"));

        var c = s.Db.Contacts.IgnoreQueryFilters().Single(x => x.Id == id);
        c.Value.Should().Be("new@example.com");
        c.Label.Should().Be("New");
    }

    [TestMethod]
    public async Task DeleteContact_SoftDeletes()
    {
        using var s = new StudentsTestScope("c-delete");
        var studentId = await SeedStudentAsync(s, "S1");
        var id = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "kid@example.com", null, true));

        await NewDeleteContact(s).HandleAsync(new DeleteContact(id));

        s.Db.Contacts.IgnoreQueryFilters().Single(c => c.Id == id).IsDeleted.Should().BeTrue();
    }

    [TestMethod]
    public async Task VerifyContact_SetsVerified()
    {
        using var s = new StudentsTestScope("c-verify");
        var studentId = await SeedStudentAsync(s, "S1");
        var id = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "kid@example.com", null, true));

        await NewVerifyContact(s).HandleAsync(new VerifyContact(id));

        s.Db.Contacts.IgnoreQueryFilters().Single(c => c.Id == id).IsVerified.Should().BeTrue();
    }

    [TestMethod]
    public async Task SetPrimaryContact_UnsetsOtherContactsForOwner()
    {
        using var s = new StudentsTestScope("c-primary");
        var studentId = await SeedStudentAsync(s, "S1");
        var first = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "first@example.com", null, true));
        var second = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.SMS, "+111", null, false));

        await NewSetPrimary(s).HandleAsync(new SetPrimaryContact(second));

        s.Db.Contacts.IgnoreQueryFilters().Single(c => c.Id == second).IsPrimary.Should().BeTrue();
        s.Db.Contacts.IgnoreQueryFilters().Single(c => c.Id == first).IsPrimary.Should().BeFalse();
    }

    // ── Subscriptions ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Subscribe_ThenUnsubscribe_TogglesSubscriptionState()
    {
        using var s = new StudentsTestScope("c-sub");
        var studentId = await SeedStudentAsync(s, "S1");
        var contactId = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "kid@example.com", null, true));

        await NewSubscribe(s).HandleAsync(new Subscribe(contactId, SubscriptionScope.AllAssignments, null));

        var subscribed = await new ListSubscribedContactsHandler(s.Db, s.Cache)
            .HandleAsync(new ListSubscribedContacts(ContactOwnerType.Student, studentId, SubscriptionScope.AllAssignments));
        subscribed.Should().ContainSingle(c => c.Id == contactId);

        await NewUnsubscribe(s).HandleAsync(new Unsubscribe(contactId, SubscriptionScope.AllAssignments, null));

        var after = await new ListSubscribedContactsHandler(s.Db, s.Cache)
            .HandleAsync(new ListSubscribedContacts(ContactOwnerType.Student, studentId, SubscriptionScope.AllAssignments));
        after.Should().NotContain(c => c.Id == contactId, "unsubscribed contacts are excluded");
    }

    [TestMethod]
    public async Task NameHistory_Query_ReturnsSnapshotsInOrder()
    {
        using var s = new StudentsTestScope("g-hist-query");
        var id = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        await NewUpdateGuardian(s).HandleAsync(new UpdateGuardian(id, null, "Janet", "Doeman", null, null, null));

        var history = await new GetGuardianNameHistoryHandler(GuardianRepo(s))
            .HandleAsync(new GetGuardianNameHistory(id));

        history.Should().HaveCount(2);
        history[0].FirstName.Should().Be("Jane");
        history[1].FirstName.Should().Be("Janet");
    }
}
