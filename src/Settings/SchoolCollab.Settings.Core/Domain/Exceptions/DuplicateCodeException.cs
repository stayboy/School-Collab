namespace SchoolCollab.Settings.Core.Domain.Exceptions;

public class DuplicateCodeException(string code, Guid? parentId)
    : DomainException(parentId.HasValue
        ? $"A coded value with code '{code}' already exists under parent '{parentId.Value}'."
        : $"A coded value with code '{code}' already exists as a root value.");
