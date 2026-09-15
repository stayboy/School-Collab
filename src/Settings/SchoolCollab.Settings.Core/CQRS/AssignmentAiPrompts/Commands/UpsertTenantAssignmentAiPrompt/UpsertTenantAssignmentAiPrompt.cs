using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Commands.UpsertTenantAssignmentAiPrompt;

/// <summary>
/// Creates or replaces the current tenant's organization AI prompt + lock posture
/// (WS-B2 / spec §3.4 line 91). Passing <see langword="null"/> restores the
/// embedded default prompt. The returned DTO is the persisted state.
/// </summary>
public sealed record UpsertTenantAssignmentAiPrompt(string? SystemPrompt, bool IsLocked) : ICommand;
