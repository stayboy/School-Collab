using Microsoft.Maui.Storage;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Services;

/// <summary>
/// The device's token custody, over MAUI Essentials' <c>SecureStorage</c> — the platform keystore
/// (Android Keystore, iOS keychain, the Windows credential vault), never the WebView's
/// <c>sessionStorage</c>/<c>localStorage</c> (the L2 readiness control). Essentials ships with the
/// head's <c>Microsoft.Maui.Controls</c> reference, which is why this implementation lives in the
/// head and only the <see cref="ITokenStore"/> seam lives in <c>Mobile.Logic</c>.
/// </summary>
/// <remarks>
/// <para>Nothing here logs, and no value read or written is ever placed in an exception message: the
/// values are the session's credentials. A failure raised here names the key and the platform
/// exception's type only.</para>
/// <para>A keystore failure never escapes as the platform's own exception: an unreadable store is
/// reported as "no session" (the app degrades to unauthenticated), while a refused write or removal
/// is raised as the typed <see cref="TokenStoreException"/> so the caller can state what failed.</para>
/// </remarks>
public sealed class SecureStorageTokenStore : ITokenStore
{
    /// <inheritdoc />
    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No readable session: whatever asked (the startup read, the gates, the bearer handler)
            // sees an empty store and treats the app as signed out. A keystore that cannot be read
            // says nothing about a token's correctness, so "no session" is the honest answer.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await SecureStorage.Default.SetAsync(key, value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new TokenStoreException(
                $"The device's token store refused the write of its '{key}' entry " +
                $"({exception.GetType().Name}).");
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            SecureStorage.Default.Remove(key);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A removal that silently failed would leave a credential on the device after a
            // sign-out, so this one is raised rather than swallowed.
            throw new TokenStoreException(
                $"The device's token store refused the removal of its '{key}' entry " +
                $"({exception.GetType().Name}).");
        }

        return Task.CompletedTask;
    }
}
