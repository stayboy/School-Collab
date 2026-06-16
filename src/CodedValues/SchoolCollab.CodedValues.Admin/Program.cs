using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.AI;
using SchoolCollab.CodedValues.Admin.Components;
using SchoolCollab.CodedValues.Admin.Services;
using SchoolCollab.Admin.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

builder.Services.AddHttpClient<CodedValuesApiClient>(client =>
    client.BaseAddress = new Uri("https+http://coded-values-api"));

// AI chat client — calls the AI API via SSE streaming (service discovery)
builder.Services.AddHttpClient<SchoolCollab.Admin.Shared.Services.AiChatClient>(client =>
    client.BaseAddress = new Uri("https+http://coded-values-ai"));

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
