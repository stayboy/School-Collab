using Microsoft.Extensions.Logging;

namespace SchoolCollab.Assignments.Core.Services.Delivery;

/// <summary>
/// Dev / standalone default email sender: logs the message and reports <b>success</b>
/// without touching SMTP (decision (d)). An unconfigured <c>Smtp:Host</c> therefore
/// produces <c>Sent</c> notification rows, never failures — the Admin failure surface
/// (ar-17) stays meaningful on a host with no mail server.
/// </summary>
public sealed class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation(
            "SMTP not configured — skipping email to {To} (subject: {Subject})",
            message.To, message.Subject);

        return Task.CompletedTask;
    }
}
