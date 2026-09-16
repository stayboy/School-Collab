namespace SchoolCollab.Core.DeepLinks;

/// <summary>
/// Shared constants for the contact-scoped deep-link tokens (ar-14-deep-links).
/// The Assignments API mints tokens and the Families host validates them, so both
/// hosts must agree on the DataProtection application name, the purpose string,
/// and the built-in validity window. Because both hosts persist their DataProtection
/// keyring to the same Redis resource, sharing <see cref="ApplicationName"/> is what
/// lets the Families host unprotect ciphertext minted by the Assignments API.
/// </summary>
public static class DeepLinkConstants
{
    /// <summary>The shared <c>SetApplicationName</c> value both hosts must use.</summary>
    public const string ApplicationName = "school-collab-deeplinks";

    /// <summary>The DataProtection purpose scoping all deep-link tokens (isolation
    /// from every other use of the shared keyring).</summary>
    public const string Purpose = "ar-deeplink";

    /// <summary>Built-in link validity (days) when the effective notification
    /// policy has no <c>LinkValidityDays</c> configured.</summary>
    public const int DefaultValidityDays = 7;
}
