using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.UpdateContact;

public sealed record UpdateContact(
    Guid Id,
    string Value,
    string? Label,
    string Reason) : ICommand
{
    public string? CountryCode { get; init; }
}
