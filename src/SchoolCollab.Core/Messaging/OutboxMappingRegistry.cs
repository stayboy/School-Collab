using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Outbox;

namespace SchoolCollab.Core.Messaging;

/// <summary>
/// Public façade over the per-<typeparamref name="TContext"/>
/// outbox flags registry. The DbContext's <c>OnModelCreating</c>
/// reads the flags from this façade so the shared
/// <see cref="OutboxMessageConfiguration"/> can be applied with the
/// right per-module shape.
///
/// <see cref="OutboxExtensions.AddOutbox{TContext}"/> sets the flags;
/// the DbContext reads them via <see cref="FlagsFor{TContext}"/>.
/// This is necessary because EF Core's <c>OnModelCreating</c> runs
/// during context construction, before any user code can inject the
/// flags through the constructor. A per-TContext static is the
/// cleanest way to pass a build-time configuration value to the
/// runtime model builder.
/// </summary>
public static class OutboxMapping
{
    /// <summary>
    /// Returns the per-module flags for the supplied
    /// <typeparamref name="TContext"/>. The default flags
    /// (<see cref="OutboxConfigurationFlags.Default"/>) are returned
    /// if <c>AddOutbox&lt;TContext&gt;</c> was never called.
    /// </summary>
    public static OutboxConfigurationFlags FlagsFor<TContext>()
        where TContext : DbContext
        => OutboxMappingRegistry<TContext>.Flags;

    /// <summary>
    /// Sets the per-module flags for the supplied
    /// <typeparamref name="TContext"/>. Called by
    /// <see cref="OutboxExtensions.AddOutbox{TContext}"/>.
    /// </summary>
    public static void SetFlagsFor<TContext>(OutboxConfigurationFlags flags)
        where TContext : DbContext
        => OutboxMappingRegistry<TContext>.Set(flags);
}

/// <summary>
/// Per-<typeparamref name="TContext"/> registry of the
/// <see cref="OutboxConfigurationFlags"/>. Internal because the
/// <see cref="OutboxMapping"/> façade is the public surface; the
/// DbContexts use the façade, not the static directly.
/// </summary>
internal static class OutboxMappingRegistry<TContext>
{
    private static OutboxConfigurationFlags _flags = OutboxConfigurationFlags.Default;

    public static OutboxConfigurationFlags Flags => _flags;

    public static void Set(OutboxConfigurationFlags flags) => _flags = flags;
}
