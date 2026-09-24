using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// One-time-code store coverage (plan step 6): single-use, TTL expiry, redirect-URI
/// binding, and concurrent redemption with exactly one winner. The clock is a
/// lightweight fake TimeProvider (the repo's testable-clock pattern) so TTL
/// expiry is deterministic without wall-clock sleeps.
/// </summary>
[TestClass]
public class OneTimeCodeStoreTests
{
    private const string Callback = "http://localhost:5300/signin-handshake";
    private const string OtherCallback = "https://localhost:7300/signin-handshake";
    private const string SessionId = "9f2c8a1e5b34d607ef82a1c39d45b670";

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static OneTimeCodeStore CreateStore(FakeTimeProvider clock, out AuthServiceOptions options)
    {
        options = new AuthServiceOptions
        {
            Keycloak = new AuthServiceOptions.KeycloakOptions
            {
                Authority = "http://keycloak:8080/realms/school-collab",
                ClientId = "school-collab-client",
                ClientSecret = "a-client-secret",
                ServiceAccountClientSecret = "a-service-account-secret",
            },
        };
        return new OneTimeCodeStore(clock, Microsoft.Extensions.Options.Options.Create(options));
    }

    [TestMethod]
    public void Redeem_WithMatchingRedirectUri_SucceedsOnce()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var code = store.Create(Callback, SessionId);

        var first = store.Redeem(code, Callback);
        first.Status.Should().Be(RedeemStatus.Success);
        first.Entry.Should().NotBeNull();
        first.Entry!.RedirectUri.Should().Be(Callback);

        store.Redeem(code, Callback).Status.Should().Be(RedeemStatus.InvalidCode,
            "a redeemed code is single-use and may never be replayed.");
    }

    [TestMethod]
    public void Redeem_CarriesTheSessionReference_BoundAtExchange()
    {
        // The pending token/claim set is stored bound to a one-time code (spec §5.2 step 4), so a
        // caller redeeming only `?code=<one-time code>` must reach the session without ever
        // holding a session id — the reference has to round-trip through create -> redeem.
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var code = store.Create(Callback, SessionId);

        var redeemed = store.Redeem(code, Callback);

        redeemed.Status.Should().Be(RedeemStatus.Success);
        redeemed.Entry!.RedirectUri.Should().Be(Callback);
        redeemed.Entry.SessionId.Should().Be(SessionId,
            "the code entry must carry the custody session reference created at exchange, so "
            + "redemption resolves the claims from the code alone (spec §5.2 steps 4-5).");
    }

    [TestMethod]
    public void Redeem_UnknownCode_IsInvalid()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);

        store.Redeem("00000000000000000000000000000000", Callback).Status
            .Should().Be(RedeemStatus.InvalidCode);
    }

    [TestMethod]
    public void Redeem_AfterTtlExpiry_IsRejected()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var code = store.Create(Callback, SessionId);

        clock.Now = clock.Now.Add(TimeSpan.FromSeconds(61));

        store.Redeem(code, Callback).Status.Should().Be(RedeemStatus.Expired);
    }

    [TestMethod]
    public void Redeem_WithMismatchedRedirectUri_IsRejected()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var code = store.Create(Callback, SessionId);

        var verdict = store.Redeem(code, OtherCallback);

        verdict.Status.Should().Be(RedeemStatus.RedirectUriMismatch,
            "a code is bound to the originating app's redirect URI (spec §5.2 / D6).");
    }

    [TestMethod]
    public void Create_PeriodicSweep_ReclaimsExpiredNeverRedeemedCodes()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var abandoned = store.Create(Callback, SessionId);

        clock.Now = clock.Now.AddHours(1); // well past the 60 s TTL — the code will never be redeemed

        for (var i = 0; i < 256; i++)
        {
            store.Create(Callback, SessionId);
        }

        store.Redeem(abandoned, Callback).Status.Should().Be(RedeemStatus.InvalidCode,
            "the periodic sweep must remove the expired, never-redeemed code — an Expired verdict here would mean the dead entry was never reclaimed.");
    }

    [TestMethod]
    public void Create_CapDrivenSweep_KeepsTheStoreBounded()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var first = store.Create(Callback, SessionId);

        // Fill past the 1024 cap with the clock standing still (all still live, so the
        // cap-driven sweep has nothing expired to remove yet).
        for (var i = 1; i < 1100; i++)
        {
            store.Create(Callback, SessionId);
        }

        // Expire everything issued so far, then issue one more code: the count is still past
        // the cap, so the sweep runs and reclaims the expired entries.
        clock.Now = clock.Now.AddHours(1);
        var live = store.Create(Callback, SessionId);

        store.Redeem(live, Callback).Status.Should().Be(RedeemStatus.Success);
        store.Redeem(first, Callback).Status.Should().Be(RedeemStatus.InvalidCode,
            "the cap-driven sweep must have reclaimed the expired entries — the store must not grow without bound.");
    }

    [TestMethod]
    public void Redeem_ConcurrentCallers_ExactlyOneWinner()
    {
        var clock = new FakeTimeProvider();
        var store = CreateStore(clock, out _);
        var code = store.Create(Callback, SessionId);

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => store.Redeem(code, Callback)))
            .ToArray();
        Task.WaitAll(tasks);

        var winners = tasks.Count(t => t.Result.Status == RedeemStatus.Success);
        winners.Should().Be(1,
            "redemption removes the entry atomically — exactly one concurrent caller may win.");
        tasks.Select(t => t.Result.Status).Distinct().Should().OnlyContain(
            status => status == RedeemStatus.Success || status == RedeemStatus.InvalidCode);
    }
}
