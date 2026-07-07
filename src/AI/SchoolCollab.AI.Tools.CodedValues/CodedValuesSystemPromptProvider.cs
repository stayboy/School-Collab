using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolCollab.AI.Abstractions;

namespace SchoolCollab.AI.Tools.CodedValues;

/// <summary>
/// CodedValues-flavoured <see cref="ISystemPromptProvider"/>: loads the
/// Coded Values system prompt from the embedded resource
/// <c>Prompts/coded-values-system-prompt.md</c> (with the
/// <c>.original.md</c> fallback), plus the Development-only file override
/// in the output <c>Prompts/</c> folder. Carried over verbatim from the
/// former <c>CodedValueAIService</c> prompt loader, retargeted to this
/// assembly and the renamed prompt file.
/// </summary>
public sealed class CodedValuesSystemPromptProvider : ISystemPromptProvider
{
    private readonly IHostEnvironment _hostEnv;
    private readonly ILogger<CodedValuesSystemPromptProvider> _logger;

    private string? _cachedSystemPrompt;
    private DateTime _systemPromptLastWrite;

    public bool IncludesToolList => false;

    public CodedValuesSystemPromptProvider(IHostEnvironment hostEnv, ILogger<CodedValuesSystemPromptProvider> logger)
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
    /// The loader prefers the trimmed prompt
    /// (<c>coded-values-system-prompt.md</c>) and falls back to the original
    /// (<c>coded-values-system-prompt.original.md</c>) if the primary
    /// file/embedded resource is missing — so a corrupted trim can always be
    /// rolled back by deleting the trimmed copy and re-deploying without
    /// service downtime.
    /// </summary>
    private string GetSystemPrompt()
    {
        // In Development, allow file-based override for rapid iteration.
        // Probe the trimmed copy first, then the original.
        if (_hostEnv.IsDevelopment())
        {
            foreach (var filename in new[] { "coded-values-system-prompt.md", "coded-values-system-prompt.original.md" })
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

        // Load from embedded resource. Prefer the trimmed prompt; fall back to
        // the original copy if the trimmed resource was not packaged.
        if (_cachedSystemPrompt is not null)
            return _cachedSystemPrompt;

        var assembly = typeof(CodedValuesSystemPromptProvider).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();
        var preferredResource = resourceNames
            .FirstOrDefault(n => n.EndsWith("coded-values-system-prompt.md", StringComparison.OrdinalIgnoreCase)
                && !n.EndsWith("coded-values-system-prompt.original.md", StringComparison.OrdinalIgnoreCase));
        var fallbackResource = resourceNames
            .FirstOrDefault(n => n.EndsWith("coded-values-system-prompt.original.md", StringComparison.OrdinalIgnoreCase));
        var resourceName = preferredResource ?? fallbackResource;

        if (resourceName is null)
        {
            _logger.LogWarning("Embedded system prompt resource not found, using fallback");
            return _cachedSystemPrompt = FallbackSystemPrompt;
        }

        if (preferredResource is null && fallbackResource is not null)
            _logger.LogWarning("Trimmed system prompt resource not found, falling back to coded-values-system-prompt.original.md");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        _cachedSystemPrompt = reader.ReadToEnd();
        _logger.LogInformation("Loaded system prompt from embedded resource {Resource} ({Length} chars)", resourceName, _cachedSystemPrompt.Length);
        return _cachedSystemPrompt;
    }

    private const string FallbackSystemPrompt = """
        You are a helpful assistant for managing coded values in a school collaboration system.
        Coded values are hierarchical lookup tables. Each has a unique code, a name, and an optional description.
        Parents define categories; children are the actual values.

        ## Critical rules for responses
        1. Always determine the parent code FIRST — it is required for every create/update. Parse the user's request (explicit code, name that implies one, or context), look it up read-only with get_coded_value_by_code. If it does not exist, derive a proposed code+name (do not create yet). If it cannot be determined or derived, stop and ask the user — never proceed without a parent code. On a confirmation turn, recover it from the prior proposal message instead of re-asking.
        2. Never list or describe tool/function calls in your text response. Just use the tools silently and present results.
        3. Never output raw JSON or technical data structures. Always use human-readable format.
        4. Two-turn gate: always present proposed values as a Markdown table and STOP for explicit user approval BEFORE any write. In the proposal turn you may ONLY call read-only lookups (get_coded_value_by_code, list_coded_value_categories) — never call create/update/set/disable/enable tools. The user's explicit "yes" in the next turn is the only trigger that authorizes a write.
        5. After a tool succeeds, describe the outcome in plain English.

        ## Workflow
        1. Identify the parent coded value from the user's request (read-only lookup only). If ambiguous, ask. If not found, propose a new parent in the table but do NOT create it yet.
        2. Present proposed values (including any new parent) as a Markdown table. Begin with a `Parent: \`CODE\` (Name) — existing|NEW` header line so the next turn can recover the code. Ask: "Shall I create these coded values?" Then STOP — end your turn. Do not write in this turn.
        3. When the user explicitly confirms (e.g. "yes", "go ahead") in the NEXT turn, recover the parent code from your previous proposal message — do NOT ask the user to restate it. Emit NO preamble text — immediately call the bulk creation tool using the exact proposed values. A text-only reply creates nothing.
        4. After creation succeeds, confirm briefly in plain English.
        """;
}