using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListSubscribedContacts;

/// <summary>
/// Cross-BC resolver contract (spec §9 G5). For guardian-owned contacts, the
/// effective <see cref="GuardianRole"/> (Primary &gt; CC) is resolved from the
/// guardian's student-guardian links.
/// </summary>
public sealed record ListSubscribedContacts(
    ContactOwnerType OwnerType,
    Guid? OwnerId = null,
    SubscriptionScope? Scope = null) : IQuery<SubscribedContactDto[]>;
