namespace SchoolCollab.Students.Worker.Services;

/// <summary>
/// Configuration for the <see cref="PromotionService"/>.
/// </summary>
public sealed class PromotionOptions
{
    public const string SectionName = "Promotion";

    /// <summary>
    /// Human-readable cron expression (for logging only; actual scheduling uses <see cref="PollInterval"/>).
    /// </summary>
    public string CronExpression { get; set; } = "0 2 * * *"; // 2 AM daily

    /// <summary>
    /// How often the service polls for periods that need processing.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Delay after an error before retrying.
    /// </summary>
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromMinutes(5);
}