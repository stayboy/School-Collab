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