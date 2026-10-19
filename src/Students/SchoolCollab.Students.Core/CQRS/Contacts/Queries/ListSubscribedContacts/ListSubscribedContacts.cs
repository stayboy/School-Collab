using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListSubscribedContacts;

/// <summary>
/// Cross-BC resolver contract (spec §9 G5). For guardian-owned contacts, the
/// effective <see cref="GuardianRole"/> (Primary &gt; CC) is resolved from the
/// guardian's student-guardian links.
/// <para><see cref="StudentId"/> (round notification-templates-b1, §3.4) names the ward the
/// caller is resolving for: a guardian can be linked to several students, so the ward's
/// display name cannot be inferred from the guardian alone. Optional — omitted (or a
/// teacher-owner call) yields a null <c>StudentName</c> in the response.</para>
/// </summary>
public sealed record ListSubscribedContacts(
    ContactOwnerType OwnerType,
    Guid? OwnerId = null,
    SubscriptionScope? Scope = null,
    Guid? StudentId = null) : IQuery<SubscribedContactDto[]>;
