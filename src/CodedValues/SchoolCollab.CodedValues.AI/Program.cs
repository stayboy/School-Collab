using Microsoft.Extensions.AI;
using SchoolCollab.CodedValues.AI;
using SchoolCollab.CodedValues.AI.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Ollama IChatClient registration
var ollamaEndpoint = builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434/v1";
var ollamaModel = builder.Configuration["Ollama:Model"] ?? "llama3.1:8b";

builder.Services.AddSingleton<IChatClient>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<AiProgramMarker>>();
    logger.LogInformation("Configuring AI chat client with Ollama at {Endpoint}, model {Model}", ollamaEndpoint, ollamaModel);

    var openAiClient = new OpenAI.OpenAIClient(
        new System.ClientModel.ApiKeyCredential("ollama"),
        new OpenAI.OpenAIClientOptions { Endpoint = new Uri(ollamaEndpoint) });

    return openAiClient.GetChatClient(ollamaModel).AsIChatClient();
});

// HttpClient for calling the Coded Values API (service discovery)
builder.Services.AddHttpClient<CodedValuesApiClient>(client =>
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
