using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.CreateSubjectEnrollmentExceptions;

/// <summary>
/// Bulk-creates subject enrollment exceptions — one row per <see cref="Item"/>, written in a
/// single transaction (subject-period-exception-model.md v6 §11.3, decision 18).
///
/// <para><b>Why a set of items rather than a set of ordinals.</b> Each sequence carries its OWN
/// span: the period holding position 1 and the period holding position 3 have different dates, and
/// a sequence whose position has no period behind it carries dates the reader typed. A single
/// shared span could not express either. So the batch is a list of
/// <see cref="CreateSubjectEnrollmentExceptionItem"/>, and a "bulk insert of one" is a one-item
/// list — which is exactly what the page always sends.</para>
///
/// <para><b>What the batch shares.</b> Owner (grade level XOR activity group), topic, division and
/// reason belong to the action rather than to an individual sequence, so they sit here once. That is
/// also why the division is validated once while the <i>span shape</i> and the <i>ordinal</i> are
/// validated per item: those two are the only per-sequence rules.</para>
///
/// <para>Nothing here consults a period — the span is supplied, not derived (§0 decision 7), and
/// the ordinal remains descriptive (§0 decision 15).</para>
/// </summary>
public sealed record CreateSubjectEnrollmentExceptions(
    Guid? GradeLevelId,
    Guid? ActivityGroupId,
    Guid TopicId,
    AcademicYearDivision Division,
    IReadOnlyList<CreateSubjectEnrollmentExceptionItem> Items,
    string? Reason = null) : ICommand;

/// <summary>
/// One sequence in a bulk create: the span it covers and, when the division names a real part, the
/// position it names.
/// </summary>
/// <param name="StartDate">The span's start. Null is an open start (an accepted, meaningful
/// bound — §0 decision 8), not a missing value. At least one of the two bounds must be present.</param>
/// <param name="EndDate">The span's end. Null is an open end.</param>
/// <param name="Ordinal">The term/semester number this sequence is, or null on a free window
/// (<see cref="AcademicYearDivision.None"/>). It is <b>descriptive</b>: availability never reads it,
/// it is not part of the duplicate key, and it is deliberately NOT cross-checked against the span —
/// v6 hides the span when it is derived, so the two are expected to agree, but a tenant that has not
/// periodised its years may still record a nameable position against typed dates.</param>
public sealed record CreateSubjectEnrollmentExceptionItem(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    int? Ordinal = null);
