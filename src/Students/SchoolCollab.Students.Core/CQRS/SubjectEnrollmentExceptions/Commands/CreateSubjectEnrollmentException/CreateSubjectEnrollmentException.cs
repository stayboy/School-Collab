using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.CreateSubjectEnrollmentException;

/// <summary>
/// Creates a subject enrollment exception on exactly one owner — a grade level or an
/// activity group (subject-period-exception-model.md v3 §2). The body carries a
/// period <b>part</b> (<see cref="AcademicYearDivision"/>) plus a span; it carries
/// <b>no</b> period id — that is the point (§0 decision 7). At least one bound is
/// required; a body with neither is a 422.
/// </summary>
public sealed record CreateSubjectEnrollmentException(
    Guid? GradeLevelId,
    Guid? ActivityGroupId,
    Guid TopicId,
    AcademicYearDivision Division,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    string? Reason = null) : ICommand;
