using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.GetOrCreateTopic;

/// <summary>
/// Find-or-create a <see cref="Domain.Topic"/> by <see cref="CodedValueId"/>
/// (safe since the unique index from §5.7 guarantees at most one subject per
/// coded value). If a subject for the coded value exists, it is reused and its
/// mirrored <see cref="Domain.Topic.Name"/>/DisplayOrder are updated;
/// otherwise a new one is created. The topic is a shared, global definition;
/// it is linked to the grade level via the <c>GradeTopicAssignment</c> bridge
/// for the current period. Used by the wizard's "Add to grade" button so the
/// user can pick a subject coded value and wire it to the grade without leaving
/// the wizard. Returns the resulting subject as a <see cref="TopicDto"/>.
/// </summary>
public sealed record GetOrCreateTopic(
    Guid GradeLevelId,
    Guid CodedValueId,
    string? Code,
    string Name,
    int DisplayOrder) : ICommand;
