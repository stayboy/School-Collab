using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.Tenants.Queries.ListTenants;

/// <summary>
/// Lists every tenant in the registry, ordered by name, flagging the platform's
/// designated default. Global (no tenant filter): the tenant registry is not itself
/// tenant-scoped. Used by the dev tenant switcher (auth-disabled) to populate its
/// dropdown and to know which entry the portal should fall back to. See
/// documents/specs/grade-level-setup.md §5.5.
/// </summary>
public sealed class ListTenantsHandler(SettingsDbContext db)
    : IQueryHandler<ListTenants, TenantDto[]>
{
    public async Task<TenantDto[]> HandleAsync(
        ListTenants query,
        CancellationToken cancellationToken = default)
    {
        var tenants = await db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToArrayAsync(cancellationToken);

        return tenants
            .Select(t => new TenantDto(t.Id, t.Name, t.Type.ToString(), t.IsDefault))
            .ToArray();
    }
}