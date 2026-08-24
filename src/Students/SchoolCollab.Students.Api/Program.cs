using Serilog;
using SchoolCollab.Students.Core;
using Microsoft.Extensions.Configuration;
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
// Minimal client used by handlers (GetByIdAsync only).
builder.Services.AddHttpClient<SchoolCollab.Students.Core.Services.CodedValuesApiClient>(client =>
{
    client.BaseAddress = new Uri("http://settings-api");
});

// Flag-gated swap (adr-cross-module-calls.md Phase 1): when
// Students:UseLocalCodedValueProjection is on, coded-value reads resolve from
// the local projection (no settings-api hop); off (default) keeps the HTTP path.
builder.Services.AddScoped<SchoolCollab.Students.Core.Services.ICodedValuesApiClient>(sp =>
    new SchoolCollab.Students.Core.Services.FlagRoutedCodedValuesApiClient(
        sp.GetRequiredService<SchoolCollab.Students.Core.Services.CodedValuesApiClient>(),
        sp.GetRequiredService<SchoolCollab.Students.Core.Services.ILocalCodedValueRepository>(),
        sp.GetRequiredService<IConfiguration>()));

// Full-featured CodedValuesApiClient for Admin.Shared UI components (e.g., TeacherEditDialog).
// Registers as SchoolCollab.Admin.Shared.Services.CodedValuesApiClient so @inject CodedValuesApiClient
// in Blazor components resolves to the full-featured version with GetChildrenByParentCodeAsync, etc.
builder.Services.AddHttpClient<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>(client =>
{
    client.BaseAddress = new Uri("http://settings-api");
});

// Phase 2 (spec activity-group-enrollment.md FR-6): HTTP client for the
// Assignments API delete-guard check. The named client is resolved via
// Aspire service discovery once the AppHost references assignments-api.
builder.Services.AddHttpClient("assignments-api");
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
