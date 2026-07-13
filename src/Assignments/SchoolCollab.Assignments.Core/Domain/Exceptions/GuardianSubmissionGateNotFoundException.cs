namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

public sealed class GuardianSubmissionGateNotFoundException : Exception
{
    public GuardianSubmissionGateNotFoundException(Guid gateId)
        : base($"Guardian submission gate with ID '{gateId}' was not found.") { }

    public GuardianSubmissionGateNotFoundException(Guid assignmentId, Guid studentId)
        : base($"Guardian submission gate for assignment '{assignmentId}' and student '{studentId}' was not found.") { }
}
