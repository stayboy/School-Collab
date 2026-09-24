using SchoolCollab.Auth;
using SchoolCollab.Auth.Endpoints;
using SchoolCollab.Core.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Options and services are registered via extension methods (dotnet-best-practices
// "Never": no inline services.Add*() in a feature host). AddAuthServiceOptions binds
// + validates AuthServiceOptions on start (the validator is shared with
// AuthServiceSmokeTests so the startup check and the tests cannot drift);
// AddAuthServices registers the Direct-Grant exchanger and the one-time-code store,
// the portal-session store, the claim factory, the Admin REST client and the audit log.
builder.Services.AddAuthServiceOptions(builder.Configuration);
builder.Services.AddAuthServices();

// Shared auth/tenancy: registers the OIDC + JwtBearer schemes (or TestAuth when
// FEATURE:DisableOIDCAuth is on), the tenant bridge and ICurrentUser. The same
// Auth:Keycloak:* env vars the options above validate are consumed here.
builder.Services.AddAuthAndTenancy(builder.Configuration);

// Fixed-window rate limiter for the credential exchange (spec §14, plan-review P1-5,
// service side). The registration lives in AuthServiceExtensions.AddAuthRateLimiting
// (expected-file row 50) so the endpoint tests can compose the real pipeline from public
// extensions only; the P1-5 ordering requirement is preserved HERE — this call still runs
// before builder.Build(), which is the property that made the host file its lawful home.
builder.Services.AddAuthRateLimiting(builder.Configuration);

var app = builder.Build();
app.UseRateLimiter();

// Auth + authorization middleware — REQUIRED for the pass-3d /auth/admin role gate to enforce
// (every other host calls these; without them the RequireAuthorization metadata is never
// evaluated and the gate is a pass-through). Mirror of the AssignmentEndpoints host order.
app.UseAuthentication();
app.UseAuthorization();

// Service-defaults health endpoints + the auth endpoint groups (extension
// methods only — never inline route maps in Program.cs, per AGENTS.md).
app.MapDefaultEndpoints();
app.MapAuthEndpoints();

app.Run();
