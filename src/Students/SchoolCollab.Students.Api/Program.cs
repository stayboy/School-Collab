using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using SchoolCollab.Students.Core;
using SchoolCollab.Settings.Core;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Api;
using SchoolCollab.Students.Api.Auth;
using SchoolCollab.Students.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");

var cacheConnectionString = builder.Configuration.GetConnectionString("cache")
    ?? builder.Configuration["Aspire:StackExchange:Redis:ConnectionString"];

if (string.IsNullOrWhiteSpace(cacheConnectionString))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.AddRedisDistributedCache("cache");
}

// Cross-module: HTTP client for the Settings Coded Values API (strand validation).
// Minimal client used by handlers (GetByIdAsync only). TenantForwardingDelegatingHandler
// forwards the inbound request's resolved tenant (x-tenant-id header, falling back to
// the tenant_id claim) so stream validation resolves the SAME tenant the enroll
// request came in under — see docs/plans/2026-08-22-tenant-propagation-enroll-stream-
// investigation.md (Class B).
// Registered TRANSIENT (not Singleton): IHttpClientFactory sets InnerHandler per
// named-client pipeline; a Singleton shared across ICodedValuesApiClient,
// Admin.Shared CodedValuesApiClient, and the "assignments-api" named client gets
// its InnerHandler overwritten, routing data to the wrong host (e.g. coded-value
// validation calls hitting the assignments-api -> 404 -> enrollment failure).
builder.Services.AddHttpContextAccessor();
builder.Services.TryAddTransient<TenantForwardingDelegatingHandler>();
builder.Services.AddHttpClient<ICodedValuesApiClient, SchoolCollab.Students.Core.Services.CodedValuesApiClient>(client =>
{
    client.BaseAddress = new Uri("http://settings-api");
})
.AddHttpMessageHandler<TenantForwardingDelegatingHandler>();

// Full-featured CodedValuesApiClient for Admin.Shared UI components (e.g., TeacherEditDialog).
// Registers as SchoolCollab.Admin.Shared.Services.CodedValuesApiClient so @inject CodedValuesApiClient
// in Blazor components resolves to the full-featured version with GetChildrenByParentCodeAsync, etc.
builder.Services.AddHttpClient<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>(client =>
{
    client.BaseAddress = new Uri("http://settings-api");
})
.AddHttpMessageHandler<TenantForwardingDelegatingHandler>();

// Phase 2 (spec activity-group-enrollment.md FR-6): HTTP client for the
// Assignments API delete-guard check. The named client is resolved via
// Aspire service discovery once the AppHost references assignments-api.
// Tenant forwarding is REQUIRED here: assignments are strict-tenant entities,
// and a tenant-less guard query can false-negative and allow a delete that
// should be blocked (Class B / data-integrity follow-up).
builder.Services.AddHttpClient("assignments-api")
    .AddHttpMessageHandler<TenantForwardingDelegatingHandler>();
builder.Services.AddScoped<SchoolCollab.Students.Core.Services.IActivityGroupAssignmentQuery,
    SchoolCollab.Students.Api.Services.ActivityGroupAssignmentQueryHttpClient>();

builder.Services.AddStudentsCore(builder.Configuration);
// Phase 2: register Settings.Core so IEntityCodeGenerator (auto-generated entity codes)
// is resolvable by the Student/Teacher creation handlers.
builder.Services.AddSettingsCore(builder.Configuration);
// Audit actor for student transfer audit rows — reads the authenticated user's claims.
builder.Services.AddSingleton<IActorAccessor, ClaimsPrincipalActorAccessor>();
builder.Services.AddOpenApi();

// Auth + tenancy (OIDC via Keycloak)
builder.Services.AddAuthAndTenancy(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.UseSerilogRequestLogging();

var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
app.MapStudentEndpoints(featureFlags);

app.Run();

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }
