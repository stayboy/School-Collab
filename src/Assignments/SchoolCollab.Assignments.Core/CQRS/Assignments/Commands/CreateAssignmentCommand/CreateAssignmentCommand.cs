using SchoolCollab.Core.CQRS;

using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;

/// <summary>Create a new draft assignment (spec §3.2 / §2.6 FR-250/251/230/210).
/// <see cref="Questions"/> and <see cref="Attachments"/> are optional trailing
/// parameters — manual assignments may omit them entirely. <see cref="AiPromptOverride"/>
/// is the per-assignment override appended to the embedded AI prompt (decision 8).</summary>
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
    IReadOnlyList<NewAttachmentDto>? Attachments = null) : ICommand;
