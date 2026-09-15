namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Tenant-level organization AI prompt for assignment question generation
/// (WS-B2 / spec §3.4 line 91). A <see langword="null"/>
/// <see cref="SystemPrompt"/> = the embedded default prompt applies;
/// <see cref="IsLocked"/> = the teacher's per-assignment guidance is suppressed.
/// </summary>
public sealed record TenantAssignmentAiPromptDto(string? SystemPrompt, bool IsLocked);
