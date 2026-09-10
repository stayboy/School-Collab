namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

public sealed class AssignmentQuestionValidationException : Exception
{
    public AssignmentQuestionValidationException(string message) : base(message) { }
}
