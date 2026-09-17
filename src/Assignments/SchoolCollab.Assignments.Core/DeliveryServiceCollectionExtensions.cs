using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;

namespace SchoolCollab.Assignments.Core;

/// <summary>
/// DI wiring for the WS-E2 (ar-16) notification delivery seam. Kept out of
/// <c>AddAssignmentsCore</c>'s body so the provider-selection branches are small,
/// explicit, and unit-testable on their own (decision: deterministic, pure choice).
/// </summary>
public static class DeliveryServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="SmtpOptions"/> and registers the channel senders: MailKit when
    /// <c>Smtp:Host</c> is configured, otherwise the log-and-skip null sender
    /// (<see cref="EmailSenderSelection"/>), plus the SMS stub and the delivery drain.
    /// </summary>
    public static IServiceCollection AddAssignmentNotificationDelivery(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));

        var smtpOptions = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
        if (EmailSenderSelection.Select(smtpOptions) == EmailProviderKind.MailKit)
        {
            services.AddScoped<IEmailSender, MailKitEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, NullEmailSender>();
        }

        // D-2: no SMS/WhatsApp provider in v1 — log-and-skip reports success so a queued
        // SMS row reaches Sent instead of filling the failure surface.
        services.AddScoped<ISmsSender, LogAndSkipSmsSender>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<NotificationDispatchService>();

        return services;
    }
}
