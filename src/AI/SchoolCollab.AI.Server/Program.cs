using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.AI.Services;
using SchoolCollab.AI.Tools.CodedValues;
using SchoolCollab.Core.Auth;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── Ollama (local) IChatClient registration ──
var ollamaEndpoint = builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434/v1";
var ollamaModel = builder.Configuration["Ollama:DefaultModel"] ?? ChatModelResolver.DefaultOllamaModel;

builder.Services.AddKeyedSingleton<IChatClient>("ollama", (sp, _) =>
{
    var logger = sp.GetRequiredService<ILogger<AiProgramMarker>>();
    logger.LogInformation("Configuring local AI chat client with Ollama at {Endpoint}, model {Model}", ollamaEndpoint, ollamaModel);

    var openAiClient = new OpenAI.OpenAIClient(
        new System.ClientModel.ApiKeyCredential("ollama"),
        new OpenAI.OpenAIClientOptions { Endpoint = new Uri(ollamaEndpoint) });

    return openAiClient.GetChatClient(ollamaModel).AsIChatClient();
});

// ── OpenRouter (cloud) IChatClient registration ──
var openRouterEndpoint = builder.Configuration["OpenRouter:Endpoint"] ?? "https://openrouter.ai/api/v1";
var openRouterApiKey = builder.Configuration["OpenRouter:ApiKey"];
var openRouterDefaultModel = builder.Configuration["OpenRouter:DefaultModel"] ?? ChatModelResolver.DefaultOpenRouterModel;

builder.Services.AddKeyedSingleton<IChatClient>("openrouter", (sp, _) =>
{
    var logger = sp.GetRequiredService<ILogger<AiProgramMarker>>();

    if (string.IsNullOrWhiteSpace(openRouterApiKey))
    {
        logger.LogWarning("OpenRouter:ApiKey not configured — cloud models will be unavailable");
        var fallbackOpenAiClient = new OpenAI.OpenAIClient(
            new System.ClientModel.ApiKeyCredential("unused"),
            new OpenAI.OpenAIClientOptions { Endpoint = new Uri(openRouterEndpoint) });
        return fallbackOpenAiClient.GetChatClient(openRouterDefaultModel).AsIChatClient();
    }

    logger.LogInformation("Configuring cloud AI chat client with OpenRouter at {Endpoint}", openRouterEndpoint);

    var openAiClient = new OpenAI.OpenAIClient(
        new System.ClientModel.ApiKeyCredential(openRouterApiKey),
        new OpenAI.OpenAIClientOptions { Endpoint = new Uri(openRouterEndpoint) });

    return openAiClient.GetChatClient(openRouterDefaultModel).AsIChatClient();
});

// ── ChatClientFactory: routes requests by explicit provider name ──
var defaultProvider = builder.Configuration["codedvalue-ai-provider"] ?? "ollama";

builder.Services.AddSingleton<IChatClientFactory>(sp =>
{
    var ollamaClient = sp.GetRequiredKeyedService<IChatClient>("ollama");
    var openRouterClient = sp.GetKeyedService<IChatClient>("openrouter");
    var logger = sp.GetRequiredService<ILogger<ChatClientFactory>>();

    logger.LogInformation("ChatClientFactory initialised — default provider: {DefaultProvider}", defaultProvider);

    return new ChatClientFactory(ollamaClient, openRouterClient, defaultProvider, logger);
});

// HttpClient for calling the Settings REST API (service discovery) + the
// CodedValues AI tool bag (9 tools, per-turn SelectToolsForPrompt narrowing,
// SSE formatting) + the CodedValues system-prompt provider. All three are
// wired by AddCodedValuesAiTools; the engine (AIChatEngine) picks them up as
// an IToolProvider + an ISystemPromptProvider. The settings-api project
// exposes the CodedValues aggregate endpoints under /api/coded-values/*
// alongside the FeatureFlag aggregate endpoints under /api/config/* +
// /api/features/*. See documents/solution/settings-context-merge-spec.md §8.
//
// Tenant forwarding: the chat endpoint receives the caller's x-tenant-id
// header; TenantForwardingDelegatingHandler forwards it onto every settings-api
// tool call so coded-value tools resolve the CALLING tenant's data instead of
// the default tenant. See docs/plans/2026-08-22-tenant-propagation-enroll-
// stream-investigation.md (Class B).
// Registered TRANSIENT (not Singleton): consistent with the same handler in
// Students.Api/Program.cs — avoids InnerHandler corruption if a second named
// client is ever attached.
builder.Services.AddHttpContextAccessor();
builder.Services.TryAddTransient<TenantForwardingDelegatingHandler>();
builder.Services.AddCodedValuesAiTools(client =>
    client.BaseAddress = new Uri("https+http://settings-api"),
    clientBuilder => clientBuilder.AddHttpMessageHandler<TenantForwardingDelegatingHandler>());

// Generic AI chat engine — drives /api/ai/chat for every registered
// IToolProvider / ISystemPromptProvider. Adding a second bounded context is a
// parallel AddXxxAiTools() call.
builder.Services.AddSingleton<AIChatEngine>();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapDefaultEndpoints();

// Configuration endpoint — returns the default AI provider and model so that
// admin clients can display the active configuration without re-implementing
// provider/model resolution on their side.
app.MapGet("/api/ai/config", (IConfiguration configuration) =>
{
    var (provider, model) = ChatModelResolver.Resolve(
        configuration["codedvalue-ai-provider"],
        configuration["Ollama:DefaultModel"],
        configuration["OpenRouter:DefaultModel"]);
    return Results.Ok(new { defaultProvider = provider, defaultModel = model });
});

// SSE streaming chat endpoint
app.MapPost("/api/ai/chat", async (HttpContext context, AIChatEngine aiService) =>
{
    var request = await context.Request.ReadFromJsonAsync<ChatRequest>(context.RequestAborted);
    if (request is null || request.Messages is null || request.Messages.Count == 0)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new { error = "Messages are required." }, context.RequestAborted);
        return;
    }

    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    var history = request.Messages
        .Select(m => new ChatMessage(
            m.Role == "user" ? ChatRole.User : ChatRole.Assistant,
            m.Text ?? string.Empty))
        .ToList();

    await foreach (var update in aiService.ChatAsync(history, context.RequestAborted))
    {
        var (eventType, payload) = update switch
        {
            ChatUpdate.TextChunk tc => ("TextChunk", System.Text.Json.JsonSerializer.Serialize(new { text = tc.Text })),
            ChatUpdate.ToolCallStart tcs => ("ToolCallStart", System.Text.Json.JsonSerializer.Serialize(new { callId = tcs.CallId, friendlyName = tcs.FriendlyName, argsSummary = tcs.ArgsSummary })),
            ChatUpdate.ToolCallEnd tce => ("ToolCallEnd", System.Text.Json.JsonSerializer.Serialize(new { callId = tce.CallId, friendlyName = tce.FriendlyName, resultSummary = tce.ResultSummary, success = tce.Success })),
            ChatUpdate.Error err => ("Error", System.Text.Json.JsonSerializer.Serialize(new { message = err.Message })),
            _ => (null, (string?)null)
        };

        if (eventType is not null && payload is not null)
        {
            await context.Response.WriteAsync($"event: {eventType}\ndata: {payload}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
});

app.Run();