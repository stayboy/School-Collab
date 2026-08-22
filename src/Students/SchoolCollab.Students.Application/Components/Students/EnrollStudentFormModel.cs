using System.ComponentModel.DataAnnotations;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>
/// Form model for the new-enrollment dialog (bound by
/// <c>EnrollStudentDialog.razor</c>). Extracted from the component's
/// <c>@code</c> block into a standalone public class per the repo-wide
/// DTO → form-model convention (documents/solution/dto-form-model-mapping.md):
/// every user-editable field lives on this ONE reference-type object that
/// the <c>EditForm</c> binds, instead of scattered primitive component
/// fields.
///
/// Field types match the server contract (<see cref="Guid"/>? for the
/// grade/stream coded-value ids, <see cref="DateOnly"/>? for the enrolled-on
/// date) so the dropdowns bind directly; the FluentDatePicker is bridged to
/// <c>DateTime?</c> in the dialog (the picker API only takes DateTime?).
/// </summary>
public sealed class EnrollStudentFormModel
{
    /// <summary>
    /// The CodedValueId picked in the grade <c>CodedValueDropdown</c> (GRADES
    /// parent) — the single selection field for BOTH feature-flag paths (the
    /// dialog renders one shared picker). The CodedValueId → GradeLevelId
    /// join (and auto-materialize + blocked enforcement) happens server-side
    /// in <c>EnrollStudentHandler</c>.
    /// </summary>
    public Guid? GradeCodedValueId { get; set; }

    /// <summary>
    /// Optional stream (GRSTREAMS child) for the enrollment. The picker is
    /// attribute-filtered by the selected grade's gradeLevel coded value, so
    /// a stream from a previous grade is never carried across a grade change.
    /// </summary>
    public Guid? StreamCodedValueId { get; set; }

    /// <summary>
    /// Required enrollment date. Defaults to today (UTC) so the common case
    /// needs no edit.
    /// </summary>
    [Required(ErrorMessage = "Pick an enrolled-on date before enrolling.")]
    public DateOnly? EnrolledOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// Projects the caller's suggested grade (the student's current
    /// active-enrollment grade, if any) into a brand-new, fully-populated
    /// model. Use when the caller owns the model (tests, or a page that
    /// swaps the whole model rather than mutating one).
    /// </summary>
    public static EnrollStudentFormModel From(GradeLevelDto? suggestedGrade)
    {
        var model = new EnrollStudentFormModel();
        model.LoadFrom(suggestedGrade);
        return model;
    }

    /// <summary>
    /// Pre-selects the suggested grade (the student's current active-enrollment
    /// grade, if any) on this existing model by its coded value. A null
    /// suggestion (new enrollment) is a no-op.
    /// </summary>
    public void LoadFrom(GradeLevelDto? suggestedGrade)
    {
        if (suggestedGrade is null) return;
        GradeCodedValueId = suggestedGrade.CodedValueId;
    }

    /// <summary>
    /// Projects the model onto the enrollment wire request. The student and
    /// period ids are dialog-level inputs (not user-editable fields), so they
    /// arrive as arguments; the grade travels as its coded value id and is
    /// resolved (or materialized) server-side.
    /// </summary>
    public EnrollStudentRequest ToEnrollRequest(Guid studentId, Guid periodId)
        => new(studentId, periodId, GradeCodedValueId!.Value, StreamCodedValueId, EnrolledOn);
}
