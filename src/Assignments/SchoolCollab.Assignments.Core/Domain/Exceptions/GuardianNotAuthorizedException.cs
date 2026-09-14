namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-C1 — the guardian attempting to sign (or assigned by reassignment) is not
/// a linked <c>StudentGuardian</c> of the ward (cross-context students-api
/// validation via <c>IStudentDirectory</c>, decisions (e)/(f)). Routes map this
/// to <c>409 Conflict</c>.
/// </summary>
public sealed class GuardianNotAuthorizedException : Exception
{
    public GuardianNotAuthorizedException(Guid studentId, Guid guardianId)
        : base($"Guardian '{guardianId}' is not a linked guardian of student '{studentId}' and cannot act on this sign-off.") { }
}
