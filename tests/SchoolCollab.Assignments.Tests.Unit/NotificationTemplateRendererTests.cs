using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// The §8 slice for round <c>notification-templates-b1</c>: the §3.2 tag vocabulary as
/// written in the spec, alias handling, channel escaping, base-URL link shape and the
/// fail-open default. The renderer is pure (Q-A), so every row here is a direct unit
/// assertion — no DI, no HTTP.
///
/// <para>These rows are written against the <b>spec</b> spellings (<c>{{Title}}</c>,
/// <c>{{Description}}</c>, <c>{{GuardianName}}</c>, <c>{{GuardianRole}}</c>,
/// <c>{{StudentName}}</c>, <c>{{SchoolName}}</c>, <c>{{LinkExpiry}}</c>), so the suite fails
/// on the pre-rework vocabulary where those tags did not exist and <c>{{Description}}</c>
/// rendered the capped excerpt.</para>
/// </summary>
[TestClass]
public class NotificationTemplateRendererTests
{
    private static NotificationTagValues Tags(
        string title = "Math homework",
        string? description = null,
        DateTimeOffset? dueDate = null,
        string? instructions = null,
        string contactName = "Mrs Dlamini",
        string guardianRole = "Primary",
        string studentName = "Thabo Dlamini",
        string schoolName = "Riverside High",
        string link = "/deeplink/tok-1",
        DateTimeOffset? linkExpiry = null)
        => new()
        {
            AssignmentTitle = title,
            Description = description,
            DueDate = dueDate,
            Instructions = instructions,
            ContactName = contactName,
            GuardianRole = guardianRole,
            StudentName = studentName,
            SchoolName = schoolName,
            Link = link,
            LinkExpiry = linkExpiry,
        };

    // ── §8: tag substitution — case-insensitivity, unknown-literal ──

