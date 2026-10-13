using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.Tenants.Commands.SetDefaultTenant;

/// <summary>
/// Moves the platform's default-tenant designation onto the requested tenant, clearing
/// the previous holder in the same transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two saves on purpose.</b> <c>ix_tenants_is_default_unique</c> is a partial unique
/// index, so at no instant may two rows carry <c>is_default = true</c>. EF Core does not
/// guarantee the order of the UPDATEs it emits for one <c>SaveChanges</c>, so clearing
/// and setting in a single batch can transiently violate the index. The clear is
/// committed first, then the designations, both inside one explicit transaction — so the
/// move is atomic to readers while the invariant holds at every statement boundary.
/// </para>
/// <para>
/// The domain methods (<c>ClearDefault</c> / <c>MarkAsDefault</c>) carry the audit
/// timestamps and are idempotent, so re-designating the current default is a no-op.
/// </para>
/// </remarks>
public sealed class SetDefaultTenantHandler(SettingsDbContext db)
    : ICommandHandler<SetDefaultTenant>
{
    public async Task HandleAsync(SetDefaultTenant command, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var target = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == command.TenantId, ct)
            ?? throw new TenantNotFoundException(command.TenantId);

        // 1. Release the current designation(s) first — the index forbids the overlap.
        var previousDefaults = await db.Tenants
            .Where(t => t.IsDefault && t.Id != command.TenantId)
            .ToListAsync(ct);

        if (previousDefaults.Count > 0)
        {
            foreach (var previous in previousDefaults)
                previous.ClearDefault();

            await db.SaveChangesAsync(ct);
        }

        // 2. Take the designation. Idempotent when this tenant already holds it.
        target.MarkAsDefault();
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }
}
