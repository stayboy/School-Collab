namespace SchoolCollab.CodedValues.Tests.Playwright;

public static class PlaywrightSettings
{
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("PLAYWRIGHT_BASE_URL")
        ?? "http://localhost:5000";
}