    [TestMethod]
    public void TagMatching_IsCaseInsensitiveAndWhitespaceTolerant()
    {
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), "Hello {{ Title }}", "<p>{{title}} is live</p>");

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, template, Tags());

        rendered.Subject.Should().Be("Hello Math homework");
        rendered.Body.Should().Contain("Math homework is live");
        rendered.TemplateId.Should().Be(template.TemplateId);
    }

    [TestMethod]
    public void UnknownTag_StaysLiteral_NeverDropped()
    {
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), null, "<p>{{Title}} — {{NoSuchTag}} stays</p>");

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, template, Tags());

        rendered.Body.Should().Contain("{{NoSuchTag}}");
        rendered.Body.Should().Contain("Math homework");
    }

    // ── §3.2: the spec vocabulary resolves ──

    [TestMethod]
    public void SpecTags_AllResolveToTheirValues()
    {
        var expiry = new DateTimeOffset(2026, 11, 8, 17, 30, 0, TimeSpan.Zero);
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(),
            null,
            "<p>{{Title}}|{{GuardianName}}|{{GuardianRole}}|{{StudentName}}|{{SchoolName}}|{{LinkExpiry}}</p>");
        var tags = Tags(linkExpiry: expiry);

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, template, tags);

        rendered.Body.Should().Contain("Math homework");
        rendered.Body.Should().Contain("Mrs Dlamini");
        rendered.Body.Should().Contain("Primary");
        rendered.Body.Should().Contain("Thabo Dlamini");
        rendered.Body.Should().Contain("Riverside High");
        rendered.Body.Should().Contain("2026-11-08 17:30 UTC");
    }

    [TestMethod]
    public void SpecTags_AreTheFullSpecSet_ForTheEditorChips()
    {
        NotificationTemplateTags.All.Should().Equal(
            "Title", "Description", "DueDate", "Instructions", "StudentName",
            "GuardianName", "GuardianRole", "SchoolName", "Link", "LinkExpiry");
    }

    [TestMethod]
    public void LegacyAlias_AssignmentTitle_StillResolvesToTitle()
    {
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), null, "<p>{{AssignmentTitle}}</p>");

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, template, Tags());

        rendered.Body.Should().Contain("Math homework");
        rendered.Body.Should().NotContain("{{AssignmentTitle}}");
    }

    [TestMethod]
    public void LegacyAliases_ContactNameAndDescriptionExcerpt_ResolveToTheirSpecTags()
    {
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), null, "<p>{{ContactName}}|{{DescriptionExcerpt}}</p>");

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, template,
            Tags(description: "Read the chapter"));

        rendered.Body.Should().Contain("Mrs Dlamini");
        rendered.Body.Should().Contain("Read the chapter");
        rendered.Body.Should().NotContain("{{ContactName}}");
        rendered.Body.Should().NotContain("{{DescriptionExcerpt}}");
    }

    [TestMethod]
    public void Canonicalize_MapsAllThreeRoundAAliases_AndLeavesUnknownTagsAlone()
    {
        // Round b3b (Q90a) moved the tag set to SchoolCollab.Core.Notifications so the Settings
        // editor can offer the chips; this pins the canonicalization table the move carried with it.
        NotificationTemplateTags.Canonicalize("AssignmentTitle").Should().Be(NotificationTemplateTags.Title);
        NotificationTemplateTags.Canonicalize("DescriptionExcerpt").Should().Be(NotificationTemplateTags.Description);
        NotificationTemplateTags.Canonicalize("ContactName").Should().Be(NotificationTemplateTags.GuardianName);
        NotificationTemplateTags.Canonicalize("title").Should().Be(NotificationTemplateTags.Title,
            "matching is case-insensitive");
        NotificationTemplateTags.Canonicalize("NoSuchTag").Should().Be("NoSuchTag",
            "an unknown tag is returned unchanged, so it stays literal at render time");
    }

    [TestMethod]
    public void AliasAuthoredTemplate_RendersByteIdenticallyToTheCanonicalSpellings()
    {
        // The Q90a move must be behaviour-free: the Round-A spelling renders exactly what the §3.2
        // spelling renders, in the subject and the body, on a content- and escape-sensitive payload.
        var tags = Tags(description: "Read the chapter & answer Q4");
        var aliasTemplate = new ResolvedNotificationTemplate(
            Guid.NewGuid(),
            "Hi {{AssignmentTitle}}",
            "<p>{{DescriptionExcerpt}} — {{ContactName}}</p>");
        var canonicalTemplate = new ResolvedNotificationTemplate(
            aliasTemplate.TemplateId,
            "Hi {{Title}}",
            "<p>{{Description}} — {{GuardianName}}</p>");

        var alias = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, aliasTemplate, tags);
        var canonical = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, canonicalTemplate, tags);

        alias.Subject.Should().Be(canonical.Subject);
        alias.Body.Should().Be(canonical.Body);
        alias.TemplateId.Should().Be(canonical.TemplateId);
    }

    // ── §3.2: {{Description}} is the FULL value; the 120-char cap is preview-only ──

    [TestMethod]
    public void DescriptionTag_RendersTheFullValue_NotThePreviewExcerpt()
    {
        var description = new string('d', 200);
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), null, "<p>{{Description}}</p>");

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, template, Tags(description: description));

        rendered.Body.Should().Contain(description, "the author placed the tag — the full value renders");
        rendered.Body.Should().NotContain("…");
    }

    [TestMethod]
    public void BuiltInEmailDefaults_IncludeDueDateAndDescriptionExcerpt_WhenPresent()
    {
        var tags = Tags(
            description: new string('d', 200),
            dueDate: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, null, tags);

        rendered.Body.Should().Contain("Due: 2026-11-01 09:00 UTC");
        rendered.Body.Should().Contain(new string('d', 120) + "…");
        rendered.Body.Should().NotContain(new string('d', 121));
    }

    // ── §3.2: values are HTML-escaped in email, plain in SMS/WhatsApp ──

    [TestMethod]
    public void Description_Tag_IsHtmlEscapedInTheEmailBody()
    {
        const string Hostile = "Q&A <b>author summary</b>";
        var template = new ResolvedNotificationTemplate(Guid.NewGuid(), null, "{{Description}}");
        var tags = Tags(description: Hostile);

        var email = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, template, tags);

        email.Body.Should().Contain("Q&amp;A &lt;b&gt;author summary&lt;/b&gt;");
        email.Body.Should().NotContain("<b>author summary</b>");
    }

    [TestMethod]
    public void PlainTextValues_AreHtmlEscapedInEmailBody_NotInSms()
    {
        const string Hostile = "Q&A <script>alert(1)</script>";
        var template = new ResolvedNotificationTemplate(Guid.NewGuid(), null, "{{Title}}");
        var tags = Tags(title: Hostile);

        var email = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, template, tags);
        var sms = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.SMS, template, tags);

        email.Body.Should().Contain("Q&amp;A");
        email.Body.Should().Contain("&lt;script&gt;");
        email.Body.Should().NotContain("<script>");
        sms.Body.Should().Be(Hostile, "SMS/WhatsApp is a plain-text channel — no escaping");
    }

    [TestMethod]
    public void DangerousMarkup_InTemplateValues_DoesNotSurviveEmailSanitize()
    {
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), null, "<p>{{Title}}</p><script>alert(1)</script>");

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, template,
            Tags(title: "<script>alert('x')</script>"));

        rendered.Body.Should().NotContain("<script>");
    }

    // ── {{Instructions}}: email-only ──

    [TestMethod]
    public void Instructions_ExpandInEmailBody_WhenTemplateCarriesTheTag()
    {
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), null, "<p>Do this: {{Instructions}}</p>");
        var tags = Tags(instructions: "<p><strong>Read chapter 4</strong></p>");

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, template, tags);

        // The authored instruction HTML expands AS markup (the one sanitizer re-applies
        // over the composed body — §3.12), so the allowed tag survives.
        rendered.Body.Should().Contain("<strong>Read chapter 4</strong>");
    }

    [TestMethod]
    public void Instructions_AreStrippedFromSmsBody_NeverSentAsLiteral()
    {
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), null, "{{Title}} {{Instructions}} due {{DueDate}}");
        var tags = Tags(
            instructions: "<p>long instructions</p>",
            dueDate: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.SMS, template, tags);

        rendered.Body.Should().NotContain("{{Instructions}}");
        rendered.Body.Should().NotContain("long instructions");
        rendered.Body.Should().Contain("Math homework");
        rendered.Body.Should().Contain("due");
    }

    // ── channels: SMS subject is the builder's; plain substitution ──

    [TestMethod]
    public void SmsTemplate_SubjectIsIgnored_BuilderSubjectWins()
    {
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), "An authored email subject", "plain body");

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.SMS, template, Tags());

        rendered.Subject.Should().Be(NotificationMessageBuilder.BuildPublishSubject("Math homework"));
    }

    [TestMethod]
    public void Sms_ValuesSubstitutePlain()
    {
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), null, "Open {{Link}} for {{Title}}");

        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.WhatsApp, template, Tags());

        rendered.Body.Should().Be("Open /deeplink/tok-1 for Math homework");
    }

    // ── §8: fail-open — a null template renders the built-in defaults, TemplateId null ──

    [TestMethod]
    public void NullTemplate_RendersBuiltInDefaults_WithNullTemplateId()
    {
        var rendered = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.Email, null, Tags());

        rendered.Subject.Should().Be(NotificationMessageBuilder.BuildPublishSubject("Math homework"));
        rendered.Body.Should().Contain("has been published");
        rendered.Body.Should().Contain("/deeplink/tok-1");
        rendered.TemplateId.Should().BeNull();
    }

    [TestMethod]
    public void BuiltInDefaults_DelegateToTheBuilder_ForEveryKindAndChannel()
    {
        // One source for the default texts: the renderer's fallback IS the builder's (§3.3).
        var tags = Tags();

        foreach (var kind in Enum.GetValues<NotificationKind>())
        {
            var email = NotificationTemplateRenderer.Render(kind, NotificationChannel.Email, null, tags);
            email.Body.Should().Be(NotificationMessageBuilder.BuildEmailBody(kind, tags));

            var sms = NotificationTemplateRenderer.Render(kind, NotificationChannel.SMS, null, tags);
            sms.Body.Should().Be(NotificationMessageBuilder.BuildSmsBody(kind, tags));
            sms.Subject.Should().Be(NotificationMessageBuilder.SubjectFor(kind, tags.AssignmentTitle));
        }
    }

    // ── §8: base URL — set ⇒ absolute, unset ⇒ relative; and the channel-driven short link
    //        (round notification-templates-b2a, §3.6). The full matrix lives in
    //        NotificationLinkOptionsTests. ──

    // ── {{DueDate}} rendering ──

    [TestMethod]
    public void DueDateTag_EmptyWhenNull_FormattedWhenSet()
    {
        var template = new ResolvedNotificationTemplate(
            Guid.NewGuid(), null, "due [{{DueDate}}]");

        var unset = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.SMS, template, Tags());
        unset.Body.Should().Be("due []");

        var set = NotificationTemplateRenderer.Render(
            NotificationKind.Publish, NotificationChannel.SMS, template,
            Tags(dueDate: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero)));
        set.Body.Should().Be("due [2026-11-01 09:00 UTC]");
    }
}
