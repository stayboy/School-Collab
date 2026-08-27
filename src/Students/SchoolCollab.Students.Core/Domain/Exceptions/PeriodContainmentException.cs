using System;

namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a <c>Term</c>/<c>Semester</c> sub-period's
/// <c>[StartDate, EndDate]</c> is not fully contained within its parent
/// academic year's range (period-hierarchy-terms-semesters.md FR-H3). Crossing
/// a year boundary is also rejected here (a sub-period can only be contained in
/// its single parent year). The API maps this to <c>422 Unprocessable Entity</c>.
/// </summary>
public sealed class PeriodContainmentException : Exception
{
    public string PeriodType { get; }
    public string ParentName { get; }
    public DateOnly ParentStart { get; }
    public DateOnly ParentEnd { get; }

    public PeriodContainmentException(
        string periodType,
        string parentName,
        DateOnly parentStart,
        DateOnly parentEnd)
        : base($"A {periodType} period's [StartDate, EndDate] must be contained within its parent " +
               $"academic year '{parentName}' ({parentStart:O}–{parentEnd:O}).")
    {
        PeriodType = periodType;
        ParentName = parentName;
        ParentStart = parentStart;
        ParentEnd = parentEnd;
    }
}