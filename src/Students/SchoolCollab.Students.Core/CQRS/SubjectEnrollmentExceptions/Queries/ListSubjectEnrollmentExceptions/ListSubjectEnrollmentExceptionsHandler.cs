using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Queries.ListSubjectEnrollmentExceptions;

/// <summary>
/// Reads subject enrollment exceptions for an owner. Deliberately NOT cached: they are
/// written rarely and read rarely, so there is no stale-window risk to trade a cache
/// key for (subject-period-exception-model.md v3 §6).
/// </summary>
public sealed class ListSubjectEnrollmentExceptionsHandler(ISubjectEnrollmentExceptionRepository repository)
    : IQueryHandler<ListSubjectEnrollmentExceptions, SubjectEnrollmentExceptionDto[]>
{
    public Task<SubjectEnrollmentExceptionDto[]> HandleAsync(
        ListSubjectEnrollmentExceptions query,
        CancellationToken cancellationToken = default) =>
        repository.ListDtosAsync(
            query.GradeLevelId,
            query.ActivityGroupId,
            query.TopicId,
            cancellationToken);
}
