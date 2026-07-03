namespace SchoolCollab.Config.Tests.Playwright;

/// <summary>
/// Base URLs for the Playwright smoke tests. The Config Flags admin UI lives in
/// the main <c>SchoolCollab.Admin</c> host; the AI-chat consumer lives in the
/// <c>SchoolCollab.CodedValues.Admin</c> host. Under <c>aspire run</c> the
/// AppHost assigns random ports, so point these at the actual service URLs
/// printed in the Aspire dashboard:
/// <list type="bullet">
///   <item><c>PLAYWRIGHT_BASE_URL</c> — the Admin host serving <c>/config-flags</c>.</item>
///   <item><c>PLAYWRIGHT_CODEDVALUES_URL</c> — the CodedValues host serving
///       <c>/coded-values</c> (cross-service gating test only).</item>
/// </list>
/// When running <c>SchoolCollab.Admin</c> directly (<c>dotnet run</c>) the
/// <c>http://localhost:5300</c> default applies, but note that the
/// <c>config-api</c> HttpClient is resolved via Aspire service discovery, so
/// the full AppHost must be running for the admin UI to reach Config.Api.
/// </summary>
public static class PlaywrightSettings
{
    /// <summary>Admin host that serves the <c>/config-flags</c> admin UI.</summary>
    public static string AdminUrl =>
        Environment.GetEnvironmentVariable("PLAYWRIGHT_BASE_URL")
        ?? "http://localhost:5300";

    /// <summary>CodedValues host that serves <c>/coded-values</c> with the gated chat.</summary>
    public static string CodedValuesUrl =>
        Environment.GetEnvironmentVariable("PLAYWRIGHT_CODEDVALUES_URL")
        ?? "http://localhost:5301";
}