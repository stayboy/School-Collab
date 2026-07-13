using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.UnlinkGuardian;

public sealed record UnlinkGuardian(Guid StudentId, Guid GuardianId) : ICommand;
