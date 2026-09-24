using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Core.Tests.Unit.Auth;

/// <summary>
/// Round-B pass B4 — the Blazor hosts' half of the flag-ON handshake (spec §5.2 / D6, design P1-3):
/// the typed redemption client, the <c>/signin-handshake</c> redemption endpoint that signs the
/// caller into the host's OWN cookie, and the <c>/login</c>/<c>/logout</c> endpoints the hosts
/// lacked (spec §13). The tests run a real in-process Kestrel host composed from the public
/// extensions only, with the auth service's redemption endpoint replaced by a scripted handler —
/// no socket ever reaches Keycloak or the auth service.
/// </summary>
/// <remarks>
/// The tests pin: the redeem request contract (the code plus this host's EXACT callback URI — the
/// URI binding the auth service enforces), the claim set that reaches the cookie INCLUDING roles
/// under <see cref="ClaimTypes.Role"/> (the D9 parity axis, proven by authenticating the cookie
/// through the host's own pipeline), fail-closed rejection (replay/TTL/unreachable: no cookie, no
/// redirect, no failure class disclosed), the open-redirect guard on <c>ReturnUrl</c>, and the
/// challenge/sign-out delegation (flag ON → portal login page via
/// <see cref="PortalRedirectChallengeHandler"/>; flag OFF → Keycloak, i.e. today's behaviour, AC7).
/// </remarks>
[TestClass]
public class PortalHandshakeTests
{
    private const string PortalLoginUrl = "https://localhost:5600/login";
    private const string KeycloakRealmUrl = "http://localhost:1/realms/school-collab";
    private const string KeycloakAuthorizeEndpoint = KeycloakRealmUrl + "/protocol/openid-connect/auth";
    private const string KeycloakEndSessionEndpoint = KeycloakRealmUrl + "/protocol/openid-connect/logout";
    private const string ReturnUrl = "/admin/students?page=2";
    private const string TenantId = "00000000-0000-0000-0000-000000000002";
    private const string TeacherId = "00000000-0000-0000-0000-000000000003";

    /// <summary>
    /// The auth service's redemption body, emitted exactly the way the REAL service emits it: its
    /// <c>PortalClaims</c> record (PascalCase) serialized by the minimal API's web defaults, so the
    /// bytes that reach the client are camelCase (<c>tenantId</c>, <c>teacherId</c>, <c>roles</c>) —
    /// verified against <c>RedeemEndpointTests</c>, which asserts those property names on the live
    /// endpoint. Deliberately NOT a hand-shaped literal of the host's own DTO: a fixture that mirrors
    /// the reader instead of the producer cannot catch a casing/options mismatch.
    /// </summary>
    private static string ClaimSetBody(IReadOnlyList<string>? roles = null) =>
        JsonSerializer.Serialize(
            new ServiceClaimSet(TenantId, "Dev School", "School", TeacherId, roles ?? ["user-admin"]),
            WebJson);

    /// <summary>The auth service's <c>PortalClaims</c> shape (PascalCase properties, D9).</summary>
    private sealed record ServiceClaimSet(
        string TenantId,
        string TenantName,
        string TenantType,
        string TeacherId,
        IReadOnlyList<string> Roles);

    /// <summary>The auth service's serializer settings (the minimal-API web defaults).</summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // ── the tests ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task Handshake_WithAValidCode_SignsTheCallerIntoTheHostCookieWithTheOidcClaimSet()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: true);
        host.Auth.Respond(HttpStatusCode.OK, ClaimSetBody());

        // The fixture speaks the PRODUCER's shape: the auth service serializes its PortalClaims
        // record with the minimal-API web defaults, so camelCase is what reaches the client. Pinned
        // here so a PascalCase (or hand-shaped) fixture cannot quietly come back and hide a
        // casing/options mismatch between this client and the service.
        ClaimSetBody().Should().Contain("\"tenantId\"").And.Contain("\"teacherId\"");

        var response = await host.Client.GetAsync(HandshakeUrl(ReturnUrl, "one-time-code"));

