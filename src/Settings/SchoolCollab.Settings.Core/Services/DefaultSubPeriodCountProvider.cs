namespace SchoolCollab.Settings.Core.Services;

/// <summary>
/// Default <see cref="ISubPeriodCountProvider"/> registered by
/// <c>AddSettingsCore</c>. Returns <c>0</c> — a standalone Settings deployment
/// (no Students service) has no sub-periods, so a framework switch is safe. The
/// Settings API host overrides this with an HTTP client that calls the Students
/// API (fail-closed: if Students is unreachable it throws, and the route rejects
/// the switch rather than risk an unsafe framework change).
/// </summary>
public sealed class DefaultSubPeriodCountProvider : ISubPeriodCountProvider
{
    public Task<int> GetSubPeriodCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}