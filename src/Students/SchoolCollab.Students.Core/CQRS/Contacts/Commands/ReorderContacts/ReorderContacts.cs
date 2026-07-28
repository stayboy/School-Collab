using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.ReorderContacts;

/// <summary>
/// Reorders all contacts belonging to an owner atomically (spec §4.9). The
/// supplied <c>OrderedContactIds</c> list defines the new ordering: index 0
/// is the preferred contact (DisplayOrder 0), index 1 is next, etc.
/// </summary>
public sealed record ReorderContacts(
    ContactOwnerType OwnerType,
    Guid OwnerId,
    IReadOnlyList<Guid> OrderedContactIds) : ICommand;