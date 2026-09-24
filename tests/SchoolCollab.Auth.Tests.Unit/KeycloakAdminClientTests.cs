using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// Admin REST client coverage (plan step 6): every operation's exact HTTP method, PATH and
/// request BODY, the token-cache discipline, the admin-base derivation trap, and the failure
/// taxonomy. HTTP is scripted with a hand-written recording handler (dotnet-best-practices:
/// "Scripted HttpMessageHandler — not Moq/NSubstitute"); the repo's MockHttp matcher pitfall is
/// avoided by asserting the explicit <see cref="HttpMethod"/> on every recorded call.
/// </summary>
[TestClass]
public class KeycloakAdminClientTests
{
    private const string Authority = "http://keycloak:8080/realms/school-collab";
    private const string TokenPath = "/realms/school-collab/protocol/openid-connect/token";
    private const string AdminUsersPath = "/admin/realms/school-collab/users";

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A scripted handler that records every request (method, path, query, body, bearer)
    /// and serves the token endpoint with a distinct token per fetch — so a test can distinguish a
    /// single cached fetch from per-call fetching.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public sealed record ObservedRequest(HttpMethod Method, string Path, string Query, string Body, string? Bearer);

        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _respond;

        public List<ObservedRequest> Calls { get; } = [];

        public int TokenFetches { get; private set; }

