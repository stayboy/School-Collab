using Microsoft.Extensions.AI;

namespace SchoolCollab.AI.Services;

/// <summary>
/// Routes chat requests to the correct <see cref="IChatClient"/> based on configuration.
/// The default provider is set via <c>codedvalue-ai-provider</c> configuration.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Returns the <see cref="IChatClient"/> for the configured default provider.
    /// </summary>
    IChatClient GetClient();

    /// <summary>
    /// Returns the name of the default provider (e.g., "ollama" or "openrouter").
    /// </summary>
    string DefaultProvider { get; }
}

/// <summary>
/// Configuration for a single AI provider.
/// </summary>
public sealed record ProviderConfig(
    string Name,
    string Endpoint,
    string? ApiKey,
    string DefaultModel);