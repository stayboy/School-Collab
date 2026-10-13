namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// The calling operator's recorded default-tenant preference for the admin portal.
/// </summary>
/// <remarks>
/// The three states mirror <c>IDevTenantSelection</c> on the client and
/// <c>AdminUserPreference.DefaultTenantId</c> on the server:
/// <see langword="null"/> = no preference recorded, so the portal falls through to the
/// platform default; <see cref="Guid.Empty"/> = an explicit preference for the
/// system / no-tenant context; any other value = a real tenant.
/// </remarks>
public sealed record UserTenantPreferenceDto(Guid? DefaultTenantId);
