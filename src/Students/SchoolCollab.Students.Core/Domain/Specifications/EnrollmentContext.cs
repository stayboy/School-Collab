using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Domain.Specifications;

/// <summary>
/// Immutable input bundle passed to every <see cref="IEnrollmentSpecification"/>.
/// Carries the student being enrolled, the target grade level (with its age/gender
/// rules), the effective enrollment date, and the student's existing active
/// enrollments (cross-period) for the single-active rule.
/// </summary>
public sealed record EnrollmentContext(
    Student Student,
    GradeLevel GradeLevel,
    DateOnly EnrollmentDate,
    IReadOnlyList<StudentEnrollment> ExistingActiveEnrollments);