using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// One exception: a topic is NOT offered for a span of time, expressed as a period
/// <b>part</b> (<see cref="AcademicYearDivision.Terms"/>/<see cref="AcademicYearDivision.Semesters"/>)
/// or a plain window (<see cref="AcademicYearDivision.None"/>)
/// (subject-period-exception-model.md v3 §2.1). The absence of an exception is the
/// normal, expected state — a bridge row
/// (<see cref="GradeTopicAssignment"/> / <see cref="ActivityGroupTopicAssignment"/>)
/// means the subject is offered.
///
/// <para>There is deliberately <b>no</b> <c>PeriodId</c> (§0 decision 7): a period
/// <i>instance</i> is year-specific, so an exception pinned to "2026 Term 3" would
/// silently fail to cover "2027 Term 3". <see cref="Division"/> names the <b>kind</b>
/// of span and never participates in availability matching (§2.3); the date test
/// does. With no period reference anywhere, v1's retired-period rules dissolve —
/// there is no period to orphan, archive or retire.</para>
///
/// <para>There is also no status/lifecycle column: an exception is immutable history.
/// Removing it soft-deletes the row (this type derives from
/// <see cref="BaseTenantEntityWithAudit"/>, which supplies <c>is_deleted</c> plus the
/// audit stamps, and implements <see cref="IHasRowVersion"/> for PostgreSQL
/// <c>xmin</c>). Uniqueness is enforced by the two COALESCE expression indexes the
/// migration creates in raw SQL (§7), which the <c>is_deleted = false</c> filter makes
/// remove-then-re-add legal.</para>
/// </summary>
public sealed class SubjectEnrollmentException : BaseTenantEntityWithAudit, IHasRowVersion
{
    private SubjectEnrollmentException() { }

    /// <summary>Grade-owned exception. Mutually exclusive with <see cref="ActivityGroupId"/>.</summary>
    public Guid? GradeLevelId { get; private set; }

    /// <summary>Group-owned exception. Mutually exclusive with <see cref="GradeLevelId"/>.</summary>
    public Guid? ActivityGroupId { get; private set; }

    /// <summary>The shared, global topic (subject) this exception applies to.</summary>
    public Guid TopicId { get; private set; }

    /// <summary>
    /// The period PART this exception is expressed in — a kind, never a period
    /// instance. <see cref="AcademicYearDivision.None"/> means the academic year
    /// itself, or a free window not expressed as a term/semester.
    /// </summary>
    public AcademicYearDivision Division { get; private set; }

    /// <summary>
    /// Which run of the <see cref="Division"/> this exception names — 1 for the first
    /// term/semester, 2 for the second, and so on
    /// (subject-period-exception-model.md v5 §0 decision 15). Set when the exception was
    /// written as "not offered in the 3rd term", so the row reads back that way without
    /// anyone re-deriving an ordinal from its dates or its name.
    ///
    /// <para><b>Requires a part.</b> A <see cref="AcademicYearDivision.None"/> exception is
    /// a free window, and a free window has no position in a run of terms, so the two
    /// cannot be combined. Enforced in the entity and at the API boundary.</para>
    ///
    /// <para><b>It is descriptive; the dates are the truth.</b> Availability matching never
    /// reads it (§2.3) — that is the whole point of §0 decision 7, and it is why an
    /// exception's ordinal and its dates are ALLOWED to disagree (the 3rd term of a year
    /// the tenant has not periodised, or a subset of one term). The consequence is
    /// deliberate and is the cost of decision 15: the ordinal is a second statement about
    /// the same span, and only the dates are authoritative. The duplicate check and the
    /// unique expression index therefore <b>exclude</b> the ordinal — two exceptions with
    /// the same owner, topic, division and span are the same row whatever they claim.</para>
    /// </summary>
    public int? Ordinal { get; private set; }

    /// <summary>First day excepted (inclusive). Null = open start.</summary>
    public DateOnly? StartDate { get; private set; }

    /// <summary>Last day excepted (inclusive). Null = open end.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>Optional free text, e.g. "teacher on leave". Never load-bearing.</summary>
    public string? Reason { get; private set; }

    /// <summary>PostgreSQL <c>xmin</c> row version (optimistic concurrency).</summary>
    public uint RowVersion { get; private set; }

    /// <summary>
    /// Creates an exception. Enforces the §2.2 invariants: exactly one of
    /// <paramref name="gradeLevelId"/> / <paramref name="activityGroupId"/> (the
    /// bridge owner rule), <b>at least one</b> span bound (an unbounded exception
    /// would mean "never offered", a different concept),
    /// <paramref name="endDate"/> &gt;= <paramref name="startDate"/> when both are set,
    /// and — since v5 — that <paramref name="ordinal"/> is a 1-based position which
    /// requires <paramref name="division"/> to name a real part.
    /// </summary>
    public static SubjectEnrollmentException Create(
        Guid tenantId,
        Guid? gradeLevelId,
        Guid? activityGroupId,
        Guid topicId,
        AcademicYearDivision division,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? reason = null,
        int? ordinal = null)
    {
        var hasGrade = gradeLevelId is not null;
        var hasGroup = activityGroupId is not null;
        if (hasGrade == hasGroup)
        {
            throw new ArgumentException(
                "A subject enrollment exception must target exactly one owner: either a grade level or an activity group.",
                hasGrade ? nameof(activityGroupId) : nameof(gradeLevelId));
        }

        if (topicId == Guid.Empty)
            throw new ArgumentException("A subject enrollment exception requires a topic.", nameof(topicId));

        if (startDate is null && endDate is null)
        {
            throw new ArgumentException(
                "A subject enrollment exception requires at least one bound: an exception with no start and no end would mean \"never offered\", which is a different concept.",
                nameof(startDate));
        }

        if (startDate is { } start && endDate is { } end && end < start)
        {
            throw new ArgumentException(
                $"A subject enrollment exception's EndDate ('{end:yyyy-MM-dd}') must be on or after its StartDate ('{start:yyyy-MM-dd}').",
                nameof(endDate));
        }

        if (ordinal is < 1)
        {
            throw new ArgumentException(
                "A subject enrollment exception's ordinal must be null or 1 or greater (it is a 1-based position: 1st term, 2nd term, …).",
                nameof(ordinal));
        }

        if (ordinal.HasValue && division == AcademicYearDivision.None)
        {
            throw new ArgumentException(
                "A subject enrollment exception can name an ordinal only when its division names a real part: a free window (" +
                $"{AcademicYearDivision.None}) has no position in a run of terms or semesters.",
                nameof(ordinal));
        }

        var now = DateTimeOffset.UtcNow;
        return new SubjectEnrollmentException
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GradeLevelId = gradeLevelId,
            ActivityGroupId = activityGroupId,
            TopicId = topicId,
            Division = division,
            Ordinal = ordinal,
            StartDate = startDate,
            EndDate = endDate,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
