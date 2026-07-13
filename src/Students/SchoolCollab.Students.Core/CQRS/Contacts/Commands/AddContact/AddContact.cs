using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.AddContact;

public sealed record AddContact(
    ContactOwnerType OwnerType,
    Guid OwnerId,
    ContactChannel Channel,
    string Value,
    string? Label,
    bool IsPrimary) : ICommand;
