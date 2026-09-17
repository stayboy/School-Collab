namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Pure retry-backoff schedule for notification delivery. Deterministic and static so
/// the sequence and the cap are unit-testable without a database or a clock.
///
/// <para>Delays after failure attempt <c>n</c>: 1m, 5m, 15m, 60m. After
/// <see cref="MaxAttempts"/> failed attempts the row is terminal (no
/// <c>NextRetryAt</c>) and surfaces on the ar-17 failure list.</para>
/// </summary>
public static class NotificationRetrySchedule
{
    /// <summary>Failed attempts after which delivery stops and the row is terminal.</summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// The delay to wait after failed attempt <paramref name="attempt"/>; null once the
    /// attempt cap is reached (terminal failure).
    /// </summary>
    public static TimeSpan? Next(int attempt) => attempt switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromMinutes(60),
        _ => null, // attempt <= 0 is not a real attempt; >= MaxAttempts is the cap.
    };

    /// <summary>
    /// The absolute retry time after failed attempt <paramref name="attempt"/>, or null
    /// when the cap is reached.
    /// </summary>
    public static DateTimeOffset? NextRetryAt(int attempt, DateTimeOffset now)
    {
        var delay = Next(attempt);
        return delay is null ? null : now + delay.Value;
    }
}
