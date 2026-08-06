using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RejectProvisionalCodedValue;

/// <summary>
/// Rejects a provisional coded value (tcv/3). The value remains tenant-owned (kept
/// for its creating tenant) but is no longer provisional, so it leaves the pending
/// approval queue. A rejected value stays isolated to its tenant — it is never made
/// globally visible and never hard-deleted.
/// </summary>
public sealed class RejectProvisionalCodedValueHandler(
    SettingsDbContext db,
    ITenantContextAccessor tenantContextAccessor,
    HybridCache cache,
    ILogger<RejectProvisionalCodedValueHandler> logger) : ICommandHandler<RejectProvisionalCodedValue>
{
    public async Task HandleAsync(RejectProvisionalCodedValue command, CancellationToken ct = default)
    {
        var codedValue = await db.CodedValues
            .IgnoreQueryFilters(["Tenant"])
            .SingleOrDefaultAsync(x => x.Id == command.Id, ct)
            ?? throw new CodedValueNotFoundException(command.Id);

        if (!codedValue.IsProvisional)
        {
            throw new ArgumentException(
                $"Coded value {command.Id} is not provisional and cannot be rejected.");
        }

        codedValue.RejectProvisional();

        // Rejection keeps the tenant_id (no change) but clears the provisional flag;
        // save under the tenant guard context so a real tenant can also trigger it.
        using (tenantContextAccessor.SuppressTenantGuard())
        {
            await db.SaveChangesAsync(ct);
        }

        await cache.RemoveByTagAsync("coded-values", ct);

        logger.LogInformation(
            "Provisional CodedValue {Id} rejected (remains tenant-scoped)",
            codedValue.Id);
    }
}
