using Microsoft.Extensions.AI;
using SchoolCollab.CodedValues.AI;
using SchoolCollab.CodedValues.AI.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── Ollama (local) IChatClient registration ──
var ollamaEndpoint = builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434/v1";
var ollamaModel = builder.Configuration["Ollama:Model"] ?? "llama3.1:8b";

builder.Services.AddSingleton<IChatClient>(sp =>
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
var openRouterDefaultModel = builder.Configuration["OpenRouter:DefaultModel"] ?? "openai/gpt-4o-mini";

builder.Services.AddSingleton<IChatClient>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<AiProgramMarker>>();

    if (string.IsNullOrWhiteSpace(openRouterApiKey))
    {
        logger.LogWarning("OpenRouter:ApiKey not configured — cloud models will be unavailable");
        // Return a no-op client that throws on use; factory will skip it if no key is provided
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

// ── ChatClientFactory: routes requests to Ollama or OpenRouter based on model name ──
var cloudPrefixes = builder.Configuration.GetSection("OpenRouter:CloudModelPrefixes").Get<string[]>()
    ?? ["openai/", "anthropic/", "google/", "meta-llama/", "mistralai/", "deepseek/"];

builder.Services.AddSingleton<IChatClientFactory>(sp =>
{
    var clients = sp.GetServices<IChatClient>().ToArray();
    var localClient = clients[0];  // Ollama — registered first
    var cloudClient = clients.Length > 1 ? clients[1] : null;  // OpenRouter — registered second
    var logger = sp.GetRequiredService<ILogger<ChatClientFactory>>();

    logger.LogInformation("ChatClientFactory initialised with {CloudPrefixCount} cloud prefixes: {Prefixes}",
        cloudPrefixes.Length, string.Join(", ", cloudPrefixes));

    return new ChatClientFactory(localClient, cloudClient, cloudPrefixes, logger);
});

// HttpClient for calling the Coded Values API (service discovery)
builder.Services.AddHttpClient<ICodedValuesApiClient, CodedValuesApiClient>(client =>
    client.BaseAddress = new Uri("https+http://coded-values-api"));

builder.Services.AddSingleton<CodedValueAIService>();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapDefaultEndpoints();

// SSE streaming chat endpoint
app.MapPost("/api/ai/chat", async (HttpContext context, CodedValueAIService aiService) =>
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

    await foreach (var update in aiService.ChatAsync(history, request.Model, context.RequestAborted))
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
