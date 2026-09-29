using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments;

/// <summary>
/// Shared period-scope validation for topic assignments (Rev. 6 FR-56/57,
/// AC-44..46, EC-23/24). Only the update handler
/// (<c>UpdateTopicAssignmentPeriod</c>) still calls the two bridge validators: the two
/// create paths and <c>CreateTopicForGrade</c> accept and ignore <c>PeriodId</c> from
/// 2026-09-26 (subject-period-exception-model.md — the exception model retires the
/// bridge's period meaning).
///
/// <para>The exception-side validators at the bottom of this class carry the v3
/// <b>exception shape</b> rules for <c>SubjectEnrollmentException</c>: the span
/// invariants (§2.2) and the group-owned FR-56 division matrix (§4.2). There is
/// deliberately <b>no</b> grade-side rule (FR-57 is unconstrained) and no
/// retired-period or active-academic-year rule — an exception never references a
/// period instance, so neither rule has anything to test.</para>
/// </summary>
public static class TopicAssignmentPeriodValidator
{
    /// <summary>
    /// FR-57: a grade-owned topic's <paramref name="periodId"/>, when set, must be
    /// a top-level academic year or a Term/Semester within the tenant's active
    /// academic year. Null = year-spanning date-based delivery (back-compat).
    /// </summary>
    public static async Task ValidateGradePeriodAsync(
        Guid? periodId,
        IPeriodRepository periodRepository,
        CancellationToken cancellationToken = default)
    {
        if (periodId is null)
            return; // null = year-spanning date-based delivery (back-compat).

        var period = await periodRepository.GetAsync(periodId.Value, cancellationToken)
            ?? throw new TopicAssignmentPeriodException($"Period '{periodId}' does not exist.", periodId);

        if (period.ParentPeriodId is null)
            return; // any top-level academic year is a valid grade-topic period.

        // Term/Semester must belong to the tenant's active academic year (FR-57, EC-24).
        var activeYear = await periodRepository.GetActiveAcademicYearAsync(
            cancellationToken: cancellationToken);
        if (activeYear is null || period.ParentPeriodId != activeYear.Id)
            throw new TopicAssignmentPeriodException(
                $"Grade topic period '{periodId}' is a {period.Division} sub-period outside the tenant's active academic year.", periodId);
    }

    /// <summary>
    /// FR-56: the group's <see cref="EnrollmentSpan"/> dictates whether/which
    /// period a group-owned topic's <paramref name="periodId"/> may reference.
    /// Null = date-based window (OpenEnded/DateRange, or period-aligned but no
    /// period set). OpenEnded/DateRange must not carry a period (EC-23).
    /// </summary>
    public static async Task ValidateGroupPeriodAsync(
        Guid activityGroupId,
        Guid? periodId,
        IActivityGroupRepository groupRepository,
        IPeriodRepository periodRepository,
        CancellationToken cancellationToken = default)
    {
        if (periodId is null)
            return; // null = date-based window (OpenEnded/DateRange, or period-aligned but no period set).

        var group = await groupRepository.GetAsync(activityGroupId, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(activityGroupId);

        // OpenEnded/DateRange carry no period → PeriodId must be null (EC-23).
        var requiredDivision = group.Span switch
        {
            EnrollmentSpan.Termly => AcademicYearDivision.Terms,
            EnrollmentSpan.Semester => AcademicYearDivision.Semesters,
            EnrollmentSpan.WholeAcademicYear => AcademicYearDivision.None,
            _ => (AcademicYearDivision?)null
        };

        if (requiredDivision is null)
            throw new TopicAssignmentPeriodException(
                $"An {group.Span} activity group topic assignment must not carry a PeriodId.", periodId);

        var period = await periodRepository.GetAsync(periodId.Value, cancellationToken)
            ?? throw new TopicAssignmentPeriodException($"Period '{periodId}' does not exist.", periodId);

        if (group.Span == EnrollmentSpan.WholeAcademicYear)
        {
            if (period.ParentPeriodId is not null)
                throw new TopicAssignmentPeriodException(
                    $"A {group.Span} activity group topic requires a top-level academic year period, but '{periodId}' is a sub-period.",
                    periodId);
        }
        else
        {
            if (period.Division != requiredDivision)
                throw new TopicAssignmentPeriodException(
                    $"A {group.Span} activity group topic requires a {requiredDivision} period, but '{periodId}' is a {period.Division}.",
                    periodId);

            // FR-H14 (Rev. 3): a Term/Semester group-topic period must belong to the
            // tenant's ACTIVE academic year — aligning with ValidateGradePeriodAsync
            // (FR-57) and the membership resolver (ResolveSpanAsync). WholeAcademicYear
            // stays any-AcademicYear (type-only).
            var activeYear = await periodRepository.GetActiveAcademicYearAsync(
                cancellationToken: cancellationToken);
            if (activeYear is null || period.ParentPeriodId != activeYear.Id)
                throw new TopicAssignmentPeriodException(
                    $"A {group.Span} activity group topic requires a {requiredDivision} of the tenant's active academic year, " +
                    $"but '{periodId}' belongs to a different (or no active) year.", periodId);
        }
    }

    // ── Subject enrollment exceptions (subject-period-exception-model.md v3 §4.2) ──

    /// <summary>
    /// The §2.2 span invariants, shared by both owners: at least one bound must be set
    /// (an unbounded exception would mean "never offered", a different concept) and,
    /// when both are set, <paramref name="endDate"/> must be on or after
    /// <paramref name="startDate"/>. A null bound is legitimately open on that side.
    /// </summary>
    public static void ValidateExceptionShape(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate is null && endDate is null)
        {
            throw new TopicAssignmentPeriodException(
                "A subject enrollment exception requires at least one bound: set a start date, an end date, or both. " +
                "An exception with no bounds would mean \"never offered\", which is a different concept.");
        }

        if (startDate is { } start && endDate is { } end && end < start)
        {
            throw new TopicAssignmentPeriodException(
                $"A subject enrollment exception's end date ('{end:yyyy-MM-dd}') must be on or after its start date ('{start:yyyy-MM-dd}').");
        }
    }

