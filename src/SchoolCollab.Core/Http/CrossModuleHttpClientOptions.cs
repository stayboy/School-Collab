namespace SchoolCollab.Core.Http;

/// <summary>
/// Resilience options for HTTP calls that cross Aspire service boundaries
/// (admin app → API, API → API, worker → API).
/// </summary>
public sealed class CrossModuleHttpClientOptions
{
    public const int DefaultMaxRetries = 1;

    /// <summary>
    /// Number of retries after the initial attempt for handler/connection
    /// level failures. One retry is enough to recover from a disposed handler
    /// or closed connection because the next request gets a fresh handler.
    /// </summary>
    public int MaxRetries { get; set; } = DefaultMaxRetries;
}
