namespace SchoolCollab.Students.Core.Domain.Exceptions;

public sealed class StudentNotFoundException : Exception
{
    public Guid StudentId { get; }
    public string? StudentNumber { get; }

    public StudentNotFoundException(Guid id) : base($"Student with ID '{id}' was not found.")
        => StudentId = id;

    public StudentNotFoundException(string studentNumber) : base($"Student with number '{studentNumber}' was not found.")
        => StudentNumber = studentNumber;
}

public sealed class GradeLevelNotFoundException : Exception
{
    public Guid GradeLevelId { get; }

    public GradeLevelNotFoundException(Guid id) : base($"Grade level with ID '{id}' was not found.")
        => GradeLevelId = id;
}

public sealed class SubjectNotFoundException : Exception
{
    public Guid SubjectId { get; }

    public SubjectNotFoundException(Guid id) : base($"Subject with ID '{id}' was not found.")
        => SubjectId = id;
}

public sealed class PeriodNotFoundException : Exception
{
    public Guid PeriodId { get; }

    public PeriodNotFoundException(Guid id) : base($"Period with ID '{id}' was not found.")
        => PeriodId = id;
}

public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(string entityName, Guid id)
        : base($"Concurrency conflict when updating {entityName} with ID '{id}'. The entity was modified by another user.")
    { }

    public ConcurrencyException(Guid id)
        : this("entity", id) { }
}

public sealed class DuplicateStudentNumberException : Exception
{
    public string StudentNumber { get; }

    public DuplicateStudentNumberException(string studentNumber)
        : base($"A student with number '{studentNumber}' already exists.")
        => StudentNumber = studentNumber;
}

public sealed class DuplicateSubjectCodeException : Exception
{
    public string Code { get; }

    public DuplicateSubjectCodeException(string code)
        : base($"A subject with code '{code}' already exists.")
        => Code = code;
}