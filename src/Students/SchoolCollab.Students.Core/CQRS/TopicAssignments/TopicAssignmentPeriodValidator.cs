using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments;

/// <summary>
/// Shared period-scope validation for topic assignments (Rev. 6 FR-56/57,
/// AC-44..46, EC-23/24). Both the create handlers (<c>AssignGradeTopic</c> /
/// <c>AssignActivityGroupTopic</c>) and the update handler
/// (<c>UpdateTopicAssignmentPeriod</c>) call these so the create and update
/// paths enforce the identical rules.
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
}
