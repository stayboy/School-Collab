namespace SchoolCollab.CodedValues.Core.Domain.Exceptions;

public class ConcurrencyException(Guid id)
    : DomainException($"Coded value '{id}' was modified by another user. Please reload and retry.");
