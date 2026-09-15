using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Queries.GetTenantAssignmentAiPrompt;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Queries.GetTenantAssignmentAiPrompt;

/// <summary>
/// Loads the current tenant's organization AI prompt row via the tenant query
/// filter, returning the DTO or <see langword="null"/> when the tenant has not
/// configured one yet (WS-B2 / spec §3.4).
/// </summary>
public sealed class GetTenantAssignmentAiPromptHandler(SettingsDbContext db)
    : IQueryHandler<GetTenantAssignmentAiPrompt, TenantAssignmentAiPromptDto?>
{
    public async Task<TenantAssignmentAiPromptDto?> HandleAsync(
        GetTenantAssignmentAiPrompt query, CancellationToken ct = default)
    {
        var row = await db.TenantAssignmentAiPrompts
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        return row is null ? null : new TenantAssignmentAiPromptDto(row.SystemPrompt, row.IsLocked);
    }
}