        /// <param name="respond">Receives the request and the current token-fetch count (1-based);
        /// return the canned response. The token path is routed by the caller for full control.</param>
        public RecordingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                TokenFetches++;
            }

            Calls.Add(new ObservedRequest(
                request.Method,
                request.RequestUri.AbsolutePath,
                request.RequestUri.Query,
                body,
                request.Headers.Authorization is null ? null : request.Headers.Authorization.Parameter));
            return _respond(request, TokenFetches);
        }
    }

    [TestMethod]
    public async Task AdminCall_WithNonRealmAuthority_ThrowsInvalidOperationException()
    {
        // AdminBase derivation must fail loudly on a non-realm-form authority (pass-3b review P2-2):
        // only a /realms/<realm> tail yields a valid Admin REST base. The token fetch must SUCCEED
        // (RoutingHandler serves a valid token) so the flow reaches the admin URL construction,
        // where BuildAdminBase throws — a failing token gate would return early without ever
        // evaluating the admin base.
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var opts = Options();
        opts.Keycloak.Authority = "https://keycloak.example.com/auth";
        var client = new KeycloakAdminClient(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(opts),
            new FakeTimeProvider());

        var act = () => client.ListUsersAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*realms*");
    }

    private static AuthServiceOptions Options() => new()
    {
        Keycloak = new AuthServiceOptions.KeycloakOptions
        {
            Authority = Authority,
            ClientId = "school-collab-client",
            ClientSecret = "a-client-secret",
            ServiceAccountClientSecret = "a-service-account-secret",
        },
    };

    private static KeycloakAdminClient Client(RecordingHandler handler, FakeTimeProvider clock)
        => new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options()), clock);

    /// <summary>Default router: the token path returns a fresh token per fetch; every other path
    /// returns the supplied canned response.</summary>
    private static RecordingHandler RoutingHandler(
        HttpResponseMessage adminResponse,
        HttpResponseMessage? tokenResponse = null)
    {
        return new RecordingHandler((request, fetchCount) =>
            request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal)
                ? tokenResponse ?? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"access_token":"admin-token-{{fetchCount}}","expires_in":300,"token_type":"Bearer"}"""),
                }
                : Canned(adminResponse));
    }

    /// <summary>Replays a canned admin response — its status, its body and its <c>Location</c>
    /// header (the <c>201 Created</c> answer's only report of a new user's id).</summary>
    private static HttpResponseMessage Canned(HttpResponseMessage canned)
    {
        var response = new HttpResponseMessage(canned.StatusCode)
        {
            Content = new StringContent(canned.Content is null
                ? string.Empty
                : canned.Content.ReadAsStringAsync().GetAwaiter().GetResult()),
        };
        response.Headers.Location = canned.Headers.Location;
        return response;
    }

    /// <summary>The realm's claim-attribute map (spec D10) in Keycloak's wire shape — the values the
    /// realm import stores on the dev user (school-collab-realm.json): name → ARRAY of strings.</summary>
    private static Dictionary<string, string[]> DevAttributes() => new(StringComparer.Ordinal)
    {
        ["tenant_id"] = ["00000000-0000-0000-0000-000000000002"],
        ["tenant_name"] = ["Dev School"],
        ["tenant_type"] = ["School"],
        ["teacher_id"] = ["00000000-0000-0000-0000-000000000003"],
    };

    [TestMethod]
    public async Task ListUsers_NoFilters_SendsExplicitGetToTheAdminRealmUsersPath()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                [{"id":"u1","username":"dev-teacher","enabled":true,"email":"dev-teacher@schoolcollab.local"}]
                """),
        });
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.ListUsersAsync();

        result.IsSuccess.Should().BeTrue();
        result.Users.Should().ContainSingle().Which.Username.Should().Be("dev-teacher");

        handler.Calls.Should().HaveCount(2);
        var adminCall = handler.Calls[1];
        adminCall.Method.Should().Be(HttpMethod.Get,
            "the Admin REST list is a GET and the repo's MockHttp matcher will not infer the verb — the explicit method is asserted.");
        adminCall.Path.Should().Be(AdminUsersPath);
        adminCall.Query.Should().BeEmpty();
        adminCall.Bearer.Should().Be("admin-token-1");
    }

    [TestMethod]
    public async Task ListUsers_Filters_SendAnEncodedQueryOnTheUsersPath()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]"),
        });
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.ListUsersAsync(username: "dev teacher", email: "a.b@c.d");

        result.IsSuccess.Should().BeTrue();
        var adminCall = handler.Calls[1];
        adminCall.Path.Should().Be(AdminUsersPath);
        adminCall.Query.Should().Be("?username=dev%20teacher&email=a.b%40c.d");
    }

    [TestMethod]
    public async Task AdminBase_FromARealmFormAuthority_NeverConcatenatesAdminOntoTheRealmPath()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]"),
        });
        var client = Client(handler, new FakeTimeProvider());

        await client.ListUsersAsync();

        // Structural trap: the authority is the REALM url; the Admin base is the SERVER ROOT.
        // A naive concat would produce /realms/school-collab/admin/... — no recorded call may.
        handler.Calls.Select(c => c.Path).Should().NotContain(path => path.Contains("/realms/school-collab/admin", StringComparison.Ordinal));
        handler.Calls[1].Path.Should().Be("/admin/realms/school-collab/users");
        // ... and the token POST uses the realm-form token endpoint, not the admin base.
        handler.Calls[0].Path.Should().Be(TokenPath);
    }

    [TestMethod]
    public async Task AccessToken_IsFetchedOnce_AndReusedAcrossAdminCalls()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]"),
        });
        var client = Client(handler, new FakeTimeProvider());

        await client.ListUsersAsync();
        await client.ListRealmRolesAsync();

        // Distinct per-fetch tokens: both calls must carry the FIRST token, proving a single
        // cached fetch — a per-call fetch would have used "admin-token-2" on the second call.
        handler.TokenFetches.Should().Be(1);
        handler.Calls[1].Bearer.Should().Be("admin-token-1");
        handler.Calls[2].Bearer.Should().Be("admin-token-1");
    }

    [TestMethod]
    public async Task AccessToken_IsRefetched_OnceTheCachedTokenPassesItsSafetyMargin()
    {
        var clock = new FakeTimeProvider();
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]"),
        });
        var client = Client(handler, clock);

        await client.ListUsersAsync();
        handler.Calls[1].Bearer.Should().Be("admin-token-1");

        // expires_in=300, margin=30 -> cache valid ~270 s; advance past it.
        clock.Now = clock.Now.AddSeconds(271);

        await client.ListUsersAsync();

        handler.TokenFetches.Should().Be(2);
        handler.Calls[^1].Bearer.Should().Be("admin-token-2");
    }

    [TestMethod]
    public async Task CreateUser_SendsExplicitPost_WithACamelCaseJsonBody()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.Created));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.CreateUserAsync(new KeycloakUser(
            Id: null, Username: "new-user", Enabled: true, Email: "new@schoolcollab.local"));

        result.IsSuccess.Should().BeTrue();
        var call = handler.Calls[1];
        call.Method.Should().Be(HttpMethod.Post);
        call.Path.Should().Be(AdminUsersPath);

        using var body = JsonDocument.Parse(call.Body);
        var root = body.RootElement;
        root.GetProperty("username").GetString().Should().Be("new-user");
        root.GetProperty("enabled").GetBoolean().Should().BeTrue();
        root.GetProperty("email").GetString().Should().Be("new@schoolcollab.local");
        root.TryGetProperty("id", out _).Should().BeFalse(
            "the create body must not carry an id (Web serialization omits nulls).");
    }

    [TestMethod]
    public async Task CreateUser_WithAttributes_SendsKeycloaksNameToArrayShape()
    {
        // R3 (the AC5 residual): the D10 claim attributes must reach Keycloak in Keycloak's OWN
        // shape — a MAP of name -> ARRAY of strings — which is how the realm import stores them
        // (school-collab-realm.json) and what the realm's four oidc-usermodel-attribute-mappers read
        // back into the token. A bare string is not that shape, so the attribute would never resolve
        // into a claim.
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.Created));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.CreateUserAsync(new KeycloakUser(
            Id: null, Username: "new-user", Attributes: DevAttributes()));

        result.IsSuccess.Should().BeTrue();
        using var body = JsonDocument.Parse(handler.Calls[1].Body);
        var attributes = body.RootElement.GetProperty("attributes");
        attributes.ValueKind.Should().Be(JsonValueKind.Object, "the wire shape is a map, not an array of pairs.");

        // The member itself is camelCase; the attribute NAMES stay the realm's own snake_case names.
        attributes.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "tenant_id", "tenant_name", "tenant_type", "teacher_id");

        attributes.GetProperty("tenant_id").ValueKind.Should().Be(JsonValueKind.Array,
            "a bare string is not Keycloak's shape — the claim would not resolve through the realm's mapper.");
        attributes.GetProperty("tenant_id")[0].GetString().Should().Be("00000000-0000-0000-0000-000000000002");
        attributes.GetProperty("tenant_name")[0].GetString().Should().Be("Dev School");
    }

    [TestMethod]
    public async Task CreateUser_WithoutAttributes_OmitsTheAttributesMember()
    {
        // An identity-only create (D8) round-trips exactly as before this pass: no
        // `"attributes": null` placeholder, because the client omits nulls.
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.Created));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.CreateUserAsync(new KeycloakUser(Id: null, Username: "new-user"));

        result.IsSuccess.Should().BeTrue();
        using var body = JsonDocument.Parse(handler.Calls[1].Body);
        body.RootElement.TryGetProperty("attributes", out _).Should().BeFalse();
    }

    [TestMethod]
    public async Task CreateUser_CapturesTheMintedUserId_FromThe201LocationHeader()
    {
        // Keycloak's 201 carries an EMPTY body; the new user's id exists only in the Location
        // header (`…/users/{id}`). Reading it here is what lets the admin endpoint answer with the
        // id instead of leaving the portal to resolve the new user by username.
        var created = new HttpResponseMessage(HttpStatusCode.Created);
        created.Headers.Location = new Uri(
            "http://keycloak:8080/admin/realms/school-collab/users/2f6a1c0e-1111-4444-8888-999999999999");
        var handler = RoutingHandler(created);
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.CreateUserAsync(new KeycloakUser(Id: null, Username: "new-user"));

        result.IsSuccess.Should().BeTrue();
        result.UserId.Should().Be("2f6a1c0e-1111-4444-8888-999999999999");
        handler.Calls[1].Method.Should().Be(HttpMethod.Post);
    }

    [TestMethod]
    public async Task CreateUser_WithoutALocationHeader_SucceedsWithNoUserId()
    {
        // A 201 without a Location still created the user: the status stays Success and only the id
        // is absent. A missing header narrows the result — it must not turn a success into a failure.
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.Created));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.CreateUserAsync(new KeycloakUser(Id: null, Username: "new-user"));

        result.IsSuccess.Should().BeTrue();
        result.UserId.Should().BeNull();
        result.Detail.Should().BeNull();
    }

    [TestMethod]
    public async Task UpdateUser_SendsExplicitPut_ToTheUsersIdPath()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.UpdateUserAsync(new KeycloakUser(
            Id: "u1", Username: "dev-teacher", Enabled: true, Email: "dev-teacher@schoolcollab.local"));

        result.IsSuccess.Should().BeTrue();
        var call = handler.Calls[1];
        call.Method.Should().Be(HttpMethod.Put);
        call.Path.Should().Be("/admin/realms/school-collab/users/u1");
    }

    [TestMethod]
    public async Task UpdateUser_WithAttributes_SendsKeycloaksNameToArrayShape()
    {
        // The PUT body is the full representation Keycloak replaces the user with, so the D10
        // attributes have to be on it — an update is how the portal's editor persists a change.
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.UpdateUserAsync(new KeycloakUser(
            Id: "u1", Username: "dev-teacher", Attributes: DevAttributes()));

        result.IsSuccess.Should().BeTrue();
        var call = handler.Calls[1];
        call.Path.Should().Be("/admin/realms/school-collab/users/u1");

        using var body = JsonDocument.Parse(call.Body);
        var attributes = body.RootElement.GetProperty("attributes");
        attributes.GetProperty("teacher_id").ValueKind.Should().Be(JsonValueKind.Array);
        attributes.GetProperty("teacher_id")[0].GetString().Should().Be("00000000-0000-0000-0000-000000000003");
        attributes.GetProperty("tenant_type")[0].GetString().Should().Be("School");
    }

    [TestMethod]
    public async Task GetUser_ReadsTheAttributeMapInKeycloaksArrayShape()
    {
        // The read half of the D10 editor: Keycloak answers with the same map-of-arrays, and the
        // representation must carry it, or the editor would render empty fields for a user whose
        // attributes are set.
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"id":"u1","username":"dev-teacher","attributes":{"tenant_id":["00000000-0000-0000-0000-000000000002"],"teacher_id":["00000000-0000-0000-0000-000000000003"]}}
                """),
        });
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.GetUserAsync("u1");

        result.IsSuccess.Should().BeTrue();
        result.User!.Attributes!["tenant_id"].Should().Equal("00000000-0000-0000-0000-000000000002");
        result.User.Attributes["teacher_id"][0].Should().Be("00000000-0000-0000-0000-000000000003");
    }

    [TestMethod]
    public async Task GetUser_WithAnUnknownId_MapsNotFoundToNotFoundStatus()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"error":"unknown_user","error_description":"User not found"}"""),
        });
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.GetUserAsync("missing");

        result.Status.Should().Be(AdminStatus.NotFound);
        result.User.Should().BeNull();
        result.Detail.Should().Contain("User not found");
    }

    [TestMethod]
    public async Task CreateUser_OnKeycloakConflict_MapsToConflictStatus()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.Conflict));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.CreateUserAsync(new KeycloakUser(Id: null, Username: "dev-teacher"));

        result.Status.Should().Be(AdminStatus.Conflict);
        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task AdminCall_OnForbidden_MapsToForbiddenStatus()
    {
        // The loud-403 cold-start check: an insufficient imported realm-management role set must
        // surface as Forbidden (a distinct class), never as some generic failure.
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.ListRealmRolesAsync();

        result.Status.Should().Be(AdminStatus.Forbidden);
    }

    [TestMethod]
    public async Task ListRealmRoles_SendsExplicitGet_ToTheAdminRolesPath()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                [{"id":"r1","name":"user-admin","composite":false},{"id":"r2","name":"platform-admin","composite":false}]
                """),
        });
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.ListRealmRolesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Roles.Should().HaveCount(2);
        result.Roles[0].Should().Be(new KeycloakRole("r1", "user-admin"));
        var call = handler.Calls[1];
        call.Method.Should().Be(HttpMethod.Get);
        call.Path.Should().Be("/admin/realms/school-collab/roles");
    }

    [TestMethod]
    public async Task ResetPassword_SendsExplicitPut_WithTypePasswordBody_AndMapsNoContentToSuccess()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.ResetPasswordAsync("u1", "new-password");

        result.IsSuccess.Should().BeTrue();
        var call = handler.Calls[1];
        call.Method.Should().Be(HttpMethod.Put);
        call.Path.Should().Be("/admin/realms/school-collab/users/u1/reset-password");

        using var body = JsonDocument.Parse(call.Body);
        var root = body.RootElement;
        root.GetProperty("type").GetString().Should().Be("password");
        root.GetProperty("value").GetString().Should().Be("new-password");
        root.GetProperty("temporary").GetBoolean().Should().BeFalse();
    }

    [TestMethod]
    public async Task AssignRealmRoles_SendsExplicitPost_ToRoleMappingsRealm_WithTheIdNameArray()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.AssignRealmRolesAsync("u1", new[] { new KeycloakRole("r1", "user-admin") });

        result.IsSuccess.Should().BeTrue();
        var call = handler.Calls[1];
        call.Method.Should().Be(HttpMethod.Post);
        call.Path.Should().Be("/admin/realms/school-collab/users/u1/role-mappings/realm");

        var roles = JsonSerializer.Deserialize<List<JsonElement>>(call.Body);
        roles.Should().ContainSingle();
        roles![0].GetProperty("id").GetString().Should().Be("r1");
        roles[0].GetProperty("name").GetString().Should().Be("user-admin");
    }

    [TestMethod]
    public async Task UnassignRealmRoles_SendsExplicitDelete_ToRoleMappingsRealm_WithTheArrayBody()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.UnassignRealmRolesAsync("u1", new[] { new KeycloakRole("r1", "user-admin") });

        result.IsSuccess.Should().BeTrue();
        var call = handler.Calls[1];
        call.Method.Should().Be(HttpMethod.Delete);
        call.Path.Should().Be("/admin/realms/school-collab/users/u1/role-mappings/realm");
        call.Body.Should().Contain("\"id\":\"r1\"");
    }

    [TestMethod]
    public async Task TokenEndpoint_UsesClientCredentials_WithTheServiceAccountClient()
    {
        var handler = RoutingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]"),
        });
        var client = Client(handler, new FakeTimeProvider());

        await client.ListUsersAsync();

        var tokenCall = handler.Calls[0];
        tokenCall.Path.Should().Be(TokenPath);
        tokenCall.Method.Should().Be(HttpMethod.Post);

        var form = System.Web.HttpUtility.ParseQueryString(tokenCall.Body);
        form["grant_type"].Should().Be("client_credentials");
        form["client_id"].Should().Be("school-collab-auth-admin");
        form["client_secret"].Should().Be("a-service-account-secret");
    }

    [TestMethod]
    public async Task AdminCall_WhenTheTokenEndpointFails_MapsToUnreachableWithNoRequest()
    {
        var handler = RoutingHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") },
            tokenResponse: new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.ListUsersAsync();

        result.Status.Should().Be(AdminStatus.Unreachable);
        handler.Calls.Should().HaveCount(1,
            "with no token no admin request may be sent (the secret never leaks past the token call).");
    }

    [TestMethod]
    public async Task AdminCall_WhenTheEndpointThrows_MapsToUnreachable()
    {
        var handler = new RecordingHandler((request, fetchCount) =>
            request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"access_token":"admin-token-{{fetchCount}}","expires_in":300,"token_type":"Bearer"}"""),
                }
                : throw new HttpRequestException("connection refused"));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.ListUsersAsync();

        result.Status.Should().Be(AdminStatus.Unreachable);
    }

    [TestMethod]
    public async Task CallerCancellation_IsRethrownNotSwallowed()
    {
        var handler = new RecordingHandler((_, _) => throw new OperationCanceledException("cancelled"));
        var client = Client(handler, new FakeTimeProvider());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => client.ListUsersAsync(cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "caller cancellation must propagate — it is not an Admin failure to be consumed into a Status result.");
    }

    [TestMethod]
    public async Task HttpClientTimeout_WithoutCallerCancellation_MapsToUnreachable()
    {
        var handler = new RecordingHandler((_, _) => throw new OperationCanceledException("timeout"));
        var client = Client(handler, new FakeTimeProvider());

        var result = await client.ListUsersAsync();

        result.Status.Should().Be(AdminStatus.Unreachable);
    }
}
