using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.CodedValues.AI.Services;

namespace SchoolCollab.CodedValues.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ChatClientFactory"/> configuration-based provider routing.
/// </summary>
[TestClass]
public class ChatClientFactoryTests
{
    private readonly Mock<IChatClient> _ollamaClient = new();
    private readonly Mock<IChatClient> _openRouterClient = new();
    private readonly Mock<ILogger<ChatClientFactory>> _logger = new();

    private ChatClientFactory CreateFactory(
        IChatClient? ollama = null,
        IChatClient? openRouter = null,
        string defaultProvider = "ollama")
        => new(
            ollama ?? _ollamaClient.Object,
            openRouter ?? _openRouterClient.Object,
            defaultProvider,
            _logger.Object);

    // =====================================================================
    // DefaultProvider
    // =====================================================================

    [TestMethod]
    [DataRow("ollama")]
    [DataRow("openrouter")]
    public void DefaultProvider_ReturnsConfiguredValue(string provider)
    {
        var factory = CreateFactory(defaultProvider: provider);
        factory.DefaultProvider.Should().Be(provider);
    }

    // =====================================================================
    // GetClient — returns the client for the configured default provider
    // =====================================================================

    [TestMethod]
    public void GetClient_DefaultOllama_ReturnsOllamaClient()
    {
        var factory = CreateFactory(defaultProvider: "ollama");
        var result = factory.GetClient();
        result.Should().Be(_ollamaClient.Object);
    }

    [TestMethod]
    public void GetClient_DefaultOpenRouter_ReturnsOpenRouterClient()
    {
        var factory = CreateFactory(defaultProvider: "openrouter");
        var result = factory.GetClient();
        result.Should().Be(_openRouterClient.Object);
    }

    // =====================================================================
    // GetClient — case-insensitive default provider resolution
    // =====================================================================

    [TestMethod]
    [DataRow("Ollama")]
    [DataRow("OLLAMA")]
    [DataRow("ollama")]
    public void GetClient_DefaultOllamaCaseInsensitive_ReturnsOllamaClient(string provider)
    {
        var factory = CreateFactory(defaultProvider: provider);
        var result = factory.GetClient();
        result.Should().Be(_ollamaClient.Object);
    }

    [TestMethod]
    [DataRow("OpenRouter")]
    [DataRow("OPENROUTER")]
    [DataRow("openrouter")]
    public void GetClient_DefaultOpenRouterCaseInsensitive_ReturnsOpenRouterClient(string provider)
    {
        var factory = CreateFactory(defaultProvider: provider);
        var result = factory.GetClient();
        result.Should().Be(_openRouterClient.Object);
    }

    // =====================================================================
    // GetClient — OpenRouter not configured (null) → fallback to Ollama
    // =====================================================================

    [TestMethod]
    public void GetClient_DefaultOpenRouterWithoutOpenRouterClient_FallsBackToOllama()
    {
        var factory = new ChatClientFactory(
            _ollamaClient.Object,
            openRouterClient: null,
            defaultProvider: "openrouter",
            _logger.Object);
        var result = factory.GetClient();
        result.Should().Be(_ollamaClient.Object);
    }

    // =====================================================================
    // GetClient — unknown default provider → falls back to Ollama
    // =====================================================================

    [TestMethod]
    public void GetClient_UnknownDefaultProvider_FallsBackToOllama()
    {
        var factory = CreateFactory(defaultProvider: "azure");
        var result = factory.GetClient();
        result.Should().Be(_ollamaClient.Object);
    }

    // =====================================================================
    // Provider constants
    // =====================================================================

    [TestMethod]
    public void Providers_OllamaConstant_IsOllama()
    {
        ChatClientFactory.Providers.Ollama.Should().Be("ollama");
    }

    [TestMethod]
    public void Providers_OpenRouterConstant_IsOpenRouter()
    {
        ChatClientFactory.Providers.OpenRouter.Should().Be("openrouter");
    }

    // =====================================================================
    // Logging on fallback
    // =====================================================================

    [TestMethod]
    public void GetClient_UnknownDefaultProvider_LogsWarning()
    {
        var factory = CreateFactory(defaultProvider: "unknown");
        factory.GetClient();
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Falling back to Ollama")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public void GetClient_OpenRouterDefaultWithoutClient_LogsWarning()
    {
        var factory = new ChatClientFactory(
            _ollamaClient.Object,
            openRouterClient: null,
            defaultProvider: "openrouter",
            _logger.Object);
        factory.GetClient();
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Falling back to Ollama")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}