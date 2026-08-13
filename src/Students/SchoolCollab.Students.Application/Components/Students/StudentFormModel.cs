using System.ComponentModel.DataAnnotations;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Students.Application.Components.Pages.Students.GradeLevels;

namespace SchoolCollab.Students.Application.Components.Students;

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

    /// <summary>Optional salutation title (SALUTS parent). Bound to a CodedValueDropdown.</summary>
    public Guid? TitleCodedValueId { get; set; }

    /// <summary>Required. Bound to a FluentDatePicker in the shared form.</summary>
    [Required(ErrorMessage = "Date of birth is required.")]
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Required. Bound to a CodedValueDropdown for parent GENDER.</summary>
    [Required(ErrorMessage = "Gender is required.")]
    public Guid? GenderCodedValueId { get; set; }

    /// <summary>
    /// Guardians drafted on the create form (create mode only). Populated by the
    /// shared form's guardian section; the calling page links them after the
    /// student is created. Ignored in edit mode, where guardians are linked to
    /// the existing student immediately via the API.
    /// </summary>
    public List<GuardianAssignment> GuardianLinks { get; set; } = new();

    /// <summary>
    /// Contacts drafted on the create form (create mode only). Populated by the
    /// shared form's contacts section (<see cref="ContactsEditor"/> in Buffered
    /// mode); the calling page flushes them to the API as part of the atomic
    /// create. Ignored in edit mode, where the <see cref="ContactsEditor"/> runs
    /// in Live mode against the existing student's persisted contacts.
    /// </summary>
    public List<ContactModel> Contacts { get; set; } = new();
}
