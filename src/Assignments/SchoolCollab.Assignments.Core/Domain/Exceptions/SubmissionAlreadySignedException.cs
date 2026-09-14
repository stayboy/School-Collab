namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-C1 / spec §6 NFR line 115 (idempotent signature handling) — a second
/// sign-off attempt on a submission whose <c>SignOffState</c> is already
/// <c>Signed</c>. The command handler checks BEFORE creating any event row so a
/// duplicate signature event can never be written; the unique
/// <c>ix_signature_events_assignment_student</c> index is the DB backstop.
/// Routes map this to <c>409 Conflict</c>.
/// </summary>
public sealed class SubmissionAlreadySignedException : Exception
{
    public SubmissionAlreadySignedException(Guid studentId)
        : base($"Submission for student '{studentId}' has already been signed and cannot be signed again.") { }
}
