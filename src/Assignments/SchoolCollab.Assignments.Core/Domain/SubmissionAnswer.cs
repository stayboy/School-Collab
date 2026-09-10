using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// One submitted answer row per (submission version, question) — WS-A3
/// (spec §3.3 pass/fail with retry logic). Persisted per version so
/// per-question analytics can replay the attempt that produced each
/// score. Standalone tenant entity (FK declared once from the parent
/// <see cref="AssignmentSubmissionVersion"/> aggregate side in
/// <c>AssignmentSubmissionVersionConfiguration</c> — NOT an owned type)
/// — mirroring the ar-4 <see cref="ContentModule"/> /
/// <see cref="AssignmentResource"/> pattern. The FK cascade is declared
/// on the parent side so deleting a version cascades its answers; the
/// dependent-side configuration stays free of relationship declarations
/// (single source of truth).
/// </summary>
public sealed class SubmissionAnswer : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private SubmissionAnswer() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    /// <summary>The owning submission version (cascade-deleted with it).</summary>
    public Guid SubmissionVersionId { get; private set; }

    /// <summary>The question this answer responds to (validated by
    /// <c>SubmissionAnswerValidator</c> on submit).</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>The chosen option for MC/TF questions. Null when the
    /// answer is a ShortAnswer text or the student left the option
    /// blank.</summary>
    public Guid? SelectedOptionId { get; private set; }

    /// <summary>The free-text answer for ShortAnswer questions
    /// (maxlength 2000 enforced by EF). Null when the question is MC/TF
    /// or the student left the field blank.</summary>
    public string? TextAnswer { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Factory — constructor hygiene only (decision (a) mirrors
    /// <see cref="ContentModule.Create"/>). Domain validation (that the
    /// ids refer to live aggregate rows) is owned by
    /// <c>SubmissionAnswerValidator</c>; this factory only enforces
    /// non-empty id shape and trims <see cref="TextAnswer"/>.</summary>
    public static SubmissionAnswer Create(
        Guid tenantId,
        Guid submissionVersionId,
        Guid questionId,
        Guid? selectedOptionId,
        string? textAnswer)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (submissionVersionId == Guid.Empty)
            throw new ArgumentException("Submission version id is required.", nameof(submissionVersionId));
        if (questionId == Guid.Empty)
            throw new ArgumentException("Question id is required.", nameof(questionId));

        var now = DateTimeOffset.UtcNow;
        return new SubmissionAnswer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubmissionVersionId = submissionVersionId,
            QuestionId = questionId,
            SelectedOptionId = selectedOptionId,
            TextAnswer = textAnswer?.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
