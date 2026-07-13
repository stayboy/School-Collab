using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.SetPrimaryContact;

/// <summary>Marks this contact as the owner's primary; other contacts are unset.</summary>
public sealed record SetPrimaryContact(Guid Id) : ICommand;
