using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.Tenants.Commands.SetUserDefaultTenant;

/// <summary>
/// Upserts the caller's <see cref="AdminUserPreference"/> row. The unique index on
/// <c>subject_id</c> is what makes "one row per operator" an invariant of the database
/// rather than of this method.
/// </summary>
public sealed class SetUserDefaultTenantHandler(SettingsDbContext db)
    : ICommandHandler<SetUserDefaultTenant>
{
    public async Task HandleAsync(SetUserDefaultTenant command, CancellationToken ct = default)
    {
        var tenantId = command.TenantId;

        // A real tenant must exist; Guid.Empty is the system / no-tenant sentinel and is
        // legitimately absent from the registry, and null means "clear".
        if (tenantId is { } id && id != Guid.Empty)
        {
            var exists = await db.Tenants.AnyAsync(t => t.Id == id, ct);
            if (!exists)
                throw new TenantNotFoundException(id);
        }

        // The entity owns subject validation (non-empty, bounded length).
        var subjectId = command.SubjectId?.Trim() ?? string.Empty;

        var existing = await db.AdminUserPreferences
            .FirstOrDefaultAsync(p => p.SubjectId == subjectId, ct);

        if (existing is null)
        {
            db.AdminUserPreferences.Add(AdminUserPreference.Create(subjectId, tenantId));
        }
        else
        {
            existing.SetDefaultTenant(tenantId);
        }

        await db.SaveChangesAsync(ct);
    }
}
