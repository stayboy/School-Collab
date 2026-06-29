using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Commands.AssignStudentSubject;

public sealed record AssignStudentSubject(
    Guid StudentId,
    Guid SubjectId,
    Guid PeriodId,
    bool IsOverride,
    SubjectAssignmentSource SourceType) : ICommand;