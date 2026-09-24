using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SchoolCollab.Auth.Options;

namespace SchoolCollab.Auth.Services;

/// <summary>Outcome status of an Admin REST operation.</summary>
public enum AdminStatus
{
    /// <summary>The operation succeeded (2xx).</summary>
    Success,

    /// <summary>The Admin REST endpoint returned 404 (user or role not found).</summary>
    NotFound,

    /// <summary>The Admin REST endpoint returned 409 (e.g. duplicate username on create).</summary>
    Conflict,

    /// <summary>The Admin REST endpoint returned 401 (token missing/expired/revoked).</summary>
    Unauthorized,

    /// <summary>The Admin REST endpoint returned 403 — the service account lacks the
    /// required <c>realm-management</c> role. This is the loud failure the cold start
    /// relies on: an insufficient imported role set must surface here instead of as
    /// a generic error.</summary>
    Forbidden,

    /// <summary>The endpoint did not answer (transport failure, 5xx, malformed body).</summary>
    Unreachable,
}

/// <summary>
/// Result of a <see cref="KeycloakAdminClient"/> mutation. <see cref="Detail"/> carries only
/// Keycloak's own error text — never a token, secret or password (AC9; the auth service is
/// the only credential holder).
/// </summary>
public sealed record AdminOperationResult(AdminStatus Status, string? Detail = null)
{
    public bool IsSuccess => Status == AdminStatus.Success;
}

/// <summary>Result of <see cref="KeycloakAdminClient.ListUsersAsync"/>.</summary>
public sealed record AdminUserListResult(AdminStatus Status, IReadOnlyList<KeycloakUser> Users, string? Detail = null)
{
    public bool IsSuccess => Status == AdminStatus.Success;
}

/// <summary>Result of <see cref="KeycloakAdminClient.GetUserAsync"/>.</summary>
public sealed record AdminUserGetResult(AdminStatus Status, KeycloakUser? User = null, string? Detail = null)
{
    public bool IsSuccess => Status == AdminStatus.Success;
}

/// <summary>Result of <see cref="KeycloakAdminClient.CreateUserAsync"/>. <see cref="UserId"/> is
/// the id Keycloak minted, read from the <c>201 Created</c> response's <c>Location</c> header
/// (<c>…/users/{id}</c>) — the only place Keycloak reports it. It is <c>null</c> when the Admin
/// REST answered without a usable location: the create still succeeded, so a missing id narrows
/// the result rather than failing it.</summary>
public sealed record AdminUserCreateResult(AdminStatus Status, string? UserId = null, string? Detail = null)
{
    public bool IsSuccess => Status == AdminStatus.Success;
}

/// <summary>Result of <see cref="KeycloakAdminClient.ListRealmRolesAsync"/>.</summary>
public sealed record AdminRealmRoleListResult(AdminStatus Status, IReadOnlyList<KeycloakRole> Roles, string? Detail = null)
{
    public bool IsSuccess => Status == AdminStatus.Success;
}

/// <summary>A minimal Admin REST user representation (camelCase JSON on the wire).
/// <para>
/// <see cref="Attributes"/> is the realm's claim-attribute map (spec D10) and its wire shape is
/// <b>name → array of strings</b> — <c>{"tenant_id":["…"],"teacher_id":["…"]}</c> — exactly how
/// the realm import stores the dev user (school-collab-realm.json) and exactly what the realm's
/// four <c>oidc-usermodel-attribute-mapper</c>s read back into the token. A bare string is not an
/// accepted shorthand: it is not Keycloak's shape, so the claim would not resolve. Attribute names
/// stay verbatim (the realm's snake_case names); only the member itself is camelCase. An absent map
/// is omitted from the body rather than written as <c>null</c>.
/// </para>
/// </summary>
public sealed record KeycloakUser(
    string? Id,
    string Username,
    bool Enabled = true,
    string? Email = null,
    string? FirstName = null,
    string? LastName = null,
    IReadOnlyDictionary<string, string[]>? Attributes = null);

/// <summary>
/// A minimal realm role representation. <see cref="Id"/> is what Keycloak's
/// role-mappings endpoints resolve on, so callers must use the id returned by
/// <see cref="KeycloakAdminClient.ListRealmRolesAsync"/> (the name alone is not sufficient).
/// </summary>
public sealed record KeycloakRole(string Id, string Name);

