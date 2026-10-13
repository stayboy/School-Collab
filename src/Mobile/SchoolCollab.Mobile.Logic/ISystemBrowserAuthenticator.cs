namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// Acquires the authorization response from the system browser — the platform half of the OIDC
/// code flow. The head implements it over MAUI Essentials' <c>WebAuthenticator</c>; the
/// abstraction lives here so the flow itself (<see cref="OidcLoginService"/>) is testable on a
/// plain <c>net10.0</c> TFM.
/// </summary>
/// <remarks>
/// <c>WebAuthenticator</c> is cross-platform Essentials API surface, so the implementation carries
/// no platform <c>#if</c>; it only has to make the platform's callback registration match
/// <paramref name="callbackUri"/>'s scheme and authority.
/// </remarks>
public interface ISystemBrowserAuthenticator
{
    /// <summary>
    /// Opens <paramref name="authorizeUrl"/> in the system browser and completes when the app is
    /// re-launched with <paramref name="callbackUri"/>'s scheme and authority, returning the
    /// callback URI's query parameters.
    /// </summary>
    /// <exception cref="SignInCancelledException">The user dismissed the browser, or the platform
    /// cancelled the flow.</exception>
    /// <exception cref="SignInFailedException">The platform could not carry out the browser hand-off
    /// (e.g. a missing callback-protocol registration); nothing was persisted.</exception>
    Task<IDictionary<string, string>> AuthenticateAsync(
        Uri authorizeUrl,
        Uri callbackUri,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The user (or the platform) cancelled the interactive sign-in. Nothing was persisted; the UI
/// surfaces this as a dismissal, not an error.
/// </summary>
/// <remarks>The message carries no credential material and never names a token value.</remarks>
public sealed class SignInCancelledException(string message) : Exception(message);
