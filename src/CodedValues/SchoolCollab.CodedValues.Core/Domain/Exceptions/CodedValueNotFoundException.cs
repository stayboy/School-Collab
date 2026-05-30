namespace SchoolCollab.CodedValues.Core.Domain.Exceptions;

public class CodedValueNotFoundException(Guid id)
    : DomainException($"Coded value with id '{id}' was not found.");
