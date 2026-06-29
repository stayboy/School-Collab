using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Commands.RemoveStudentSubject;

public sealed record RemoveStudentSubject(Guid Id) : ICommand;