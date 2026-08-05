namespace SchoolCollab.Settings.Tests.Playwright;

/// <summary>
/// Base URLs for the Playwright smoke tests. The Config Flags admin UI lives in
/// the main <c>SchoolCollab.Admin</c> host; the AI-chat consumer lives in the
/// <c>SchoolCollab.Settings.Application</c> host. Under <c>aspire run</c> the
/// AppHost assigns random ports, so point these at the actual service URLs
/// printed in the Aspire dashboard:
/// <list type="bullet">
///   <item><c>PLAYWRIGHT_BASE_URL</c> — the Admin host serving <c>/config-flags</c>.</item>
///   <item><c>PLAYWRIGHT_CODEDVALUES_URL</c> — the CodedValues host serving
///       the Blazor <c>/coded-values</c> landing page (the API surface it
///       drives is at <c>/api/coded-values</c> on the unified
///       <c>settings-api</c>; cross-service gating test only).</item>
/// </list>
/// When running <c>SchoolCollab.Admin</c> directly (<c>dotnet run</c>) the
/// <c>http://localhost:5300</c> default applies, but note that the
/// <c>settings-api</c> HttpClient is resolved via Aspire service discovery, so
/// the full AppHost must be running for the admin UI to reach the Settings API.
/// </summary>
public static class PlaywrightSettings
{
    /// <summary>Base URL of the unified <c>SchoolCollab.Admin</c> host. The
    /// Settings merge folded the legacy CodedValues + Config admin pages into
    /// one host, so the Playwright tests now drive the Blazor
    /// <c>/coded-values</c> page, the <c>/config-flags</c> page, and the
    /// shared layout all from the same origin. The Coded Values UI talks to
    /// the Settings API at <c>/api/coded-values/*</c> (the
    /// <c>/api/</c> prefix matches the Config aggregate's <c>/api/config</c>
    /// and <c>/api/features</c> routes). Under <c>aspire run</c> the AppHost
    /// assigns a random port, so point this at the actual Admin URL printed
    /// in the Aspire dashboard via the <c>PLAYWRIGHT_BASE_URL</c> env var.
    /// The legacy <c>PLAYWRIGHT_CODEDVALUES_URL</c> env var is still
    /// honoured for compatibility with any external Playwright runners.</summary>
    public static string AdminUrl =>
        Environment.GetEnvironmentVariable("PLAYWRIGHT_BASE_URL")
        ?? "http://localhost:5300";

    /// <summary>Backward-compat alias for the legacy CodedValues test that
    /// expected a separate <c>coded-values</c> host. In the unified Settings
    /// host both UIs are served from <see cref="AdminUrl"/>; the env-var
    /// override is preserved so a developer running a split-host setup can
    /// still point this at a different origin.</summary>
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("PLAYWRIGHT_CODEDVALUES_URL")
        ?? AdminUrl;

    /// <summary>Backward-compat alias for legacy Config-Flags cross-service
    /// gating tests that hit the CodedValues landing page. Same value as
    /// <see cref="BaseUrl"/> in the unified host.</summary>
    public static string CodedValuesUrl =>
        Environment.GetEnvironmentVariable("PLAYWRIGHT_CODEDVALUES_URL")
        ?? AdminUrl;
}