using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.VerifyContact;

/// <summary>Marks the contact verified (v1: admin/teacher-set, no OTP).</summary>
public sealed record VerifyContact(Guid Id) : ICommand;
