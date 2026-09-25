namespace SchoolCollab.Auth.Options;

/// <summary>
/// Configuration for the SchoolCollab.Auth service. Bound from the <c>Auth</c>
/// section by Program.cs (the AppHost fans the values out as <c>Auth__Keycloak__*</c>
/// env vars — see <c>WireKeycloakAuth</c> and the <c>keycloak-auth-admin-secret</c>
/// parameter) and validated at startup via
/// <see cref="FirstValidationError"/>. The <c>Auth:Keycloak:*</c> names match the
/// shared consumers in SchoolCollab.Core (<see cref="SchoolCollab.Core.Auth.AuthTenancyExtensions"/>).
/// Round A pass 2 carries the Keycloak authority/client id/secret and the
/// service-account credential the later passes consume; pass 3a extends this
/// class with the timing values (one-time-code and portal-session TTLs, rate-limit
/// window/permit); round B pass B5b adds the per-app callback allowlist
/// (<see cref="AppCallbackPrefixes"/>); the `keycloak-logout-oidc` round's pass 3 adds the post-logout landing
/// URI (<see cref="PostLogoutRedirectUri"/>).
/// </summary>
public sealed class AuthServiceOptions
{
    /// <summary>Default configuration section name: <c>Auth</c>.</summary>
    public const string SectionName = "Auth";

    /// <summary>Keycloak connection settings (spec §11).</summary>
    public KeycloakOptions Keycloak { get; set; } = new();

    /// <summary>
    /// Lifetime of a one-time handshake code (spec §5.2 / D6). Short by design —
    /// single-use plus a short TTL is the whole mitigation. Default 60 s; the
    /// validator caps it at 5 minutes.
    /// </summary>
    public TimeSpan OneTimeCodeTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Lifetime of a portal session (spec §5.4 / D12). Default 8 h.
    /// </summary>
    public TimeSpan PortalSessionTtl { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// The per-app callback allowlist (spec §14, plan-review P1-3): a semicolon-separated
    /// list of <b>exact callback prefixes</b> — scheme + host + port + path; the query is
    /// ignored — that a one-time handshake code may be minted for. It is the load-bearing
    /// control against the crafted <c>portal/login?return_uri=attacker</c> attack: a redirect
    /// target that is not on this list never yields a redeemable code, so an attacker-supplied
    /// URI cannot receive the victim's claim set. Consumed through
    /// <see cref="SchoolCollab.Auth.Services.AppCallbackAllowlist"/>, which denies by default.
    /// <para>
    /// There is deliberately <b>no safe default</b>: an empty, blank or absent value is a
    /// startup failure (<see cref="FirstValidationError"/>), never a fail-open. Dev supplies the
    /// four Blazor hosts' <c>/signin-handshake</c> callbacks plus the portal's bootstrap
    /// redemption URI via the AppHost's <c>app-callback-prefixes</c> parameter.
    /// </para>
    /// </summary>
    public string AppCallbackPrefixes { get; set; } = string.Empty;

    /// <summary>
    /// The portal's post-logout landing URI (spec §9 / D13 option ii): the
    /// <c>post_logout_redirect_uri</c> the auth service puts on the built <c>end_session</c> URL
    /// (<see cref="SchoolCollab.Auth.Providers.KeycloakAuthProvider.BuildEndSessionUrl"/>).
    /// Keycloak matches it against the client's registered post-logout redirect URIs
    /// <b>exactly</b> — trailing slash included, no wildcard — so this value and the realm literal
    /// must be spelled identically (the AppHost pins the portal's host port for that reason).
    /// <para>
    /// In the realm import that list is the client attribute
    /// <c>attributes["post.logout.redirect.uris"]</c>, <c>##</c>-separated — <b>not</b> a top-level
    /// <c>postLogoutRedirectUris</c> field: Keycloak 26.4's <c>ClientRepresentation</c> rejects that
    /// field as unrecognized and the entire import fails, so re-adding it breaks auth startup (see
    /// <c>documents/solution/keycloak-realm-post-logout-uris-import-fix.md</c>).
    /// </para>
    /// <para>
    /// There is deliberately <b>no safe default</b>, exactly like <see cref="AppCallbackPrefixes"/>:
    /// an empty or absent value is a startup failure (<see cref="FirstValidationError"/>), never a
    /// guessed fallback. A wrong guess would only surface at logout time, after the local session
    /// is already gone, as a Keycloak rejection the portal cannot act on.
    /// </para>
    /// </summary>
    public string PostLogoutRedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Rate-limit window for the credential endpoint <c>POST /auth/exchange</c>
    /// (spec §14, consumed by pass 3c). Default 1 minute.
    /// </summary>
    public TimeSpan CredentialEndpointRateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Maximum permitted calls to <c>POST /auth/exchange</c> within the window
    /// above (spec §14, consumed by pass 3c). Default 5.
    /// </summary>
    public int CredentialEndpointRateLimitPermits { get; set; } = 5;

    /// <summary>
    /// Returns the first validation failure message, or <c>null</c> when the options
    /// are valid. Shared by Program.cs (<c>.Validate(...).ValidateOnStart()</c>) and
    /// the smoke test so the startup guard and the tests cannot drift.
    /// </summary>
    public static string? FirstValidationError(AuthServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Keycloak.Authority))
        {
            return "Auth:Keycloak:Authority must be set (see AuthServiceOptions).";
        }

