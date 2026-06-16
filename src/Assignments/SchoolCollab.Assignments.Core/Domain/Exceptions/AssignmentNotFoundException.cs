namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

public sealed class AssignmentNotFoundException : Exception
{
    public AssignmentNotFoundException(Guid id) : base($"Assignment with ID '{id}' was not found.") { }
}