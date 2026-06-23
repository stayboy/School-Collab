using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.RemoveSubjectStrand;

public sealed record RemoveSubjectStrand(Guid Id) : ICommand;
