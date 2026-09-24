using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Options;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// Smoke test proving the auth service skeleton's wiring is real (AC-A-C): the
/// shared validator used by Program.cs's
/// <c>AddOptions&lt;AuthServiceOptions&gt;().Validate(...).ValidateOnStart()</c>
/// rejects a missing Keycloak client id, so a host launched without the AppHost's
/// <c>Auth__Keycloak__*</c> env vars fails fast at startup instead of surfacing
/// mid-request.
/// </summary>
[TestClass]
public class AuthServiceSmokeTests
{
    [TestMethod]
    public void OptionsValidator_RejectsMissingKeycloakClientId()
    {
        var options = new AuthServiceOptions
        {
            Keycloak = new AuthServiceOptions.KeycloakOptions
            {
                Authority = "http://keycloak:8080/realms/school-collab",
                ClientSecret = "a-client-secret",
                ServiceAccountClientSecret = "a-service-account-secret",
            },
        };

        var failure = AuthServiceOptions.FirstValidationError(options);

        failure.Should().NotBeNull();
        failure.Should().Contain("ClientId",
            "the missing value must be named so an operator can fix the AppHost env var.");
    }

    [TestMethod]
    public void OptionsValidator_AcceptsCompleteConfiguration()
    {
        var options = new AuthServiceOptions
        {
            Keycloak = new AuthServiceOptions.KeycloakOptions
            {
                Authority = "http://keycloak:8080/realms/school-collab",
                ClientId = "school-collab-client",
                ClientSecret = "a-client-secret",
                ServiceAccountClientSecret = "a-service-account-secret",
            },
            // Round B pass B5b: AppCallbackPrefixes is a required key (no safe default — an empty
            // allowlist must fail the start rather than deny/fail open mid-request), so a
            // "complete configuration" fixture has to carry it. Pass 3 adds the same class of
            // required key: PostLogoutRedirectUri (the end_session URL's post_logout_redirect_uri,
            // which Keycloak matches exactly).
            AppCallbackPrefixes = "http://localhost:5300/signin-handshake",
            PostLogoutRedirectUri = "http://localhost:5700/",
        };

        AuthServiceOptions.FirstValidationError(options).Should().BeNull();
    }
}
