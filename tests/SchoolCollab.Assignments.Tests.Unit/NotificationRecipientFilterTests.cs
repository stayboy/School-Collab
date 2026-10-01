using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round B1 (<c>documents/rounds/round-assignment-policy-ui.md</c> Plan (f) AC6) — the publish
/// recipient filter: blocked channels, preferred-channel order, <c>MaxNotifications</c> (D5,
/// unchanged) and the two per-role contact caps (D4) with the Q7 non-positive ⇒ uncapped branch.
/// Bucketing keys on <see cref="AssignmentRecipient.OwnerType"/>; student-owned rows carry no
/// guardian role by construction and are exempt from both caps.
/// </summary>
[TestClass]
public class NotificationRecipientFilterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AssignmentId = Guid.NewGuid();

    private static AssignmentRecipient Recipient(
        ContactChannel channel, int n, ContactOwnerType ownerType, GuardianRole? role) =>
        AssignmentRecipient.Create(
            TenantId, AssignmentId, ownerType, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), channel, role, notifyOnBroadcast: true, subscriptionActive: true);

    /// <summary>A guardian-owned recipient whose role is mirrored from the StudentGuardian link.</summary>
    private static AssignmentRecipient Guardian(ContactChannel channel, GuardianRole role, int n) =>
        Recipient(channel, n, ContactOwnerType.Guardian, role);

    /// <summary>A student-owned recipient — no guardian role exists for it (Role is null).</summary>
    private static AssignmentRecipient Student(ContactChannel channel, int n) =>
        Recipient(channel, n, ContactOwnerType.Student, role: null);

    private static EffectiveNotificationPolicy Policy(
        NotificationChannel[]? preferred = null,
        NotificationChannel[]? blocked = null,
        int? maxNotifications = null)
    {
        preferred ??= [];
        blocked ??= [];
        return new EffectiveNotificationPolicy(
            preferred, blocked, maxNotifications, null, null, null, null, null,
            PreferredChannelOrderFromOverride: false, BlockedChannelsFromOverride: false, MaxNotificationsFromOverride: false,
            MaxRemindersFromOverride: false, ReminderIntervalHoursFromOverride: false, LinkValidityDaysFromOverride: false,
            SendoutTimeOfDayFromOverride: false, SendoutIntervalMinutesFromOverride: false);
    }

    /// <summary>
    /// The effective assignment policy's contribution to the filter: the two contact caps (D4).
    /// A null cap means uncapped; a non-positive cap is stored-but-meaningless and must behave
    /// as uncapped too (Q7).
    /// </summary>
    private static EffectiveAssignmentPolicy AssignmentPolicy(int? maxPrimary = null, int? maxCopy = null) =>
        new(SignatureRequirementMode.Disabled, RequiresApprovalBeforePublish: false,
            MaxPrimaryContacts: maxPrimary, MaxCopyContacts: maxCopy,
            SignatureRequirementFromOverride: false, RequiresApprovalBeforePublishFromOverride: false,
            MaxPrimaryContactsFromOverride: false, MaxCopyContactsFromOverride: false);

    private static IReadOnlyList<AssignmentRecipient> Apply(
        IReadOnlyList<AssignmentRecipient> recipients,
        EffectiveNotificationPolicy policy,
        EffectiveAssignmentPolicy? assignmentPolicy = null) =>
        NotificationRecipientFilter.Apply(recipients, policy, assignmentPolicy ?? AssignmentPolicy());

    [TestMethod]
    public void Empty_recipients_returns_empty()
    {
        Apply([], Policy()).Should().BeEmpty();
    }

    [TestMethod]
    public void No_policy_keeps_all_in_enum_order()
    {
        var input = new[] { Guardian(ContactChannel.SMS, GuardianRole.Primary, 1), Guardian(ContactChannel.Email, GuardianRole.Primary, 2), Guardian(ContactChannel.WhatsApp, GuardianRole.Primary, 3) };

        var result = Apply(input, Policy());

        result.Select(r => r.Channel).Should().Equal(ContactChannel.Email, ContactChannel.SMS, ContactChannel.WhatsApp);
    }

    [TestMethod]
    public void Filters_blocked_channel_recipients()
    {
        var input = new[] { Guardian(ContactChannel.Email, GuardianRole.Primary, 1), Guardian(ContactChannel.SMS, GuardianRole.Primary, 2), Guardian(ContactChannel.WhatsApp, GuardianRole.Primary, 3) };
        var policy = Policy(blocked: [NotificationChannel.SMS]);

        var result = Apply(input, policy);

        result.Select(r => r.Channel).Should().Equal(ContactChannel.Email, ContactChannel.WhatsApp);
    }

    [TestMethod]
    public void Applies_preferred_channel_order()
    {
        var input = new[] { Guardian(ContactChannel.Email, GuardianRole.Primary, 1), Guardian(ContactChannel.SMS, GuardianRole.Primary, 2), Guardian(ContactChannel.WhatsApp, GuardianRole.Primary, 3) };
        var policy = Policy(preferred: [NotificationChannel.SMS, NotificationChannel.Email]);

        var result = Apply(input, policy);

        result.Select(r => r.Channel).Should().Equal(ContactChannel.SMS, ContactChannel.Email, ContactChannel.WhatsApp);
    }

    [TestMethod]
    public void Channels_not_in_preferred_order_go_last_then_by_enum()
    {
        var input = new[] { Guardian(ContactChannel.WhatsApp, GuardianRole.Primary, 1), Guardian(ContactChannel.Email, GuardianRole.Primary, 2), Guardian(ContactChannel.SMS, GuardianRole.Primary, 3) };
        var policy = Policy(preferred: [NotificationChannel.Email]);

        var result = Apply(input, policy);

        result.Select(r => r.Channel).Should().Equal(ContactChannel.Email, ContactChannel.SMS, ContactChannel.WhatsApp);
    }

    [TestMethod]
    public void Caps_sendout_at_max_notifications()
    {
        var input = new[] { Guardian(ContactChannel.Email, GuardianRole.Primary, 1), Guardian(ContactChannel.SMS, GuardianRole.Primary, 2), Guardian(ContactChannel.WhatsApp, GuardianRole.Primary, 3) };
        var policy = Policy(maxNotifications: 2);

        var result = Apply(input, policy);

        result.Select(r => r.Channel).Should().Equal(ContactChannel.Email, ContactChannel.SMS);
    }

    [TestMethod]
    public void Max_notifications_zero_broadcasts_nobody()
    {
        var input = new[] { Guardian(ContactChannel.Email, GuardianRole.Primary, 1) };
        var policy = Policy(maxNotifications: 0);

        Apply(input, policy).Should().BeEmpty();
    }

    // ── D4 — the two per-role contact caps ──────────────────────────────

    [TestMethod]
    public void Max_primary_contacts_trims_only_primary_guardians_preserving_channel_order()
    {
        var primaryEmail = Guardian(ContactChannel.Email, GuardianRole.Primary, 1);
        var primarySms = Guardian(ContactChannel.SMS, GuardianRole.Primary, 2);
        var copyEmail = Guardian(ContactChannel.Email, GuardianRole.CC, 3);
        var copySms = Guardian(ContactChannel.SMS, GuardianRole.CC, 4);
        var input = new[] { primaryEmail, primarySms, copyEmail, copySms };

        var result = Apply(input, Policy(), AssignmentPolicy(maxPrimary: 1));

        // Channel order (Email, SMS) is preserved across the surviving rows, and only the
        // SECOND primary was dropped — copy recipients are untouched by the primary cap.
        result.Select(r => (r.Role, r.Channel)).Should().Equal(
            (GuardianRole.Primary, ContactChannel.Email),
            (GuardianRole.CC, ContactChannel.Email),
            (GuardianRole.CC, ContactChannel.SMS));
        result.Should().Contain(primaryEmail).And.Contain(copyEmail).And.Contain(copySms);
        result.Should().NotContain(primarySms, "the cap keeps the first primary in the established order");
    }

    [TestMethod]
    public void Max_copy_contacts_trims_only_other_guardians_preserving_channel_order()
    {
        var primaryEmail = Guardian(ContactChannel.Email, GuardianRole.Primary, 1);
        var primarySms = Guardian(ContactChannel.SMS, GuardianRole.Primary, 2);
        var copyEmail = Guardian(ContactChannel.Email, GuardianRole.CC, 3);
        var copySms = Guardian(ContactChannel.SMS, GuardianRole.CC, 4);
        var input = new[] { primaryEmail, primarySms, copyEmail, copySms };

        var result = Apply(input, Policy(), AssignmentPolicy(maxCopy: 1));

        result.Select(r => (r.Role, r.Channel)).Should().Equal(
            (GuardianRole.Primary, ContactChannel.Email),
            (GuardianRole.CC, ContactChannel.Email),
            (GuardianRole.Primary, ContactChannel.SMS));
        result.Should().NotContain(copySms, "the copy cap keeps the first copy in the established order");
    }

    [TestMethod]
    public void Student_owned_recipients_are_exempt_from_both_caps()
    {
        var studentEmail = Student(ContactChannel.Email, 1);
        var studentSms = Student(ContactChannel.SMS, 2);
        var primary = Guardian(ContactChannel.Email, GuardianRole.Primary, 3);
        var copy = Guardian(ContactChannel.Email, GuardianRole.CC, 4);
        var input = new[] { studentEmail, studentSms, primary, copy };

        // The invariant D4's split rests on (assignment-policy-fields.md §9.2): a guardian-owned
        // recipient can never be role-less, because StudentGuardian.Role is a non-nullable enum —
        // so a null role on a guardian row is a data defect, not a bucketing choice.
        input.Where(r => r.OwnerType == ContactOwnerType.Guardian)
            .Should().OnlyContain(r => r.Role != null,
                "a guardian-owned recipient always carries the mirrored GuardianRole");

        var result = Apply(input, Policy(), AssignmentPolicy(maxPrimary: 1, maxCopy: 1));

        result.Should().Contain(studentEmail).And.Contain(studentSms);
        result.Where(r => r.OwnerType == ContactOwnerType.Student)
            .Should().HaveCount(2, "an OwnerType.Student row is consumed by neither cap")
            .And.OnlyContain(r => r.Role == null,
                "a student-owned recipient has no guardian role by construction");
        result.Should().Contain(primary).And.Contain(copy);
    }

    [TestMethod]
    public void Role_caps_apply_after_the_preferred_channel_order()
    {
        var primaryEmail = Guardian(ContactChannel.Email, GuardianRole.Primary, 1);
        var primarySms = Guardian(ContactChannel.SMS, GuardianRole.Primary, 2);
        var input = new[] { primaryEmail, primarySms };

        // SMS is preferred, so it is the primary that survives the cap of one.
        var result = Apply(
            input,
            Policy(preferred: [NotificationChannel.SMS]),
            AssignmentPolicy(maxPrimary: 1));

        result.Should().ContainSingle().Which.Should().Be(primarySms);
    }

    [TestMethod]
    public void Smaller_max_notifications_wins_over_the_role_caps()
    {
        var primaryEmail = Guardian(ContactChannel.Email, GuardianRole.Primary, 1);
        var primarySms = Guardian(ContactChannel.SMS, GuardianRole.Primary, 2);
        var copyEmail = Guardian(ContactChannel.Email, GuardianRole.CC, 3);
        var input = new[] { primaryEmail, primarySms, copyEmail };

        // Two role caps of 2 keep all three; the sendout cap of 1 then trims to the head
        // of the ordered list (D5 — whichever cap is smaller wins).
        var result = Apply(input, Policy(maxNotifications: 1), AssignmentPolicy(maxPrimary: 2, maxCopy: 2));

        result.Should().ContainSingle().Which.Should().Be(primaryEmail);
    }

    [TestMethod]
    public void Null_caps_are_uncapped()
    {
        var input = new[]
        {
            Guardian(ContactChannel.Email, GuardianRole.Primary, 1),
            Guardian(ContactChannel.SMS, GuardianRole.Primary, 2),
            Guardian(ContactChannel.WhatsApp, GuardianRole.CC, 3),
        };

        Apply(input, Policy(), AssignmentPolicy(maxPrimary: null, maxCopy: null)).Should().HaveCount(3);
    }

    [TestMethod]
    public void Non_positive_caps_behave_as_uncapped()
    {
        // Q7: the write path rejects a non-positive cap, but rows written before that rule
        // exist — a stored 0/-3 must never wipe the sendout; it is treated as uncapped.
        var input = new[]
        {
            Guardian(ContactChannel.Email, GuardianRole.Primary, 1),
            Guardian(ContactChannel.SMS, GuardianRole.Primary, 2),
            Guardian(ContactChannel.WhatsApp, GuardianRole.CC, 3),
        };

        Apply(input, Policy(), AssignmentPolicy(maxPrimary: 0, maxCopy: -3)).Should().HaveCount(3);
    }

    [TestMethod]
    public void One_cap_hit_and_the_other_missed_leaves_the_other_role_uncapped()
    {
        var primaryEmail = Guardian(ContactChannel.Email, GuardianRole.Primary, 1);
        var primarySms = Guardian(ContactChannel.SMS, GuardianRole.Primary, 2);
        var copyEmail = Guardian(ContactChannel.Email, GuardianRole.CC, 3);
        var copySms = Guardian(ContactChannel.SMS, GuardianRole.CC, 4);
        var input = new[] { primaryEmail, primarySms, copyEmail, copySms };

        var result = Apply(input, Policy(), AssignmentPolicy(maxPrimary: 1, maxCopy: null));

        result.Should().HaveCount(3);
        result.Should().Contain(copySms, "an unset copy cap leaves every copy recipient in place");
        result.Should().NotContain(primarySms);
    }

    [TestMethod]
    public void Caps_are_ignored_when_the_recipient_set_is_empty()
    {
        Apply([], Policy(), AssignmentPolicy(maxPrimary: 1, maxCopy: 1)).Should().BeEmpty();
    }
}
