using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// <see cref="AppCallbackAllowlist"/> coverage (round B pass B5b, spec §14 / plan-review P1-3):
/// the fail-closed matcher behind the redirect-target allowlist. The axes are the ones the
/// security fix turns on — an allowlisted callback passes (including the Blazor hosts' nested
/// <c>ReturnUrl</c> query), scheme + host + port + path must all match (a substring/loose-prefix
/// implementation must NOT pass this suite), an attacker URI in any spelling is denied, malformed
/// input on either side grants nothing, and an empty allowlist denies everything rather than
/// failing open.
/// </summary>
[TestClass]
public class AppCallbackAllowlistTests
{
    /// <summary>The admin host's handshake callback (launch profile <c>http://localhost:5300</c>).</summary>
    private const string AdminCallback = "http://localhost:5300/signin-handshake";

    /// <summary>The admin host's https scheme spelling (<c>https://localhost:7300</c>).</summary>
    private const string AdminCallbackHttps = "https://localhost:7300/signin-handshake";

    /// <summary>The portal's D16 bootstrap redemption URI — the second kind of allowlisted target
    /// (derived from the portal's Aspire endpoint by the AppHost, never hardcoded there).</summary>
    private const string PortalBootstrap = "http://localhost:9091/bootstrap";

    private static AppCallbackAllowlist Allowlist()
        => new AppCallbackAllowlist($"{AdminCallback};{AdminCallbackHttps};{PortalBootstrap}");

    [TestMethod]
    public void IsAllowed_AcceptsAnAllowlistedCallback_IgnoringTheQuery() =>
        // The Blazor hosts mint `{scheme}://{host}{pathBase}/signin-handshake?ReturnUrl=<escaped>`
        // (B1's challenge handler and B4's BuildCallbackUri share the spelling), so the query half
        // — including a nested ReturnUrl that itself looks like a URL — must be ignored, not
        // treated as a mismatch.
        Allowlist().IsAllowed($"{AdminCallback}?ReturnUrl=%2Fstudents%3Fpage%3D2").Should().BeTrue();

    [TestMethod]
    public void IsAllowed_AcceptsAnAllowlistedCallback_WithoutAnyQuery() =>
        Allowlist().IsAllowed(AdminCallback).Should().BeTrue();

    [TestMethod]
    public void IsAllowed_AcceptsThePortalsBootstrapRedemptionUri() =>
        Allowlist().IsAllowed(PortalBootstrap).Should().BeTrue();

    [TestMethod]
    public void IsAllowed_AcceptsAPathExtensionOfAnAllowlistedPath()
    {
        // The plan pins path PREFIX matching — at a segment boundary: the scheme/host/port bind the
        // code's destination, and a DEEPER path on an allowlisted origin stays on that origin.
        Allowlist().IsAllowed($"{AdminCallback}/continuation").Should().BeTrue();
    }

    [TestMethod]
    public void IsAllowed_AcceptsATrailingSlashSpellingOfAnAllowlistedPath()
    {
        // Review P1-1: `/signin-handshake/` and `/signin-handshake` are the same resource; the
        // entry's trailing slash is normalized, so both spellings match.
        Allowlist().IsAllowed($"{AdminCallback}/").Should().BeTrue();

        var trailingSlashEntry = new AppCallbackAllowlist($"{AdminCallback}/");
        trailingSlashEntry.IsAllowed(AdminCallback).Should().BeTrue(
            "a configured trailing slash must not build a `//` comparison that matches nothing.");
        trailingSlashEntry.IsAllowed($"{AdminCallback}/continuation").Should().BeTrue();
    }

    [TestMethod]
    public void IsAllowed_RejectsASuffixAppendedToAnAllowlistedPath()
    {
        // DISCRIMINATION (review P1-1): a plain `AbsolutePath.StartsWith(prefix.Path)` accepts
        // both of these — `-evil` and `X` are appended with no `/` boundary, so they are DIFFERENT
        // resources on the allowlisted origin that would inherit the callback's trust.
        var allowlist = Allowlist();

        allowlist.IsAllowed($"{AdminCallback}-evil")
            .Should().BeFalse("a suffix-confused path is not the allowlisted callback.");
        allowlist.IsAllowed($"{AdminCallback}X")
            .Should().BeFalse("a path extension that is not `/`-delimited is a different resource.");
        allowlist.IsAllowed($"{AdminCallback}-evil?ReturnUrl=%2F")
            .Should().BeFalse("ignoring the query must not turn a suffix-confused path into a match.");
        allowlist.IsAllowed($"{PortalBootstrap}-evil")
            .Should().BeFalse("the portal's bootstrap entry is boundary-checked like every other.");
    }

