using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SchoolCollab.Auth;
using SchoolCollab.Auth.Endpoints;
using SchoolCollab.Auth.Services;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// A real, in-process Kestrel host for the auth service's endpoint tests (pass 3c, row 51).
/// <para>
/// WHY this exists: the endpoint tests must prove the rate limiter rejects the N+1-th request
/// with <c>429</c>, which requires the genuine middleware pipeline. The usual route —
/// <c>Microsoft.AspNetCore.Mvc.Testing</c>'s <c>WebApplicationFactory</c> — was rejected to
/// honour round A's no-new-package constraint, so this host composes the app from the service's
/// PUBLIC extension methods only and runs it on <c>http://127.0.0.1:0</c> (ephemeral port).
/// </para>
/// <para>
/// The Direct-Grant exchanger's primary HTTP handler is replaced with a shared
/// <see cref="StubHttpHandler"/> so no test ever touches the network; the limiter, routing and
/// endpoint pipeline themselves stay real.
/// </para>
/// </summary>
public sealed class AuthEndpointTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private AuthEndpointTestHost(
        WebApplication app,
        HttpClient client,
        StubHttpHandler exchangeStub,
        StubHttpHandler adminStub,
        CapturingLoggerProvider auditLogs)
    {
        _app = app;
        Client = client;
        ExchangeStub = exchangeStub;
        AdminStub = adminStub;
        AuditLogs = auditLogs;
    }

    /// <summary>Configured client rooted at the ephemeral listener.</summary>
    public HttpClient Client { get; }

    /// <summary>Primary handler of the typed <c>DirectGrantExchanger</c> client — tests set
    /// <see cref="StubHttpHandler.OnSendAsync"/> to control exchange outcomes hermetically.</summary>
    public StubHttpHandler ExchangeStub { get; }

    /// <summary>Primary handler of the typed <c>KeycloakAdminClient</c> — the Admin REST tests
    /// route token fetches and operations through <see cref="StubHttpHandler.OnSendAsync"/>.</summary>
    public StubHttpHandler AdminStub { get; }

    /// <summary>Captured records from the REAL logging path (the AuthAuditLog records).</summary>
    public CapturingLoggerProvider AuditLogs { get; }

    /// <summary>The host's service provider (for any DI override the tests need).</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Issuer the host's JwtBearer accepts (<c>ValidateIssuer</c> is off; the value just
    /// keeps minted tokens issuer-realistic). Shared with the test token minter.</summary>
    public const string TestIssuer = "school-collab-test-issuer";

    /// <summary>Test signing material (HS256) shared by the host's JwtBearer validator and the
    /// test token minter, so what the tests mint is exactly what the host validates.</summary>
    public static SymmetricSecurityKey TestSigningKey { get; } =
        new(Encoding.UTF8.GetBytes(new string('k', 32)));

    /// <summary>Starts the host on an ephemeral loopback port with the real pipeline.</summary>
    public static async Task<AuthEndpointTestHost> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();

        // ValidateOnStart demands the complete Auth config; the timing values are tuned so the
        // endpoint tests hit the rate limit quickly (permit 3 within a 20 s window). The
        // authority points at a closed loopback port — the stub below intercepts every call,
        // so nothing here ever reaches the network.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Keycloak:Authority"] = "http://localhost:1/realms/school-collab",
            ["Auth:Keycloak:ClientId"] = "school-collab-client",
            ["Auth:Keycloak:ClientSecret"] = "test-client-secret",
            ["Auth:Keycloak:ServiceAccountClientSecret"] = "test-service-account-secret",
            // B5b: AuthServiceOptions.AppCallbackPrefixes has no safe default (ValidateOnStart
            // rejects a blank value), so the composed host cannot start without it. The single
            // entry is the admin host's real handshake callback — the value the exchange tests
            // post as RedirectUri.
            ["Auth:AppCallbackPrefixes"] = "http://localhost:5300/signin-handshake",
            // Pass 3: AuthServiceOptions.PostLogoutRedirectUri is startup-validated too (the
            // end_session URL's post_logout_redirect_uri must be the portal's registered landing
            // URI), so the composed host cannot start without it. The value is the literal the
            // realm registers (Keycloak matches it exactly, trailing slash included).
            ["Auth:PostLogoutRedirectUri"] = "http://localhost:5700/",
            ["Auth:OneTimeCodeTtl"] = "00:00:20",
            ["Auth:PortalSessionTtl"] = "00:30:00",
            ["Auth:CredentialEndpointRateLimitWindow"] = "00:00:20",
            ["Auth:CredentialEndpointRateLimitPermits"] = "3",
            // The Auth project's appsettings.json — transitively copied into this test output —
            // sets FEATURE:DisableOIDCAuth=true for dev, which would switch AddAuthAndTenancy to
            // TestAuth (requiring IDistributedCache, which this composed host does not register).
            // That is NOT what this host is for: it exists to exercise the REAL bearer pipeline,
            // so the flag is pinned OFF here — this in-memory provider is consulted before the
            // copied file, and the real OIDC + JwtBearer schemes always register.
            ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "false",
        });

        // Reserve a free loopback port up front (the test project cannot reference the
        // IServerAddressesFeature type it would need to read a dynamic :0 assignment back out,
        // and WebApplication.Urls keeps the literal :0) — Kestrel then binds this exact port.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var baseUrl = $"http://127.0.0.1:{port}";
        builder.WebHost.UseUrls(baseUrl);

        // The real pipeline, composed from the app's public extensions only.
        builder.Services.AddAuthServiceOptions(builder.Configuration);
        builder.Services.AddAuthServices();

        // Hermetic tests: replace the exchanger's primary handler with the shared stub. The
        // DefaultHttpMessageHandlerBuilder only instantiates the default HttpClientHandler when
        // PrimaryHandler is null, so this later ConfigurePrimaryHttpMessageHandler wins.
        var exchangeStub = new StubHttpHandler();
        builder.Services.AddSingleton(exchangeStub);
        builder.Services.AddHttpClient<DirectGrantExchanger>()
            .ConfigurePrimaryHttpMessageHandler(() => exchangeStub);

        builder.Services.AddAuthRateLimiting(builder.Configuration);

        // Pass 3d: the REAL auth/tenancy registration, exactly as Program.cs wires it — the host
        // config declares no FEATURE:DisableOIDCAuth, so the OIDC + JwtBearer schemes register
        // (never TestAuth) and the /auth/admin role gate becomes exercisable.
        builder.Services.AddAuthAndTenancy(builder.Configuration);

        // The host exercises the BEARER path only: (a) policy evaluation for the /auth/admin gate
        // has no explicit AuthenticationSchemes, so it authenticates via DefaultAuthenticateScheme —
        // AddAuthAndTenancy defaults that to the Cookie scheme, which would ignore a Bearer token;
        // (b) an unauthenticated call to a guarded endpoint must then challenge the bearer scheme
        // (-> 401) rather than OIDC (-> a 302 to Keycloak, which is what the real app wants for its
        // browser flows). Both routing choices are test-host adjustments, documented: the JwtBearer
        // handler itself stays fully real.
        builder.Services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        });

        // The bearer handler validates against a STATIC test configuration — no metadata fetch,
        // and the Auth:Keycloak:Authority in the host config (a closed port) is never opened.
        // TokenValidationParameters is mutated IN PLACE so the RoleClaimType = ClaimTypes.Role that
        // AddAuthAndTenancy pinned on this handler survives; replacing the instance would
        // silently drop the role wiring this host exists to exercise.
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var issuerConfig = new OpenIdConnectConfiguration { Issuer = TestIssuer };
            issuerConfig.SigningKeys.Add(TestSigningKey);
            options.Authority = null;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(issuerConfig);
            options.TokenValidationParameters.ValidateIssuer = false;
            options.TokenValidationParameters.ValidateAudience = false;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.IssuerSigningKey = TestSigningKey;

        });

        // Hermetic Admin REST: the Admin client is a SECOND typed HttpClient (registered by
        // AddAuthServices); stub its primary handler exactly like the exchanger's.
        var adminStub = new StubHttpHandler();
        builder.Services.AddSingleton(adminStub);
        builder.Services.AddHttpClient<KeycloakAdminClient>()
            .ConfigurePrimaryHttpMessageHandler(() => adminStub);

        // Capture the AuthAuditLog's structured records through a REAL ILogger provider, so the
        // audit assertions run over the genuine logging path, never a swapped-in spy.
        var auditLogs = new CapturingLoggerProvider();
        builder.Logging.AddProvider(auditLogs);
        builder.Services.AddSingleton(auditLogs);

        var app = builder.Build();
        app.UseRateLimiter();

        // Auth + authorization middleware — mirror the real Program.cs (pass 3d part i): without
        // these, the /auth/admin RequireAuthorization metadata is never enforced.
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapAuthEndpoints();

        await app.StartAsync();

        var client = new HttpClient { BaseAddress = new Uri(baseUrl) };

        return new AuthEndpointTestHost(app, client, exchangeStub, adminStub, auditLogs);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> standing in for the Direct-Grant exchanger's real
