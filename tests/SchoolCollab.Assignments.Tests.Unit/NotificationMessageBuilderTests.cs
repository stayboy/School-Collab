using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>E3 (ar-19) — the reminder / completion / overdue message templates render the
/// destination, a kind-specific subject, and the recipient's deep link (queue-time
/// decision (b)).</summary>
[TestClass]
public class NotificationMessageBuilderTests
{
    private const string Address = "guardian@example.com";
    private const string Title = "Chapter 4 Homework";
    private const string Token = "tok-abc";

    [TestMethod]
    public void BuildReminderEmail_IncludesSubjectAndDeepLink()
    {
        var m = NotificationMessageBuilder.BuildReminderEmail(Address, Title, Token);
        m.To.Should().Be(Address);
        m.Subject.Should().Be("Reminder: Chapter 4 Homework");
        m.BodyHtml.Should().Contain(NotificationMessageBuilder.DeepLinkPathPrefix + Token);
    }

    [TestMethod]
    public void BuildReminderSms_UsesReminderSubject()
    {
        var m = NotificationMessageBuilder.BuildReminderSms(Address, Title, Token);
        m.To.Should().Be(Address);
        m.Body.Should().Contain("/deeplink/" + Token);
    }

    [TestMethod]
    public void BuildCompletionEmail_IsSignDirected()
    {
        var m = NotificationMessageBuilder.BuildCompletionEmail(Address, Title, Token);
        m.Subject.Should().Be("Ready to sign: Chapter 4 Homework");
        m.BodyHtml.Should().Contain("ready for your signature");
        m.BodyHtml.Should().Contain("/deeplink/" + Token);
    }

    [TestMethod]
    public void BuildCompletionSms_CarriesSignLink()
    {
        var m = NotificationMessageBuilder.BuildCompletionSms(Address, Title, Token);
        m.Body.Should().Contain("Sign: /deeplink/" + Token);
    }

    [TestMethod]
    public void BuildOverdueEmail_UsesOverdueSubject()
    {
        var m = NotificationMessageBuilder.BuildOverdueEmail(Address, Title, Token);
        m.Subject.Should().Be("Overdue: Chapter 4 Homework");
        m.BodyHtml.Should().Contain("now overdue");
    }

    [TestMethod]
    public void BuildOverdueSms_CarriesDeepLink()
    {
        var m = NotificationMessageBuilder.BuildOverdueSms(Address, Title, Token);
        m.Body.Should().Contain("/deeplink/" + Token);
    }

    [TestMethod]
    public void SubjectFor_SelectsKindSubject()
    {
        NotificationMessageBuilder.SubjectFor(NotificationKind.Reminder, Title).Should().Be("Reminder: Chapter 4 Homework");
        NotificationMessageBuilder.SubjectFor(NotificationKind.Completion, Title).Should().Be("Ready to sign: Chapter 4 Homework");
        NotificationMessageBuilder.SubjectFor(NotificationKind.Overdue, Title).Should().Be("Overdue: Chapter 4 Homework");
    }
}
