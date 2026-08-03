using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicForGrade;

/// <summary>
/// Creates a <see cref="Domain.Topic"/> (find-or-create by
/// <paramref name="CodedValueId"/> if provided, else by <paramref name="Code"/>)
/// **and** a <see cref="Domain.GradeSubjectAssignment"/> linking it to the given
/// grade level for the <b>current period</b> (derived server-side). Used by the
/// Topics landing page's <c>+ New Topic</c> tool (§8.1). Returns the
/// resulting <see cref="DTOs.TopicDto"/>.
/// </summary>
public sealed record CreateTopicForGrade(
    Guid GradeLevelId,
    Guid? CodedValueId,
    string Code,
    string Name,
    int DisplayOrder) : ICommand;