/// network. The default response is a bare 502, so a test that forgets to configure
/// <see cref="OnSendAsync"/> fails loudly instead of silently touching the network.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private int _receivedRequests;

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> OnSendAsync { get; set; }
        = static (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway));

    /// <summary>How many requests reached the stub (proves the network was never touched).</summary>
    public int ReceivedRequests => Volatile.Read(ref _receivedRequests);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _receivedRequests);
        return await OnSendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Capturing <see cref="ILoggerProvider"/>: the audit assertions run over the REAL logging path —
/// the endpoints call the registered <see cref="AuthAuditLog"/>, which logs through the host's
/// <see cref="ILoggerFactory"/> including this provider, and the structured state (Actor / Action /
/// Target / TenantId / Role) is preserved exactly as <c>AuthAuditLogTests</c> captures it.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    /// <summary>One captured record with its raw structured state.</summary>
    public sealed record Record(string Category, LogLevel Level, IReadOnlyDictionary<string, object?> State);

    private readonly ConcurrentQueue<Record> _records = new();

    /// <summary>A point-in-time snapshot of every record captured so far.</summary>
    public IReadOnlyList<Record> Snapshot() => _records.ToList();

    /// <summary>Snapshots only the records whose category ends with the given suffix
    /// (e.g. <c>"AuthAuditLog"</c>), so tests can ignore the framework's own logging noise.</summary>
    public IReadOnlyList<Record> ForCategory(string categorySuffix) =>
        _records.Where(r => r.Category.EndsWith(categorySuffix, StringComparison.Ordinal)).ToList();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider parent, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                parent._records.Enqueue(new Record(
                    category,
                    logLevel,
                    pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)));
            }
        }
    }
}
