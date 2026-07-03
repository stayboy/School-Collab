using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Config.Api;
using SchoolCollab.Config.Api.Auth;
using SchoolCollab.Config.Core;
using SchoolCollab.Config.Core.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;

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

builder.Services.AddAuthAndTenancy(builder.Configuration);
builder.Services.AddConfigCore(builder.Configuration);

// Replace the default system actor with the ClaimsPrincipal-backed accessor so
// audit rows capture the OIDC sub/name of the operator who made each change.
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

var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
app.MapConfigEndpoints(featureFlags);

app.Run();

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }