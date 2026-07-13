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

/// <summary>
/// Thrown when an operation requires a current period (one whose
/// [StartDate, EndDate] contains today) but none exists. See spec §5.3 / §6.3 / §8.1.
/// </summary>
public sealed class NoCurrentPeriodException : Exception
{
    public NoCurrentPeriodException(string message) : base(message) { }
}

/// <summary>
/// Thrown when an operation requires an Active (open) period but none exists
/// for the current tenant, or when an enrollment targets a non-active period.
/// See spec active-period-per-tenancy.md (FR-A3).
/// </summary>
public sealed class PeriodNotOpenException : Exception
{
    public PeriodNotOpenException(string message) : base(message) { }
}

public sealed class GuardianNotFoundException : Exception
{
    public Guid GuardianId { get; }

    public GuardianNotFoundException(Guid id) : base($"Guardian with ID '{id}' was not found.")
        => GuardianId = id;
}

public sealed class GuardianLinkNotFoundException : Exception
{
    public Guid StudentId { get; }
    public Guid GuardianId { get; }

    public GuardianLinkNotFoundException(Guid studentId, Guid guardianId)
        : base($"No link exists between student '{studentId}' and guardian '{guardianId}'.")
    {
        StudentId = studentId;
        GuardianId = guardianId;
    }
}

public sealed class GuardianLinkAlreadyExistsException : Exception
{
    public Guid StudentId { get; }
    public Guid GuardianId { get; }

    public GuardianLinkAlreadyExistsException(Guid studentId, Guid guardianId)
        : base($"A link already exists between student '{studentId}' and guardian '{guardianId}'.")
    {
        StudentId = studentId;
        GuardianId = guardianId;
    }
}

public sealed class ContactNotFoundException : Exception
{
    public Guid ContactId { get; }

    public ContactNotFoundException(Guid id) : base($"Contact with ID '{id}' was not found.")
        => ContactId = id;
}

public sealed class TeacherNotFoundException : Exception
{
    public Guid TeacherId { get; }

    public TeacherNotFoundException(Guid id) : base($"Teacher with ID '{id}' was not found.")
        => TeacherId = id;
}