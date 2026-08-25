using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.AddContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.DeleteContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.Subscribe;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.Unsubscribe;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.UpdateContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.SetContactOrder;
using SchoolCollab.Students.Core.CQRS.Contacts.Commands.VerifyContact;
using SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListContactAuditEntries;
using SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListContacts;
using SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListSubscribedContacts;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.CreateGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.DeleteGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.LinkGuardianToStudent;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.UnlinkGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.UpdateGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.UpdateGuardianLink;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.GetGuardianNameHistory;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardians;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardiansByStudent;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Services;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Contracts.Events;

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
        new(ContactRepo(s), s.Db, s.Tenants, new ContactAuditor(new SystemActorAccessor("test", "Test Actor")), s.Cache, NullLogger<UpdateContactHandler>.Instance);
    private static DeleteContactHandler NewDeleteContact(StudentsTestScope s) =>
        new(ContactRepo(s), s.Db, s.Tenants, new ContactAuditor(new SystemActorAccessor("test", "Test Actor")), s.Cache, NullLogger<DeleteContactHandler>.Instance);
    private static ListContactAuditEntriesHandler NewListAudit(StudentsTestScope s) =>
        new(s.Db);
    private static VerifyContactHandler NewVerifyContact(StudentsTestScope s) =>
        new(ContactRepo(s), s.Cache, NullLogger<VerifyContactHandler>.Instance);
    private static SubscribeHandler NewSubscribe(StudentsTestScope s) =>
        new(ContactRepo(s), s.Cache, s.Tenants, NullLogger<SubscribeHandler>.Instance);
    private static UnsubscribeHandler NewUnsubscribe(StudentsTestScope s) =>
        new(ContactRepo(s), s.Cache, s.Tenants, NullLogger<UnsubscribeHandler>.Instance);

    /// <summary>Recording publisher — captures every enqueued integration event
    /// so a test can assert both the count and the payload. Mirrors the
    /// <c>RecordingPublisher</c> in <c>EnrollStudentHandlerTests</c>.</summary>
    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<object> Enqueued { get; } = new();
        public Task EnqueueAsync<T>(T message, CancellationToken ct = default) where T : class
        {
            Enqueued.Add(message);
            return Task.CompletedTask;
        }

        public Task EnqueueAsync<T>(T message, Guid? tenantStamp, CancellationToken ct = default)
            where T : class
            => EnqueueAsync(message, ct);
    }

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
            new AddContact(ContactOwnerType.Guardian, guardianId, ContactChannel.Email, "jane@example.com", null));

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

    [TestMethod]
    public async Task UpdateGuardianLink_PersistsChange_AndEnqueuesSingleUpdatedEvent()
    {
        using var s = new StudentsTestScope("g-link-update");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentId = await SeedStudentAsync(s, "S1");
        await NewLink(s).HandleAsync(new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.Primary, false, null));

        var publisher = new RecordingPublisher();
        var handler = new UpdateGuardianLinkHandler(
            GuardianRepo(s), publisher, s.Cache, NullLogger<UpdateGuardianLinkHandler>.Instance);

        var relId = Guid.NewGuid();
        await handler.HandleAsync(new UpdateGuardianLink(studentId, guardianId, GuardianRole.CC, relId, true));

        // The link metadata is updated in place.
        var link = s.Db.StudentGuardians.IgnoreQueryFilters().Single(l => l.GuardianId == guardianId);
        link.Role.Should().Be(GuardianRole.CC);
        link.RelationshipCodedValueId.Should().Be(relId);
        link.IsEmergencyContact.Should().BeTrue();

        // Spec §3.2 / §5: exactly one StudentGuardianUpdated integration event
        // is enqueued (no unlink+relink double event).
        publisher.Enqueued.Should().ContainSingle(e => e is StudentGuardianUpdated);
        var evt = (StudentGuardianUpdated)publisher.Enqueued.Single(e => e is StudentGuardianUpdated);
        evt.StudentId.Should().Be(studentId);
        evt.GuardianId.Should().Be(guardianId);
        evt.Role.Should().Be(nameof(GuardianRole.CC));
        evt.RelationshipCodedValueId.Should().Be(relId);
        evt.IsEmergencyContact.Should().BeTrue();
    }

    // ── Contacts ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AddContact_CreatesMultiChannelContact_PerOwner()
    {
        using var s = new StudentsTestScope("c-add");
        var studentId = await SeedStudentAsync(s, "S1");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));

        var studentContact = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "kid@example.com", null));
        var guardianContact = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Guardian, guardianId, ContactChannel.SMS, "+12345", "Mobile"));

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
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "old@example.com", "Old"));

        await NewUpdateContact(s).HandleAsync(new UpdateContact(id, "new@example.com", "New", "Updated email"));

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
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "kid@example.com", null));

        await NewDeleteContact(s).HandleAsync(new DeleteContact(id, "No longer needed"));

        s.Db.Contacts.IgnoreQueryFilters().Single(c => c.Id == id).IsDeleted.Should().BeTrue();
    }

    [TestMethod]
    public async Task UpdateContact_WritesAuditEntry_WithReasonAndActor()
    {
        using var s = new StudentsTestScope("c-audit-update");
        var studentId = await SeedStudentAsync(s, "S1");
        var id = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "old@example.com", "Old"));

        await NewUpdateContact(s).HandleAsync(
            new UpdateContact(id, "new@example.com", "New", "Parent requested change"));

        var entries = await NewListAudit(s).HandleAsync(
            new ListContactAuditEntries(null, ContactOwnerType.Student, studentId, 0, 50));
        entries.Should().ContainSingle();
        var e = entries[0];
        e.ChangeKind.Should().Be("Updated");
        e.PreviousValue.Should().Be("old@example.com");
        e.NewValue.Should().Be("new@example.com");
        e.Reason.Should().Be("Parent requested change");
        e.ActorDisplayName.Should().Be("Test Actor");
        e.ContactId.Should().Be(id);
    }

    [TestMethod]
    public async Task DeleteContact_WritesAuditEntry_WithReason()
    {
        using var s = new StudentsTestScope("c-audit-delete");
        var studentId = await SeedStudentAsync(s, "S1");
        var id = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "kid@example.com", null));

        await NewDeleteContact(s).HandleAsync(new DeleteContact(id, "Duplicate entry"));

        var entries = await NewListAudit(s).HandleAsync(
            new ListContactAuditEntries(null, ContactOwnerType.Student, studentId, 0, 50));
        entries.Should().ContainSingle();
        var e = entries[0];
        e.ChangeKind.Should().Be("Deleted");
        e.PreviousValue.Should().Be("kid@example.com");
        e.Reason.Should().Be("Duplicate entry");
    }

    [TestMethod]
    public async Task ListContactAuditEntries_FiltersByOwner()
    {
        using var s = new StudentsTestScope("c-audit-filter");
        var studentId = await SeedStudentAsync(s, "S1");
        var otherStudentId = await SeedStudentAsync(s, "S2");
        var id = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "kid@example.com", null));
        var otherId = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, otherStudentId, ContactChannel.Email, "other@example.com", null));

        await NewDeleteContact(s).HandleAsync(new DeleteContact(id, "Duplicate"));
        await NewDeleteContact(s).HandleAsync(new DeleteContact(otherId, "Duplicate"));

        var forStudent = await NewListAudit(s).HandleAsync(
            new ListContactAuditEntries(null, ContactOwnerType.Student, studentId, 0, 50));
        forStudent.Should().ContainSingle();
        forStudent[0].ContactId.Should().Be(id);
    }

    [TestMethod]
    public async Task VerifyContact_SetsVerified()
    {
        using var s = new StudentsTestScope("c-verify");
        var studentId = await SeedStudentAsync(s, "S1");
        var id = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "kid@example.com", null));

        await NewVerifyContact(s).HandleAsync(new VerifyContact(id));

        s.Db.Contacts.IgnoreQueryFilters().Single(c => c.Id == id).IsVerified.Should().BeTrue();
    }

    [TestMethod]
    public async Task SetContactOrder_MovesContactToPreferredPosition()
    {
        using var s = new StudentsTestScope("c-order");
        var studentId = await SeedStudentAsync(s, "S1");
        var first = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "first@example.com", null));
        var second = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.SMS, "+111", null) { DisplayOrder = 1 });

        await new SetContactOrderHandler(ContactRepo(s), s.Cache, NullLogger<SetContactOrderHandler>.Instance)
            .HandleAsync(new SetContactOrder(second, 0));

        s.Db.Contacts.IgnoreQueryFilters().Single(c => c.Id == second).DisplayOrder.Should().Be(0);
        s.Db.Contacts.IgnoreQueryFilters().Single(c => c.Id == first).DisplayOrder.Should().Be(1);
    }

    [TestMethod]
    public async Task AddContact_WithCountryCode_StoresCountryCode()
    {
        using var s = new StudentsTestScope("c-add-cc");
        var studentId = await SeedStudentAsync(s, "S1");

        var id = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.SMS, "201234567", "Mobile")
            { CountryCode = "+233" });

        var c = s.Db.Contacts.IgnoreQueryFilters().Single(x => x.Id == id);
        c.CountryCode.Should().Be("+233");
        c.Value.Should().Be("201234567");
    }

    [TestMethod]
    public async Task UpdateContact_ChangesCountryCode()
    {
        using var s = new StudentsTestScope("c-update-cc");
        var studentId = await SeedStudentAsync(s, "S1");
        var id = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.SMS, "201234567", "Mobile")
            { CountryCode = "+233" });

        await NewUpdateContact(s).HandleAsync(
            new UpdateContact(id, "208765432", "Mobile", "Changed to SA number") { CountryCode = "+27" });

        var c = s.Db.Contacts.IgnoreQueryFilters().Single(x => x.Id == id);
        c.Value.Should().Be("208765432");
        c.CountryCode.Should().Be("+27");
    }

    [TestMethod]
    public async Task ListContacts_ProjectsCountryCode()
    {
        using var s = new StudentsTestScope("c-list-cc");
        var studentId = await SeedStudentAsync(s, "S1");
        await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.SMS, "201234567", "Mobile")
            { CountryCode = "+233" });

        var results = await new ListContactsHandler(s.Db, s.Cache)
            .HandleAsync(new ListContacts(ContactOwnerType.Student, studentId));

        results.Should().ContainSingle(c => c.CountryCode == "+233");
    }

    // ── Subscriptions ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Subscribe_ThenUnsubscribe_TogglesSubscriptionState()
    {
        using var s = new StudentsTestScope("c-sub");
        var studentId = await SeedStudentAsync(s, "S1");
        var contactId = await NewAddContact(s).HandleAsync(
            new AddContact(ContactOwnerType.Student, studentId, ContactChannel.Email, "kid@example.com", null));

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

    // ── ListGuardiansByStudent: TotalContactCount + capped Contacts ─────────────
    // (spec 2026-07-27 §4.2 / §5). The handler projects TotalContactCount
    // (all non-deleted contacts) alongside the top-3 Contacts, so the
    // student-view grid can show the "View all (N) contacts" anchor only
    // when a guardian has MORE than 3 contacts (Contacts.Length == 3 is
    // ambiguous between exactly-3 and more-than-3).

    /// <summary>Adds <paramref name="count"/> guardian-owned contacts with
    /// distinct values so each add is a separate row. Returns the contact
    /// ids in creation order.</summary>
    private static async Task<Guid[]> AddContactsAsync(
        StudentsTestScope s, Guid guardianId, int count)
    {
        var ids = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = await NewAddContact(s).HandleAsync(
                new AddContact(ContactOwnerType.Guardian, guardianId,
                    ContactChannel.Email, $"g{i}@example.com", null));
        }
        return ids;
    }

    [TestMethod]
    public async Task ListGuardiansByStudent_TotalContactCount_ReflectsAllContacts_AndContactsCappedAtThree()
    {
        using var s = new StudentsTestScope("g-list-by-student-count");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentId = await SeedStudentAsync(s, "S1");
        await NewLink(s).HandleAsync(new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.Primary, false, null));
        await AddContactsAsync(s, guardianId, count: 5);

        var rows = await new ListGuardiansByStudentHandler(s.Db, s.Cache)
            .HandleAsync(new ListGuardiansByStudent(studentId));

        rows.Should().ContainSingle("one guardian is linked");
        var row = rows[0];
        row.GuardianId.Should().Be(guardianId);
        row.TotalContactCount.Should().Be(5, "all five non-deleted contacts are counted");
        row.Contacts.Should().HaveCount(3, "the inline grid columns cap at 3 (C1/C2/C3)");
        row.HasMoreContacts.Should().BeTrue("5 > 3 → the View-all anchor should show");
    }

    [TestMethod]
    public async Task ListGuardiansByStudent_WithExactlyThreeContacts_HasMoreContactsFalse()
    {
        using var s = new StudentsTestScope("g-list-by-student-exactly-three");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentId = await SeedStudentAsync(s, "S1");
        await NewLink(s).HandleAsync(new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.Primary, false, null));
        await AddContactsAsync(s, guardianId, count: 3);

        var rows = await new ListGuardiansByStudentHandler(s.Db, s.Cache)
            .HandleAsync(new ListGuardiansByStudent(studentId));

        var row = rows.Single();
        row.TotalContactCount.Should().Be(3, "exactly three contacts");
        row.Contacts.Should().HaveCount(3);
        row.HasMoreContacts.Should().BeFalse("3 is NOT more than 3 → the anchor must NOT show");
    }

    [TestMethod]
    public async Task ListGuardiansByStudent_WithTwoContacts_TotalCountTwo_ContactsTwo()
    {
        using var s = new StudentsTestScope("g-list-by-student-two");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentId = await SeedStudentAsync(s, "S1");
        await NewLink(s).HandleAsync(new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.Primary, false, null));
        await AddContactsAsync(s, guardianId, count: 2);

        var rows = await new ListGuardiansByStudentHandler(s.Db, s.Cache)
            .HandleAsync(new ListGuardiansByStudent(studentId));

        var row = rows.Single();
        row.TotalContactCount.Should().Be(2);
        row.Contacts.Should().HaveCount(2, "fewer than 3 contacts render inline (C1, C2)");
        row.HasMoreContacts.Should().BeFalse();
    }

    [TestMethod]
    public async Task ListGuardiansByStudent_ExcludesSoftDeletedContactsFromTotalCount()
    {
        using var s = new StudentsTestScope("g-list-by-student-deleted");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentId = await SeedStudentAsync(s, "S1");
        await NewLink(s).HandleAsync(new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.Primary, false, null));
        var contactIds = await AddContactsAsync(s, guardianId, count: 5);

        // Soft-delete one of the five. The handler filters !c.IsDeleted, so
        // TotalContactCount must drop to 4 (still > 3 → anchor still shows)
        // and Contacts must still cap at 3.
        await NewDeleteContact(s).HandleAsync(new DeleteContact(contactIds[0], "Duplicate"));

        var rows = await new ListGuardiansByStudentHandler(s.Db, s.Cache)
            .HandleAsync(new ListGuardiansByStudent(studentId));

        var row = rows.Single();
        row.TotalContactCount.Should().Be(4, "the soft-deleted contact is excluded from the count");
        row.Contacts.Should().HaveCount(3);
        row.HasMoreContacts.Should().BeTrue();
    }

    // ── ListGuardians: ExcludeStudentId (picker double-link prevention) ────────
    // The guardian picker passes the student id so the backend hides
    // guardians already linked to that student — the user cannot pick a
    // guardian that is already linked (spec 2026-07-27 §4.4 / §4.5 wiring).

    [TestMethod]
    public async Task ListGuardians_ExcludeStudentId_HidesGuardiansAlreadyLinkedToThatStudent()
    {
        using var s = new StudentsTestScope("g-list-exclude-student");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentId = await SeedStudentAsync(s, "S1");
        await NewLink(s).HandleAsync(new LinkGuardianToStudent(studentId, guardianId, null, GuardianRole.Primary, false, null));

        // No exclusion → the linked guardian IS offered (the picker's
        // default-when-no-student-context path, e.g. a fresh wizard).
        var none = await new ListGuardiansHandler(s.Db, s.Cache)
            .HandleAsync(new ListGuardians(null, null));
        none.Should().Contain(g => g.Id == guardianId,
            "with no exclusion the guardian is offered");

        // Excluding the linked student → the guardian is HIDDEN.
        var excluded = await new ListGuardiansHandler(s.Db, s.Cache)
            .HandleAsync(new ListGuardians(null, studentId));
        excluded.Should().NotContain(g => g.Id == guardianId,
            "a guardian already linked to the excluded student is not offered (prevents double-linking)");
    }

    [TestMethod]
    public async Task ListGuardians_ExcludeStudentId_Only_Hides_That_Students_Guardians()
    {
        using var s = new StudentsTestScope("g-list-exclude-student-other");
        var guardianId = await NewCreateGuardian(s).HandleAsync(new CreateGuardian(null, "Jane", "Doe", null, null, null));
        var studentA = await SeedStudentAsync(s, "SA");
        var studentB = await SeedStudentAsync(s, "SB");
        // Guardian is linked to student A only.
        await NewLink(s).HandleAsync(new LinkGuardianToStudent(studentA, guardianId, null, GuardianRole.Primary, false, null));

        // Excluding student B (a DIFFERENT student) must NOT hide the
        // guardian — a guardian can be linked to multiple students, so
        // excluding one student only hides that student's links.
        var forB = await new ListGuardiansHandler(s.Db, s.Cache)
            .HandleAsync(new ListGuardians(null, studentB));
        forB.Should().Contain(g => g.Id == guardianId,
            "excluding a different student does not hide a guardian linked only to student A");

        // Excluding student A (the one the guardian IS linked to) hides it.
        var forA = await new ListGuardiansHandler(s.Db, s.Cache)
            .HandleAsync(new ListGuardians(null, studentA));
        forA.Should().NotContain(g => g.Id == guardianId,
            "excluding student A hides the guardian linked to A");
    }
}
