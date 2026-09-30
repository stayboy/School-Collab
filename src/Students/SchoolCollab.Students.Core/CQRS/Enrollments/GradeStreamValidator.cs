using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Enrollments;

/// <summary>
/// The one implementation of FR-9 stream validation: a stream is valid for a
/// grade when the grade offers it — i.e. a <see cref="GradeStreamAssignment"/>
/// bridge row exists for (grade, stream coded value).
/// </summary>
/// <remarks>
/// Shared by all three enrollment write paths
/// (<c>EnrollStudentHandler</c>, <c>TransferStudentHandler</c>,
/// <c>CreateStudentWithLinkedDataHandler</c>) so the rule cannot drift. The
/// lookup is a Students-database read with the strict tenant filter applied —
/// the ad-hoc Settings HTTP hop + <c>gradeLevel</c> attribute string-parse this
/// replaces is gone.
/// </remarks>
internal static class GradeStreamValidator
{
    /// <exception cref="StreamGradeMismatchException">The grade does not offer the stream.</exception>
    public static async Task ValidateAsync(
        IGradeStreamAssignmentRepository repository,
        Domain.GradeLevel gradeLevel,
        Guid streamCodedValueId,
        CancellationToken cancellationToken)
    {
        var offered = await repository.ExistsAsync(gradeLevel.Id, streamCodedValueId, cancellationToken);
        if (!offered)
        {
            throw new StreamGradeMismatchException(streamCodedValueId, gradeLevel.Id);
        }
    }
}
