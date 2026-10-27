using System.Text.Json.Serialization;
using SchoolCollab.Students.Core.Domain;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using SchoolCollab.Students.Core;
using Microsoft.Extensions.Configuration;
using SchoolCollab.Settings.Core;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Http;
using SchoolCollab.Students.Api;
using SchoolCollab.Students.Api.Auth;
using SchoolCollab.Students.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");

// The wire contract for SubjectAssignmentSource is its NAME, not its ordinal. The admin client has
// always posted "IndividualAssignment"; without this converter the Students API failed to bind the
// payload and answered 400 ("The JSON value could not be converted to ...AssignStudentTopic. Path:
// $.sourceType"), which surfaced in the UI as "could not be added to this student. Try again." - the
// enrol action of the subject-enrolment feature, broken end-to-end while every stub-HTTP unit test
// stayed green. Registered per enum type, matching the Assignments API precedent rather than a
// blanket converter, so no other endpoint's JSON changes.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<SubjectAssignmentSource>());
});

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
// Named client (unique name — Admin.Shared also registers a typed client whose
// short type name is 'CodedValuesApiClient', and typed names ignore namespace).
// TenantForwardingDelegatingHandler forwards the inbound request's resolved
// tenant (x-tenant-id header, falling back to the tenant_id claim) so stream
// validation resolves the SAME tenant the enroll request came in under — see
// docs/plans/2026-08-22-tenant-propagation-enroll-stream-investigation.md (Class B).
// Registered TRANSIENT (TryAdd, not Add): IHttpClientFactory sets InnerHandler per
// named-client pipeline; a Singleton shared across ICodedValuesApiClient, the
// Admin.Shared CodedValuesApiClient, and the "assignments-api" named client would
// have its InnerHandler overwritten, routing data to the wrong host.
builder.Services.AddHttpContextAccessor();
builder.Services.TryAddTransient<TenantForwardingDelegatingHandler>();
builder.Services.AddCrossModuleHttpClient("students-core-coded-values", "http://settings-api", propagateTenant: false)
    .AddHttpMessageHandler<TenantForwardingDelegatingHandler>()
    .AddHttpMessageHandler<BearerForwardingDelegatingHandler>()
    .AddTypedClient<SchoolCollab.Students.Core.Services.CodedValuesApiClient>();

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
// TenantForwardingDelegatingHandler forwards the inbound request's tenant (see Class B
// investigation) so Admin.Shared components resolve the same tenant as the API request.
builder.Services.AddCrossModuleHttpClient<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>("http://settings-api", propagateTenant: false)
    .AddHttpMessageHandler<TenantForwardingDelegatingHandler>()
    .AddHttpMessageHandler<BearerForwardingDelegatingHandler>();

// Phase 2 (spec activity-group-enrollment.md FR-6): HTTP client for the
// Assignments API delete-guard check. The named client is resolved via
// Aspire service discovery once the AppHost references assignments-api.
// TenantForwardingDelegatingHandler forwards the inbound request's tenant
// (Class B): assignments are strict-tenant entities, and a tenant-less guard
// query can false-negative and allow a delete that should be blocked.
builder.Services.AddCrossModuleHttpClient("assignments-api", "https+http://assignments-api", propagateTenant: false)
    .AddHttpMessageHandler<TenantForwardingDelegatingHandler>()
    .AddHttpMessageHandler<BearerForwardingDelegatingHandler>();
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

// Must sit inside the request-logging middleware: Serilog logs a passing exception at Error and
// rethrows, so a catcher placed outside it would not silence the log entry this fixes.
app.UseClientAbortHandling();

var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
app.MapStudentEndpoints(featureFlags);

app.Run();

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }
