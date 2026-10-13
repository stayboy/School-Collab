namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// The device-side custody of the OIDC session's tokens. The abstraction lives here so every
/// consumer on the auth path (<see cref="TokenSession"/>, <see cref="OidcLoginService"/>) is
/// testable on a plain <c>net10.0</c> TFM; the implementation is platform code and lives in the
/// head, over MAUI Essentials' <c>SecureStorage</c> — never <c>sessionStorage</c>/
/// <c>localStorage</c> (the L2 readiness control).
/// </summary>
/// <remarks>
/// Implementations must never log or otherwise surface a stored value: the values written through
/// this interface are credential material.
/// </remarks>
public interface ITokenStore
{
    /// <summary>Reads the value stored under <paramref name="key"/>, or <see langword="null"/> when absent.</summary>
    /// <remarks>A keystore that cannot be read is reported as "no session" (<see langword="null"/>), so
    /// a storage failure degrades the app to unauthenticated instead of failing in whatever asked.</remarks>
    Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="value"/> under <paramref name="key"/>, replacing any previous value.</summary>
    /// <exception cref="TokenStoreException">The platform keystore refused the write.</exception>
    Task WriteAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Removes the value stored under <paramref name="key"/>. Absent keys are not an error.</summary>
    /// <exception cref="TokenStoreException">The platform keystore refused the removal.</exception>
    Task ClearAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// The device's token store could not carry out a write or a removal: the platform keystore
/// (Android Keystore, iOS keychain, the Windows credential vault) reported a failure.
/// </summary>
/// <remarks>
/// <para>It is raised instead of letting the platform's own exception escape: an arbitrary
/// platform exception would bypass the auth path's typed failures and surface somewhere that
/// cannot describe it. Reads do not raise it — an unreadable store reads as "no session".</para>
/// <para>The message names the key and the platform failure's type — never a token value, a
/// stored value, or <c>Authorization</c> header content.</para>
/// </remarks>
public sealed class TokenStoreException(string message) : Exception(message);

/// <summary>
/// The token store's keys — one constants block, so the read, write and clear sites cannot spell a
/// key differently and silently miss each other.
/// </summary>
public static class TokenKeys
{
    /// <summary>The access token presented as <c>Authorization: Bearer</c>.</summary>
    public const string AccessToken = "access_token";

    /// <summary>The refresh token used to renew the access token without an interactive sign-in.</summary>
    public const string RefreshToken = "refresh_token";

    /// <summary>
    /// The instant the access token stops being usable, as a round-trip ISO 8601 timestamp — the
    /// token's own expiry minus the clock-skew margin (<see cref="TokenSession"/>).
    /// </summary>
    public const string ExpiresAt = "expires_at";
}
