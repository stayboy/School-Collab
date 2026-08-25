namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>Dialog model for the new-enrollment dialog.</summary>
/// <param name="StudentId">Student to enroll (caller fills).</param>
/// <param name="SuggestedPeriodId">Pre-select this period if it exists (best-effort).</param>
/// <param name="SuggestedGradeLevelId">Pre-select this grade if it exists (best-effort).</param>
/// <param name="SuggestedStreamCodedValueId">Pre-select this stream (the student's current
/// enrollment stream, if any). Applied together with the grade suggestion so the
/// attribute-filtered stream picker shows the current value on re-enrollment.</param>
public sealed record EnrollStudentModel(
    Guid StudentId,
    Guid? SuggestedPeriodId = null,
    Guid? SuggestedGradeLevelId = null,
    Guid? SuggestedStreamCodedValueId = null);

/// <summary>Result of the new-enrollment dialog: whether the enrollment succeeded.</summary>
public sealed record EnrollStudentResult(bool Success);
