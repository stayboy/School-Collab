using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacher;

/// <summary>Soft-delete = block only. Links, subjects and grade levels are retained.</summary>
public sealed record DeleteTeacher(Guid Id) : ICommand;
