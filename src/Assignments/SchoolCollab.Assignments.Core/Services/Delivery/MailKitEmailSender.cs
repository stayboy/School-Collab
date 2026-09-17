using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.Services.Delivery;

/// <summary>
/// MailKit-backed <see cref="IEmailSender"/>. Selected only when <c>Smtp:Host</c> is
/// configured (<see cref="EmailSenderSelection"/>). All transport faults are wrapped in
/// <see cref="EmailDeliveryException"/> so the dispatcher records a reason and retries.
/// </summary>
public sealed class MailKitEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<MailKitEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var smtp = options.Value;
        var mime = BuildMimeMessage(message, smtp);

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(
                smtp.Host,
                smtp.Port,
                smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(smtp.User))
                await client.AuthenticateAsync(smtp.User, smtp.Password, cancellationToken);

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            logger.LogInformation("Delivered notification email to {To} via {Host}", message.To, smtp.Host);
        }
        catch (EmailDeliveryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EmailDeliveryException(
                $"SMTP delivery to '{message.To}' failed via {smtp.Host}:{smtp.Port}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Pure message build (no transport, no clock) so To / From / Subject / HTML body
    /// are unit-testable. A message without its own <see cref="EmailMessage.From"/> falls
    /// back to <see cref="SmtpOptions.FromAddress"/>.
    /// </summary>
    public static MimeMessage BuildMimeMessage(EmailMessage message, SmtpOptions smtp)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(smtp);

        var from = string.IsNullOrWhiteSpace(message.From) ? smtp.FromAddress : message.From;

        var mime = new MimeMessage
        {
            Subject = message.Subject,
            Body = new TextPart("html") { Text = message.BodyHtml },
        };
        mime.From.Add(MailboxAddress.Parse(from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        return mime;
    }
}
