using System.ComponentModel.DataAnnotations;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Students.Application.Components.Pages.Students.GradeLevels;
using SchoolCollab.Students.Application.Services;
using StudentGuardianViewDto = SchoolCollab.Students.Core.DTOs.StudentGuardianViewDto;
using ContactDto = SchoolCollab.Students.Core.DTOs.ContactDto;

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

    /// <summary>Postgres xmin row version captured at load, echoed back as
    /// <c>ExpectedRowVersion</c> on save (optimistic concurrency).</summary>
    public uint RowVersion { get; set; }

    /// <summary>Guardian-link guardian-ids the client saw at load, echoed back so the
    /// server can detect a guardian added/removed by another user since then.</summary>
    public Guid[] LoadedGuardianIds { get; set; } = [];

    /// <summary>Contact-ids the client saw at load, echoed back so the server can detect
    /// a contact added/removed by another user since then.</summary>
    public Guid[] LoadedContactIds { get; set; } = [];

    /// <summary>
    /// All-inclusive load: profile + guardians + contacts from the relevant DTOs, plus the
    /// concurrency snapshot (<see cref="RowVersion"/>, <see cref="LoadedGuardianIds"/>,
    /// <see cref="LoadedContactIds"/>) so <see cref="ToUpdateRequest"/> can echo them back.
    /// Used by the all-inclusive edit dialog.
    /// </summary>
    public void LoadFrom(
        StudentDto student,
        IReadOnlyList<StudentGuardianViewDto> guardians,
        IReadOnlyList<ContactDto> contacts)
    {
        LoadFrom(student);
        RowVersion = student.RowVersion;
        LoadedGuardianIds = guardians.Select(g => g.GuardianId).ToArray();
        LoadedContactIds = contacts.Select(c => c.Id).ToArray();
        GuardianLinks = guardians.Select(ToGuardianAssignment).ToList();
        Contacts = contacts.Select(ToContactModel).ToList();
    }

    /// <summary>
    /// Projects this model back to an <see cref="UpdateStudentWithLinkedDataRequest"/> for
    /// the all-inclusive edit save (profile + guardians + contacts + concurrency snapshot).
    /// </summary>
    public UpdateStudentWithLinkedDataRequest ToUpdateRequest()
    {
        return new UpdateStudentWithLinkedDataRequest(
            FirstName!, LastName!, DateOfBirth, GenderCodedValueId, TitleCodedValueId,
            ExpectedRowVersion: RowVersion,
            Guardians: GuardianLinks.Select(ToGuardianDraft).ToArray(),
            Contacts: Contacts.Select(ToContactDraft).ToArray(),
            LoadedGuardianIds: LoadedGuardianIds,
            LoadedContactIds: LoadedContactIds);
    }

    private static GuardianAssignment ToGuardianAssignment(StudentGuardianViewDto g) => new(
        g.GuardianId, g.FirstName, g.LastName, g.RelationshipCodedValueId,
        ContactChannel: null, ContactValue: null, TitleCodedValueId: g.TitleCodedValueId,
        Role: g.Role, IsEmergencyContact: g.IsEmergencyContact);

    private static ContactModel ToContactModel(ContactDto c) => new()
    {
        Channel = c.Channel,
        Value = c.Value,
        Label = c.Label,
        CountryCode = c.CountryCode,
        Order = c.DisplayOrder,
        PersistedId = c.Id
    };

    private static GuardianDraftRequest ToGuardianDraft(GuardianAssignment g) => new(
        g.ExistingGuardianId, g.Role, g.RelationshipCodedValueId, g.IsEmergencyContact,
        TitleCodedValueId: g.TitleCodedValueId, FirstName: g.FirstName, LastName: g.LastName);

    private static ContactDraftRequest ToContactDraft(ContactModel c) => new(
        c.Channel, c.Value, c.Label, c.CountryCode, c.Order, Id: c.PersistedId);
}
