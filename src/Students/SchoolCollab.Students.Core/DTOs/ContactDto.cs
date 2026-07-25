using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.DTOs;

public sealed record ContactDto(
    Guid Id,
    ContactOwnerType OwnerType,
    Guid OwnerId,
    ContactChannel Channel,
    string Value,
    string? Label,
    bool IsPrimary,
    bool IsVerified,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string? CountryCode { get; init; }
}
