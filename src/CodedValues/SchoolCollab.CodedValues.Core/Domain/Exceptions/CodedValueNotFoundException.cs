namespace SchoolCollab.CodedValues.Core.Domain.Exceptions;

public class CodedValueNotFoundException : DomainException
{
    public CodedValueNotFoundException(Guid id)
        : base($"Coded value with id '{id}' was not found.") { }

    public CodedValueNotFoundException(string identifier)
        : base($"Coded value '{identifier}' was not found.") { }
}
