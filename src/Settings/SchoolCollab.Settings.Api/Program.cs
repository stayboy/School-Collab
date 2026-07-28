using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api;
using SchoolCollab.Settings.Api.Auth;
using SchoolCollab.Settings.Core;
using SchoolCollab.Settings.Core.Services;
using Serilog;

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

// Register auth/tenancy first so AddSettingsCore can resolve ITenantProvider
// and IFeatureFlagService from the same scope the AuthN/AuthZ middleware uses.
builder.Services.AddAuthAndTenancy(builder.Configuration);

// Unified Settings module — one DbContext, one connection string, both
// aggregates (CodedValues + FeatureFlag). See spec §6.
builder.Services.AddSettingsCore(builder.Configuration);

// Replace the default system actor with the ClaimsPrincipal-backed accessor so
// audit rows capture the OIDC sub/name of the operator who made each change.
// Default registered in AddSettingsCore is `system:unknown` so handlers resolve
// during startup (migrator, etc.); the API host overrides it here.
builder.Services.AddHttpContextAccessor();
builder.Services.RemoveAll<IActorAccessor>();
builder.Services.AddSingleton<IActorAccessor, ClaimsPrincipalActorAccessor>();

// The flag_admin role policy gates write endpoints when OIDC is enabled.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("flag_admin", policy => policy.RequireClaim("role", "flag_admin"));
});

builder.Services.AddOpenApi();

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
// Map both legacy endpoint groups in this single host. See spec §8.
app.MapCodedValueEndpoints(featureFlags);
app.MapTenantEndpoints(featureFlags);
app.MapConfigEndpoints(featureFlags);
// Phase 3: EntityCodeRule admin endpoints (spec §4.7).
app.MapEntityCodeRuleEndpoints(featureFlags);

app.Run();

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }
