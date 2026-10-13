using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Tests.Unit;

/// <summary>
/// Covers <see cref="MobileAuthOptions"/> — the credential settings and the addresses built from them.
/// Addresses are composed with <see cref="UriBuilder"/> rather than typed as literals on purpose: the
/// rule the class enforces is "never a URL string literal in C#" (spec D8), and a test that violated it
/// would be the one place the rule is allowed to rot. The last test pins the rule at class granularity.
/// </summary>
[TestClass]
public class MobileAuthOptionsTests
{
    private static readonly string Authority =
        new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab").Uri.ToString();

    [TestMethod]
    public void FromConfiguration_BindsEverySetting_FromTheMobileAuthSection()
    {
        var options = MobileAuthOptions.FromConfiguration(BuildConfiguration(
            ("MobileAuth:Authority", Authority),
            ("MobileAuth:ClientId", "school-collab-mobile-client"),
            ("MobileAuth:RedirectScheme", "schoolcollab"),
            ("MobileAuth:RedirectHost", "signin-oidc"),
            ("MobileAuth:Scope", "openid profile"),
            ("MobileAuth:RequiredScopes:0", "email")));

        options.Authority.Should().Be(Authority);
        options.ClientId.Should().Be("school-collab-mobile-client");
        options.RedirectScheme.Should().Be("schoolcollab");
        options.RedirectHost.Should().Be("signin-oidc");
        options.Scope.Should().Be("openid profile");
        options.RequiredScopes.Should().Equal("email");
    }

    [TestMethod]
    public void FromConfiguration_WithoutTheSection_YieldsTheDefaults()
    {
        var options = MobileAuthOptions.FromConfiguration(BuildConfiguration());

        options.Scope.Should().Be("openid", "the ID token requires the openid scope, so it is the one non-empty default.");
        options.RequiredScopes.Should().BeEmpty();
        options.Authority.Should().BeNull();
        options.ClientId.Should().BeNull();
        options.RedirectScheme.Should().BeNull(
            "the scheme and host are configuration (the realm and the platform manifests must agree with them), not literals.");
        options.RedirectHost.Should().BeNull();
    }

    [TestMethod]
    public void FromConfiguration_RejectsANullConfiguration()
    {
        var bind = () => MobileAuthOptions.FromConfiguration(null!);

        bind.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void BuildRedirectUri_JoinsTheSchemeAndTheAuthority()
    {
        var options = Options();

        options.BuildRedirectUri().Should().Be(new Uri("schoolcollab://signin-oidc"),
            "the same literal the realm registers in redirectUris and the platform filter/plist declares — Keycloak matches it exactly.");
    }

    [TestMethod]
    public void BuildRedirectUri_ThrowsWhenTheSchemeOrHostIsMissing()
    {
        var options = Options();
        options.RedirectHost = "   ";

        var build = () => options.BuildRedirectUri();

        build.Should().Throw<InvalidOperationException>().WithMessage("*RedirectHost*");
    }

    [TestMethod]
    public void AuthorityUri_NormalizesToAnAbsoluteUriWithATrailingSlash()
    {
        var options = Options();

        options.AuthorityUri.Should().Be(new Uri($"{Authority}/"));
    }

    [TestMethod]
    public void AuthorityUri_IsNull_WhenTheAuthorityIsBlankOrUnset()
    {
        var options = Options();
        options.Authority = "  ";

        options.AuthorityUri.Should().BeNull("the head must be able to start without a credential configuration and fail only at sign-in.");
    }

    [TestMethod]
    public void AuthorityUri_RejectsARelativeAuthority()
    {
        var options = Options();
        options.Authority = "realms/school-collab";

        var read = () => options.AuthorityUri;

        read.Should().Throw<InvalidOperationException>().WithMessage("*MobileAuth:Authority*");
    }

    [TestMethod]
    public void BuildAuthorizeAndTokenEndpoints_AppendTheKeycloakPaths_ToTheAuthority()
    {
        var options = Options();

        // The trailing-slash normalization is load-bearing: without it `new Uri(authority, path)` would
        // replace the last realm segment instead of appending.
        options.BuildAuthorizeEndpointUri().Should()
            .Be(new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab/protocol/openid-connect/auth").Uri);
        options.BuildTokenEndpointUri().Should()
            .Be(new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab/protocol/openid-connect/token").Uri);
    }

    [TestMethod]
    public void BuildTokenEndpointUri_ThrowsWhenTheAuthorityIsUnset()
    {
        var options = Options();
        options.Authority = null;

        var build = () => options.BuildTokenEndpointUri();

        build.Should().Throw<InvalidOperationException>().WithMessage("*MobileAuth:Authority*");
    }

    [TestMethod]
    public void ResolveScope_RequestsTheScopePlusTheRequiredScopes_Deduplicated()
    {
        var options = Options();
        options.Scope = "openid";
        options.RequiredScopes = ["profile", "openid", " "];

        options.ResolveScope().Should().Be("openid profile",
            "the request scope is Scope plus the required scopes, de-duplicated in request order.");
    }

    [TestMethod]
    public void MobileAuthOptions_ClassCarriesNoBaseAddressLiteral()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "src",
            "Mobile",
            "SchoolCollab.Mobile.Logic",
            "MobileAuthOptions.cs"));

        // Spec D8: no `http(s)://` URL string literal anywhere under src/. The CI guard cannot see one
        // (CrossModuleWiringTests' excludedHosts silently skips localhost), so this class-level check is
        // the mechanical half of the rule for the credential settings.
        source.Should().NotMatchRegex(
            "\"(https?)://[^\"]+\"",
            "an authority or endpoint URL must come from configuration, never from this class.");
    }

    private static MobileAuthOptions Options() => new()
    {
        Authority = Authority,
        ClientId = "school-collab-mobile-client",
        RedirectScheme = "schoolcollab",
        RedirectHost = "signin-oidc",
    };

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    /// <summary>Walks up from the test bin directory to the repository root. THROWS when it cannot be
    /// found, so a moved file cannot make the source-shape assertion pass vacuously (the same
    /// fail-loud rule the architecture tests' discovery helpers follow).</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !Directory.Exists(Path.Combine(directory.FullName, "src", "Mobile", "SchoolCollab.Mobile.Logic")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate src/Mobile/SchoolCollab.Mobile.Logic from " + AppContext.BaseDirectory);
    }
}
