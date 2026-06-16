using Microsoft.Extensions.AI;

namespace SchoolCollab.AI.Services;

/// <summary>
/// Routes chat requests to the correct <see cref="IChatClient"/> based on configuration.
/// The default provider is set via <c>AI:DefaultProvider</c> configuration.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    /// <summary>Well-known provider names.</summary>
    public static class Providers
    {
        public const string Ollama = "ollama";
        public const string OpenRouter = "openrouter";
    }

    private readonly IChatClient _defaultClient;
    private readonly string _defaultProvider;
    private readonly ILogger<ChatClientFactory> _logger;

    public ChatClientFactory(
        IChatClient ollamaClient,
        IChatClient? openRouterClient,
        string defaultProvider,
        ILogger<ChatClientFactory> logger)
    {
        _defaultProvider = defaultProvider;
        _logger = logger;

        _defaultClient = defaultProvider.ToLowerInvariant() switch
        {
            Providers.OpenRouter when openRouterClient is not null => openRouterClient,
            Providers.OpenRouter => LogFallback(ollamaClient, "OpenRouter client not configured"),
            Providers.Ollama => ollamaClient,
            _ => LogFallback(ollamaClient, $"Unknown provider '{defaultProvider}'")
        };
    }

    /// <inheritdoc />
    public string DefaultProvider => _defaultProvider;

    /// <inheritdoc />
    public IChatClient GetClient() => _defaultClient;

    private IChatClient LogFallback(IChatClient fallbackClient, string reason)
    {
        _logger.LogWarning("Falling back to Ollama: {Reason} (default was: {Provider})", reason, _defaultProvider);
        return fallbackClient;
    }
}