        response.StatusCode.Should().Be(
            HttpStatusCode.Redirect,
            "a redeemed code continues to the page the challenge blocked (D6 step 5)");
        response.Headers.Location!.ToString().Should().Be(ReturnUrl);

        var cookie = SingleCookie(response);
        cookie.Should().StartWith(
            $"{CookieName}=",
            "the host signs in with its OWN cookie — the same mechanism the OIDC path uses");

        // The proof that matters: the host's real authentication pipeline accepts that cookie and
        // resolves the claim set the auth service returned, roles included.
        using var authed = host.ClientWithCookie(cookie);
        var claims = await authed.GetFromJsonAsync<JsonElement>("/probe/claims");

        claims.GetProperty("isAuthenticated").GetBoolean().Should().BeTrue();
        claims.GetProperty("tenantId").GetString().Should().Be(TenantId);
        claims.GetProperty("tenantName").GetString().Should().Be("Dev School");
        claims.GetProperty("tenantType").GetString().Should().Be("School");
        claims.GetProperty("teacherId").GetString().Should().Be(TeacherId);
        claims.GetProperty("roles")[0].GetString().Should().Be("user-admin");
        claims.GetProperty("isAdmin").GetBoolean().Should().BeTrue(
            "roles must land under ClaimTypes.Role — the type the OIDC handler's default inbound "
            + "mapping produces (D9) — or RequireRole would resolve on one path only");
    }

    [TestMethod]
    public async Task Handshake_PostsTheCodeToTheAuthService_BoundToThisHostsExactCallbackUri()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: true);
        host.Auth.Respond(HttpStatusCode.OK, ClaimSetBody());

        await host.Client.GetAsync(HandshakeUrl(ReturnUrl, "one-time-code"));

        var request = host.Auth.CapturedRequests.Should().ContainSingle().Subject;
        request.Method.Should().Be("POST");
        request.PathAndQuery.Should().Be(PortalHandshakeClient.RedeemPath);
        request.Body.Should().Contain("\"one-time-code\"");

        // The auth service rejects a redemption whose redirect URI differs from the code's binding
        // (redirect_uri_mismatch), so this EXACT value is the contract: scheme + host + port of the
        // origin the browser used + the fixed callback path + the escaped blocked path — the shape
        // PortalRedirectChallengeHandler minted at exchange time.
        request.RedirectUri.Should().Be(
            host.BaseUrl + PortalRedirectChallengeHandler.HandshakeCallbackPath
            + $"?{PortalRedirectChallengeHandler.ReturnUrlParameter}={Uri.EscapeDataString(ReturnUrl)}");
    }

    [TestMethod]
    public async Task Handshake_WhenTheCodeWasAlreadyRedeemed_IssuesNoCookieAndFailsClosed()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: true);
        host.Auth.Respond(HttpStatusCode.NotFound, """{"error":"invalid_code"}""");

        var response = await host.Client.GetAsync(HandshakeUrl(ReturnUrl, "replayed-code"));

        await AssertFailClosedAsync(response);
    }

    [TestMethod]
    public async Task Handshake_WhenTheCodeExpired_IssuesNoCookieAndFailsClosed()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: true);
        host.Auth.Respond(HttpStatusCode.BadRequest, """{"error":"code_expired"}""");

        var response = await host.Client.GetAsync(HandshakeUrl(ReturnUrl, "expired-code"));

        await AssertFailClosedAsync(response);
    }

    [TestMethod]
    public async Task Handshake_WhenTheAuthServiceIsUnreachable_IssuesNoCookieAndFailsClosed()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: true);
        host.Auth.Respond(HttpStatusCode.BadGateway, """{"error":"upstream_unreachable"}""");

        var response = await host.Client.GetAsync(HandshakeUrl(ReturnUrl, "one-time-code"));

        await AssertFailClosedAsync(response);
    }

    [TestMethod]
    public async Task Handshake_WithoutACode_NeverCallsTheAuthService()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: true);
        host.Auth.Respond(HttpStatusCode.OK, ClaimSetBody());

        var response = await host.Client.GetAsync(
            PortalRedirectChallengeHandler.HandshakeCallbackPath
            + $"?{PortalRedirectChallengeHandler.ReturnUrlParameter}={Uri.EscapeDataString(ReturnUrl)}");

        await AssertFailClosedAsync(response);
        host.Auth.CapturedRequests.Should().BeEmpty(
            "there is nothing to redeem without a code, so the host must not touch the auth service");
    }

    [TestMethod]
    public async Task Handshake_WhenReturnUrlIsNotSiteLocal_DoesNotRedirectOffSite()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: true);
        host.Auth.Respond(HttpStatusCode.OK, ClaimSetBody());

        var response = await host.Client.GetAsync(HandshakeUrl("//attacker.example/steal", "one-time-code"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(
            "/",
            "ReturnUrl is caller-supplied on a public endpoint — an absolute or protocol-relative "
            + "value must never become the post-handshake Location (open-redirect guard)");
    }

    [TestMethod]
    public async Task Login_WhenTheLoginUiFlagIsOn_RedirectsToThePortalLoginWithTheHandshakeCallback()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: true);

        var response = await host.Client.GetAsync(PortalHandshakeExtensions.LoginPath);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith(PortalLoginUrl + "?");

        var returnUri = ExtractReturnUri(location);
        returnUri.Should().Be(
            host.BaseUrl + PortalRedirectChallengeHandler.HandshakeCallbackPath
            + $"?{PortalRedirectChallengeHandler.ReturnUrlParameter}="
            + Uri.EscapeDataString(PortalHandshakeExtensions.LoginPath),
            "login defers to the same challenge the policy scheme selects, so the callback the portal "
            + "receives is byte-identical to a gated page's challenge (D6/D5)");
    }

    [TestMethod]
    public async Task Login_WhenTheLoginUiFlagIsOff_ChallengesKeycloakAsBefore()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: false);

        var response = await host.Client.GetAsync(PortalHandshakeExtensions.LoginPath);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith(
            KeycloakAuthorizeEndpoint,
            "with the flag OFF the new login route must present today's login UI — Keycloak's hosted "
            + "page (AC7), never the portal");
        host.Auth.CapturedRequests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Login_WhenAlreadySignedIn_SendsTheCallerOnToTheRequestedPage()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: true);
        host.Auth.Respond(HttpStatusCode.OK, ClaimSetBody());

        var handshake = await host.Client.GetAsync(HandshakeUrl(ReturnUrl, "one-time-code"));
        using var authed = host.ClientWithCookie(SingleCookie(handshake));

        var response = await authed.GetAsync(PortalHandshakeExtensions.LoginPath + "?returnUrl=/admin/students");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(
            "/admin/students",
            "the post-handshake continuation comes back through /login; re-challenging an "
            + "authenticated caller would loop through the portal");
    }

    [TestMethod]
    public async Task Logout_SignsOutThroughTheOidcHandler()
    {
        await using var host = await HandshakeTestHost.StartAsync(loginUiFlagOn: true);
        host.Auth.Respond(HttpStatusCode.OK, ClaimSetBody());

        var handshake = await host.Client.GetAsync(HandshakeUrl(ReturnUrl, "one-time-code"));
        using var authed = host.ClientWithCookie(SingleCookie(handshake));

        var response = await authed.GetAsync(PortalHandshakeExtensions.LogoutPath);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith(
            KeycloakEndSessionEndpoint,
            "logout ends the central Keycloak SSO session through the OIDC handler (D13)");
        ExtractQueryParameter(location, "post_logout_redirect_uri").Should().Be(
            host.BaseUrl + "/signout-callback-oidc",
            "the OIDC handler builds post_logout_redirect_uri from its own SignedOutCallbackPath — "
            + "the per-host URI the realm registers (school-collab-realm.json), which is the only "
            + "value Keycloak accepts as a post-logout target");
        response.Headers.GetValues("Set-Cookie").Should().Contain(
            header => header.StartsWith(
                $"{CookieName}=",
                StringComparison.Ordinal),
            "the host clears its own cookie on the way out (D13)");
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static string HandshakeUrl(string returnUrl, string code)
        => PortalRedirectChallengeHandler.HandshakeCallbackPath
           + $"?{PortalRedirectChallengeHandler.ReturnUrlParameter}={Uri.EscapeDataString(returnUrl)}"
           + $"&code={code}";

    /// <summary>The host cookie's name: the cookie handler's prefix + the scheme name (the scheme
    /// name alone is NOT the cookie name).</summary>
    private static readonly string CookieName = CookieAuthenticationDefaults.CookiePrefix
        + CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>The single <c>Set-Cookie</c> the host emitted, reduced to its name=value pair.</summary>
    private static string SingleCookie(HttpResponseMessage response)
        => response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

    /// <summary>Decodes one query parameter of an absolute URL.</summary>
    private static string ExtractQueryParameter(string url, string name)
    {
        var query = url[(url.IndexOf('?', StringComparison.Ordinal) + 1)..];
        var parameter = query.Split('&')
            .Single(part => part.StartsWith($"{name}=", StringComparison.Ordinal));

        return Uri.UnescapeDataString(parameter[(name.Length + 1)..]);
    }

    private static string ExtractReturnUri(string loginRedirect)
    {
        var query = loginRedirect[(loginRedirect.IndexOf('?', StringComparison.Ordinal) + 1)..];
        var parameter = query.Split('&')
            .Single(part => part.StartsWith(
                $"{PortalRedirectChallengeHandler.ReturnUriParameter}=", StringComparison.Ordinal));

        return Uri.UnescapeDataString(
            parameter[(PortalRedirectChallengeHandler.ReturnUriParameter.Length + 1)..]);
    }

    private static async Task AssertFailClosedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "a rejected handshake must fail closed with one bounded status — never a redirect and "
            + "never a distinguishable failure class (no oracle for code probing)");
        response.Headers.Contains("Set-Cookie").Should().BeFalse("no identity may be established");
        response.Headers.Contains("Location").Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().NotContain(TenantId);
    }

    /// <summary>
    /// A real in-process Kestrel host for the hosts' handshake surface, composed from the PUBLIC
    /// extensions exactly as <c>Admin/Program.cs</c> and <c>Families/Program.cs</c> do
    /// (<c>AddAuthAndTenancy</c> + <c>AddPortalHandshake</c> + <c>MapPortalHandshakeEndpoints</c>),
    /// mirroring <c>AuthEndpointTestHost</c>'s no-new-package approach.
    /// <para>
    /// Two test-host adjustments, both documented: the OIDC metadata is a static configuration
    /// (so the challenge and sign-out paths never open a socket to a real Keycloak), and the
    /// redemption client's primary handler is the <see cref="ScriptedHandler"/> (no test touches the
    /// auth service over the network).
    /// </para>
    /// </summary>
    private sealed class HandshakeTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private HandshakeTestHost(WebApplication app, HttpClient client, ScriptedHandler auth, string baseUrl)
        {
            _app = app;
            _client = client;
            Auth = auth;
            BaseUrl = baseUrl;
        }

        /// <summary>Configured client rooted at the ephemeral listener; redirects are NOT followed
        /// so every test can assert the exact Location and Set-Cookie the host produced.</summary>
        public HttpClient Client => _client;

        /// <summary>The scripted auth service (the typed client's primary handler).</summary>
        public ScriptedHandler Auth { get; }

        /// <summary>The listener's base URL — the "scheme://host:port" the challenge handler derives
        /// the handshake callback from.</summary>
        public string BaseUrl { get; }

        public static async Task<HandshakeTestHost> StartAsync(bool loginUiFlagOn)
        {
            var builder = WebApplication.CreateBuilder();

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "false",
                ["FeatureFlags:FEATURE:DisableKeycloakLoginUi"] = loginUiFlagOn ? "true" : "false",
                ["Auth:Keycloak:Authority"] = KeycloakRealmUrl,
                ["Auth:Keycloak:ClientId"] = "school-collab-client",
                ["Auth:Keycloak:ClientSecret"] = "test-client-secret",
                ["Auth:Portal:LoginUrl"] = PortalLoginUrl,
            });

            // Reserve a free loopback port up front (WebApplication.Urls keeps a literal :0), then
            // bind it — the same ephemeral-port pattern AuthEndpointTestHost uses.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var baseUrl = $"http://127.0.0.1:{port}";
            builder.WebHost.UseUrls(baseUrl);

            builder.Services.AddLogging();
            builder.Services.AddDataProtection();
            builder.Services.AddDistributedMemoryCache();

            // The REAL registrations — exactly what both hosts call.
            builder.Services.AddAuthAndTenancy(builder.Configuration);
            builder.Services.AddPortalHandshake("https+http://auth");

            var auth = new ScriptedHandler();
            builder.Services.AddSingleton(auth);
            builder.Services.AddHttpClient<PortalHandshakeClient>()
                .ConfigurePrimaryHttpMessageHandler(() => auth);

            // Hermetic OIDC: a static metadata document, so the challenge and the sign-out redirects
            // are built without a discovery round-trip (the real handlers stay fully real).
            builder.Services.PostConfigure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme,
                options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = KeycloakRealmUrl,
                        AuthorizationEndpoint = KeycloakAuthorizeEndpoint,
                        TokenEndpoint = KeycloakRealmUrl + "/protocol/openid-connect/token",
                        EndSessionEndpoint = KeycloakEndSessionEndpoint,
                    };

                    options.Authority = null;
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.RequireHttpsMetadata = false;
                });

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapPortalHandshakeEndpoints();

            // Test-only probe: proves the cookie this host's own pipeline reads resolves the claims.
            app.MapGet("/probe/claims", (ClaimsPrincipal user) => Results.Ok(new
            {
                isAuthenticated = user.Identity?.IsAuthenticated ?? false,
                tenantId = user.FindFirst("tenant_id")?.Value,
                tenantName = user.FindFirst("tenant_name")?.Value,
                tenantType = user.FindFirst("tenant_type")?.Value,
                teacherId = user.FindFirst("teacher_id")?.Value,
                roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(),
                isAdmin = user.IsInRole("user-admin"),
            }));

            await app.StartAsync();

            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(baseUrl),
            };

            return new HandshakeTestHost(app, client, auth, baseUrl);
        }

        /// <summary>A client carrying the given cookie pair, for the authenticated follow-up.</summary>
        public HttpClient ClientWithCookie(string cookie)
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(BaseUrl),
            };
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// Scripted <see cref="HttpMessageHandler"/> standing in for the auth service's redemption
    /// endpoint — the repo's HTTP-testing pattern (never Moq for HTTP). The default answer is a
    /// bare 502, so a test that forgets to script the outcome fails loudly instead of passing
    /// vacuously.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        /// <summary>One captured redemption request: method, path and the two body fields the
        /// contract is about.</summary>
        public sealed record Captured(string Method, string PathAndQuery, string Body, string? RedirectUri);

        private readonly ConcurrentQueue<Captured> _captured = new();
        private HttpStatusCode _status = HttpStatusCode.BadGateway;
        private string _body = "";

        public IReadOnlyList<Captured> CapturedRequests => _captured.ToArray();

        /// <summary>Scripts the answer the next redemption receives.</summary>
        public void Respond(HttpStatusCode status, string json)
        {
            _status = status;
            _body = json;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            string? redirectUri = null;
            if (body.Length > 0)
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("redirectUri", out var value))
                {
                    redirectUri = value.GetString();
                }
            }

            _captured.Enqueue(new Captured(
                request.Method.Method,
                request.RequestUri!.PathAndQuery,
                body,
                redirectUri));

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
