namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException() : base("The entity was modified by another user. Please refresh and try again.") { }
}