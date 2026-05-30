namespace SchoolCollab.CodedValues.Core.Domain.Exceptions;

public class DuplicateCodeException(string code)
    : DomainException($"A coded value with code '{code}' already exists.");
