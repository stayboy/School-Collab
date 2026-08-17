using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Queries.ListContactAuditEntries;

/// <summary>
/// List append-only contact audit rows, optionally filtered to a single contact
/// or to all changes against one owner.
/// </summary>
public sealed record ListContactAuditEntries(
    Guid? ContactId,
    ContactOwnerType? OwnerType,
    Guid? OwnerId,
    int Skip,
    int Take) : IQuery<ContactAuditEntryDto[]>;
