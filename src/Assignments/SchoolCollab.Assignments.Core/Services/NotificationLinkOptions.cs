using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Core.Notifications;

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
    /// Builds the recipient's link target (round notification-templates-b2a, spec §3.6 — the
    /// choice is channel-driven):
    /// <list type="bullet">
    /// <item><b>SMS / WhatsApp</b> on a configured public base with a minted
    /// <paramref name="shortLinkCode"/> ⇒ <c>{base}/l/{code}</c> — the short form that fits a
    /// text message and resolves publicly to the same contact-scoped token;</item>
    /// <item>every other case (email, a plain-text recipient with no code yet, or no configured
    /// base) ⇒ <c>/deeplink/{token}</c>, relative or absolute.</item>
    /// </list>
    /// The no-base case stays relative and un-shortened: <c>/l/</c> exists on the Families host,
    /// which is exactly what an unset base URL cannot address (the fail-closed dev posture is
    /// unchanged — email clients cannot resolve relative links, so the deployment posture
    /// decides, §3.2).
    /// </summary>
    public string BuildDeepLink(string token, NotificationChannel channel, string? shortLinkCode)
    {
        var hasBase = !string.IsNullOrWhiteSpace(PublicBaseUrl);

        if (channel != NotificationChannel.Email
            && hasBase
            && !string.IsNullOrWhiteSpace(shortLinkCode))
        {
            return string.Concat(
                PublicBaseUrl!.TrimEnd('/'), DeepLinkConstants.ShortLinkPathPrefix, shortLinkCode);
        }

        var path = string.Concat(NotificationMessageBuilder.DeepLinkPathPrefix, token);
        return hasBase
            ? string.Concat(PublicBaseUrl!.TrimEnd('/'), path)
            : path;
    }
}
