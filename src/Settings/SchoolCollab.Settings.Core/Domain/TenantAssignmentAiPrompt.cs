using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Per-tenant organization-level AI system prompt for assignment question
/// generation (WS-B2 / spec §3.4 line 91 — "locked or editable by admin"). One
/// row per tenant. A <see langword="null"/> <see cref="SystemPrompt"/> means the
/// AI service falls back to its embedded default prompt; <see cref="IsLocked"/>
/// suppresses the teacher's per-assignment guidance (<c>AiPromptOverride</c>) —
/// enforced server-side and reflected by the disabled authoring textarea.
/// Mirrors <see cref="TenantAssignmentPolicy"/> (WS-C1) and
/// <see cref="TenantSignatureConsentText"/> (WS-C2).
/// </summary>
public sealed class TenantAssignmentAiPrompt : BaseTenantEntityWithAudit, IHasRowVersion
{
    private TenantAssignmentAiPrompt() { }

    /// <summary>
    /// Maximum length of <see cref="SystemPrompt"/> in characters — matches the
    /// per-assignment <c>AiPromptOverride</c> column cap and the AI service's
    /// prompt-override cap, so the same guidance can be authored at either level.
    /// </summary>
    public const int MaxSystemPromptLength = 4000;

    /// <summary>
    /// The tenant's organization-level system prompt. <see langword="null"/> =
    /// the embedded default prompt applies.
    /// </summary>
    public string? SystemPrompt { get; private set; }

    /// <summary>
    /// When <see langword="true"/>, the teacher's per-assignment AI guidance is
    /// ignored server-side and disabled in the authoring UI.
    /// </summary>
    public bool IsLocked { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// Creates the single AI-prompt row for <paramref name="tenantId"/>. Callers
    /// typically use <see cref="SetPrompt"/> on an existing row instead; this
    /// factory is for the initial insert.
    /// </summary>
    public static TenantAssignmentAiPrompt Create(Guid tenantId, string? systemPrompt = null, bool isLocked = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new TenantAssignmentAiPrompt
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SystemPrompt = Normalize(systemPrompt),
            IsLocked = isLocked,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the tenant's organization prompt and lock posture (pass
    /// <see langword="null"/> to restore the embedded default). Stamps
    /// <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetPrompt(string? systemPrompt, bool isLocked)
    {
        SystemPrompt = Normalize(systemPrompt);
        IsLocked = isLocked;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string? Normalize(string? systemPrompt)
    {
        var trimmed = systemPrompt?.Trim();
        if (trimmed is { Length: > MaxSystemPromptLength })
            throw new ArgumentException(
                $"The organization AI prompt must be {MaxSystemPromptLength} characters or fewer.",
                nameof(systemPrompt));

        // Empty / whitespace-only input restores the embedded default rather than
        // persisting an empty string (WS-B2: null is the "no organization prompt"
        // signal the AI service checks).
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
