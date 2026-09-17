namespace SchoolCollab.Assignments.Core.Services.Delivery;

/// <summary>
/// Sends a rendered <see cref="EmailMessage"/>. Implementations MUST throw a typed
/// <see cref="Domain.Exceptions.EmailDeliveryException"/> on a delivery failure so the
/// dispatcher can record a durable failure reason and retry (never a bare
/// <see cref="Exception"/>).
/// </summary>
public interface IEmailSender
{
    /// <summary>Delivers the message, or throws <see cref="Domain.Exceptions.EmailDeliveryException"/>.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
