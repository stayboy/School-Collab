using Microsoft.Extensions.Configuration;

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
    /// <c>OpenRouter:DefaultModel</c> is not configured.
    /// </summary>
    public const string DefaultOpenRouterModel = "google/gemma-4-26b-a4b-it";

    /// <summary>
    /// Resolves the provider and model from application configuration.
    /// Reads <c>codedvalue-ai-provider</c>, <c>Ollama:DefaultModel</c>,
    /// and <c>OpenRouter:DefaultModel</c>.
    /// </summary>
    public static (string Provider, string Model) Resolve(IConfiguration configuration)
        => Resolve(
            configuration["codedvalue-ai-provider"],
            configuration["Ollama:DefaultModel"],
            configuration["OpenRouter:DefaultModel"]);

    /// <summary>
    /// Pure resolution core. Normalises the provider name and selects the
    /// configured model, falling back to the provider's default model when
    /// the configured value is missing or blank. Any unrecognised provider
    /// resolves to Ollama.
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