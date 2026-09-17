namespace SchoolCollab.Assignments.Core.Services.Delivery;

/// <summary>
/// Sends a rendered <see cref="SmsMessage"/>. v1 has only the log-and-skip stub
/// (D-2 decision — no SMS/WhatsApp provider yet); the seam exists so a provider can
/// plug in without touching the dispatcher.
/// </summary>
public interface ISmsSender
{
    /// <summary>Delivers the message, or throws a typed delivery exception.</summary>
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}
