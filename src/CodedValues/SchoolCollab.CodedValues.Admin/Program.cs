using SchoolCollab.CodedValues.Admin.Components;
using SchoolCollab.CodedValues.Admin.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<CodedValuesApiClient>(client =>
    client.BaseAddress = new Uri("https+http://coded-values-api"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapDefaultEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
