using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Queries.GetTenantAssignmentAiPrompt;

/// <summary>
/// Returns the current tenant's organization AI prompt + lock posture, or
/// <see langword="null"/> when none has been configured yet (a null result means
/// the AI service falls back to the embedded default — WS-B2 / spec §3.4).
/// </summary>
public sealed record GetTenantAssignmentAiPrompt : IQuery<TenantAssignmentAiPromptDto?>
{
    public static readonly GetTenantAssignmentAiPrompt Instance = new();
}
