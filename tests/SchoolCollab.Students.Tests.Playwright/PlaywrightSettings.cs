namespace SchoolCollab.Students.Tests.Playwright;

/// <summary>
/// Base URL for the Students Admin Playwright smoke tests. The guardian UI
/// (Guardians landing page, GuardianSetupWizard, student Guardians + Contacts
/// tabs) is served by the unified <c>SchoolCollab.Admin</c> host — the same
/// origin as the Settings Playwright tests. Under <c>aspire run</c> the AppHost
/// assigns a random port, so point this at the actual Admin URL printed in the
/// Aspire dashboard via the <c>PLAYWRIGHT_BASE_URL</c> env var. When running
/// <c>SchoolCollab.Admin</c> directly (<c>dotnet run</c>) the
/// <c>http://localhost:5300</c> default applies. The full AppHost must be running
/// for the admin UI to reach the Students API (Aspire service discovery).
/// </summary>
public static class PlaywrightSettings
{
    public static string AdminUrl =>
        Environment.GetEnvironmentVariable("PLAYWRIGHT_BASE_URL")
        ?? "http://localhost:5300";
}