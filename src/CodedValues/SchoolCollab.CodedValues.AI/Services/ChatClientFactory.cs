using Microsoft.Extensions.AI;

namespace SchoolCollab.CodedValues.AI.Services;

/// <summary>
/// Routes chat requests to the correct <see cref="IChatClient"/> based on the provider name.
/// Supported providers: <c>"ollama"</c> (local) and <c>"openrouter"</c> (cloud).
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    /// <summary>Well-known provider names.</summary>
    public static class Providers
    {
        public const string Ollama = "ollama";
        public const string OpenRouter = "openrouter";
    }

    private readonly IChatClient _ollamaClient;
    private readonly IChatClient? _openRouterClient;
    private readonly string _defaultProvider;
    private readonly ILogger<ChatClientFactory> _logger;

    public ChatClientFactory(
        IChatClient ollamaClient,
        IChatClient? openRouterClient,
        string defaultProvider,
        ILogger<ChatClientFactory> logger)
    {
        _ollamaClient = ollamaClient;
        _openRouterClient = openRouterClient;
        _defaultProvider = defaultProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string DefaultProvider => _defaultProvider;

    /// <inheritdoc />
    public IChatClient GetClient(string? provider = null)
    {
        var resolvedProvider = string.IsNullOrWhiteSpace(provider) ? _defaultProvider : provider;

        return resolvedProvider.ToLowerInvariant() switch
        {
            Providers.Ollama => _ollamaClient,
            Providers.OpenRouter when _openRouterClient is not null => _openRouterClient,
            Providers.OpenRouter => FallbackToOllama(provider!, "OpenRouter client not configured"),
            _ => FallbackToOllama(provider!, "Unknown provider")
        };
    }

    private IChatClient FallbackToOllama(string requestedProvider, string reason)
    {
        _logger.LogWarning("Falling back to Ollama: {Reason} (requested: {Provider})", reason, requestedProvider);
        return _ollamaClient;
    }
}