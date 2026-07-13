using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.DeleteGuardian;

/// <summary>Soft-delete = block only. History, links and contacts are retained.</summary>
public sealed record DeleteGuardian(Guid Id) : ICommand;
