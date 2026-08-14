using System.ComponentModel.DataAnnotations;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Students.Application.Components.Pages.Students.GradeLevels;
using SchoolCollab.Students.Application.Services;

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

    /// <summary>
    /// Projects an app-level <see cref="StudentDto"/> (the API projection
    /// returned by <c>GetStudentByIdAsync</c>) into a brand-new, fully-populated
    /// <see cref="StudentFormModel"/>. Use when the caller owns the model
    /// (tests, or a page that swaps the whole model rather than mutating one).
    /// Collection state (<see cref="GuardianLinks"/>, <see cref="Contacts"/>)
    /// is not copied from the DTO — it starts empty.
    /// </summary>
    public static StudentFormModel From(StudentDto student)
    {
        var model = new StudentFormModel();
        model.LoadFrom(student);
        return model;
    }

    /// <summary>
    /// Loads this model's profile fields from a <see cref="StudentDto"/> in
    /// place. Use when the caller holds a <c>readonly</c> model instance it
    /// must populate after the async load (the edit dialog / edit page keep
    /// <c>_model</c> as a <c>readonly</c> field and mutate it after the load).
    /// Collection state is left untouched.
    /// </summary>
    public void LoadFrom(StudentDto student)
    {
        StudentNumber = student.StudentNumber;
        FirstName = student.FirstName;
        LastName = student.LastName;
        DateOfBirth = student.DateOfBirth;
        GenderCodedValueId = student.GenderCodedValueId;
        TitleCodedValueId = student.TitleCodedValueId;
    }
}
