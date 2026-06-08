using Microsoft.Extensions.AI;

namespace SchoolCollab.CodedValues.AI.Services;

/// <summary>
/// Routes chat requests to the correct <see cref="IChatClient"/> based on model name.
/// Models matching a cloud prefix are routed to OpenRouter; all others go to Ollama.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    private readonly IChatClient _localClient;
    private readonly IChatClient? _cloudClient;
    private readonly HashSet<string> _cloudPrefixes;
    private readonly ILogger<ChatClientFactory> _logger;

    public ChatClientFactory(
        IChatClient localClient,
        IChatClient? cloudClient,
        IEnumerable<string> cloudPrefixes,
        ILogger<ChatClientFactory> logger)
    {
        _localClient = localClient;
        _cloudClient = cloudClient;
        _cloudPrefixes = new HashSet<string>(cloudPrefixes, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    /// <inheritdoc />
    public IChatClient GetClient(string? model = null)
    {
        if (model is not null && _cloudClient is not null && IsCloudModel(model))
        {
            _logger.LogDebug("Routing model {Model} to cloud provider", model);
            return _cloudClient;
        }

        _logger.LogDebug("Routing model {Model} to local provider", model ?? "(default)");
        return _localClient;
    }

    /// <summary>
    /// Determines whether a model name belongs to a cloud provider
    /// by checking if it starts with any of the configured cloud prefixes.
    /// </summary>
    private bool IsCloudModel(string model)
    {
        foreach (var prefix in _cloudPrefixes)
        {
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}