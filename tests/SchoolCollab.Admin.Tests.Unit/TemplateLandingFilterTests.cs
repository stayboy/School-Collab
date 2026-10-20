using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The landing''s filtering, at its pure seam (redesign spec §3.2): the type filter, the contact-mode
/// filter, the subject-and-body search and the grade deep link. Expected values come from the fixture,
/// not from the implementation, so a broken rule fails here instead of being mirrored.
/// </summary>
[TestClass]
public class TemplateLandingFilterTests
{
    private static readonly Guid GradeA = Guid.Parse(TemplateApiHandler.GradeA);
    private static readonly Guid GradeB = Guid.Parse(TemplateApiHandler.GradeB);

    private static TenantNotificationTemplateDto Row(
        NotificationTemplateKind kind,
        NotificationChannel channel,
        string? subject = null,
        string body = "<p>body</p>",
        params (TemplateConstraintDimension Dimension, string Value)[] constraints) =>
        new(Guid.NewGuid(), kind, channel, subject, body,
            constraints.Select(c => new TemplateConstraintDto(Guid.NewGuid(), c.Dimension, c.Value)).ToList(),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [TestMethod]
    public void TheTypeFilter_KeepsOnlyThatKind()
    {
        var rows = new[]
        {
            Row(NotificationTemplateKind.Publish, NotificationChannel.Email, "PUBLISH"),
            Row(NotificationTemplateKind.Reminder, NotificationChannel.Email, "REMINDER"),
        };

        var result = TemplateLandingFilter.Apply(rows, null, "Reminder", string.Empty, string.Empty);

        result.Should().ContainSingle().Which.Subject.Should().Be("REMINDER");
    }

    [TestMethod]
    public void TheModeFilter_KeepsOnlyThatContactMode()
    {
        var rows = new[]
        {
            Row(NotificationTemplateKind.Publish, NotificationChannel.Email, "EMAIL-ROW"),
            Row(NotificationTemplateKind.Publish, NotificationChannel.WhatsApp, "WHATSAPP-ROW"),
        };

        var result = TemplateLandingFilter.Apply(rows, null, string.Empty, "WhatsApp", string.Empty);

        result.Should().ContainSingle().Which.Subject.Should().Be("WHATSAPP-ROW");
    }

    [TestMethod]
    public void TheSearch_MatchesSubjectAndPlainBodyText()
    {
        var rows = new[]
        {
            Row(NotificationTemplateKind.Publish, NotificationChannel.Email, "MENTIONS-HOMEWORK"),
            Row(NotificationTemplateKind.Reminder, NotificationChannel.SMS, body: "<p>body says LIBRARY</p>"),
            Row(NotificationTemplateKind.Overdue, NotificationChannel.SMS, "UNRELATED"),
        };

        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, "homework")
            .Should().ContainSingle().Which.Subject.Should().Be("MENTIONS-HOMEWORK");

        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, "library")
            .Should().ContainSingle().Which.Kind.Should().Be(NotificationTemplateKind.Reminder);
    }

    [TestMethod]
    public void TheGradeDeepLink_HidesAnOtherGradesRow_AndKeepsUnconstrainedRows()
    {
        var rows = new[]
        {
            Row(NotificationTemplateKind.Publish, NotificationChannel.Email, "EVERY-GRADE"),
            Row(NotificationTemplateKind.Publish, NotificationChannel.SMS, body: "GRADE-A-ONLY",
                constraints: (TemplateConstraintDimension.GradeLevel, GradeA.ToString("D"))),
            Row(NotificationTemplateKind.Publish, NotificationChannel.SMS, body: "GRADE-B-ONLY",
                constraints: (TemplateConstraintDimension.GradeLevel, GradeB.ToString("D"))),
        };

        var result = TemplateLandingFilter.Apply(rows, GradeA, string.Empty, string.Empty, string.Empty);

        result.Should().HaveCount(2);
        result.Select(r => r.Subject ?? r.Body).Should().BeEquivalentTo(["EVERY-GRADE", "GRADE-A-ONLY"]);
    }

    [TestMethod]
    public void NoFilters_KeepEveryRow()
    {
        var rows = new[]
        {
            Row(NotificationTemplateKind.Publish, NotificationChannel.Email, "ONE"),
            Row(NotificationTemplateKind.Overdue, NotificationChannel.WhatsApp, "TWO"),
        };

        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, string.Empty).Should().HaveCount(2);
    }
}