using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Commands.UpsertTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Commands.UpsertTenantAssignmentPolicy;

/// <summary>
/// Upserts the single global-default assignment-policy row for the current tenant.
/// The tenant query filter scopes reads to the current tenant; the row is created
/// when absent (WS-C1 / spec §7 Q1).
/// </summary>
public sealed class UpsertTenantAssignmentPolicyHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider) : ICommandHandler<UpsertTenantAssignmentPolicy, TenantAssignmentPolicyDto>
{
    public async Task<TenantAssignmentPolicyDto> HandleAsync(
        UpsertTenantAssignmentPolicy command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var existing = await db.TenantAssignmentPolicies.SingleOrDefaultAsync(ct);
        TenantAssignmentPolicy policy;
        if (existing is not null)
        {
            existing.SetPolicy(command.RequiresSignatureDefault);
            policy = existing;
        }
        else
        {
            policy = TenantAssignmentPolicy.Create(tenantId, command.RequiresSignatureDefault);
            db.TenantAssignmentPolicies.Add(policy);
        }

        await db.SaveChangesAsync(ct);

        return new TenantAssignmentPolicyDto(policy.RequiresSignatureDefault);
    }
}
