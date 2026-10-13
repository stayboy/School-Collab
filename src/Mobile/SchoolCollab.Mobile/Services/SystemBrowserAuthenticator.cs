using Microsoft.Maui.Authentication;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Services;

/// <summary>
/// Opens the OIDC authorize URL in the system browser through MAUI Essentials'
/// <c>WebAuthenticator</c> and hands the callback's query parameters back to
/// <see cref="OidcLoginService"/>.
/// </summary>
/// <remarks>
/// <para><c>WebAuthenticator</c> is cross-platform Essentials surface, so — unlike the rest of
/// <c>Platforms/</c> — this class needs no platform <c>#if</c>. What <i>is</i> platform-specific is
/// the callback registration: the Android <c>[IntentFilter]</c> on <c>MainActivity</c> and the
/// Apple <c>CFBundleURLTypes</c> entries must carry exactly the scheme and authority of the
/// <see cref="Uri"/> passed as the callback URL, or the browser returns the app to nowhere.</para>
/// <para>Platform cancellation (the user dismissing the browser) is surfaced as the typed
/// <see cref="SignInCancelledException"/>, and every other platform failure as the typed
/// <see cref="SignInFailedException"/>, never raw. No token material passes through this type;
/// the exception's message repeats the platform failure's <i>type name</i> only, never its text.</para>
/// </remarks>
public sealed class SystemBrowserAuthenticator : ISystemBrowserAuthenticator
{
    /// <inheritdoc />
    public async Task<IDictionary<string, string>> AuthenticateAsync(
        Uri authorizeUrl,
        Uri callbackUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizeUrl);
        ArgumentNullException.ThrowIfNull(callbackUri);

        try
        {
            var result = await WebAuthenticator.Default.AuthenticateAsync(
                new WebAuthenticatorOptions
                {
                    Url = authorizeUrl,
                    CallbackUrl = callbackUri,
                },
                cancellationToken);

            return result.Properties;
        }
        catch (OperationCanceledException)
        {
            throw new SignInCancelledException(
                "The sign-in was cancelled before the browser returned a callback.");
        }
        catch (Exception exception)
        {
            // A platform failure is not a cancellation, and it must not escape raw: the auth path
            // owns two typed failures, and an untyped one would bypass the sign-in surface and land
            // in the ErrorBoundary instead. The Windows leg's missing protocol registration
            // (FeatureNotSupportedException on an unpackaged app) is the known case. The message
            // names the platform failure's TYPE only — a platform message can echo request content.
            throw new SignInFailedException(
                "The system browser could not complete the sign-in on this platform " +
                $"({exception.GetType().Name}).");
        }
    }
}
