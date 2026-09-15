using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Commands.UpsertTenantAssignmentAiPrompt;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Commands.UpsertTenantAssignmentAiPrompt;

/// <summary>
/// Upserts the single organization-AI-prompt row for the current tenant. The
/// tenant query filter scopes reads to the current tenant; the row is created
/// when absent (WS-B2 / spec §3.4 line 91). The 4000-character cap is enforced
/// by <see cref="TenantAssignmentAiPrompt.SetPrompt"/> (typed
/// <see cref="ArgumentException"/>, mapped to 400 at the route).
/// </summary>
public sealed class UpsertTenantAssignmentAiPromptHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider)
    : ICommandHandler<UpsertTenantAssignmentAiPrompt, TenantAssignmentAiPromptDto>
{
    public async Task<TenantAssignmentAiPromptDto> HandleAsync(
        UpsertTenantAssignmentAiPrompt command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var existing = await db.TenantAssignmentAiPrompts.SingleOrDefaultAsync(ct);
        TenantAssignmentAiPrompt row;
        if (existing is not null)
        {
            existing.SetPrompt(command.SystemPrompt, command.IsLocked);
            row = existing;
        }
        else
        {
            row = TenantAssignmentAiPrompt.Create(tenantId, command.SystemPrompt, command.IsLocked);
            db.TenantAssignmentAiPrompts.Add(row);
        }

        await db.SaveChangesAsync(ct);

        return new TenantAssignmentAiPromptDto(row.SystemPrompt, row.IsLocked);
    }
}
