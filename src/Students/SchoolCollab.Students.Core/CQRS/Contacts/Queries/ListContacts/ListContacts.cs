using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListContacts;

public sealed record ListContacts(ContactOwnerType OwnerType, Guid OwnerId) : IQuery<ContactDto[]>;
