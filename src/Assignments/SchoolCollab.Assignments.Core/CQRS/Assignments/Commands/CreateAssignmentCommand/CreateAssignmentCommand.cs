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
    Guid? GradeLevelId,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    bool MandatoryReview = true,
    string? AiPromptOverride = null,
    IReadOnlyList<NewQuestionDto>? Questions = null,
    IReadOnlyList<NewAttachmentDto>? Attachments = null,
    IReadOnlyList<NewContentModuleDto>? ContentModules = null,
    IReadOnlyList<NewResourceDto>? Resources = null,
    /// <summary>WS-A2 (spec §7 Q6): archive grace window in days.
    /// Threaded to <c>Assignment.Create(...)</c>.</summary>
    int ArchiveGraceDays = 30,
    /// <summary>WS-A3 (spec §3.3): pass/fail score threshold.
    /// Threaded to <c>Assignment.Create(...)</c>.</summary>
    decimal? PassScore = null,
    /// <summary>WS-A3 (spec §7 Q4): max submission attempts.
    /// Threaded to <c>Assignment.Create(...)</c>.</summary>
    int? MaxAttempts = null,
    /// <summary>WS-C1 / spec §7 Q1: whether a guardian signature is
    /// required after completion. Threaded to
    /// <c>Assignment.Create(...)</c>.</summary>
    bool RequiresSignature = false) : ICommand;
