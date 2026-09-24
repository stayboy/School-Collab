using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// Portal-session custody coverage (plan step 6): round-trip, TTL expiry on the fake clock,
/// revocation exposing the refresh token and the id token (D13 option ii's logout hint), opaque
/// ids, bounded eviction, and round B's in-place token replacement (D18: the single custody source
/// of truth after a transparent refresh). Note the
/// "no token in a serialized response shape" boundary (AC9): the store itself is NEVER
/// serialized — token custody stays in the service's memory, and the response-shape guard is
/// enforced at the pass-3c endpoint layer (SessionEndpoints), where the tests for that live.
/// </summary>
[TestClass]
public class PortalSessionStoreTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static PortalSessionStore CreateStore(FakeTimeProvider clock, out AuthServiceOptions options)
    {
        options = new AuthServiceOptions { PortalSessionTtl = TimeSpan.FromMinutes(1) };
        // Fully-qualified on purpose: `SchoolCollab.Auth.Options` (the namespace) shadows the
        // static type — the repo precedent (OneTimeCodeStoreTests) does the same.
        return new PortalSessionStore(clock, Microsoft.Extensions.Options.Options.Create(options));
    }

    [TestMethod]
    public void Create_ReturnsOpaqueSessionId_AndGetRoundTripsTheTokenSet()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var id = store.Create("access-token", "refresh-token", "id-token", 300);

        id.Should().MatchRegex("^[0-9A-F]{64}$",
            "session ids are 64 hex chars = 256 bits of entropy (GetHexString(64)), opaque and never derived from the tokens.");

        var entry = store.Get(id);
        entry.Should().NotBeNull();
        entry!.AccessToken.Should().Be("access-token");
        entry.RefreshToken.Should().Be("refresh-token");
        entry.IdToken.Should().Be("id-token");
        entry.ExpiresInSeconds.Should().Be(300);
    }

    [TestMethod]
    public void Get_AfterTtlExpiry_IsAbsentAndCleanedUp()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var id = store.Create("a", "r", "i", 300);

        clock.Now = clock.Now.AddMinutes(2); // past the 1-minute TTL

        store.Get(id).Should().BeNull("an expired session must not be served.");

        var revoke = store.Revoke(id);
        revoke.Found.Should().BeFalse("Get already consumed the expired entry.");
        revoke.RefreshToken.Should().BeNull();
        revoke.IdToken.Should().BeNull("an expired session yields no revocation and no logout hint.");
    }

    [TestMethod]
    public void Revoke_DeletesTheSession_AndExposesItsRefreshTokenAndIdToken()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var id = store.Create("a", "the-refresh-token", "the-id-token", 300);

        var first = store.Revoke(id);

        first.Found.Should().BeTrue();
        first.RefreshToken.Should().Be("the-refresh-token",
            "revocation hands the refresh token to the caller so it can be sent to Keycloak's revocation endpoint (logout).");
        first.IdToken.Should().Be("the-id-token",
            "the same record carries the id token — the end_session URL's id_token_hint (D13 option ii) — "
            + "to the in-service caller only, with no second read of custody.");

        store.Get(id).Should().BeNull("revocation deletes the entry.");
        store.Revoke(id).Found.Should().BeFalse("a second revocation finds nothing.");
    }

    [TestMethod]
    public void Revoke_UnknownSession_IsNotFound_WithNeitherToken()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);

        var revoke = store.Revoke("00000000000000000000000000000000");

        revoke.Found.Should().BeFalse();
        revoke.RefreshToken.Should().BeNull();
        revoke.IdToken.Should().BeNull("an unknown session has no hint to carry.");
    }

    [TestMethod]
    public void ReplaceTokens_KeepsTheSessionIdAndExpiry_AndALaterReaderSeesTheReplacedSet()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var id = store.Create("access-a", "refresh-a", "id-a", 300);
        var sessionExpiry = store.Get(id)!.ExpiresAtUtc;

        clock.Now = clock.Now.AddSeconds(10);
        var replaced = store.ReplaceTokens(id, "access-b", "refresh-b", "id-b", 600);

        replaced.Should().NotBeNull("the session is live, so the swap lands.");
        replaced!.AccessToken.Should().Be("access-b",
            "the store hands back the authoritative set it wrote, so the caller needs no second read.");
        var entry = store.Get(id)!;
        entry.AccessToken.Should().Be("access-b");
        entry.RefreshToken.Should().Be("refresh-b");
        entry.IdToken.Should().Be("id-b");
        entry.ExpiresInSeconds.Should().Be(600);
        entry.ExpiresAtUtc.Should().Be(sessionExpiry,
            "replacing tokens must NOT move the session's absolute expiry (the id and the TTL survive).");
        entry.AccessTokenExpiresAtUtc.Should().Be(clock.Now.AddSeconds(600),
            "the new token set's own expiry is what decides when the next refresh is due.");

        // The session id is untouched by the swap, so revocation still works on it — and it hands
        // out the ROTATED refresh token, which is the one Keycloak will accept at logout.
        var revocation = store.Revoke(id);
        revocation.Found.Should().BeTrue();
        revocation.RefreshToken.Should().Be("refresh-b");
        revocation.IdToken.Should().Be("id-b",
            "the hint comes from the set the store now holds — the rotated one, not the exchanged one.");
        store.Get(id).Should().BeNull();
    }

    [TestMethod]
    public void ReplaceTokens_OnAnUnknownOrExpiredSession_IsRejectedAndCreatesNothing()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);

        store.ReplaceTokens("00000000000000000000000000000000", "a", "r", "i", 300)
            .Should().BeNull("an unknown id must never be brought into existence by a replace.");

        var expired = store.Create("access-a", "refresh-a", "id-a", 300);
        clock.Now = clock.Now.AddMinutes(2); // past the 1-minute TTL

        store.ReplaceTokens(expired, "access-b", "refresh-b", "id-b", 600)
            .Should().BeNull("an expired session is rejected, not resurrected.");
        store.Get(expired).Should().BeNull();
        store.Revoke(expired).Found.Should().BeFalse("the rejected replace wrote nothing back.");
    }

    [TestMethod]
    public void ReplaceTokens_DoesNotExtendTheSessionLifetime()
    {
        // The session's own TTL stays authoritative: no amount of refreshing keeps a session alive
        // past it — the invariant behind the refresh/expiry race being decided inside the store.
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var id = store.Create("access-a", "refresh-a", "id-a", 300);

        for (var minute = 1; minute <= 2; minute++)
        {
            clock.Now = clock.Now.AddSeconds(20);
            store.ReplaceTokens(id, $"access-{minute}", $"refresh-{minute}", $"id-{minute}", 300)
                .Should().NotBeNull();
        }

        clock.Now = clock.Now.AddMinutes(1); // 80 s after creation: past the TTL despite the refreshes

        store.Get(id).Should().BeNull("refreshing tokens must not extend the portal session's TTL.");
    }

    [TestMethod]
    public void Create_PeriodicSweep_ReclaimsExpiredNeverRevokedSessions()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var abandoned = store.Create("a", "r", "i", 300);

        clock.Now = clock.Now.AddHours(1); // far past the 1-minute TTL; will never be revoked

        for (var i = 0; i < 256; i++)
        {
            store.Create("a", "r", "i", 300);
        }

        store.Revoke(abandoned).Found.Should().BeFalse(
            "the periodic sweep must remove the expired, never-revoked session — Found=true here "
            + "would mean the dead entry was never reclaimed.");
    }

    [TestMethod]
    public void Create_CapDrivenSweep_KeepsTheStoreBounded()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var first = store.Create("a", "r", "i", 300);

        // Fill past the 1024 cap with the clock standing still (all still live, so the sweeps
        // have nothing expired to remove yet — live entries must never be evicted).
        for (var i = 1; i < 1100; i++)
        {
            store.Create("a", "r", "i", 300);
        }

        // Expire everything issued so far, then issue one more: the count is still past the cap,
        // so the sweep runs and reclaims the expired entries.
        clock.Now = clock.Now.AddHours(1);
        var live = store.Create("a", "r", "i", 300);

        store.Revoke(live).Found.Should().BeTrue("the post-expiry create must still succeed as a live session.");
        store.Revoke(first).Found.Should().BeFalse(
            "the cap-driven sweep must have reclaimed the expired entries — the store must not grow without bound.");
    }
}
