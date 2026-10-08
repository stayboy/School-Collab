using SchoolCollab.Core.CQRS;

using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;

/// <summary>Update a draft assignment (spec §3.2 / decision b). Questions,
/// attachments, content modules, and resources are full-replacement semantics
/// on the draft (snapshot existing child ids → remove each → re-add inbound).
/// The aggregate stays draft-only — non-draft updates are rejected by the
/// domain (FR-252). Content modules + resources are the WS-A1 children
/// (spec §4.10 / FR-210–212).</summary>
public sealed record UpdateAssignmentCommand(
    Guid Id,
    string Title,
    string? Description,
    AssignmentType AssignmentType,
    GradingFormat GradingFormat,
    TargetAudienceType TargetAudienceType,
    Guid TopicId,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    /// <summary>D3/OD1 (round <c>assignment-rules-policy-rework</c>): the AUTHOR half of the
    /// guardian-review value — see <c>CreateAssignmentCommand.MandatoryReview</c>.</summary>
    bool? MandatoryReview,
    string? AiPromptOverride = null,
    IReadOnlyList<NewQuestionDto>? Questions = null,
    IReadOnlyList<NewAttachmentDto>? Attachments = null,
    IReadOnlyList<NewContentModuleDto>? ContentModules = null,
    IReadOnlyList<NewResourceDto>? Resources = null,
    /// <summary>WS-A3 (spec §3.3): pass/fail score threshold.
    /// Threaded to <c>Assignment.Update(...)</c>.</summary>
    decimal? PassScore = null,
    /// <summary>WS-A3 (spec §7 Q4): max submission attempts.
    /// Threaded to <c>Assignment.Update(...)</c>.</summary>
    int? MaxAttempts = null,
    /// <summary>WS-B2 (spec §3.4 line 70): requested per-difficulty counts.
    /// Threaded to <c>Assignment.Update(...)</c>.</summary>
    int? DifficultyEasyCount = null,
    int? DifficultyMediumCount = null,
    int? DifficultyHardCount = null,
    /// <summary>INS-1 (assignment-authoring-compartments §9): student-facing task
    /// text. Threaded to <c>Assignment.Update(...)</c>.</summary>
    string? Instructions = null,
    /// <summary>R2 (TGT-1 / D-1 / UX-21): the authored targeting constraints — full
    /// replacement when non-null, preserve when null. Threaded to <c>SetTargets</c>.</summary>
    IReadOnlyList<AssignmentTargetDto>? Targets = null,
    /// <summary>R4 (CP-10/D23): the picked strand ids — null preserves the persisted set, a
    /// non-null (even empty) list is a full replacement. Threaded to
    /// <c>Assignment.SetContextPicks</c>, which preserves the kind whose argument is null.</summary>
    IReadOnlyList<Guid>? ContextStrandIds = null,
    /// <summary>R4 (CP-10/D23): the picked lesson ids — see <see cref="ContextStrandIds"/>.</summary>
    IReadOnlyList<Guid>? ContextLessonIds = null) : ICommand;
