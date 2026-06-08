using Microsoft.Extensions.AI;

namespace SchoolCollab.CodedValues.AI.Services;

/// <summary>
/// Routes chat requests to the correct <see cref="IChatClient"/> based on the provider name.
/// Supported providers: <c>"ollama"</c> (local) and <c>"openrouter"</c> (cloud).
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Returns the appropriate <see cref="IChatClient"/> for the given provider.
    /// When <paramref name="provider"/> is null or empty, the default provider is used.
    /// </summary>
    IChatClient GetClient(string? provider = null);

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