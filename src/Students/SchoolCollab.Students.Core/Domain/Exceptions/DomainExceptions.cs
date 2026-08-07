using System;

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

public sealed class TopicNotFoundException : Exception
{
    public Guid TopicId { get; }

    public TopicNotFoundException(Guid id) : base($"Topic with ID '{id}' was not found.")
        => TopicId = id;
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

public sealed class DuplicateTopicCodeException : Exception
{
    public string Code { get; }

    public DuplicateTopicCodeException(string code)
        : base($"A topic with code '{code}' already exists.")
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

/// <summary>
/// Thrown when a subject or grade-level link already exists for a teacher
/// (spec §4.12). Mirrors <see cref="GuardianLinkAlreadyExistsException"/>.
/// </summary>
public sealed class TeacherLinkAlreadyExistsException : Exception
{
    public Guid TeacherId { get; }
    public Guid RefId { get; }

    public TeacherLinkAlreadyExistsException(Guid teacherId, Guid refId)
        : base($"A link already exists between teacher '{teacherId}' and '{refId}'.")
    {
        TeacherId = teacherId;
        RefId = refId;
    }
}

/// <summary>
/// Thrown when a subject or grade-level link does not exist for a teacher
/// (spec §4.12). Mirrors <see cref="GuardianLinkNotFoundException"/>.
/// </summary>
public sealed class TeacherLinkNotFoundException : Exception
{
    public Guid TeacherId { get; }
    public Guid RefId { get; }

    public TeacherLinkNotFoundException(Guid teacherId, Guid refId)
        : base($"No link exists between teacher '{teacherId}' and '{refId}'.")
    {
        TeacherId = teacherId;
        RefId = refId;
    }
}

/// <summary>
/// Thrown when there is a constraint violation in a GradeLevel entity itself,
/// such as MinAge being greater than MaxAge. Distinct from
/// <see cref="EnrollmentValidationException"/> (which guards the *enrollment*
/// act against a grade level's rules).
/// </summary>
public sealed class GradeLevelConstraintException : Exception
{
    public GradeLevelConstraintException(string message) : base(message) { }
}

// ── Enrollment validation (plan §4) ──────────────────────────────────────

/// <summary>
/// Base type for enrollment validation failures. Carries the student and grade
/// level involved so the API/UI can render actionable, context-rich messages
/// (mirrors the <see cref="PeriodNotOpenException"/> style).
/// </summary>
public abstract class EnrollmentValidationException : Exception
{
    public Guid StudentId { get; }
    public Guid GradeLevelId { get; }

    protected EnrollmentValidationException(Guid studentId, Guid gradeLevelId, string message)
        : base(message)
    {
        StudentId = studentId;
        GradeLevelId = gradeLevelId;
    }
}

/// <summary>
/// Thrown when a student's age (computed from DOB vs. enrollment date) falls
/// outside the grade level's <c>[MinAge, MaxAge]</c>. Message names student,
/// grade, DOB, computed age, and the required range.
/// </summary>
public sealed class StudentAgeViolationException : EnrollmentValidationException
{
    public int CalculatedAge { get; }
    public int? MinAge { get; }
    public int? MaxAge { get; }
    public DateOnly DateOfBirth { get; }
    public DateOnly EnrollmentDate { get; }

    public StudentAgeViolationException(
        Guid studentId,
        Guid gradeLevelId,
        int calculatedAge,
        int? minAge,
        int? maxAge,
        DateOnly dateOfBirth,
        DateOnly enrollmentDate)
        : base(studentId, gradeLevelId,
            $"Student (ID: {studentId}) is {calculatedAge} years old (DOB: {dateOfBirth:yyyy-MM-dd}), " +
            $"but grade level requires age within [{(minAge is null ? "any" : minAge.ToString())}, " +
            $"{(maxAge is null ? "any" : maxAge.ToString())}] (enrolled: {enrollmentDate:yyyy-MM-dd}).")
    {
        CalculatedAge = calculatedAge;
        MinAge = minAge;
        MaxAge = maxAge;
        DateOfBirth = dateOfBirth;
        EnrollmentDate = enrollmentDate;
    }
}

/// <summary>
/// Thrown when a student's gender does not match the grade level's
/// <see cref="Domain.GradeLevel.AllowedGenderCodedValueId"/> (when set).
/// Message names student, grade, the required gender coded value id, and the
/// student's gender coded value id.
/// </summary>
public sealed class StudentGenderViolationException : EnrollmentValidationException
{
    public Guid? AllowedGenderCodedValueId { get; }
    public Guid? StudentGenderCodedValueId { get; }

    public StudentGenderViolationException(
        Guid studentId,
        Guid gradeLevelId,
        Guid? allowedGenderCodedValueId,
        Guid? studentGenderCodedValueId)
        : base(studentId, gradeLevelId,
            $"Student (ID: {studentId}) gender ({(studentGenderCodedValueId is null ? "unspecified" : studentGenderCodedValueId.ToString())}) " +
            $"does not match the allowed gender ({(allowedGenderCodedValueId is null ? "none" : allowedGenderCodedValueId.ToString())}) " +
            $"for grade level '{gradeLevelId}'.")
    {
        AllowedGenderCodedValueId = allowedGenderCodedValueId;
        StudentGenderCodedValueId = studentGenderCodedValueId;
    }
}

/// <summary>
/// Thrown when a student already holds one or more active enrollments and a new
/// enrollment is attempted. Message names the student and the existing active
/// enrollment id(s). The single-active rule is cross-period.
/// </summary>
public sealed class MultipleActiveEnrollmentsException : EnrollmentValidationException
{
    public IReadOnlyList<Guid> ExistingActiveEnrollmentIds { get; }

    public MultipleActiveEnrollmentsException(
        Guid studentId,
        Guid gradeLevelId,
        IReadOnlyList<Guid> existingActiveEnrollmentIds)
        : base(studentId, gradeLevelId,
            $"Student (ID: {studentId}) already has {existingActiveEnrollmentIds.Count} active enrollment(s): " +
            $"{string.Join(", ", existingActiveEnrollmentIds)}. Withdraw or transfer the existing enrollment before enrolling again.")
    {
        ExistingActiveEnrollmentIds = existingActiveEnrollmentIds;
    }
}