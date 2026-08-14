using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Mutable in-memory contact model used by <see cref="ContactsEditor"/> in
/// <see cref="ContactsEditor.EditorMode.Buffered"/> mode. The parent owns
/// the list and flushes it to the API on save; the editor never calls the
/// API in Buffered mode.
///
/// This is the create-time counterpart of <c>ContactDto</c>: it carries the
/// same display fields (Channel, Value, Label, CountryCode) plus an
/// <see cref="Order"/> that drives priority (lowest = preferred) and a
/// stable <see cref="TempId"/> for Blazor <c>@key</c> during reorders. The
/// real <c>Contact.Id</c> is assigned by the database when the parent
/// persists the list.
/// </summary>
public sealed class ContactModel
{
    /// <summary>Stable client-side id for Blazor <c>@key</c> during
    /// reorders. Not persisted.</summary>
    public Guid TempId { get; set; } = Guid.NewGuid();

    /// <summary>The persisted <c>Contact.Id</c> this model was loaded from, or null for a
    /// brand-new contact. Lets the all-inclusive edit reconcile contacts by id on save
    /// (a null <see cref="PersistedId"/> means "add"; a set one means "update").</summary>
    public Guid? PersistedId { get; set; }

    public ContactChannel Channel { get; set; } = ContactChannel.Email;

    /// <summary>The raw contact value (email address or phone number).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Optional display label (e.g. "Home", "Work").</summary>
    public string? Label { get; set; }

    /// <summary>Resolved dial-code string (e.g. "+233") for SMS/WhatsApp.
    /// Null for Email.</summary>
    public string? CountryCode { get; set; }

    /// <summary>Display/priority order (0 = highest priority / preferred).
    /// Assigned by add sequence and updated by move-up / move-down.
    /// The lowest-ordered contact is the owner's preferred contact.</summary>
    public int Order { get; set; }
}
