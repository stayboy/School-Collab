using System.ComponentModel.DataAnnotations;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels;

namespace SchoolCollab.Students.Admin.Components.Students;

/// <summary>Form-state model for <see cref="GuardianPickerDialog"/>.</summary>
public sealed class GuardianAssignmentModel
{
    /// <summary>Set when linking an existing tenant guardian; null ⇒ create a new one.</summary>
    public Guid? ExistingGuardianId { get; set; }

    [Required] public string? FirstName { get; set; }
    [Required] public string? LastName { get; set; }

    public Guid? TitleCodedValueId { get; set; }
    public Guid? RelationshipCodedValueId { get; set; }
    public ContactChannel ContactChannel { get; set; } = ContactChannel.Email;
    public string? ContactValue { get; set; }

    /// <summary>The coded-value id of the selected country calling code
    /// (from the <c>CNCODES</c> category). Bound to the
    /// <c>CodedValueDropdown</c> in <c>GuardianFormFields</c>; only
    /// relevant when <c>ContactChannel</c> is <c>SMS</c> or
    /// <c>WhatsApp</c>. The companion <c>CountryCode</c> field carries
    /// the resolved dial-code string (e.g. "+233") that gets persisted on
    /// the <c>Contact</c>. Kept separate from
    /// <c>CountryCode</c> so the dropdown can two-way bind by id while
    /// the create flow passes the dial-code string to the API.</summary>
    public Guid? CountryCodeCodedValueId { get; set; }

    /// <summary>The resolved dial-code string (e.g. "+233") for the selected
    /// <c>CountryCodeCodedValueId</c>, or null when no country code is
    /// selected / the channel is Email. Mirrors the <c>Contact.CountryCode</c>
    /// domain field and is passed through <c>GuardianAssignment</c> into
    /// <c>AddContactRequest.CountryCode</c> at create time.</summary>
    public string? CountryCode { get; set; }

    /// <summary>True when editing an existing assignment (affects button labels).</summary>
    public bool IsEdit { get; set; }

    public static GuardianAssignmentModel ForAdd() => new();

    public static GuardianAssignmentModel ForEdit(GuardianAssignment a) => new()
    {
        ExistingGuardianId = a.ExistingGuardianId,
        FirstName = a.FirstName,
        LastName = a.LastName,
        TitleCodedValueId = a.TitleCodedValueId,
        RelationshipCodedValueId = a.RelationshipCodedValueId,
        ContactChannel = a.ContactChannel ?? ContactChannel.Email,
        ContactValue = a.ContactValue,
        CountryCodeCodedValueId = null,
        CountryCode = a.CountryCode,
        IsEdit = true,
    };
}
