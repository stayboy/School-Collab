using SchoolCollab.Core.CQRS;

using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;

/// <summary>Create a new draft assignment (spec §3.2 / §2.6 FR-250/251/230/210).
/// <see cref="Questions"/>, <see cref="Attachments"/>, <see cref="ContentModules"/>,
/// and <see cref="Resources"/> are optional trailing parameters — manual assignments
/// may omit them entirely. <see cref="AiPromptOverride"/> is the per-assignment
/// override appended to the embedded AI prompt (decision 8). Content modules +
/// resources are the WS-A1 children (spec §4.10 / FR-210–212).</summary>
public sealed record CreateAssignmentCommand(
    string Title,
    string? Description,
    AssignmentType AssignmentType,
    GradingFormat GradingFormat,
    TargetAudienceType TargetAudienceType,
    Guid TopicId,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    /// <summary>D3/OD1 (round <c>assignment-rules-policy-rework</c>): the AUTHOR half of the
    /// guardian-review value. Null = the author left it unset; the handler stores
    /// <c>effective.MandatoryReview ?? MandatoryReview ?? true</c>, so a resolved policy always
    /// wins an author payload.</summary>
    bool? MandatoryReview = null,
    string? AiPromptOverride = null,
    IReadOnlyList<NewQuestionDto>? Questions = null,
    IReadOnlyList<NewAttachmentDto>? Attachments = null,
    IReadOnlyList<NewContentModuleDto>? ContentModules = null,
    IReadOnlyList<NewResourceDto>? Resources = null,
    /// <summary>WS-A3 (spec §3.3): pass/fail score threshold.
    /// Threaded to <c>Assignment.Create(...)</c>.</summary>
    decimal? PassScore = null,
    /// <summary>WS-A3 (spec §7 Q4): max submission attempts.
    /// Threaded to <c>Assignment.Create(...)</c>.</summary>
    int? MaxAttempts = null,
    /// <summary>WS-B2 (spec §3.4 line 70): requested per-difficulty counts.
    /// Threaded to <c>Assignment.Create(...)</c>.</summary>
    int? DifficultyEasyCount = null,
    int? DifficultyMediumCount = null,
    int? DifficultyHardCount = null,
    /// <summary>INS-1 (assignment-authoring-compartments §9): student-facing task
    /// text. Threaded to <c>Assignment.Create(...)</c>.</summary>
    string? Instructions = null,
    /// <summary>R2 (TGT-1 / D-1): the authored targeting constraints. Null = none supplied;
    /// a non-null list must hold at least one entry (TGT-13). The handler attaches them with
    /// <c>SetTargets</c> once the tenant is stamped.</summary>
    IReadOnlyList<AssignmentTargetDto>? Targets = null,
    /// <summary>R4 (CP-5/D23): the picked strand ids, threaded to
    /// <c>Assignment.SetContextPicks</c> (a create passes both lists straight through — there is
    /// no persisted state to preserve).</summary>
    IReadOnlyList<Guid>? ContextStrandIds = null,
    /// <summary>R4 (CP-5/D23): the picked lesson ids — see <see cref="ContextStrandIds"/>.</summary>
    IReadOnlyList<Guid>? ContextLessonIds = null) : ICommand;
