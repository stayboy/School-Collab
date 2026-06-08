using Microsoft.Extensions.AI;

namespace SchoolCollab.CodedValues.AI.Services;

/// <summary>
/// Routes chat requests to the correct <see cref="IChatClient"/> based on the model identifier.
/// Local/Ollama models use one client, cloud/OpenRouter models use another.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Returns the appropriate <see cref="IChatClient"/> for the given model.
    /// If the model name matches a cloud prefix, the cloud provider is used;
    /// otherwise, the local (Ollama) provider is used.
    /// </summary>
    IChatClient GetClient(string? model = null);
}

/// <summary>
/// Configuration for a single AI provider.
/// </summary>
public sealed record ProviderConfig(
    string Name,
    string Endpoint,
    string? ApiKey,
    string DefaultModel,
    string[] CloudModelPrefixes);