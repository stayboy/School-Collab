using Serilog;
using SchoolCollab.Students.Core;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Api;

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

builder.Services.AddStudentsCore(builder.Configuration);
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
