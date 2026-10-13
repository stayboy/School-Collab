using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.Tenants.Commands.SetUserDefaultTenant;

/// <summary>
/// Records (or clears) one operator's personal default tenant for the admin portal —
/// the tenant that operator's portal load applies when no explicit selection exists.
/// </summary>
/// <param name="SubjectId">
/// The identity provider's subject identifier (the OIDC <c>sub</c> claim; under
/// <c>TestAuthHandler</c> the synthetic <c>test-user-id</c>). Supplied by the API endpoint
/// from the authenticated principal — never taken from the request body, so a caller
/// cannot write another operator's preference.
/// </param>
/// <param name="TenantId">
/// <see langword="null"/> clears the preference (the platform default applies again);
/// <see cref="Guid.Empty"/> records an explicit preference for the system / no-tenant
/// context; any other value must resolve to a registry row.
/// </param>
public sealed record SetUserDefaultTenant(string SubjectId, Guid? TenantId) : ICommand;
