using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Commands.RemoveGradeSubject;

public sealed record RemoveGradeSubject(Guid Id) : ICommand;