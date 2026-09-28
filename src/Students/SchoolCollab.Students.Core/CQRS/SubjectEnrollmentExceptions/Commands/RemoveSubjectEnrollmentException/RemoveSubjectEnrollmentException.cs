using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.RemoveSubjectEnrollmentException;

/// <summary>
/// Removes a subject enrollment exception. Idempotent: removing an exception that is
/// already gone is a no-op, not an error.
/// </summary>
public sealed record RemoveSubjectEnrollmentException(Guid Id) : ICommand;
