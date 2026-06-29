using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitMQ.Client;

namespace SchoolCollab.Core.Messaging;

/// <summary>
/// Extension methods to register the shared transactional outbox in a
/// bounded context. See
/// <c>documents/solution/messaging-consolidation-plan.md</c> for the
/// migration plan that introduces this helper.
/// </summary>
public static class OutboxExtensions
{
    /// <summary>
    /// Wires up the outbox for a bounded context:
    /// <list type="bullet">
    ///   <item>configures <see cref="OutboxOptions"/> from the
    ///     <see cref="OutboxOptions.SectionName"/> configuration
    ///     section;</item>
    ///   <item>registers <see cref="IIntegrationEventPublisher"/> as the
    ///     shared <see cref="OutboxIntegrationEventPublisher{TContext}"/>
    ///     (singleton);</item>
    ///   <item>registers <see cref="OutboxDispatcher{TContext}"/> as a
    ///     hosted background service.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <param name="sectionName">
    /// Optional override for the configuration section name. Defaults to
    /// <see cref="OutboxOptions.SectionName"/>.
    /// </param>
    /// <typeparam name="TContext">
    /// The bounded-context <see cref="DbContext"/> that owns the
    /// <c>outbox_messages</c> table.
    /// </typeparam>
    /// <remarks>
    /// The caller is responsible for registering
    /// <c>IDbContextFactory&lt;TContext&gt;</c> (via
    /// <c>services.AddDbContextFactory&lt;TContext&gt;(...)</c> or
    /// <c>services.AddPooledDbContextFactory&lt;TContext&gt;(...)</c>) and
    /// the scoped <typeparamref name="TContext"/> itself, plus the
    /// <see cref="IConnection"/> from the Aspire RabbitMQ client. The
    /// helper adds no EF or RabbitMQ wiring of its own; it only adds the
    /// publisher and dispatcher on top of an already-configured context.
    /// </remarks>
    public static IServiceCollection AddOutbox<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = OutboxOptions.SectionName)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(sectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ExchangeName),
                $"{nameof(OutboxOptions.ExchangeName)} must be set in the '{sectionName}' configuration section.")
            .ValidateOnStart();

        services.TryAddSingleton<IIntegrationEventPublisher, OutboxIntegrationEventPublisher<TContext>>();
        services.AddHostedService<OutboxDispatcher<TContext>>();

        return services;
    }
}
