using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.Tenants.Queries.ListTenants;

/// <summary>
/// Lists every tenant in the registry, ordered by name. Global (no tenant filter):
/// the tenant registry is not itself tenant-scoped. Used by the dev tenant switcher
/// (auth-disabled) to populate its dropdown. See documents/specs/grade-level-setup.md §5.5.
/// </summary>
public sealed record ListTenants : IQuery<TenantDto[]>;