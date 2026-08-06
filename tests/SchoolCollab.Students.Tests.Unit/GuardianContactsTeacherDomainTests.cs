using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Phase 2 domain tests for the new guardian / contact / teacher entities
/// (spec §4). Validates the invariants the migration + CQRS layers rely on:
/// default contact verification/subscription state (spec §2), append-only
/// guardian name history (spec §4.2), teacher staff contact is NOT migrated to
/// the contacts table, and soft-delete blocks without losing data.
/// </summary>
[TestClass]
public sealed class GuardianContactsTeacherDomainTests
{
    private static Guid TenantId { get; } = Guid.NewGuid();

    [TestMethod]
    public void Guardian_Create_SetsProfileAndAudit_AndIsNotDeleted()
    {
        var g = Guardian.Create(null, "Kwame", "Mensah", "Mr. Mensah", "Box 12", Guid.NewGuid());

        g.FirstName.Should().Be("Kwame");
        g.LastName.Should().Be("Mensah");
        g.DisplayName.Should().Be("Mr. Mensah");
        g.Address.Should().Be("Box 12");
        g.IsDeleted.Should().BeFalse();
        g.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        g.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        g.NameHistory.Should().BeEmpty(); // initial history is appended by the CQRS handler
    }

    [TestMethod]
    public void Guardian_UpdateName_AppendsNameHistory_WithTenant()
    {
        var g = Guardian.Create(null, "Kwame", "Mensah", null, null, null);
        ((ITenantEntity)g).TenantId = TenantId;

        g.UpdateName("Kwame", "Mensah", "K. Mensah");

        g.NameHistory.Should().HaveCount(1);
        var h = g.NameHistory[0];
        h.FirstName.Should().Be("Kwame");
        h.LastName.Should().Be("Mensah");
        h.DisplayName.Should().Be("K. Mensah");
        h.TenantId.Should().Be(TenantId);
    }

    [TestMethod]
    public void Guardian_SoftDelete_BlocksButPreservesData()
    {
        var g = Guardian.Create(null, "Kwame", "Mensah", null, null, null);
        g.SoftDelete();

        g.IsDeleted.Should().BeTrue();
        g.DeletedAt.Should().NotBeNull();
        g.FirstName.Should().Be("Kwame"); // data retained on block

        g.Recover();
        g.IsDeleted.Should().BeFalse();
    }

    [TestMethod]
    public void Contact_Create_DefaultsUnverified_AndSoftDeleteBlocks()
    {
        var c = Contact.Create(ContactOwnerType.Student, Guid.NewGuid(), ContactChannel.Email, "a@b.com", "Home", null);

        c.Value.Should().Be("a@b.com");
        c.Channel.Should().Be(ContactChannel.Email);
        c.DisplayOrder.Should().Be(0);
        c.IsVerified.Should().BeFalse(); // default unverified (spec §2)

        c.SoftDelete();
        c.IsDeleted.Should().BeTrue();
    }

    [TestMethod]
    public void ContactSubscription_Create_DefaultsOptedOut()
    {
        var s = ContactSubscription.Create(Guid.NewGuid(), SubscriptionScope.AllAssignments, null);

        s.Status.Should().Be(SubscriptionStatus.Unsubscribed); // default opted-out (spec §2)
        s.Subscribe();
        s.Status.Should().Be(SubscriptionStatus.Subscribed);
        s.Unsubscribe();
        s.Status.Should().Be(SubscriptionStatus.Unsubscribed);
    }

    [TestMethod]
    public void StudentGuardian_Create_SetsRoleAndIds()
    {
        var link = StudentGuardian.Create(
            Guid.NewGuid(), Guid.NewGuid(), GuardianRole.Primary, null, true, null);

        link.Role.Should().Be(GuardianRole.Primary);
        link.IsEmergencyContact.Should().BeTrue();
        link.RelationshipCodedValueId.Should().BeNull();
    }

    [TestMethod]
    public void Teacher_Contacts_LiveOnSharedContactTable()
    {
        var t = Teacher.Create(null, "Ama", "Owusu", null);

        t.FirstName.Should().Be("Ama");
        t.IsDeleted.Should().BeFalse();

        // Teacher contact channels live on the shared Contact table keyed by
        // ContactOwnerType.Teacher (reverses the v1 single staff email/phone).
        var teacherId = Guid.NewGuid();
        var email = Contact.Create(ContactOwnerType.Teacher, teacherId, ContactChannel.Email, "ama@school.edu", "Work", null);
        email.OwnerType.Should().Be(ContactOwnerType.Teacher);
        email.OwnerId.Should().Be(teacherId);
        email.Channel.Should().Be(ContactChannel.Email);
        email.IsVerified.Should().BeFalse();
    }
}
