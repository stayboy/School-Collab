using Microsoft.Extensions.Logging;

namespace SchoolCollab.Assignments.Core.Services.Delivery;

/// <summary>
/// SMS / WhatsApp stub (D-2 decision: log-and-skip, no provider in v1). Reports
/// <b>success</b> so a queued SMS row reaches <c>Sent</c> rather than filling the
/// failure surface while no gateway exists. The policy <c>BlockedChannels</c> set is
/// what actually suppresses unwanted channels at publish time.
/// </summary>
public sealed class LogAndSkipSmsSender(ILogger<LogAndSkipSmsSender> logger) : ISmsSender
{
    /// <inheritdoc />
    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation(
            "No SMS provider configured — skipping message to {To}", message.To);

        return Task.CompletedTask;
    }
}
