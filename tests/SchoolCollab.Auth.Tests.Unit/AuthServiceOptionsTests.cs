using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Options;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// Options-validator coverage for pass 3a (plan step 6): the shared
/// <see cref="AuthServiceOptions.FirstValidationError"/> used by Program.cs's
/// ValidateOnStart rejects EACH missing required Keycloak value, the new timing
/// values carry safe defaults, and the range guards on them reject misconfiguration.
/// Round B pass B5b adds the per-app callback allowlist's presence guard
/// (<c>Auth:AppCallbackPrefixes</c>) — the key has no safe default, so a blank value must
/// fail the startup validation instead of failing open at request time. Round B pass 3 adds the
/// same class of guard for <c>Auth:PostLogoutRedirectUri</c>, whose value Keycloak matches
/// against the realm's registered <c>postLogoutRedirectUris</c>.
/// </summary>
[TestClass]
public class AuthServiceOptionsTests
{
    /// <summary>The dev-shaped value the AppHost fans out: the four Blazor handshake callbacks
    /// (spec §14 / plan-review P1-3). A single valid entry is enough for the validator.</summary>
    private const string CallbackPrefixes =
        "http://localhost:5300/signin-handshake;https://localhost:7300/signin-handshake";

    /// <summary>The portal's registered post-logout landing URI (spec §9 / D13 option ii).</summary>
    private const string PostLogoutRedirectUri = "http://localhost:5700/";

    private static AuthServiceOptions Valid()
        => new()
        {
            Keycloak = new AuthServiceOptions.KeycloakOptions
            {
                Authority = "http://keycloak:8080/realms/school-collab",
                ClientId = "school-collab-client",
                ClientSecret = "a-client-secret",
                ServiceAccountClientSecret = "a-service-account-secret",
            },
            AppCallbackPrefixes = CallbackPrefixes,
            PostLogoutRedirectUri = PostLogoutRedirectUri,
        };

    [DataTestMethod]
    [DataRow(nameof(AuthServiceOptions.KeycloakOptions.Authority), "Auth:Keycloak:Authority")]
    [DataRow(nameof(AuthServiceOptions.KeycloakOptions.ClientId), "Auth:Keycloak:ClientId")]
    [DataRow(nameof(AuthServiceOptions.KeycloakOptions.ClientSecret), "Auth:Keycloak:ClientSecret")]
    [DataRow(nameof(AuthServiceOptions.KeycloakOptions.ServiceAccountClientSecret), "Auth:Keycloak:ServiceAccountClientSecret")]
    public void Validator_RejectsEachMissingRequiredKeycloakValue(string memberName, string expectedSectionName)
    {
        var options = Valid();

        switch (memberName)
        {
            case nameof(AuthServiceOptions.KeycloakOptions.Authority):
                options.Keycloak.Authority = string.Empty;
                break;
            case nameof(AuthServiceOptions.KeycloakOptions.ClientId):
                options.Keycloak.ClientId = string.Empty;
                break;
            case nameof(AuthServiceOptions.KeycloakOptions.ClientSecret):
                options.Keycloak.ClientSecret = string.Empty;
                break;
            default:
                options.Keycloak.ServiceAccountClientSecret = string.Empty;
                break;
        }

        var failure = AuthServiceOptions.FirstValidationError(options);

        failure.Should().NotBeNull();
        failure.Should().Contain(expectedSectionName,
            "the failing key must be named so an operator can fix the AppHost env var.");
    }

    [TestMethod]
    public void Validator_AcceptsCompleteConfiguration()
    {
        AuthServiceOptions.FirstValidationError(Valid()).Should().BeNull();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Validator_RejectsEmptyAppCallbackPrefixes(string configuredPrefixes)
    {
        // DISCRIMINATION: without the presence guard the auth service starts with an empty
        // allowlist — every app callback is then denied (fail-closed on the matcher side, but a
        // misconfiguration that looks like a login outage). The guard turns it into the loud
        // startup failure the AppHost/config rows promise; a hardcoded non-empty default would
        // instead make production fail OPEN, which is the defect this replaces.
        var options = Valid();
        options.AppCallbackPrefixes = configuredPrefixes;

        var failure = AuthServiceOptions.FirstValidationError(options);

        failure.Should().NotBeNull();
        failure.Should().Contain("Auth:AppCallbackPrefixes",
            "the failing key must be named so an operator can fix the AppHost parameter/env var.");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Validator_RejectsEmptyPostLogoutRedirectUri(string configuredUri)
    {
        // DISCRIMINATION: without the presence guard the service starts with no post-logout landing
        // URI and builds an end_session URL Keycloak rejects — after the local session is already
        // gone. There is deliberately no code fallback: a guessed host would fail the exact match
        // in the realm's postLogoutRedirectUris at logout time, which is exactly the late, silent
        // failure the guard replaces.
        var options = Valid();
        options.PostLogoutRedirectUri = configuredUri;

        var failure = AuthServiceOptions.FirstValidationError(options);

        failure.Should().NotBeNull();
        failure.Should().Contain("Auth:PostLogoutRedirectUri",
            "the failing key must be named so an operator can fix the AppHost fan-out (Auth__PostLogoutRedirectUri).");
    }

    [TestMethod]
    public void TimingDefaults_AreSafe()
    {
        var options = Valid();

        options.OneTimeCodeTtl.Should().BeGreaterThan(TimeSpan.Zero)
            .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(60),
                "the plan pins the one-time code TTL default at <= 60 s (spec §5.2).");
        options.PortalSessionTtl.Should().BePositive();
        options.CredentialEndpointRateLimitWindow.Should().BePositive();
        options.CredentialEndpointRateLimitPermits.Should().BeGreaterThanOrEqualTo(1);
    }

    [TestMethod]
    public void Validator_RejectsZeroOneTimeCodeTtl()
    {
        var options = Valid();
        options.OneTimeCodeTtl = TimeSpan.Zero;

        var failure = AuthServiceOptions.FirstValidationError(options);

        failure.Should().Contain("OneTimeCodeTtl",
            "a zero TTL would make a single-use code live forever.");
    }

    [TestMethod]
    public void Validator_RejectsZeroPortalSessionTtl()
    {
        var options = Valid();
        options.PortalSessionTtl = TimeSpan.Zero;

        AuthServiceOptions.FirstValidationError(options).Should().Contain("PortalSessionTtl");
    }

    [TestMethod]
    public void Validator_RejectsZeroCredentialEndpointRateLimitPermits()
    {
        var options = Valid();
        options.CredentialEndpointRateLimitPermits = 0;

        AuthServiceOptions.FirstValidationError(options).Should().Contain("CredentialEndpointRateLimitPermits");
    }

    [TestMethod]
    public void Validator_RejectsOneTimeCodeTtlLongerThanFiveMinutes()
    {
        var options = Valid();
        options.OneTimeCodeTtl = TimeSpan.FromMinutes(6);

        var failure = AuthServiceOptions.FirstValidationError(options);

        failure.Should().Contain("OneTimeCodeTtl",
            "a TTL past 5 minutes would widen the single-use code's exposure window beyond what the plan permits (diff-review P2-3).");
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Validator_RejectsNonPositiveCredentialEndpointRateLimitWindow(int minutes)
    {
        var options = Valid();
        options.CredentialEndpointRateLimitWindow = TimeSpan.FromMinutes(minutes);

        AuthServiceOptions.FirstValidationError(options).Should().Contain("CredentialEndpointRateLimitWindow",
            "a zero or negative window would make the rate-limit policy meaningless (diff-review P2-3).");
    }
}
