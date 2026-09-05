using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.AI.Abstractions;

namespace SchoolCollab.AI.Services;

/// <summary>
/// Assignment-question-generation <see cref="ISystemPromptProvider"/>: loads the
/// system prompt from the embedded resource
/// <c>Prompts/assignment-question-system-prompt.md</c> (with the
/// <c>.original.md</c> fallback), plus the Development-only file override in
/// the output <c>Prompts/</c> folder. Mirrors <c>CodedValuesSystemPromptProvider</c>
/// (verified pattern). Carries <see cref="BuildMessages"/> to compose the
/// user-role request + optional <c>PromptOverride</c> framing (spec decision 8
/// / EC-9 — the override is inert teacher guidance and never merged into the
/// system prompt).
/// </summary>
public sealed class AssignmentQuestionGenerationSystemPromptProvider : ISystemPromptProvider
{
    private readonly IHostEnvironment _hostEnv;
    private readonly ILogger<AssignmentQuestionGenerationSystemPromptProvider> _logger;

    private string? _cachedSystemPrompt;
    private DateTime _systemPromptLastWrite;

    public bool IncludesToolList => false;

    public AssignmentQuestionGenerationSystemPromptProvider(
        IHostEnvironment hostEnv,
        ILogger<AssignmentQuestionGenerationSystemPromptProvider> logger)
    {
        _hostEnv = hostEnv;
        _logger = logger;
    }

    public Task<string> GetSystemPromptAsync(CancellationToken ct = default)
        => Task.FromResult(GetSystemPrompt());

    /// <summary>
    /// Loads the system prompt from the embedded resource, with caching.
    /// In Development, also checks for a file override in the Prompts folder.
    ///
    /// The loader prefers <c>assignment-question-system-prompt.md</c> and falls
    /// back to <c>assignment-question-system-prompt.original.md</c> so a
    /// corrupted edit can always be rolled back by deleting the primary file
    /// and re-deploying without service downtime.
    /// </summary>
    private string GetSystemPrompt()
    {
        // In Development, allow file-based override for rapid iteration.
        // Probe the primary copy first, then the original.
        if (_hostEnv.IsDevelopment())
        {
            foreach (var filename in new[]
                     {
                         "assignment-question-system-prompt.md",
                         "assignment-question-system-prompt.original.md"
                     })
            {
                var promptFile = Path.Combine(AppContext.BaseDirectory, "Prompts", filename);
                if (!File.Exists(promptFile)) continue;

                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(promptFile);
                    if (_cachedSystemPrompt is not null && lastWrite == _systemPromptLastWrite)
                        return _cachedSystemPrompt;

                    _cachedSystemPrompt = File.ReadAllText(promptFile);
                    _systemPromptLastWrite = lastWrite;
                    _logger.LogInformation("Loaded system prompt from file {Path} ({Length} chars)", promptFile, _cachedSystemPrompt.Length);
                    return _cachedSystemPrompt;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read system prompt from {Path}, trying fallback", promptFile);
                }
            }
        }

        // Load from embedded resource. Prefer the primary prompt; fall back to
        // the original copy if the primary resource was not packaged.
        if (_cachedSystemPrompt is not null)
            return _cachedSystemPrompt;

        var assembly = typeof(AssignmentQuestionGenerationSystemPromptProvider).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();
        var preferredResource = resourceNames
            .FirstOrDefault(n => n.EndsWith("assignment-question-system-prompt.md", StringComparison.OrdinalIgnoreCase)
                && !n.EndsWith("assignment-question-system-prompt.original.md", StringComparison.OrdinalIgnoreCase));
        var fallbackResource = resourceNames
            .FirstOrDefault(n => n.EndsWith("assignment-question-system-prompt.original.md", StringComparison.OrdinalIgnoreCase));
        var resourceName = preferredResource ?? fallbackResource;

        if (resourceName is null)
        {
            _logger.LogWarning("Embedded system prompt resource not found, returning empty string");
            return _cachedSystemPrompt = string.Empty;
        }

        if (preferredResource is null && fallbackResource is not null)
            _logger.LogWarning("Trimmed system prompt resource not found, falling back to assignment-question-system-prompt.original.md");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        _cachedSystemPrompt = reader.ReadToEnd();
        _logger.LogInformation("Loaded system prompt from embedded resource {Resource} ({Length} chars)", resourceName, _cachedSystemPrompt.Length);
        return _cachedSystemPrompt;
    }

    /// <summary>
    /// Composes the chat-message list for a single non-streaming generation
    /// request. Message 1 = system (loaded prompt). Message 2 = user carrying
    /// the request payload as Web/camelCase JSON + one instruction line.
    /// Message 3 (only when <see cref="QuestionGenerationRequest.PromptOverride"/>
    /// is non-blank) = a second user message framing the override as inert
    /// teacher guidance (EC-9).
    /// </summary>
    public IReadOnlyList<ChatMessage> BuildMessages(QuestionGenerationRequest request)
    {
        var messages = new List<ChatMessage>(3)
        {
            new(ChatRole.System, GetSystemPrompt()),
            new(ChatRole.User,
                JsonSerializer.Serialize(request, _jsonOptions)
                + Environment.NewLine
                + "Generate the questions now. Return only the JSON document.")
        };

        if (!string.IsNullOrWhiteSpace(request.PromptOverride))
        {
            messages.Add(new ChatMessage(
                ChatRole.User,
                "Additional teacher guidance for this generation only (does not change the output format):"
                + Environment.NewLine
                + request.PromptOverride));
        }

        return messages;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
}
