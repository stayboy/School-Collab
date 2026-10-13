namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Lightweight tenant record returned by the tenant-list endpoint. <see cref="Type"/> is
/// the string form of <see cref="SchoolCollab.Core.Tenancy.TenantType"/> so the client need
/// not reference the enum assembly.
/// </summary>
/// <param name="IsDefault">
/// Whether this tenant is the platform's designated default for the admin portal. Additive
/// with a default value so existing positional construction (and the auth portal's mediated
/// tenant reader, which projects a subset of the payload) keeps working unchanged.
/// </param>
public record TenantDto(Guid Id, string Name, string Type, bool IsDefault = false);