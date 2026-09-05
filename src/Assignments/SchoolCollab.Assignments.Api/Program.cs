using System.Text.Json.Serialization;
using Serilog;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core;
using SchoolCollab.Settings.Core;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<AssignmentTypeDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<AssignmentStatusDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<GradingFormatDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<TargetAudienceTypeDto>());
    // AI spec §3.2: question payloads round-trip the discriminator as a string
    // (e.g. "multipleChoice") — register the converter on the same options block
    // as the other assignment enums so existing callers stay valid.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<QuestionTypeDto>());
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

builder.Services.AddAssignmentsCore(builder.Configuration);
// Phase 2: register Settings.Core so IEntityCodeGenerator (auto-generated entity codes)
// is resolvable by the CreateAssignmentCommandHandler.
builder.Services.AddSettingsCore(builder.Configuration);

// Cross-bounded-context contact resolver (spec §9 G5): resolves subscribed
// contacts from the Students API. The named client is resolved via Aspire
// service discovery once the AppHost references students-api.
builder.Services.AddHttpClient("students-api");
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.IContactResolver, SchoolCollab.Assignments.Api.Services.StudentsContactResolver>();

// Effective-policy resolver (notification-delivery-plan.md §3): reads the tenant
// default (Settings API) + grade override (Students API), merges at publish time.
builder.Services.AddHttpClient("settings-api");
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.INotificationPolicyResolver,
    SchoolCollab.Assignments.Api.Services.NotificationPolicyResolver>();

// Phase 3 (spec activity-group-enrollment.md FR-20..22): activity-group lookup
// port (Assignments → Students) for the link command and SelectedGroups publish.
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.IActivityGroupLookup,
    SchoolCollab.Assignments.Api.Services.ActivityGroupLookupHttpClient>();

// Rev. 6 (spec activity-group-enrollment.md FR-58): subject/period consistency
// check at publish (Assignments → Students topic-assignment lookup).
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.ITopicAssignmentLookup,
    SchoolCollab.Assignments.Api.Services.TopicAssignmentLookupHttpClient>();

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

// All assignment endpoints require an authenticated user
var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
app.MapAssignmentEndpoints(featureFlags); 

app.Run();

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }