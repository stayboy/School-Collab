using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.SetContactOrder;

/// <summary>
/// Sets a single contact's <c>DisplayOrder</c> (spec §4.9). The contact with
/// the lowest <c>DisplayOrder</c> for an owner is the preferred contact.
/// IsPrimary is kept in sync for the additive phase (order 0 == primary).
/// </summary>
public sealed record SetContactOrder(Guid Id, int Order) : ICommand;