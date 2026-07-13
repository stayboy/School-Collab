using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.UpdateGuardian;

public sealed record UpdateGuardian(
    Guid Id,
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? Address,
    Guid? CommunityId) : ICommand;
