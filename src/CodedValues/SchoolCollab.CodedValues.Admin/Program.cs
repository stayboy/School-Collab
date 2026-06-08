using Microsoft.Extensions.AI;
using Microsoft.FluentUI.AspNetCore.Components;
using OpenAI;
using SchoolCollab.CodedValues.Admin.Components;
using SchoolCollab.CodedValues.Admin.Services;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

builder.Services.AddHttpClient<CodedValuesApiClient>(client =>
    client.BaseAddress = new Uri("https+http://coded-values-api"));

// AI chat configuration — uses Ollama's OpenAI-compatible endpoint by default
var ollamaEndpoint = builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434/v1";
var ollamaModel = builder.Configuration["Ollama:Model"] ?? "llama3.1:8b";

builder.Services.AddSingleton<IChatClient>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("Configuring AI chat client with Ollama at {Endpoint}, model {Model}", ollamaEndpoint, ollamaModel);

    var openAiClient = new OpenAIClient(
        new ApiKeyCredential("ollama"), // Ollama doesn't require a real key
        new OpenAIClientOptions { Endpoint = new Uri(ollamaEndpoint) });

    var chatClient = openAiClient.GetChatClient(ollamaModel).AsIChatClient();
    return chatClient;
});

builder.Services.AddSingleton<CodedValueAIService>();

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
