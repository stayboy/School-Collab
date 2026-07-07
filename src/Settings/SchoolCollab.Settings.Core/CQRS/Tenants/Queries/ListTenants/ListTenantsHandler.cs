using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.Tenants.Queries.ListTenants;

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
            .Select(t => new TenantDto(t.Id, t.Name, t.Type.ToString()))
            .ToArray();
    }
}