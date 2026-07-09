namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// Thrown when a write operation targets a strict tenant-scoped entity
/// (<see cref="ITenantEntity"/>) but no real tenant context is in scope
/// (<see cref="TenantContext.IsDefault"/> is <see langword="true"/>, i.e.
/// <see cref="Guid.Empty"/>).
/// </summary>
/// <remarks>
/// <para>This is the save-guard counterpart to the handler-level guard
/// (<c>FR-4</c> / <c>FR-5</c> in <c>global-tenant-filter.md</c>): if a strict
/// entity reaches <c>SaveChanges</c> with <c>TenantId == Guid.Empty</c> and the
/// tenant guard is not suppressed, this exception is thrown rather than
/// persisting a tenant-less row.</para>
/// <para>For hybrid entities (<see cref="IHybridTenantEntity"/>), <see langword="null"/>
/// is a valid <c>TenantId</c> (shared blueprint); only <see cref="Guid.Empty"/>
/// triggers this exception.</para>
/// </remarks>
public sealed class TenantContextRequiredException : InvalidOperationException
{
    /// <summary>The operation that required a tenant (e.g. "SaveChanges").</summary>
    public string Caller { get; }

    /// <summary>The entity type that could not be saved without a tenant, if known.</summary>
    public Type? EntityType { get; }

    public TenantContextRequiredException(string caller, Type? entityType)
        : base(
            $"A real tenant context is required to {caller}"
            + (entityType is null ? "" : $" for {entityType.Name}")
            + ". No strict entity may be created with an empty/null TenantId. "
            + "Select a real tenant (dev switcher) or wrap the call in ITenantContextAccessor.")
    {
        Caller = caller;
        EntityType = entityType;
    }
}
