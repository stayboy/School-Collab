namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// WS-B2 (spec §3.4 line 70) — resolves whether the tenant has LOCKED the
/// org-level AI prompt, so the wizard's prompt-override textarea can be disabled.
/// The interface lives in Assignments.Core; the HTTP implementation lives in
/// Assignments.Api (this module stays free of HTTP). Mirrors
/// <see cref="ISignatureDefaultResolver"/>'s fail-open posture: a resolution
/// failure degrades to <see langword="false"/> and never blocks the create wizard.
/// </summary>
public interface IAiPromptPolicyResolver
{
    Task<bool> ResolveAiPromptLockedAsync(CancellationToken cancellationToken = default);
}
