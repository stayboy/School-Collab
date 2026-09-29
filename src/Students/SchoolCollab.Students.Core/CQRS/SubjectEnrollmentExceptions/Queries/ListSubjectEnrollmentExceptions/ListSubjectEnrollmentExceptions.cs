using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Queries.ListSubjectEnrollmentExceptions;

/// <summary>
/// Lists subject enrollment exceptions for one owner (grade level or activity group),
/// optionally narrowed to a single topic — the picker query
/// (<c>?gradeLevelId=&amp;topicId=</c>) is the narrow form
/// (subject-period-exception-model.md v3 §6). There is no period filter: an exception
/// never names a period instance (§0 decision 7).
/// </summary>
public sealed record ListSubjectEnrollmentExceptions(
    Guid? GradeLevelId,
    Guid? ActivityGroupId,
    Guid? TopicId = null) : IQuery<SubjectEnrollmentExceptionDto[]>;
