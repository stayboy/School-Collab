using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Core.Data.Outbox;
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
    ///     hosted background service;</item>
    ///   <item>registers the shared <see cref="OutboxMessageConfiguration"/>
    ///     against the per-module <see cref="OutboxConfigurationFlags"/>
    ///     built from the optional <paramref name="configure"/> callback.
    ///     The default flags cover the common case; only override them
    ///     when a module genuinely needs a per-domain column type,
    ///     max length, default value, or index strategy.</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <param name="configureOutbox">
    /// Optional fluent callback that customises the per-module
    /// <see cref="OutboxConfigurationFlags"/> (e.g. switching the
    /// <c>Payload</c> column to PostgreSQL <c>jsonb</c> in
    /// CodedValues). Pass <c>null</c> to use the default flags.
    /// </param>
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
    /// publisher, dispatcher, and shared outbox configuration on top
    /// of an already-configured context.
    /// </remarks>
    public static IServiceCollection AddOutbox<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IOutboxConfigurationBuilder>? configureOutbox = null,
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

        // Build the per-module flags once. Stored in the per-TContext
        // static registry (read by the DbContext's OnModelCreating
        // when it applies the shared OutboxMessageConfiguration) and
        // also registered as a singleton for any future diagnostic
        // use.
        var flags = OutboxConfigurationFlags.FromConfiguration(configureOutbox);
        services.AddSingleton(flags);
        OutboxMapping.SetFlagsFor<TContext>(flags);

        services.TryAddSingleton<IIntegrationEventPublisher, OutboxIntegrationEventPublisher<TContext>>();
        services.AddHostedService<OutboxDispatcher<TContext>>();

        return services;
    }
}
