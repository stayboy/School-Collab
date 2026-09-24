using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SchoolCollab.Auth.Options;

namespace SchoolCollab.Auth.Services;

/// <summary>Server-side custody record for one portal session (spec §5.4 / D12). The token set
/// lives ONLY here, in the service's memory: the store has NO serialized shape, so no token ever
/// reaches a response body (AC9) — the pass-3c endpoint DTOs carry only derived, non-token data.
/// <see cref="ExpiresAtUtc"/> is the SESSION's absolute expiry (set at create, TTL-bounded) and
/// <see cref="AccessTokenExpiresAtUtc"/> is the current token set's own expiry, which a refresh
/// moves forward — the difference decides when the next refresh is due (D18).</summary>
public sealed record PortalSessionEntry(
    string AccessToken,
    string RefreshToken,
    string IdToken,
    int ExpiresInSeconds,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset AccessTokenExpiresAtUtc);

/// <summary>
/// Outcome of a revocation. The refresh token is handed to the CALLER only, so the caller can
/// send it to Keycloak's revocation endpoint (logout) — it is not a response payload and never
/// leaves the service.
/// </summary>
public sealed record PortalSessionRevocation(bool Found, string? RefreshToken = null);

/// <summary>
/// In-memory portal-session custody (spec §5.4 / D12): opaque session id → token set, served
/// only server-side, TTL-bounded on the injected <see cref="TimeProvider"/> (never wall-clock).
/// Round A's store is single-instance and deliberately NOT restart-durable — the plan claims
/// neither property; durable/multi-instance custody is a later round's concern. Revocation
/// deletes the entry and exposes its refresh token for Keycloak revocation (logout flow).
/// <para>
/// Round B (D18) adds <see cref="ReplaceTokens"/> so a transparent refresh can swap the token set
/// **in place**: the session id and the session's absolute expiry survive, and the store stays the
/// ONE source of truth for a session's tokens (a reader never sees a stale set).
/// </para>
/// </summary>
public sealed class PortalSessionStore
{
    private readonly ConcurrentDictionary<string, PortalSessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    // Bounded housekeeping (the pass-3a review standard for OneTimeCodeStore, applied here so the
    // finding does not repeat): an expired-but-never-revoked session is swept opportunistically
    // on Create — once per SweepEveryNCreates creates and whenever the live count passes
    // MaxLiveSessions. A swept entry is already past its TTL (Get would reject it anyway), so no
    // live custody is ever evicted, and the sweep uses value-compared removal so it cannot remove
    // an entry a concurrent Get/Revoke just consumed. Live entries are never evicted.
    private const int MaxLiveSessions = 1024;
    private const int SweepEveryNCreates = 256;

    /// <summary>Bounded compare-and-swap retries for <see cref="ReplaceTokens"/>: losing every
    /// attempt means a concurrent revoke/sweep won, which is a "no live session" answer, not an
    /// error to loop on.</summary>
    private const int ReplaceAttempts = 3;

    private long _createdCount;

    public PortalSessionStore(TimeProvider timeProvider, IOptions<AuthServiceOptions> options)
    {
        _timeProvider = timeProvider;
        _ttl = options.Value.PortalSessionTtl;
    }

    /// <summary>Stores a token set under a fresh opaque session id (64 hex chars = 256 bits of
    /// entropy, never derived from the tokens) and returns that id — the only value a caller ever
    /// holds. (Pass 3b-brief: session ids are to be 256-bit — note OneTimeCodeStore's
    /// GetHexString(32) is 128-bit; the pass-3a review's "256 bits" claim for it was inaccurate.)</summary>
    public string Create(string accessToken, string refreshToken, string idToken, int expiresInSeconds)
    {
        var sessionId = RandomNumberGenerator.GetHexString(64);
        var now = _timeProvider.GetUtcNow();
        _sessions[sessionId] = new PortalSessionEntry(
            accessToken,
            refreshToken,
            idToken,
            expiresInSeconds,
            now + _ttl,
            now + TimeSpan.FromSeconds(expiresInSeconds));

        var created = Interlocked.Increment(ref _createdCount);
        if (created % SweepEveryNCreates == 0 || _sessions.Count > MaxLiveSessions)
        {
            SweepExpired();
        }

        return sessionId;
    }

    /// <summary>
    /// Swaps a live session's token set in place (spec D18's transparent in-custody refresh): the
    /// opaque session id and the session's absolute expiry (<see cref="PortalSessionEntry.ExpiresAtUtc"/>)
    /// survive, only the tokens and their own expiry move forward. This keeps the store the ONE
    /// source of truth for a session's tokens — a caller that reads the store after a refresh sees
    /// the rotated set, so a rotated (single-use) refresh token is never left behind.
    /// </summary>
    /// <returns>
    /// The authoritative replacement entry when a live session was found — the caller builds its
    /// response from it, with no second read and no second race — or <c>null</c> when the session is
    /// unknown or already past its TTL. A rejected replacement is NOT a silent create: an expired
    /// session is swept, and nothing is written under that id.
    /// </returns>
    /// <remarks>
    /// The swap is a value-compared <see cref="ConcurrentDictionary{TKey,TValue}.TryUpdate(TKey,TValue,TValue)"/>
    /// loop, like the store's other mutations: a racer that revoked (or the sweep that reclaimed)
    /// the entry mid-flight wins, and the caller learns there is no live session rather than
    /// resurrecting one. Because the expiry check and the write happen inside the SAME call, a
    /// session that elapses while an async refresh is in flight is decided here: the D18 refresh
    /// path never re-reads custody and races a second time.
    /// </remarks>
    public PortalSessionEntry? ReplaceTokens(
        string sessionId,
        string accessToken,
        string refreshToken,
        string idToken,
        int expiresInSeconds)
    {
        for (var attempt = 0; attempt < ReplaceAttempts; attempt++)
        {
            if (!_sessions.TryGetValue(sessionId, out var entry))
            {
                return null;
            }

            var now = _timeProvider.GetUtcNow();
            if (entry.ExpiresAtUtc <= now)
            {
                _sessions.TryRemove(new KeyValuePair<string, PortalSessionEntry>(sessionId, entry));
                return null;
            }

            var replacement = new PortalSessionEntry(
                accessToken,
                refreshToken,
                idToken,
                expiresInSeconds,
                entry.ExpiresAtUtc,
                now + TimeSpan.FromSeconds(expiresInSeconds));

            if (_sessions.TryUpdate(sessionId, replacement, entry))
            {
                return replacement;
            }
        }

        return null;
    }

    /// <summary>Reads a live session (custody access for the host). An expired session is treated
    /// as absent and removed opportunistically.</summary>
    public PortalSessionEntry? Get(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            _sessions.TryRemove(new KeyValuePair<string, PortalSessionEntry>(sessionId, entry));
            return null;
        }

        return entry;
    }

    /// <summary>Deletes a session and hands its refresh token to the caller for Keycloak
    /// revocation (logout, spec §5.4 / round-B AC-G). An already-gone or already-swept session
    /// reports <see cref="PortalSessionRevocation.Found"/> = false.</summary>
    public PortalSessionRevocation Revoke(string sessionId)
    {
        return _sessions.TryRemove(sessionId, out var entry)
            ? new PortalSessionRevocation(Found: true, entry.RefreshToken)
            : new PortalSessionRevocation(Found: false);
    }

    private void SweepExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (key, entry) in _sessions)
        {
            if (entry.ExpiresAtUtc <= now)
            {
                _sessions.TryRemove(new KeyValuePair<string, PortalSessionEntry>(key, entry));
            }
        }
    }
}
