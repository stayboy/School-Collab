namespace SchoolCollab.Students.Admin.Components.Students;

/// <summary>Dialog model for the withdraw-enrollment dialog.</summary>
/// <param name="EnrollmentId">The active enrollment to close.</param>
/// <param name="StudentName">For display in the dialog body / confirmation.</param>
/// <param name="GradeName">For display in the dialog body / confirmation.</param>
/// <param name="DefaultExitDate">Initial value for the exit-date picker (today).</param>
public sealed record WithdrawEnrollmentModel(
    Guid EnrollmentId,
    string? StudentName = null,
    string? GradeName = null,
    DateOnly? DefaultExitDate = null);

/// <summary>Result of the withdraw-enrollment dialog.</summary>
public sealed record WithdrawEnrollmentResult(bool Success);
