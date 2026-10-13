using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.Tenants.Queries.GetUserDefaultTenant;

/// <summary>
/// Reads one operator's recorded default-tenant preference for the admin portal.
/// </summary>
/// <remarks>
/// A read, not a resolution: it never consults the platform default, so the caller can
/// tell "no preference recorded" (<c>DefaultTenantId</c> is null) apart from "explicitly
/// prefers the system context" (<see cref="Guid.Empty"/>). The portal composes the two
/// sources itself.
/// </remarks>
/// <param name="SubjectId">The authenticated principal's OIDC <c>sub</c> claim.</param>
public sealed record GetUserDefaultTenant(string SubjectId) : IQuery<UserTenantPreferenceDto>;