    /// <summary>
    /// The exception's <paramref name="division"/> must be one of the defined
    /// <see cref="AcademicYearDivision"/> members. The enum is int-backed, so an
    /// undefined integer (e.g. 42) would otherwise be stored verbatim — a 422 at the
    /// boundary instead of a bad row (§2.4: the division is descriptive and never matched
    /// against dates, so enum membership is the only thing left to check here).
    /// </summary>
    public static void ValidateExceptionDivision(AcademicYearDivision division)
    {
        if (!Enum.IsDefined(division))
        {
            throw new TopicAssignmentPeriodException(
                $"'{(int)division}' is not a valid subject enrollment exception division.");
        }
    }

    /// <summary>
    /// The ordinal invariants (v5 §0 decision 15). Two rules, both about the ordinal being
    /// a 1-based position in a run of the exception's <paramref name="division"/>:
    /// it must be 1 or greater, and it may only be supplied when the division actually
    /// names a part — a free window (<see cref="AcademicYearDivision.None"/>) has no
    /// position in a run of terms or semesters, so "the 2nd" of a free window is a
    /// category error rather than a value the server could accept and ignore.
    ///
    /// <para><b>What this deliberately does NOT check:</b> that the ordinal matches the
    /// span. A tenant may not have periodised its years at all, in which case "not offered
    /// in the 3rd term" is a perfectly meaningful thing to record against typed dates; and
    /// a subset of one term is a legitimate span for a whole-term ordinal. The dates are
    /// the truth for matching (§2.3) and the ordinal is a label beside them, so the two are
    /// allowed to disagree — and the price of that is recorded in the entity's
    /// <c>Ordinal</c> documentation rather than hidden here.</para>
    /// </summary>
    public static void ValidateExceptionOrdinal(AcademicYearDivision division, int? ordinal)
    {
        if (ordinal is < 1)
        {
            throw new TopicAssignmentPeriodException(
                "A subject enrollment exception's ordinal must be 1 or greater (it is a 1-based position: 1st term, 2nd term, …).");
        }

        if (ordinal.HasValue && division == AcademicYearDivision.None)
        {
            throw new TopicAssignmentPeriodException(
                "A subject enrollment exception can name an ordinal only when its division names a real part: a free window " +
                $"({AcademicYearDivision.None}) has no position in a run of terms or semesters.");
        }
    }

    /// <summary>
    /// FR-56 for the exception side (v3 §4.2): the exception's
    /// <paramref name="division"/> must match the group's <see cref="EnrollmentSpan"/>
    /// — <c>Termly</c>→<see cref="AcademicYearDivision.Terms"/>,
    /// <c>Semester</c>→<see cref="AcademicYearDivision.Semesters"/>,
    /// <c>WholeAcademicYear</c>/<c>OpenEnded</c>/<c>DateRange</c>→<see cref="AcademicYearDivision.None"/>.
    ///
    /// <para>A <c>DateRange</c> group additionally requires the span to fall
    /// <b>inside</b> the group's window: a null group bound is unbounded on that side,
    /// and a null exception bound is open, so it can never breach a bound window. v1's
    /// "<c>OpenEnded</c>/<c>DateRange</c> groups cannot be excepted at all" clause is
    /// gone — the division is always expressible.</para>
    /// </summary>
    public static async Task ValidateGroupExceptionDivisionAsync(
        Guid activityGroupId,
        AcademicYearDivision division,
        DateOnly? startDate,
        DateOnly? endDate,
        IActivityGroupRepository groupRepository,
        CancellationToken cancellationToken = default)
    {
        var group = await groupRepository.GetAsync(activityGroupId, cancellationToken)
            ?? throw new ActivityGroupNotFoundException(activityGroupId);

        var requiredDivision = group.Span switch
        {
            EnrollmentSpan.Termly => AcademicYearDivision.Terms,
            EnrollmentSpan.Semester => AcademicYearDivision.Semesters,
            _ => AcademicYearDivision.None
        };

        if (division != requiredDivision)
        {
            throw new TopicAssignmentPeriodException(
                $"A {group.Span} activity group requires a {requiredDivision} enrollment exception, but this one is {division}.");
        }

        if (group.Span != EnrollmentSpan.DateRange)
            return;

        // Q10: the exception span must fall inside the group's enrollment window.
        // A null window bound is unbounded on that side (a DateRange group always sets
        // both, but the rule is stated for what the data can hold).
        if (startDate is { } s && group.EnrollmentStartDate is { } windowStart && s < windowStart)
        {
            throw new TopicAssignmentPeriodException(
                $"A {group.Span} activity group's exception may not start before its enrollment window opens ('{windowStart:yyyy-MM-dd}').");
        }

        if (endDate is { } e && group.EnrollmentEndDate is { } windowEnd && e > windowEnd)
        {
            throw new TopicAssignmentPeriodException(
                $"A {group.Span} activity group's exception may not end after its enrollment window closes ('{windowEnd:yyyy-MM-dd}').");
        }
    }
}
