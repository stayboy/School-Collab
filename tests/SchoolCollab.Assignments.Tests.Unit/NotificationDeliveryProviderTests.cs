using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-E2 (ar-16) — binding cases 1–3: deterministic provider selection (both
/// branches, pure + through DI), the null/SMS stub success posture (decision (d)),
/// and the pure <c>MimeMessage</c>/payload rendering.
/// </summary>
[TestClass]
public class NotificationDeliveryProviderTests
{
    private const string Token = "tok-abc";
    private const string Title = "Math homework";

    private static ServiceProvider BuildProvider(string? smtpHost)
    {
        var settings = new Dictionary<string, string?>();
        if (smtpHost is not null)
        {
            settings["Smtp:Host"] = smtpHost;
            settings["Smtp:Port"] = "1025";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssignmentNotificationDelivery(configuration);
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public void Select_ConfiguredHost_ChoosesMailKit()
    {
        EmailSenderSelection.Select(new SmtpOptions { Host = "localhost" })
            .Should().Be(EmailProviderKind.MailKit);
    }

    [TestMethod]
    public void Select_UnsetHost_ChoosesNull()
    {
        EmailSenderSelection.Select(new SmtpOptions { Host = string.Empty })
            .Should().Be(EmailProviderKind.Null);
    }

    [TestMethod]
    public void Select_BlankHost_ChoosesNull()
    {
        EmailSenderSelection.Select(new SmtpOptions { Host = "   " })
            .Should().Be(EmailProviderKind.Null);
    }

    [TestMethod]
    public void AddDelivery_ConfiguredHost_RegistersMailKitSender()
    {
        using var provider = BuildProvider("localhost");

        provider.GetRequiredService<IEmailSender>().Should().BeOfType<MailKitEmailSender>();
    }

    [TestMethod]
    public void AddDelivery_NoHostConfigured_RegistersNullSender()
    {
        using var provider = BuildProvider(smtpHost: null);

        provider.GetRequiredService<IEmailSender>().Should().BeOfType<NullEmailSender>();
    }

    [TestMethod]
    public async Task NullEmailSender_ReportsSuccessWithoutSmtp()
    {
        var sender = new NullEmailSender(NullLogger<NullEmailSender>.Instance);

        var task = sender.SendAsync(new EmailMessage("guardian@example.com", "subject", "<p>body</p>"));

        // Completed synchronously ⇒ no transport was contacted, and no exception ⇒ success.
        task.IsCompleted.Should().BeTrue();
        await task;
    }

    [TestMethod]
    public async Task LogAndSkipSmsSender_ReportsSuccess()
    {
        var sender = new LogAndSkipSmsSender(NullLogger<LogAndSkipSmsSender>.Instance);

        var task = sender.SendAsync(new SmsMessage("+27123456789", "New assignment"));

        task.IsCompleted.Should().BeTrue();
        await task;
    }

    [TestMethod]
    public void BuildMimeMessage_MapsToFromSubjectAndHtmlBody()
    {
        var message = new EmailMessage("guardian@example.com", "New assignment: Math homework", "<p>hello</p>");

        var mime = MailKitEmailSender.BuildMimeMessage(
            message, new SmtpOptions { FromAddress = "no-reply@schoolcollab.local" });

        mime.To.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("guardian@example.com");
        mime.From.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("no-reply@schoolcollab.local");
        mime.Subject.Should().Be("New assignment: Math homework");
        mime.HtmlBody.Should().Be("<p>hello</p>");
    }

    [TestMethod]
    public void BuildMimeMessage_ExplicitFrom_Wins()
    {
        var message = new EmailMessage("guardian@example.com", "subject", "<p>hello</p>", From: "teacher@school.test");

        var mime = MailKitEmailSender.BuildMimeMessage(
            message, new SmtpOptions { FromAddress = "no-reply@schoolcollab.local" });

        mime.From.Mailboxes.Single().Address.Should().Be("teacher@school.test");
    }

    [TestMethod]
    public void BuildPublishEmail_CarriesRecipientDeepLink()
    {
        var message = NotificationMessageBuilder.BuildPublishEmail("guardian@example.com", Title, Token);

        message.To.Should().Be("guardian@example.com");
        message.Subject.Should().Be($"New assignment: {Title}");
        message.BodyHtml.Should().Contain(
            $"{NotificationMessageBuilder.DeepLinkPathPrefix}{Token}");
    }

    [TestMethod]
    public void BuildPublishSms_CarriesRecipientDeepLink()
    {
        var message = NotificationMessageBuilder.BuildPublishSms("+27123456789", Title, Token);

        message.To.Should().Be("+27123456789");
        message.Body.Should().Contain($"{NotificationMessageBuilder.DeepLinkPathPrefix}{Token}");
    }
}
