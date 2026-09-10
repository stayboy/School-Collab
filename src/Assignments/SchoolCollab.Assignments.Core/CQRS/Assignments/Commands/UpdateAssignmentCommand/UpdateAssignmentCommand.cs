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
    Guid? GradeLevelId,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    bool MandatoryReview,
    string? AiPromptOverride = null,
    IReadOnlyList<NewQuestionDto>? Questions = null,
    IReadOnlyList<NewAttachmentDto>? Attachments = null,
    IReadOnlyList<NewContentModuleDto>? ContentModules = null,
    IReadOnlyList<NewResourceDto>? Resources = null,
    /// <summary>WS-A2 (spec §7 Q6): archive grace window in days.
    /// Threaded to <c>Assignment.Update(...)</c>.</summary>
    int ArchiveGraceDays = 30) : ICommand;
