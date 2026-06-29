namespace SchoolCollab.AI.Services;

/// <summary>
/// Resolves the active AI provider and model from configuration.
/// Extracted as a pure function so it can be unit-tested independently
/// of the Blazor component that previously owned this logic.
/// </summary>
public static class ChatModelResolver
{
    /// <summary>
    /// Default model used for the local Ollama provider when
    /// <c>Ollama:DefaultModel</c> is not configured.
    /// </summary>
    public const string DefaultOllamaModel = "gemma4:31b-cloud";

    /// <summary>
    /// Default model used for the cloud OpenRouter provider when
    /// <c>OpenRouter:DefaultModel</c> is not configured. Kept in lock-step
    /// with <c>src/AppHost/SchoolCollab.AppHost/appsettings.json</c> under
    /// <c>Parameters:openrouter-default-model</c> so the constant fallback
    /// matches the default the AppHost injects. Avoid the <c>:free</c>-tagged
    /// OpenRouter aliases here — they are heavily rate-limited, intermittently
    /// return empty responses, and surface non-rate-limit-formatted errors
    /// that confuse client-side retry logic.
    /// </summary>
    public const string DefaultOpenRouterModel = "google/gemma-3-12b-it";

    /// <summary>
    /// Resolves the provider and model from three raw configuration values.
    /// Pure function — kept free of <see cref="IConfiguration"/> so callers can
    /// pull values from any source (appsettings, user-secrets, environment,
    /// or in-memory dictionaries in tests) without coupling to a specific
    /// configuration provider.
    /// </summary>
    public static (string Provider, string Model) Resolve(
        string? provider,
        string? ollamaModel,
        string? openRouterModel)
    {
        var normalizedProvider = NormalizeProvider(provider);

        var model = normalizedProvider switch
        {
            ChatClientFactory.Providers.OpenRouter => openRouterModel?.Trim(),
            _ => ollamaModel?.Trim()
        };

        model = string.IsNullOrWhiteSpace(model)
            ? normalizedProvider switch
            {
                ChatClientFactory.Providers.OpenRouter => DefaultOpenRouterModel,
                _ => DefaultOllamaModel
            }
            : model;

        return (normalizedProvider, model!);
    }

    /// <summary>
    /// Normalises a raw provider string to one of the well-known
    /// <see cref="ChatClientFactory.Providers"/> values. Comparison is
    /// case-insensitive. A null/blank value defaults to Ollama, and any
    /// unrecognised value also defaults to Ollama.
    /// </summary>
    public static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return ChatClientFactory.Providers.Ollama;

        return provider.Trim().Equals(ChatClientFactory.Providers.OpenRouter, StringComparison.OrdinalIgnoreCase)
            ? ChatClientFactory.Providers.OpenRouter
            : ChatClientFactory.Providers.Ollama;
    }
}