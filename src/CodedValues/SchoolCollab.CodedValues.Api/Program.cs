using Serilog;
using SchoolCollab.CodedValues.Core;
using SchoolCollab.CodedValues.Api;
using SchoolCollab.CodedValues.Api.Infrastructure.Auth;
using SchoolCollab.CodedValues.Api.Infrastructure.Data;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddRemoteFeatureFlags("https+http://config");

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

builder.Services.AddCodedValuesCore(builder.Configuration);
builder.Services.AddOpenApi();

// Tenancy & Auth Infrastructure
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
app.MapCodedValueEndpoints(featureFlags);

app.Run();

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }
