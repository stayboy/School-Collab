using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SchoolCollab.Auth.Options;

namespace SchoolCollab.Auth.Services;

/// <summary>Outcome of a one-time code redemption (spec §5.2 / D6).</summary>
public enum RedeemStatus
{
    /// <summary>The code was valid, unexpired, bound to the given redirect URI, and consumed.</summary>
    Success,
    /// <summary>No live code exists under that value — either it was never issued or a previous redemption/rejection consumed it.</summary>
    InvalidCode,
    /// <summary>The code existed but its TTL elapsed before redemption.</summary>
    Expired,
    /// <summary>The code existed but is bound to a different redirect URI than the caller supplied.</summary>
    RedirectUriMismatch,
}

/// <summary>Payload stored for a live one-time code: the redirect-URI binding, the custody session
/// reference (the pending token/claim set, spec §5.2 step 4 / D12), and the expiry.</summary>
public sealed record OneTimeCodeEntry(string RedirectUri, string SessionId, DateTimeOffset ExpiresAtUtc);

/// <summary>Redeem outcome with the consumed entry on success.</summary>
public sealed record RedeemResult(RedeemStatus Status, OneTimeCodeEntry? Entry = null);

/// <summary>
/// Server-side store for the flag-ON one-time handshake codes (spec §5.2 / D6):
/// single-use, short TTL (default 60 s, configured via <c>Auth:OneTimeCodeTtl</c>),
/// and bound to the redirect URI of the originating app so a code observed in one
/// portal call site cannot be redeemed from another. Each live entry also carries the
/// **session reference** created alongside it at exchange, so a caller redeeming only
/// `?code=<one-time code>` reaches the pending claim set without ever holding a session
/// id or a token (spec §5.2 steps 4-5). Redemption removes the entry
/// atomically (a value-compared <c>ConcurrentDictionary.TryRemove</c>), so exactly
/// one concurrent caller wins. The clock comes from <see cref="TimeProvider"/> (the
/// repo's testable-clock pattern) — never <c>DateTime.UtcNow</c> directly.
/// </summary>
public sealed class OneTimeCodeStore
{
    private readonly ConcurrentDictionary<string, OneTimeCodeEntry> _codes = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    // Bounded housekeeping (diff-review P2-1): a created-but-never-redeemed code (or one burned
    // by a redirect-URI mismatch) used to stay in the dictionary forever. The store now sweeps
    // expired entries opportunistically on Create — once per SweepEveryNCreates issued codes
    // (periodic, so a low-traffic store also reclaims) and whenever the live count passes
    // MaxLiveEntries (cap-driven). A code being swept is one that is already past its TTL, so
    // the exactly-one-winner redemption semantics are unaffected. Live entries are never evicted.
    private const int MaxLiveEntries = 1024;
    private const int SweepEveryNCreates = 256;
    private long _createdCount;

    public OneTimeCodeStore(TimeProvider timeProvider, IOptions<AuthServiceOptions> options)
    {
        _timeProvider = timeProvider;
        _ttl = options.Value.OneTimeCodeTtl;
    }

    /// <summary>Issues a new unguessable code bound to <paramref name="redirectUri"/>. The entry also
    /// carries <paramref name="sessionId"/> — the custody reference for the token set created by the
    /// same exchange, so redemption reaches the claims from the code alone (spec §5.2 step 4 stores the
    /// pending token/claim set bound to a one-time code) and the calling app never needs — or holds — a
    /// session id.</summary>
    public string Create(string redirectUri, string sessionId)
    {
        var code = RandomNumberGenerator.GetHexString(32);
        _codes[code] = new OneTimeCodeEntry(redirectUri, sessionId, _timeProvider.GetUtcNow() + _ttl);

        var created = Interlocked.Increment(ref _createdCount);
        if (created % SweepEveryNCreates == 0 || _codes.Count > MaxLiveEntries)
        {
            SweepExpired();
        }

        return code;
    }

    /// <summary>
    /// Removes every expired entry using value-compared removal, so a racer that is mid-<see
    /// cref="Redeem"/> can never have its exactly-one-winner semantics broken by the sweep: an
    /// entry the sweep removes is already past its TTL, which the racer's own expiry check would
    /// have rejected anyway. The value-compared overload also stops the sweep from removing an
    /// entry that a concurrent Redeem has just consumed.
    /// </summary>
    private void SweepExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (key, entry) in _codes)
        {
            if (entry.ExpiresAtUtc <= now)
            {
                _codes.TryRemove(new KeyValuePair<string, OneTimeCodeEntry>(key, entry));
            }
        }
    }

    /// <summary>
    /// Redeems a code exactly once. A replay, an expired code and a redirect-URI
    /// mismatch all fail; the winner of the concurrent race is the only caller that
    /// observes <see cref="RedeemStatus.Success"/>.
    /// </summary>
    public RedeemResult Redeem(string code, string redirectUri)
    {
        if (!_codes.TryGetValue(code, out var entry))
        {
            return new RedeemResult(RedeemStatus.InvalidCode);
        }

        var now = _timeProvider.GetUtcNow();
        if (entry.ExpiresAtUtc <= now)
        {
            _codes.TryRemove(code, out _); // sweep the dead entry
            return new RedeemResult(RedeemStatus.Expired);
        }

        if (!string.Equals(entry.RedirectUri, redirectUri, StringComparison.Ordinal))
        {
            // A mismatched URI is treated as misuse: consume the code so it cannot
            // be probed again with a different caller.
            _codes.TryRemove(code, out _);
            return new RedeemResult(RedeemStatus.RedirectUriMismatch);
        }

        // Value-compared removal: when two callers race, only the one whose entry
        // snapshot still matches the dictionary wins; the loser sees false.
        return _codes.TryRemove(new KeyValuePair<string, OneTimeCodeEntry>(code, entry))
            ? new RedeemResult(RedeemStatus.Success, entry)
            : new RedeemResult(RedeemStatus.InvalidCode);
    }
}
