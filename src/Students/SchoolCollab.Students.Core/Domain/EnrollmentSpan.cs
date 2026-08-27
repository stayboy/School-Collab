namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Rev. 3/4 enrollment span of an <see cref="ActivityGroup"/>
/// (spec activity-group-enrollment.md FR-42). Immutable after creation — the
/// span's granularity cannot change without invalidating existing memberships.
/// </summary>
public enum EnrollmentSpan
{
    /// <summary>Enrollment derives its window from the active AcademicYear Period.</summary>
    WholeAcademicYear = 0,

    /// <summary>Enrollment derives its window from an active Term Period.</summary>
    Termly = 1,

    /// <summary>Enrollment derives its window from an active Semester Period.</summary>
    Semester = 2,

    /// <summary>A bounded, admin-defined window [EnrollmentStartDate, EnrollmentEndDate]
    /// not tied to any academic Period. Memberships carry window_start/end_date.</summary>
    DateRange = 3,

    /// <summary>An open interval: EnrollmentStartDate and EnrollmentEndDate both null,
    /// memberships continuous until the member exits/removed or the group is turned off.
    /// No window end and no rollover (FR-44/48).</summary>
    OpenEnded = 4
}