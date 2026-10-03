using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// R3 (D4/P1-2) — one append-only header per AI question generation, recording <b>what was
/// asked for</b> and <b>which server-resolved model answered</b>.
/// <para><b>Why a header and not a copy of the questions:</b> a separate <c>GeneratedQuestion</c>
/// table is rejected — it would duplicate <see cref="AssignmentQuestion"/> and fork the source of
/// truth the future sorting / max-publish feature will read. The header deliberately stores the
/// <i>request shape</i> plus the resolved model, never question content, so provenance survives the
/// author's later edits of the generated rows.</para>
/// <para><b>Tenant:</b> a strict tenant entity (<see cref="BaseTenantEntity"/> — operational data,
/// direct tenancy) carrying the module convention's <c>(TenantId, AssignmentId)</c> index. It is
/// written by the host that owns the assignment, which is what keeps the AI host stateless.</para>
/// </summary>
public sealed class AssignmentQuestionGeneration : BaseTenantEntity
{
    private AssignmentQuestionGeneration() { }

    private AssignmentQuestionGeneration(
        Guid tenantId,
        Guid assignmentId,
        int questionCount,
        string? types,
        int? difficultyEasy,
        int? difficultyMedium,
        int? difficultyHard,
        string? provider,
        string model)
        : base(tenantId)
    {
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));
        if (questionCount < 1)
            throw new ArgumentException("Question count must be at least one.", nameof(questionCount));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("The resolved model is required.", nameof(model));

        AssignmentId = assignmentId;
        QuestionCount = questionCount;
        Types = types;
        DifficultyEasyCount = difficultyEasy;
        DifficultyMediumCount = difficultyMedium;
        DifficultyHardCount = difficultyHard;
        Provider = provider;
        Model = model;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid AssignmentId { get; private set; }

    /// <summary>The requested <c>QuestionCount</c>.</summary>
    public int QuestionCount { get; private set; }

    /// <summary>The requested type mix as comma-separated <c>QuestionTypeDto</c> names, or null when
    /// the author left "all types" selected (the balanced server default, FR-221).</summary>
    public string? Types { get; private set; }

    public int? DifficultyEasyCount { get; private set; }
    public int? DifficultyMediumCount { get; private set; }
    public int? DifficultyHardCount { get; private set; }

    /// <summary>The provider the <b>AI host's own</b> <c>ChatModelResolver</c> selected. Null when the
    /// provider is the host default and unresolved; never taken from a client-supplied value (D6).</summary>
    public string? Provider { get; private set; }

    /// <summary>The model the AI host's <c>ChatModelResolver</c> resolved for this request (P1-1).
    /// The Assignments host cannot resolve it itself and must not try.</summary>
    public string Model { get; private set; } = default!;

    /// <summary>Append-only: set once, never updated.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Records one generation. <paramref name="model"/> is the value the AI host surfaced for this
    /// request (P1-1) — the caller must never synthesise or echo one from the client.
    /// </summary>
    public static AssignmentQuestionGeneration Create(
        Guid tenantId,
        Guid assignmentId,
        int questionCount,
        string? types,
        int? difficultyEasy,
        int? difficultyMedium,
        int? difficultyHard,
        string? provider,
        string model)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));

        return new AssignmentQuestionGeneration(
            tenantId, assignmentId, questionCount, types,
            difficultyEasy, difficultyMedium, difficultyHard, provider, model);
    }
}
