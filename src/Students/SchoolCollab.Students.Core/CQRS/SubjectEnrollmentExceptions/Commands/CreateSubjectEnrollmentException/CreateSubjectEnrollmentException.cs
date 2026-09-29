using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.CreateSubjectEnrollmentException;

/// <summary>
/// Creates a subject enrollment exception on exactly one owner — a grade level or an
/// activity group (subject-period-exception-model.md v3 §2). The body carries a
/// period <b>part</b> (<see cref="AcademicYearDivision"/>) plus a span; it carries
/// <b>no</b> period id — that is the point (§0 decision 7). At least one bound is
/// required; a body with neither is a 422.
///
/// <para><see cref="Ordinal"/> (v5 §0 decision 15) is the term/semester number the row
/// names — "not offered in the 3rd term" — and is <b>descriptive only</b>: availability
/// matching and the duplicate key both stay on the span, so it is optional, trailing, and
/// deliberately absent from <c>repository.ExistsAsync</c>. It is rejected (422) when it is
/// below 1 or when <see cref="Division"/> is a free window (<see cref="AcademicYearDivision.None"/>),
/// which has no position in a run of terms.</para>
/// </summary>
public sealed record CreateSubjectEnrollmentException(
    Guid? GradeLevelId,
    Guid? ActivityGroupId,
    Guid TopicId,
    AcademicYearDivision Division,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    string? Reason = null,
    int? Ordinal = null) : ICommand;
