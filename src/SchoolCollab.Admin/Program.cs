using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Components;
using SchoolCollab.Assignments.Admin;
using SchoolCollab.CodedValues.Admin;
using SchoolCollab.Students.Admin;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Auth + tenancy (OIDC via Keycloak for the unified admin host)
// Disable OIDC when FEATURE:DisableOIDCAuth is enabled; falls back to TestAuth for local development.
builder.Services.AddAuthAndTenancy(builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

// Register module services (HttpClient factories for each bounded context)
builder.Services.AddCodedValuesModule();
builder.Services.AddAssignmentsModule();
builder.Services.AddStudentsModule();

var app = builder.Build();

var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
var disableOIDC = featureFlags.IsEnabled("FEATURE:DisableOIDCAuth");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}


app.UseHttpsRedirection();
if (!disableOIDC)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();

// Feature Flags API removed from Admin, now served by SchoolCollab.Config
app.MapStaticAssets();
app.MapDefaultEndpoints();

var razorComponents = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(SchoolCollab.CodedValues.Admin.Components._Imports).Assembly,
        typeof(SchoolCollab.Assignments.Admin.Components._Imports).Assembly,
        typeof(SchoolCollab.Students.Admin.Components._Imports).Assembly);

if (!disableOIDC)
{
    razorComponents.RequireAuthorization();
}

app.Run();