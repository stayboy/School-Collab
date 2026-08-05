namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>Dialog model for the new-enrollment dialog.</summary>
/// <param name="StudentId">Student to enroll (caller fills).</param>
/// <param name="SuggestedPeriodId">Pre-select this period if it exists (best-effort).</param>
/// <param name="SuggestedGradeLevelId">Pre-select this grade if it exists (best-effort).</param>
public sealed record EnrollStudentModel(
    Guid StudentId,
    Guid? SuggestedPeriodId = null,
    Guid? SuggestedGradeLevelId = null);

/// <summary>Result of the new-enrollment dialog: whether the enrollment succeeded.</summary>
public sealed record EnrollStudentResult(bool Success);
