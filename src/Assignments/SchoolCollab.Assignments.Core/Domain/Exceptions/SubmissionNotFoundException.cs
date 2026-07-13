namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

public sealed class SubmissionNotFoundException : Exception
{
    public SubmissionNotFoundException(Guid submissionId)
        : base($"Assignment submission with ID '{submissionId}' was not found.") { }
}
