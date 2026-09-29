namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a sub-period would take a position (its <see cref="Period.Sequence"/>) that a
/// sibling sub-period of the same academic year and division already holds
/// (subject-period-exception-model.md v5 §0 decision 15: one 1st term, one 2nd term, … per
/// year per division). The API maps it to <c>422 Unprocessable Entity</c>, matching the
/// other sub-period shape rules (<see cref="PeriodOverlapException"/>,
/// <see cref="PeriodContainmentException"/>).
///
/// <para><b>Why a pre-check exists at all.</b> The rule is already enforced by the filtered
/// unique index <c>ix_periods_tenant_parent_division_sequence</c>, but a violation raised
/// only by the index reaches the caller as an unhandled <c>DbUpdateException</c> — a 500
/// with no sentence in it. This exception is the readable half: the handler detects the
/// collision first and the message <b>names the sibling that holds the position</b>, because
/// a caller told only "that position is taken" cannot act on it.</para>
/// </summary>
public sealed class PeriodSequenceTakenException : Exception
{
    /// <summary>The academic year whose run of positions the collision was detected in.</summary>
    public Guid ParentPeriodId { get; }

    /// <summary>The division whose run of positions the collision was detected in.</summary>
    public AcademicYearDivision Division { get; }

    /// <summary>The position that is already held.</summary>
    public int Sequence { get; }

    /// <summary>The sibling sub-period holding <see cref="Sequence"/>.</summary>
    public Guid HolderId { get; }

    /// <summary>The sibling's name — the part of the message the caller acts on.</summary>
    public string HolderName { get; }

    public PeriodSequenceTakenException(
        Guid parentPeriodId,
        AcademicYearDivision division,
        int sequence,
        Guid holderId,
        string holderName)
        : base($"This academic year already has a {division} sub-period in position {sequence}: " +
               $"'{holderName}'. Only one sub-period of the same kind can hold a position, so " +
               "choose a position no sibling sub-period holds.")
    {
        ParentPeriodId = parentPeriodId;
        Division = division;
        Sequence = sequence;
        HolderId = holderId;
        HolderName = holderName;
    }
}
