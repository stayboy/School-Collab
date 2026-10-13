using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.Tenants.Commands.SetDefaultTenant;

/// <summary>
/// Designates one tenant as the platform's default tenant for the admin portal — the
/// tenant a portal load falls back to when the operator has recorded no preference.
/// Exactly one tenant carries the flag afterwards: the previous default is cleared in
/// the same unit of work.
/// </summary>
/// <remarks>
/// Throws <c>TenantNotFoundException</c> when <paramref name="TenantId"/> is not in the
/// registry. There is deliberately no "clear the default" command: the platform default
/// is either a real tenant or a designated-by-absence state, and a portal with no default
/// simply falls through to the system / no-tenant context.
/// </remarks>
public sealed record SetDefaultTenant(Guid TenantId) : ICommand;
