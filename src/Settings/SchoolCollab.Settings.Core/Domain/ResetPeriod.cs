namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// The schedule on which an <see cref="EntityCodeSegment"/> sequence resets.
/// Stored per segment so different segments on the same rule can reset on
/// different schedules (spec §3.3).
/// </summary>
public enum ResetPeriod
{
    /// <summary>Sequence never resets; monotonically increasing across all time.</summary>
    None = 0,

    /// <summary>Sequence resets to its initial value on Jan 1 of each calendar year.</summary>
    Yearly = 1,

    /// <summary>Sequence resets to its initial value on the 1st of each month.</summary>
    Monthly = 2,

    /// <summary>Sequence resets at the start of each calendar quarter (Jan/Apr/Jul/Oct 1).</summary>
    Quarterly = 3
}

/// <summary>
/// Period-bucket helpers for <see cref="ResetPeriod"/>.
/// </summary>
public static class ResetPeriodExtensions
{
    /// <summary>
    /// Computes the opaque period-bucket key for <paramref name="resetPeriod"/> at
    /// <paramref name="instant"/>. Two instants that yield the same bucket share a
    /// sequence run; when the bucket changes, the segment resets. <see cref="ResetPeriod.None"/>
    /// always returns <see cref="string.Empty"/> so the sequence never resets.
    /// </summary>
    public static string ComputeBucket(this ResetPeriod resetPeriod, DateTimeOffset instant) => resetPeriod switch
    {
        ResetPeriod.None => string.Empty,
        ResetPeriod.Yearly => instant.Year.ToString(),
        ResetPeriod.Monthly => $"{instant.Year}-{instant.Month:D2}",
        ResetPeriod.Quarterly => $"{instant.Year}-Q{(instant.Month - 1) / 3 + 1}",
        _ => string.Empty
    };
}