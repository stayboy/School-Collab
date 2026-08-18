using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.DeleteContact;

/// <summary>Soft-delete blocks the contact (retained for audit).</summary>
public sealed record DeleteContact(
    Guid Id,
    string Reason) : ICommand;
