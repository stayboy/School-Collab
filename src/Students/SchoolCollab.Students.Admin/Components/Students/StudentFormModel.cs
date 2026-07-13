using System.ComponentModel.DataAnnotations;

namespace SchoolCollab.Students.Admin.Components.Students;

/// <summary>
/// Shared form model for the student create / edit / inline-wizard flows. All
/// three sites (Create.razor, Edit.razor, the inline "new student" form in
/// GradeLevelWizard) bind to this single model so the field set, validation
/// rules, and CSS class names stay in lockstep.
///
/// Field types match the server contract (DateOnly? for the date-of-birth,
/// Guid? for the gender coded-value id) so the parent doesn't have to do
/// string parsing — the date picker and coded-value dropdown bind directly.
/// </summary>
public sealed class StudentFormModel
{
    [Required]
    public string? StudentNumber { get; set; }

    [Required]
    public string? FirstName { get; set; }

    [Required]
    public string? LastName { get; set; }

    [Required]
    [EmailAddress]
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Optional. Validated with <see cref="PhoneAttribute"/> when provided so
    /// obviously malformed values (e.g. "abc") are caught client-side before
    /// the round-trip to the server.
    /// </summary>
    [Phone]
    public string? ContactPhone { get; set; }

    /// <summary>Optional. Bound to a FluentDatePicker in the shared form.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Optional. Bound to a CodedValueDropdown for parent GENDER.</summary>
    public Guid? GenderCodedValueId { get; set; }
}
