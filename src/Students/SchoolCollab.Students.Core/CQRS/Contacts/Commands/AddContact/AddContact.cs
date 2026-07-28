using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.AddContact;

public sealed record AddContact(
    ContactOwnerType OwnerType,
    Guid OwnerId,
    ContactChannel Channel,
    string Value,
    string? Label) : ICommand
{
    public string? CountryCode { get; init; }
    /// <summary>Initial display order (spec §4.9). Lower renders first.</summary>
    public int DisplayOrder { get; init; }
}