/// <summary>
/// Admin REST client for the <c>school-collab-auth-admin</c> service-account client (spec
/// §13 / §16.2). Two structural traps this class exists to get right: (a) the configured
/// <c>Auth:Keycloak:Authority</c> is the REALM URL (<c>…/realms/school-collab</c>) while the
/// Admin REST base is the SERVER ROOT plus <c>/admin/realms/{realm}/…</c> — the root and realm
/// are derived from the authority, never concatenated onto the realm path; (b) the admin token
/// is fetched ONCE per expiry window and cached (with a safety margin on the injected
/// <see cref="TimeProvider"/>), never requested per call. The client secret and the admin token
/// stay inside this service: nothing is logged and neither ever reaches a response body.
/// </summary>
public sealed class KeycloakAdminClient(
    HttpClient httpClient,
    IOptions<AuthServiceOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Client id of the realm import's service-account client (school-collab-realm.json).
    /// It is a fixed, declarative realm constant rather than an option because the realm is
    /// fully declarative (D3) and the parity guard already pins its secret; the Direct-Grant
    /// <c>ClientId</c> option is the OIDC app client and is deliberately NOT reused here.
    /// </summary>
    private const string ServiceAccountClientId = "school-collab-auth-admin";

    /// <summary>Fetched tokens are considered stale this many seconds before their real expiry,
    /// so a slow Admin call cannot race the token's actual expiry.</summary>
    private const int TokenExpirySafetyMarginSeconds = 30;

    /// <summary>CamelCase (Keycloak's wire convention) with nulls omitted — a create body must
    /// not carry <c>"id": null</c> placeholders. Reads tolerate both forms.</summary>
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string? _cachedAccessToken;
    private DateTimeOffset _accessTokenExpiresAtUtc;
    private string? _adminBase;

    /// <summary>
    /// The Admin REST base URL — <c>{serverRoot}/admin/realms/{realm}</c> — derived from the
    /// realm-form <c>Auth:Keycloak:Authority</c>. Derived once and cached; throws on a
    /// malformed authority so a misconfiguration surfaces loudly instead of 404ing per call.
    /// </summary>
    private string AdminBase => _adminBase ??= BuildAdminBase(Options.Keycloak.Authority);

    private AuthServiceOptions Options => options.Value;

    private static string BuildAdminBase(string authority)
    {
        var uri = new Uri(authority);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !string.Equals(segments[^2], "realms", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The Auth:Keycloak:Authority must end in /realms/<realm> so the Admin REST base can be derived (got: \"{authority}\").");
        }

        var realm = segments[^1];
        var root = new UriBuilder(uri) { Path = "/" }.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return $"{root}/admin/realms/{realm}";
    }

    /// <summary>Sends an authorized Admin REST request. Returns the raw response on success, or a
    /// typed failure for the transport cases (endpoint silent, HTTP timeout, no token).</summary>
    private async Task<SendResult> SendAuthorizedAsync(
        HttpMethod method,
        string path,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        if (token is null)
        {
            return new SendResult(null, new AdminOperationResult(AdminStatus.Unreachable,
                "the Admin REST token could not be obtained"));
        }

        using var request = new HttpRequestMessage(method, new Uri($"{AdminBase}{path}", UriKind.Absolute))
        {
            Content = jsonBody is null
                ? null
                : new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var response = await httpClient.SendAsync(request, cancellationToken);
            return new SendResult(response, null);
        }
        catch (HttpRequestException)
        {
            // Network / TLS / DNS: the endpoint did not answer at all.
            return new SendResult(null, new AdminOperationResult(AdminStatus.Unreachable));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation is not an Admin failure — rethrow, never swallow.
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout: the endpoint did not answer in time.
            return new SendResult(null, new AdminOperationResult(AdminStatus.Unreachable));
        }
    }

    /// <summary>
    /// Returns the cached admin token, or fetches one via <c>client_credentials</c> against the
    /// token endpoint. The cache is valid only until shortly before the token's real expiry
    /// (<see cref="TokenExpirySafetyMarginSeconds"/>), checked on the injected
    /// <see cref="TimeProvider"/> so tests can fake the clock. Never logs the token or the secret.
    /// </summary>
    private async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedAccessToken is not null && timeProvider.GetUtcNow() < _accessTokenExpiresAtUtc)
        {
            return _cachedAccessToken;
        }

        var endpoint = new Uri($"{Options.Keycloak.Authority.TrimEnd('/')}/protocol/openid-connect/token");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ServiceAccountClientId,
            ["client_secret"] = Options.Keycloak.ServiceAccountClientSecret,
        });

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(endpoint, form, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var token = root.TryGetProperty("access_token", out var accessToken) ? accessToken.GetString() : null;
                var expiresIn = root.TryGetProperty("expires_in", out var expires)
                    && expires.TryGetInt32(out var seconds)
                        ? seconds
                        : 0;
                if (string.IsNullOrEmpty(token))
                {
                    return null;
                }

                var margin = expiresIn > TokenExpirySafetyMarginSeconds * 2
                    ? TokenExpirySafetyMarginSeconds
                    : expiresIn / 2;
                _cachedAccessToken = token;
                _accessTokenExpiresAtUtc = timeProvider.GetUtcNow().AddSeconds(expiresIn - margin);
                return token;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>Lists realm users, optionally filtered by username and/or email (spec §13).</summary>
    public async Task<AdminUserListResult> ListUsersAsync(
        string? username = null,
        string? email = null,
        CancellationToken cancellationToken = default)
    {
        var filters = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(username))
        {
            filters.Add($"username={Uri.EscapeDataString(username)}");
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            filters.Add($"email={Uri.EscapeDataString(email)}");
        }

        var query = filters.Count == 0 ? string.Empty : "?" + string.Join("&", filters);

        var send = await SendAuthorizedAsync(HttpMethod.Get, $"/users{query}", jsonBody: null, cancellationToken);
        if (send.Failure is not null)
        {
            return new AdminUserListResult(send.Failure.Status, Array.Empty<KeycloakUser>(), send.Failure.Detail);
        }

        using var response = send.Response!;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var failure = Classify(response, body);
            return new AdminUserListResult(failure.Status, Array.Empty<KeycloakUser>(), failure.Detail);
        }

        try
        {
            var users = JsonSerializer.Deserialize<List<KeycloakUser>>(body, _json) ?? [];
            return new AdminUserListResult(AdminStatus.Success, users, null);
        }
        catch (JsonException)
        {
            return new AdminUserListResult(AdminStatus.Unreachable, Array.Empty<KeycloakUser>(),
                "the Admin REST returned a malformed user list");
        }
    }

    /// <summary>Gets one realm user by id. A 404 maps to <see cref="AdminStatus.NotFound"/>.</summary>
    public async Task<AdminUserGetResult> GetUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        var send = await SendAuthorizedAsync(HttpMethod.Get, $"/users/{Uri.EscapeDataString(userId)}", jsonBody: null, cancellationToken);
        if (send.Failure is not null)
        {
            return new AdminUserGetResult(send.Failure.Status, null, send.Failure.Detail);
        }

        using var response = send.Response!;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new AdminUserGetResult(AdminStatus.NotFound, null, TryGetErrorDescription(body));
        }

        if (!response.IsSuccessStatusCode)
        {
            var failure = Classify(response, body);
            return new AdminUserGetResult(failure.Status, null, failure.Detail);
        }

        try
        {
            var user = JsonSerializer.Deserialize<KeycloakUser>(body, _json);
            return user is null
                ? new AdminUserGetResult(AdminStatus.Unreachable, null, "the Admin REST returned an empty user body")
                : new AdminUserGetResult(AdminStatus.Success, user, null);
        }
        catch (JsonException)
        {
            return new AdminUserGetResult(AdminStatus.Unreachable, null,
                "the Admin REST returned a malformed user representation");
        }
    }

    /// <summary>Creates a realm user, reporting the id Keycloak minted. A duplicate username
    /// surfaces as <see cref="AdminStatus.Conflict"/>.
    /// <para>
    /// Keycloak answers <c>201 Created</c> with an empty body and the new user's id only in the
    /// <c>Location</c> header (<c>…/users/{id}</c>), so it is read off the response here: the caller
    /// gets the id without a second resolve-by-username round trip.
    /// </para>
    /// </summary>
    public async Task<AdminUserCreateResult> CreateUserAsync(
        KeycloakUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var send = await SendAuthorizedAsync(
            HttpMethod.Post, "/users", JsonSerializer.Serialize(user, _json), cancellationToken);
        if (send.Failure is not null)
        {
            return new AdminUserCreateResult(send.Failure.Status, null, send.Failure.Detail);
        }

        using var response = send.Response!;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var failure = Classify(response, body);
            return new AdminUserCreateResult(failure.Status, null, failure.Detail);
        }

        return new AdminUserCreateResult(AdminStatus.Success, TryGetCreatedUserId(response), null);
    }

    /// <summary>The created user's id: the last path segment of a <c>201 Created</c>
    /// <c>Location</c> header (<c>…/users/{id}</c>), percent-decoded because the id is escaped
    /// again on the way back out (<see cref="GetUserAsync"/>). An absent, unusable or id-less
    /// location yields <c>null</c>.</summary>
    private static string? TryGetCreatedUserId(HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        if (location is null)
        {
            return null;
        }

        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        var id = path[(path.LastIndexOf('/') + 1)..];
        return id.Length == 0 ? null : Uri.UnescapeDataString(id);
    }

    /// <summary>Updates a realm user (PUT semantics: the body is the full representation to
    /// replace, so <see cref="KeycloakUser.Attributes"/> in that body is what the user's claim
    /// attributes become).</summary>
    public async Task<AdminOperationResult> UpdateUserAsync(
        KeycloakUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(user.Id))
        {
            throw new ArgumentException("user.Id is required for an update (Keycloak resolves the user by id).", nameof(user));
        }

        return await SendMutationAsync(
            HttpMethod.Put,
            $"/users/{Uri.EscapeDataString(user.Id)}",
            JsonSerializer.Serialize(user, _json),
            cancellationToken);
    }

    /// <summary>Resets a realm user's password.</summary>
    public async Task<AdminOperationResult> ResetPasswordAsync(
        string userId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(newPassword);

        var body = $$"""
            {"type":"password","value":{{JsonSerializer.Serialize(newPassword, _json)}},"temporary":false}
            """;
        return await SendMutationAsync(
            HttpMethod.Put,
            $"/users/{Uri.EscapeDataString(userId)}/reset-password",
            body,
            cancellationToken);
    }

    /// <summary>Grants realm roles to a user (the id-and-name array Keycloak's role-mappings endpoint requires).</summary>
    public async Task<AdminOperationResult> AssignRealmRolesAsync(
        string userId,
        IReadOnlyCollection<KeycloakRole> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(roles);

        return await SendMutationAsync(
            HttpMethod.Post,
            $"/users/{Uri.EscapeDataString(userId)}/role-mappings/realm",
            JsonSerializer.Serialize(roles.ToList(), _json),
            cancellationToken);
    }

    /// <summary>Revokes realm roles from a user (Keycloak accepts the same array body on DELETE).</summary>
    public async Task<AdminOperationResult> UnassignRealmRolesAsync(
        string userId,
        IReadOnlyCollection<KeycloakRole> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(roles);

        return await SendMutationAsync(
            HttpMethod.Delete,
            $"/users/{Uri.EscapeDataString(userId)}/role-mappings/realm",
            JsonSerializer.Serialize(roles.ToList(), _json),
            cancellationToken);
    }

    /// <summary>Lists the realm's roles (used to resolve role ids before assign/unassign).</summary>
    public async Task<AdminRealmRoleListResult> ListRealmRolesAsync(CancellationToken cancellationToken = default)
    {
        var send = await SendAuthorizedAsync(HttpMethod.Get, "/roles", jsonBody: null, cancellationToken);
        if (send.Failure is not null)
        {
            return new AdminRealmRoleListResult(send.Failure.Status, Array.Empty<KeycloakRole>(), send.Failure.Detail);
        }

        using var response = send.Response!;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var failure = Classify(response, body);
            return new AdminRealmRoleListResult(failure.Status, Array.Empty<KeycloakRole>(), failure.Detail);
        }

        try
        {
            var roles = JsonSerializer.Deserialize<List<KeycloakRole>>(body, _json) ?? [];
            return new AdminRealmRoleListResult(AdminStatus.Success, roles, null);
        }
        catch (JsonException)
        {
            return new AdminRealmRoleListResult(AdminStatus.Unreachable, Array.Empty<KeycloakRole>(),
                "the Admin REST returned a malformed role list");
        }
    }

    /// <summary>Shared mutation path: send, then classify the HTTP status.</summary>
    private async Task<AdminOperationResult> SendMutationAsync(
        HttpMethod method,
        string path,
        string jsonBody,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jsonBody);

        var send = await SendAuthorizedAsync(method, path, jsonBody, cancellationToken);
        if (send.Failure is not null)
        {
            return send.Failure;
        }

        using var response = send.Response!;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? new AdminOperationResult(AdminStatus.Success)
            : Classify(response, body);
    }

    /// <summary>Classifies a non-success Admin REST response. 404/409/401/403 get distinct
    /// statuses; 5xx and everything else fails toward <see cref="AdminStatus.Unreachable"/>.</summary>
    private static AdminOperationResult Classify(HttpResponseMessage response, string body)
    {
        var detail = TryGetErrorDescription(body) ?? body;
        return response.StatusCode switch
        {
            HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.NoContent
                => new AdminOperationResult(AdminStatus.Success),
            HttpStatusCode.NotFound => new AdminOperationResult(AdminStatus.NotFound, detail),
            HttpStatusCode.Conflict => new AdminOperationResult(AdminStatus.Conflict, detail),
            HttpStatusCode.Unauthorized => new AdminOperationResult(AdminStatus.Unauthorized, detail),
            HttpStatusCode.Forbidden => new AdminOperationResult(AdminStatus.Forbidden, detail),
            _ => new AdminOperationResult(AdminStatus.Unreachable,
                detail == body ? $"Admin REST returned HTTP {(int)response.StatusCode}" : detail),
        };
    }

    private static string? TryGetErrorDescription(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error_description", out var description)
                ? description.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record SendResult(HttpResponseMessage? Response, AdminOperationResult? Failure);
}
