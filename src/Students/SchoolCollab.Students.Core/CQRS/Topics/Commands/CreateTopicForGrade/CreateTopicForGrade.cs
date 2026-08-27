using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicForGrade;

/// <summary>
/// Creates a <see cref="Domain.Topic"/> (find-or-create by
/// <paramref name="CodedValueId"/> if provided, else by <paramref name="Code"/>)
/// **and** a <see cref="Domain.GradeTopicAssignment"/> linking it to the given
/// grade level for the <b>current period</b> (derived server-side). Used by the
/// Topics landing page's <c>+ New Topic</c> tool (§8.1). Returns the
/// resulting <see cref="DTOs.TopicDto"/>.
/// </summary>
/// <remarks>
/// <paramref name="Code"/> is optional (tcv/5): when omitted (or blank) the
/// handler generates it from <paramref name="Name"/> via the <c>TOPIC_CODE</c>
/// entity-code rule (<c>IEntityCodeGenerator</c>), e.g. "computer science" →
/// <c>CS01</c>. Pass an explicit code only to override template generation.
/// </remarks>
public sealed record CreateTopicForGrade(
    Guid GradeLevelId,
    Guid? CodedValueId,
    string? Code,
    string Name,
    int DisplayOrder,
    Guid? PeriodId = null) : ICommand;