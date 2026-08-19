using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>Working-copy model for <see cref="ContactFormFields"/>. Holds
/// the contact's four field values (Channel / CountryCodeId / Value /
/// Label) as a single reference-type object so the host binds the component
/// with one <c>Model</c> parameter instead of four two-way primitive
/// bindings. The component mutates <c>Model.Channel</c> etc. via
/// <c>@bind-Value</c> / <c>@bind-SelectedValue</c> / <c>@bind-SelectedId</c>
/// on the inputs, and the host reads the accumulated values on Save
/// (<c>AddAsync</c> / <c>SaveEditAsync</c> in <c>ContactsEditor</c>).
/// Mirrors the pattern used by <see cref="GuardianEditFieldsModel"/> for
/// the sibling guardian edit fields.</summary>
public sealed class ContactFormFieldsModel
{
    /// <summary>The contact channel. Defaults to
    /// <see cref="ContactChannel.Email"/>.</summary>
    public ContactChannel Channel { get; set; } = ContactChannel.Email;

    /// <summary>The selected country calling-code id, or null for
    /// non-phone channels.</summary>
    public Guid? CountryCodeId { get; set; }

    /// <summary>The contact value (email / phone / WhatsApp number).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The optional contact label (e.g. "Home").</summary>
    public string Label { get; set; } = string.Empty;
}