    [TestMethod]
    public void IsAllowed_RejectsUserInfoOnTheCandidate()
    {
        // Review P2: `http://attacker@localhost:5300/signin-handshake` parses with host=localhost
        // and UserInfo="attacker" — the browser hides the prefix, so a userinfo-carrying target is
        // never the allowlisted callback even though its scheme/host/port/path match.
        var allowlist = Allowlist();

        allowlist.IsAllowed("http://attacker@localhost:5300/signin-handshake").Should().BeFalse();
        allowlist.IsAllowed("http://attacker:secret@localhost:5300/signin-handshake").Should().BeFalse();
        allowlist.IsAllowed("http://attacker@localhost:5300/signin-handshake?ReturnUrl=%2F").Should().BeFalse();
    }

    [TestMethod]
    public void IsAllowed_SkipsConfiguredEntriesCarryingUserInfo()
    {
        // Review P2, entry direction — DISCRIMINATION: the entry's userinfo is NOT one of the
        // compared components, so an entry that were merely kept would still grant the clean
        // candidate `host=localhost, port=5300, path=/signin-handshake`. Dropping it is what makes
        // the list below deny the clean candidate.
        var userInfoOnly = new AppCallbackAllowlist("http://attacker@localhost:5300/signin-handshake");

        userInfoOnly.IsAllowed(AdminCallback)
            .Should().BeFalse("a configured userinfo entry grants nothing — it is dropped, not honoured.");

        // …and dropping it keeps the other entries working.
        var mixed = new AppCallbackAllowlist($"http://attacker@localhost:5300/signin-handshake;{AdminCallback}");

        mixed.IsAllowed(AdminCallback).Should().BeTrue();
        mixed.IsAllowed(AdminCallbackHttps).Should().BeFalse("the dropped entry did not become a host-wide grant.");
    }

    [TestMethod]
    public void IsAllowed_RejectsAHostThatMerelyContainsAnAllowlistedPrefix()
    {
        var allowlist = Allowlist();

        // Each of these contains an allowlisted prefix as a substring but delivers the code
        // somewhere else entirely — the loose-substring implementation this guard replaces would
        // accept every one of them.
        allowlist.IsAllowed("http://attacker.example/?next=http://localhost:5300/signin-handshake")
            .Should().BeFalse("the allowlisted prefix appears only in the attacker's query string.");
        allowlist.IsAllowed("http://attacker.example#http://localhost:5300/signin-handshake")
            .Should().BeFalse("the allowlisted prefix appears only in the fragment.");
        allowlist.IsAllowed("http://localhost.attacker.example:5300/signin-handshake")
            .Should().BeFalse("the attacker host merely starts with the allowlisted host name.");
        allowlist.IsAllowed("http://attacker.example/signin-handshake")
            .Should().BeFalse("the path matches but the host does not.");
    }

    [TestMethod]
    public void IsAllowed_RequiresTheExactSchemeHostAndPort()
    {
        var allowlist = Allowlist();

        allowlist.IsAllowed("https://localhost:5300/signin-handshake")
            .Should().BeFalse("the allowlisted admin callback is http-only (https is 7300).");
        allowlist.IsAllowed("http://localhost:5400/signin-handshake")
            .Should().BeFalse("5400 is another host's port — a per-app prefix list must not be host-wide.");
        allowlist.IsAllowed("http://localhost/signin-handshake")
            .Should().BeFalse("the implicit port 80 is not the allowlisted 5300.");
        allowlist.IsAllowed("http://localhost:5301/signin-handshake")
            .Should().BeFalse("one port off is still another origin.");
    }

    [TestMethod]
    public void IsAllowed_RequiresAnAllowlistedPath()
    {
        var allowlist = Allowlist();

        allowlist.IsAllowed("http://localhost:5300/login")
            .Should().BeFalse("only the handshake callback may receive a code.");
        allowlist.IsAllowed("http://localhost:5300/")
            .Should().BeFalse("the origin alone is not an allowlisted callback.");
        allowlist.IsAllowed("http://localhost:9091/session")
            .Should().BeFalse("the portal's allowlisted path is its bootstrap route.");
    }

    [TestMethod]
    public void IsAllowed_TreatsTheExplicitDefaultPortAsTheAbsentOne()
    {
        // Uri normalizes the default port, so an entry written without a port still matches a
        // candidate that spells it out — and still cannot match a non-default port.
        var allowlist = new AppCallbackAllowlist("http://callback.example/signin-handshake");

        allowlist.IsAllowed("http://callback.example:80/signin-handshake").Should().BeTrue();
        allowlist.IsAllowed("http://callback.example:8080/signin-handshake").Should().BeFalse();
    }

