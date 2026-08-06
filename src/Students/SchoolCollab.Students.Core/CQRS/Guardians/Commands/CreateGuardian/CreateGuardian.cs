using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Commands.CreateGuardian;

/// <summary>
/// Creates a guardian (no email/phone — add via <c>AddContact</c>). The initial
/// name-history snapshot is appended in the handler after tenant assignment.
/// </summary>
public sealed record CreateGuardian(
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? Address,
    Guid? CommunityId,
    DateOnly? DateOfBirth = null,
    Guid? GenderCodedValueId = null) : ICommand;
