namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a tenant id supplied by a caller does not resolve to a row in the
/// tenant registry — e.g. designating a platform default for a tenant that was
/// deleted, or recording an operator preference for an unknown tenant.
/// </summary>
/// <remarks>
/// Typed rather than <see cref="KeyNotFoundException"/> so the API layer can map it to
/// a 404 without guessing which "not found" it caught, matching
/// <c>CodedValueNotFoundException</c>.
/// </remarks>
public sealed class TenantNotFoundException : DomainException
{
    /// <summary>The tenant id that did not resolve.</summary>
    public Guid TenantId { get; }

    public TenantNotFoundException(Guid tenantId)
        : base($"Tenant with id '{tenantId}' was not found.")
        => TenantId = tenantId;
}