    [TestMethod]
    public void IsAllowed_IsCaseInsensitiveForSchemeAndHost_ButNotForThePath()
    {
        var allowlist = Allowlist();

        allowlist.IsAllowed("HTTP://LOCALHOST:5300/signin-handshake").Should().BeTrue();
        allowlist.IsAllowed("http://localhost:5300/SIGNIN-HANDSHAKE")
            .Should().BeFalse("URI paths are case-sensitive; a case-insensitive path compare widens the target set.");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("/signin-handshake")]
    [DataRow("not a uri at all")]
    [DataRow("ftp://localhost:5300/signin-handshake")]
    [DataRow("file:///signin-handshake")]
    [DataRow("http:///signin-handshake")]
    public void IsAllowed_DeniesMalformedCandidates(string? candidate) =>
        Allowlist().IsAllowed(candidate)
            .Should().BeFalse("an unparseable, relative, hostless or non-http(s) target is never allowed.");

    [TestMethod]
    public void IsAllowed_SkipsMalformedEntries_WithoutWideningOrDisablingTheList()
    {
        // The malformed entries (an unparseable token, a blank entry, and an entry carrying its
        // own query — which could never match, since the candidate's query is ignored) grant
        // nothing; the valid entry still works.
        var allowlist = new AppCallbackAllowlist(
            $"not a uri; ;ftp://localhost:5300/signin-handshake;{AdminCallback}?ReturnUrl=x;{AdminCallback}");

        allowlist.IsAllowed(AdminCallback).Should().BeTrue();
        allowlist.IsAllowed("http://localhost:5300/signin-handshake?ReturnUrl=x")
            .Should().BeTrue("the valid entry covers it — the malformed query-carrying entry must not be the only rule.");
        allowlist.IsAllowed("ftp://localhost:5300/signin-handshake")
            .Should().BeFalse("a non-http(s) entry is malformed, not an alternative transport.");
        allowlist.IsAllowed("http://attacker.example/signin-handshake").Should().BeFalse();
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(";;")]
    [DataRow("; ;")]
    public void IsAllowed_DeniesEverything_WhenTheAllowlistIsEmptyOrBlank(string? configuredPrefixes)
    {
        // DISCRIMINATION (the fail-open defect this pass exists to prevent): an allowlist that
        // resolves to no usable entry must deny every candidate. An implementation that skips the
        // match when nothing is configured would mint a code for ANY caller-supplied URI — the
        // crafted `return_uri=attacker` link would work again.
        var allowlist = new AppCallbackAllowlist(configuredPrefixes);

        allowlist.IsAllowed(AdminCallback).Should().BeFalse();
        allowlist.IsAllowed("http://attacker.example/signin-handshake").Should().BeFalse();
        allowlist.IsAllowed($"{AdminCallback}?ReturnUrl=%2Fstudents").Should().BeFalse();
    }

    [TestMethod]
    public void IsAllowed_RejectsADotSegmentPathSpelling()
    {
        // R1 (round B residuals pass): this matcher and the portal's Python copy (`_split_uri` in
        // app.py, whose test carries the same name) are two implementations of ONE control and must
        // refuse the same spellings. `Uri.AbsolutePath` applies RFC 3986 `remove_dot_segments`, so
        // `…/signin-handshake/../evil` arrives as `/evil` and a boundary check refuses it anyway —
        // but `…/signin-handshake/./continuation` arrives as `…/signin-handshake/continuation`,
        // which a boundary check ACCEPTS. A downstream decoder may resolve the same spelling to a
        // different resource on the allowlisted origin, so the refusal is explicit, on the path as
        // written — never inherited from the parser's normalization.
        var allowlist = Allowlist();

        allowlist.IsAllowed($"{AdminCallback}/../evil")
            .Should().BeFalse("a dot-segment spelling must be refused, not silently resolved.");
        allowlist.IsAllowed($"{AdminCallback}/./continuation")
            .Should().BeFalse("`/./` resolves to the allowlisted path itself — accepting the spelling widens the target set.");
        allowlist.IsAllowed("http://localhost:5300/../signin-handshake")
            .Should().BeFalse("a dot segment must not be normalized into a match.");

        // …and the refusal is on the candidate's PATH: a dot segment inside the ignored query (the
        // hosts' nested ReturnUrl) is not a path spelling and must not break the match.
        allowlist.IsAllowed($"{AdminCallback}?ReturnUrl=%2Fstudents%2F..%2Fevil")
            .Should().BeTrue("only the path as written is checked — the query stays ignored.");
    }

    [TestMethod]
    public void Constructor_BindsTheConfiguredValue_FromAuthServiceOptions()
    {
        var options = new OptionsWrapper<AuthServiceOptions>(new AuthServiceOptions
        {
            AppCallbackPrefixes = AdminCallback,
        });

        var allowlist = new AppCallbackAllowlist(options);

        allowlist.IsAllowed(AdminCallback).Should().BeTrue();
        allowlist.IsAllowed(AdminCallbackHttps).Should().BeFalse(
            "the matcher reads Auth:AppCallbackPrefixes — not a wider default of its own.");
    }

    [TestMethod]
    public void Constructor_RejectsMissingOptions()
    {
        var act = () => new AppCallbackAllowlist((IOptions<AuthServiceOptions>)null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
