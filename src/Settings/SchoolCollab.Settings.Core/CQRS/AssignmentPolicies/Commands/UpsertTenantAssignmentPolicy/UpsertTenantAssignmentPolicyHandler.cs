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
/// when absent (WS-C1 / spec §7 Q1). The two contact caps are validated here (owner
/// decision Q7): a non-positive cap is rejected rather than stored, because the
/// recipient filter can only treat it as "uncapped" and a silent no-op cap is worse
/// than a rejected write. Null stays "unset".
/// </summary>
public sealed class UpsertTenantAssignmentPolicyHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider) : ICommandHandler<UpsertTenantAssignmentPolicy, TenantAssignmentPolicyDto>
{
    public async Task<TenantAssignmentPolicyDto> HandleAsync(
        UpsertTenantAssignmentPolicy command, CancellationToken ct = default)
    {
        GuardContactCaps(command.MaxPrimaryContacts, command.MaxCopyContacts);

        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var existing = await db.TenantAssignmentPolicies.SingleOrDefaultAsync(ct);
        TenantAssignmentPolicy policy;
        if (existing is not null)
        {
            existing.SetPolicy(
                command.SignatureRequirement,
                command.RequiresApprovalBeforePublish,
                command.MaxPrimaryContacts,
                command.MaxCopyContacts);
            policy = existing;
        }
        else
        {
            policy = TenantAssignmentPolicy.Create(
                tenantId,
                command.SignatureRequirement,
                command.RequiresApprovalBeforePublish,
                command.MaxPrimaryContacts,
                command.MaxCopyContacts);
            db.TenantAssignmentPolicies.Add(policy);
        }

        await db.SaveChangesAsync(ct);

        return new TenantAssignmentPolicyDto(
            policy.SignatureRequirement,
            policy.RequiresApprovalBeforePublish,
            policy.MaxPrimaryContacts,
            policy.MaxCopyContacts);
    }

    /// <summary>
    /// Owner decision Q7: a contact cap must be positive when set. The Students API's
    /// assignment-policy PUT maps this to 400, mirroring the notification-policy PUT.
    /// </summary>
    private static void GuardContactCaps(int? maxPrimaryContacts, int? maxCopyContacts)
    {
        if (maxPrimaryContacts is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UpsertTenantAssignmentPolicy.MaxPrimaryContacts), maxPrimaryContacts,
                "The maximum primary contacts must be a positive number (null = unset).");
        }

        if (maxCopyContacts is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UpsertTenantAssignmentPolicy.MaxCopyContacts), maxCopyContacts,
                "The maximum copy contacts must be a positive number (null = unset).");
        }
    }
}
