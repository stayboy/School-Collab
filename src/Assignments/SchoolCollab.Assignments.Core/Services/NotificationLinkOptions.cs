namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// The Families public base URL the renderer uses to build absolute deep links
/// (round notification-templates-b1, S1's AppHost parameter). Bound from the
/// <c>Families</c> configuration section — the AppHost fans
/// <c>families-public-base-url</c> out to assignments-api as
/// <c>Families__PublicBaseUrl</c> (documents/configuration.md §2/§11). Empty/absent
/// ⇒ relative <c>/deeplink/{token}</c> links (fail-closed dev posture).
/// </summary>
public sealed class NotificationLinkOptions
{
    /// <summary>The configuration section the option binds from.</summary>
    public const string SectionName = "Families";

    /// <summary>The public origin of the Families app, e.g. <c>https://families.example.com</c>.
    /// Null/empty = render relative links.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Builds the recipient's link target: relative <c>/deeplink/{token}</c> when no base
    /// URL is configured; absolute (<c>{base}/deeplink/{token}</c>) when it is. Email
    /// clients cannot resolve relative links, so the deployment posture decides (§3.2).
    /// </summary>
    public string BuildDeepLink(string token)
    {
        var path = string.Concat(NotificationMessageBuilder.DeepLinkPathPrefix, token);
        return string.IsNullOrWhiteSpace(PublicBaseUrl)
            ? path
            : string.Concat(PublicBaseUrl.TrimEnd('/'), path);
    }
}