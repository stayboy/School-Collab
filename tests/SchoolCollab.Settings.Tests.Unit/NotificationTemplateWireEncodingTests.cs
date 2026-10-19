using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Round notification-templates-b2c — the numeric encoding the <b>cross-context</b> template read
/// depends on. The Settings host registers no <c>JsonStringEnumConverter</c>, so the template DTO's
/// enums serialize with their underlying values; the Assignments side mirrors them as <c>int</c> and
/// translates the constraint dimension back to a name. That only works while these numbers hold.
///
/// <para>This is not a stylistic preference: <see cref="TenantNotificationTemplate.Kind"/>,
/// <see cref="TenantNotificationTemplate.Channel"/> and <see cref="TemplateConstraint.Dimension"/> are
/// persisted with <c>HasConversion&lt;int&gt;()</c>, so the values below are already frozen in
/// settings-db. Reordering or re-numbering a member is a data migration plus a matcher change — this
/// test exists so it cannot happen by accident and silently disable constrained matching.</para>
/// </summary>
[TestClass]
public class NotificationTemplateWireEncodingTests
{
    [TestMethod]
    public void TemplateConstraintDimension_NumericValues_AreTheFrozenWireAndStorageEncoding()
    {
        // The Assignments-side resolver maps these numbers to GradeLevel/Stream/ActivityGroup/
        // Subject/AssignmentType in this exact order.
        ((int)TemplateConstraintDimension.GradeLevel).Should().Be(0);
        ((int)TemplateConstraintDimension.Stream).Should().Be(1);
        ((int)TemplateConstraintDimension.ActivityGroup).Should().Be(2);
        ((int)TemplateConstraintDimension.Subject).Should().Be(3);
        ((int)TemplateConstraintDimension.AssignmentType).Should().Be(4);
    }

    [TestMethod]
    public void NotificationTemplateKind_NumericValues_MatchTheAssignmentsSideNotificationKind()
    {
        // The candidate read filters the list payload on these numbers, and Assignments.Core's own
        // NotificationKind uses the same order (Publish/Reminder/Completion/Overdue).
        ((int)NotificationTemplateKind.Publish).Should().Be(0);
        ((int)NotificationTemplateKind.Reminder).Should().Be(1);
        ((int)NotificationTemplateKind.Completion).Should().Be(2);
        ((int)NotificationTemplateKind.Overdue).Should().Be(3);
    }
}