        if (string.IsNullOrWhiteSpace(options.Keycloak.ClientId))
        {
            return "Auth:Keycloak:ClientId must be set (see AuthServiceOptions).";
        }

        if (string.IsNullOrWhiteSpace(options.Keycloak.ClientSecret))
        {
            return "Auth:Keycloak:ClientSecret must be set (see AuthServiceOptions).";
        }

        if (string.IsNullOrWhiteSpace(options.Keycloak.ServiceAccountClientSecret))
        {
            return "Auth:Keycloak:ServiceAccountClientSecret must be set (see AuthServiceOptions).";
        }

        // Round B (plan-review P1-3): the app-callback allowlist has no safe default. A host
        // that started with an empty list would match nothing and fail closed per request, but
        // the misconfiguration must be loud at launch — and, more importantly, no deployment may
        // ever be tempted to treat "no allowlist" as "allow everything".
        if (string.IsNullOrWhiteSpace(options.AppCallbackPrefixes))
        {
            return "Auth:AppCallbackPrefixes must list at least one allowed app callback "
                + "(semicolon-separated scheme+host+port+path prefixes) — see AuthServiceOptions.";
        }

        // Round B pass 3 (D13 option ii): the post-logout landing URI has no safe default either.
        // Keycloak matches it exactly against the realm's registered post-logout redirect URIs (the
        // client attribute "post.logout.redirect.uris", not a top-level postLogoutRedirectUris
        // field — see the property doc), so a guessed value makes every logout fail after the
        // session is already gone — fail closed at launch.
        if (string.IsNullOrWhiteSpace(options.PostLogoutRedirectUri))
        {
            return "Auth:PostLogoutRedirectUri must be the portal's registered post-logout "
                + "landing URI (see AuthServiceOptions).";
        }

        // Timing values carry defaults, so these guards are range checks, not
        // presence checks: a zero/negative TTL would make a one-time code
        // immediately consumable forever, and a zero permit count would
        // rate-limit the endpoint to nothing.
        if (options.OneTimeCodeTtl <= TimeSpan.Zero || options.OneTimeCodeTtl > TimeSpan.FromMinutes(5))
        {
            return "Auth:OneTimeCodeTtl must be a positive duration of at most 5 minutes (see AuthServiceOptions).";
        }

        if (options.PortalSessionTtl <= TimeSpan.Zero)
        {
            return "Auth:PortalSessionTtl must be a positive duration (see AuthServiceOptions).";
        }

        if (options.CredentialEndpointRateLimitWindow <= TimeSpan.Zero)
        {
            return "Auth:CredentialEndpointRateLimitWindow must be a positive duration (see AuthServiceOptions).";
        }

        if (options.CredentialEndpointRateLimitPermits < 1)
        {
            return "Auth:CredentialEndpointRateLimitPermits must be at least 1 (see AuthServiceOptions).";
        }

        return null;
    }

    /// <summary>
    /// Keycloak connection settings, bound from <c>Auth:Keycloak:*</c>.
    /// The service-account credential is the <c>school-collab-auth-admin</c> client
    /// secret (the realm import), supplied by the AppHost's
    /// <c>keycloak-auth-admin-secret</c> parameter. It never appears in a response
    /// body or a log line.
    /// </summary>
    public sealed class KeycloakOptions
    {
        public string Authority { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        public string ClientSecret { get; set; } = string.Empty;

        public string ServiceAccountClientSecret { get; set; } = string.Empty;
    }
}
