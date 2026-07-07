namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Lightweight tenant record returned by the tenant-list endpoint. <see cref="Type"/>
/// is the string form of <see cref="SchoolCollab.Core.Tenancy.TenantType"/> so the
/// client need not reference the enum assembly.
/// </summary>
public record TenantDto(Guid Id, string Name, string Type);