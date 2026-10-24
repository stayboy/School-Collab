using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The landing's filtering, at its pure seam (redesign spec §3.2): the type filter, the contact-mode
/// filter, the name-and-content search (a template's title, its description, its subject and its plain
/// body text) and the grade deep link. Expected values come from the fixture, not from the
/// implementation, so a broken rule fails here instead of being mirrored.
/// </summary>
[TestClass]
public class TemplateLandingFilterTests
{
    private static readonly Guid GradeA = Guid.Parse(TemplateApiHandler.GradeA);
    private static readonly Guid GradeB = Guid.Parse(TemplateApiHandler.GradeB);
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static TenantNotificationTemplateDto Row(
        NotificationTemplateKind kind,
        NotificationChannel channel,
        string? subject = null,
        string body = "<p>body</p>",
        params (TemplateConstraintDimension Dimension, string Value)[] constraints) =>
        new(Guid.NewGuid(), kind, channel,
            // The filter reads the subject and the body only (redesign spec §3.2); these fixtures carry the
            // title the migration would have backfilled from that same label, or none for a body-only row.
            Title: subject ?? string.Empty, Description: null, Code: null, Subject: subject, Body: body,
            Constraints: constraints.Select(c => new TemplateConstraintDto(Guid.NewGuid(), c.Dimension, c.Value)).ToList(),
            CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch,
            TenantId: null, OwnerName: "System");

    /// <summary>A row with an explicit owner (redesign spec §3.7): null = system blueprint.</summary>
    private static TenantNotificationTemplateDto OwnedRow(Guid? tenantId, string title) =>
        new(Guid.NewGuid(), NotificationTemplateKind.Publish, NotificationChannel.Email,
            Title: title, Description: null, Code: null, Subject: null,
            Body: "<p>body</p>", Constraints: [],
            CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch,
            tenantId, tenantId is null ? "System" : "Northfield");

    [TestMethod]
    public void TheTypeFilter_KeepsOnlyThatKind()
    {
        var rows = new[]
        {
            Row(NotificationTemplateKind.Publish, NotificationChannel.Email, "PUBLISH"),
            Row(NotificationTemplateKind.Reminder, NotificationChannel.Email, "REMINDER"),
        };

        var result = TemplateLandingFilter.Apply(rows, null, "Reminder", string.Empty, string.Empty, string.Empty);

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

        var result = TemplateLandingFilter.Apply(rows, null, string.Empty, "WhatsApp", string.Empty, string.Empty);

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

        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, string.Empty, "homework")
            .Should().ContainSingle().Which.Subject.Should().Be("MENTIONS-HOMEWORK");

        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, string.Empty, "library")
            .Should().ContainSingle().Which.Kind.Should().Be(NotificationTemplateKind.Reminder);
    }

    /// <summary>
    /// A row named only by its TITLE (naming spec D1/D3) — the SMS/WhatsApp shape, where the domain
    /// keeps no subject at all and the title is the only name the row has.
    /// </summary>
    private static TenantNotificationTemplateDto NamedRow(
        string title, string? description = null, string body = "<p>body</p>") =>
        new(Guid.NewGuid(), NotificationTemplateKind.Reminder, NotificationChannel.SMS,
            Title: title, Description: description, Code: null, Subject: null,
            Body: body, Constraints: [],
            CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch,
            TenantId: null, OwnerName: "System");

    [TestMethod]
    public void TheSearch_FindsATemplateByTheNameTheOperatorGaveIt()
    {
        // Arrange — the term appears in exactly one row, in the field under test.
        var rows = new[]
        {
            NamedRow("Grade 8 Science reminder"),
            NamedRow("Field trip packing list", description: "Sent the day before the museum trip"),
            NamedRow("Unrelated overdue chase", body: "<p>please return the lab coats</p>"),
        };

        // Act / Assert — D1/D3: a template is named by its title, so the search reads it, and the
        // description under it; the subject and the plain body stay part of the search too.
        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, string.Empty, "grade 8")
            .Should().ContainSingle().Which.Title.Should().Be("Grade 8 Science reminder",
                "an operator who knows a template by its title can find it");

        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, string.Empty, "museum trip")
            .Should().ContainSingle().Which.Title.Should().Be("Field trip packing list",
                "the description is part of the name the operator gave the row");

        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, string.Empty, "lab coats")
            .Should().ContainSingle().Which.Title.Should().Be("Unrelated overdue chase",
                "the body text is still searchable");
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

        var result = TemplateLandingFilter.Apply(rows, GradeA, string.Empty, string.Empty, string.Empty, string.Empty);

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

        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, string.Empty, string.Empty).Should().HaveCount(2);
    }

    [TestMethod]
    public void TheOwnerFilter_SeparatesTheBlueprintFromTheSessionsOwnRows()
    {
        var rows = new[]
        {
            OwnedRow(tenantId: null, "SYSTEM-ROW"),
            OwnedRow(TenantId, "TENANT-ROW"),
        };

        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, TemplateLandingFilter.SystemOwner, string.Empty)
            .Should().ContainSingle().Which.Title.Should().Be("SYSTEM-ROW");

        TemplateLandingFilter.Apply(rows, null, string.Empty, string.Empty, TemplateLandingFilter.TenantOwner, string.Empty)
            .Should().ContainSingle().Which.Title.Should().Be("TENANT-ROW");
    }
}