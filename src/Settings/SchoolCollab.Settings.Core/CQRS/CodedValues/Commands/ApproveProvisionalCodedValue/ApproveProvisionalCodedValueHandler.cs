using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateProvisionalCodedValue;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.ApproveProvisionalCodedValue;

/// <summary>
/// Promotes a provisional coded value to the shared global blueprint (tcv/3).
///
/// <para>Approval is a system-wide (Settings admin) operation, so the value is
/// loaded with the "Tenant" query filter <b>ignored</b> — a provisional row owned by
/// another tenant would otherwise be invisible to the admin/default tenant context.
/// Promotion guards against a duplicate shared-blueprint code: if a shared row with
/// the same (parent, code) already exists, the promotion is rejected.</para>
/// </summary>
public sealed class ApproveProvisionalCodedValueHandler(
    SettingsDbContext db,
    ITenantContextAccessor tenantContextAccessor,
    HybridCache cache,
    ILogger<ApproveProvisionalCodedValueHandler> logger) : ICommandHandler<ApproveProvisionalCodedValue>
{
    public async Task HandleAsync(ApproveProvisionalCodedValue command, CancellationToken ct = default)
    {
        var codedValue = await db.CodedValues
            .IgnoreQueryFilters(["Tenant"])
            .SingleOrDefaultAsync(x => x.Id == command.Id, ct)
            ?? throw new CodedValueNotFoundException(command.Id);

        if (!codedValue.IsProvisional)
        {
            throw new ArgumentException(
                $"Coded value {command.Id} is not provisional and cannot be approved.");
        }

        // Duplicate shared-blueprint guard: reject promotion if a shared (NULL-tenant)
        // row with the same (parent, code) already exists.
        var conflict = await db.CodedValues
            .IgnoreQueryFilters(["Tenant"])
            .Where(x => x.Code == codedValue.Code
                && (codedValue.ParentId.HasValue
                    ? x.ParentId == codedValue.ParentId
                    : x.ParentId == null)
                && x.TenantId == null
                && x.Id != command.Id)
            .FirstOrDefaultAsync(ct);
        if (conflict is not null)
        {
            throw new CodedValueCodeConflictException(
                codedValue.Code, codedValue.ParentId, conflict.Id,
                existingIsSharedBlueprint: true);
        }

        codedValue.ApproveAsGlobalBlueprint();

        // Promotion rewrites tenant_id from a real tenant to NULL. The strict hybrid
        // save-guard rejects that transition, so suppress it for this sanctioned
        // system-wide operation.
        using (tenantContextAccessor.SuppressTenantGuard())
        {
            await db.SaveChangesAsync(ct);
        }

        await cache.RemoveByTagAsync("coded-values", ct);

        logger.LogInformation(
            "Provisional CodedValue {Id} approved as shared blueprint (code {Code})",
            codedValue.Id, codedValue.Code);
    }
}
