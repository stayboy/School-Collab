using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Periods;

/// <summary>
/// The handler-side half of "one sub-period per position per year per division"
/// (subject-period-exception-model.md v5 §0 decision 15) — shared by
/// <c>CreatePeriodHandler</c> and <c>UpdatePeriodHandler</c> so the two cannot disagree
/// about when a position is free.
///
/// <para>The storage-side half is the filtered unique index
/// <c>ix_periods_tenant_parent_division_sequence</c>. This guard exists so the collision is a
/// <see cref="PeriodSequenceTakenException"/> (a 422 naming the sibling) instead of the
/// unhandled <c>DbUpdateException</c> the index alone would raise (a 500).</para>
/// </summary>
internal static class PeriodSequenceGuard
{
    /// <summary>
    /// Rejects a position that is not a legal one at all: below 1, or supplied for a
    /// <paramref name="parentPeriodId"/>-less (top-level) period — a year is not the "1st" of
    /// anything. The rules mirror <c>Period.ValidateSequence</c> exactly, deliberately: the
    /// entity is the invariant's home, and this is the API boundary that turns the same two
    /// rejections into a <see cref="PeriodSequenceInvalidException"/> (a 422) rather than the
    /// <see cref="ArgumentException"/> the entity's own check raises (a 400).
    ///
    /// <para>Called by both handlers before any repository lookup, so an illegal position is
    /// answered from the request alone and never costs a query.</para>
    /// </summary>
    public static void EnsureDeclarable(int? sequence, Guid? parentPeriodId)
    {
        if (sequence is < 1)
        {
            throw new PeriodSequenceInvalidException(
                "A period's position must be 1 or greater (it is a 1-based position: 1st term, " +
                "2nd term, …), or left out.");
        }

        if (sequence.HasValue && parentPeriodId is null)
        {
            throw new PeriodSequenceInvalidException(
                "Only a sub-period can carry a position; a top-level academic year has no " +
                "position in a run of terms or semesters.");
        }
    }

    /// <summary>
    /// Throws when <paramref name="sequence"/> is already held by a sub-period of
    /// <paramref name="parentPeriodId"/> in <paramref name="division"/> — that is, by a
    /// sibling of the row being written. <paramref name="excludeId"/> is the period being
    /// UPDATED: a sub-period editing its own row must be able to keep the position it already
    /// holds, so its own row is not a collision with itself.
    /// </summary>
    public static async Task EnsureFreeAsync(
        IPeriodRepository repository,
        Guid parentPeriodId,
        AcademicYearDivision division,
        int sequence,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var holder = await repository.GetSubPeriodBySequenceAsync(
            parentPeriodId, division, sequence, excludeId, cancellationToken);

        if (holder is not null)
        {
            throw new PeriodSequenceTakenException(
                parentPeriodId, division, sequence, holder.Id, holder.Name);
        }
    }
}
