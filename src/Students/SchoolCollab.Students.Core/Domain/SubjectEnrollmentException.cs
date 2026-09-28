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

    /// <summary>First day excepted (inclusive). Null = open start.</summary>
    public DateOnly? StartDate { get; private set; }

    /// <summary>Last day excepted (inclusive). Null = open end.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>Optional free text, e.g. "teacher on leave". Never load-bearing.</summary>
    public string? Reason { get; private set; }

    /// <summary>PostgreSQL <c>xmin</c> row version (optimistic concurrency).</summary>
    public uint RowVersion { get; private set; }

    /// <summary>
    /// Creates an exception. Enforces the three §2.2 invariants: exactly one of
    /// <paramref name="gradeLevelId"/> / <paramref name="activityGroupId"/> (the
    /// bridge owner rule), <b>at least one</b> span bound (an unbounded exception
    /// would mean "never offered", a different concept), and
    /// <paramref name="endDate"/> &gt;= <paramref name="startDate"/> when both are set.
    /// </summary>
    public static SubjectEnrollmentException Create(
        Guid tenantId,
        Guid? gradeLevelId,
        Guid? activityGroupId,
        Guid topicId,
        AcademicYearDivision division,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? reason = null)
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

        var now = DateTimeOffset.UtcNow;
        return new SubjectEnrollmentException
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GradeLevelId = gradeLevelId,
            ActivityGroupId = activityGroupId,
            TopicId = topicId,
            Division = division,
            StartDate = startDate,
            EndDate = endDate,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
