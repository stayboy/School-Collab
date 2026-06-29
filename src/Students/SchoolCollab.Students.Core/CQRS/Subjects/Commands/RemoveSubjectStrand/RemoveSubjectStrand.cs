using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.RemoveSubjectStrand;

public sealed record RemoveSubjectStrand(Guid Id) : ICommand;
