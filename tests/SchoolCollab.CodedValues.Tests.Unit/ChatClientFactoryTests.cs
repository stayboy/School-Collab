using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.CodedValues.AI.Services;

namespace SchoolCollab.CodedValues.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ChatClientFactory"/> provider-based routing logic.
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
    // GetClient — null or empty provider → default provider
    // =====================================================================

    [TestMethod]
    public void GetClient_NullProvider_UsesDefaultOllama()
    {
        var factory = CreateFactory(defaultProvider: "ollama");
        var result = factory.GetClient(null);
        result.Should().Be(_ollamaClient.Object);
    }

    [TestMethod]
    public void GetClient_NullProvider_UsesDefaultOpenRouter()
    {
        var factory = CreateFactory(defaultProvider: "openrouter");
        var result = factory.GetClient(null);
        result.Should().Be(_openRouterClient.Object);
    }

    [TestMethod]
    public void GetClient_EmptyProvider_UsesDefault()
    {
        var factory = CreateFactory(defaultProvider: "ollama");
        var result = factory.GetClient("");
        result.Should().Be(_ollamaClient.Object);
    }

    [TestMethod]
    public void GetClient_WhitespaceProvider_UsesDefault()
    {
        var factory = CreateFactory(defaultProvider: "ollama");
        var result = factory.GetClient("   ");
        result.Should().Be(_ollamaClient.Object);
    }

    // =====================================================================
    // GetClient — explicit provider routing
    // =====================================================================

    [TestMethod]
    public void GetClient_OllamaProvider_ReturnsOllamaClient()
    {
        var factory = CreateFactory();
        var result = factory.GetClient("ollama");
        result.Should().Be(_ollamaClient.Object);
    }

    [TestMethod]
    public void GetClient_OpenRouterProvider_ReturnsOpenRouterClient()
    {
        var factory = CreateFactory();
        var result = factory.GetClient("openrouter");
        result.Should().Be(_openRouterClient.Object);
    }

    // =====================================================================
    // GetClient — case-insensitive provider matching
    // =====================================================================

    [TestMethod]
    [DataRow("Ollama")]
    [DataRow("OLLAMA")]
    [DataRow("ollama")]
    public void GetClient_OllamaProviderCaseInsensitive_ReturnsOllamaClient(string provider)
    {
        var factory = CreateFactory();
        var result = factory.GetClient(provider);
        result.Should().Be(_ollamaClient.Object);
    }

    [TestMethod]
    [DataRow("OpenRouter")]
    [DataRow("OPENROUTER")]
    [DataRow("openrouter")]
    public void GetClient_OpenRouterProviderCaseInsensitive_ReturnsOpenRouterClient(string provider)
    {
        var factory = CreateFactory();
        var result = factory.GetClient(provider);
        result.Should().Be(_openRouterClient.Object);
    }

    // =====================================================================
    // GetClient — OpenRouter not configured (null)
    // =====================================================================

    [TestMethod]
    public void GetClient_OpenRouterProviderWithoutOpenRouterClient_FallsBackToOllama()
    {
        var factory = new ChatClientFactory(
            _ollamaClient.Object,
            openRouterClient: null,
            defaultProvider: "ollama",
            _logger.Object);
        var result = factory.GetClient("openrouter");
        result.Should().Be(_ollamaClient.Object);
    }

    [TestMethod]
    public void GetClient_DefaultOpenRouterWithoutOpenRouterClient_FallsBackToOllama()
    {
        var factory = new ChatClientFactory(
            _ollamaClient.Object,
            openRouterClient: null,
            defaultProvider: "openrouter",
            _logger.Object);
        // Default is openrouter but client is null → falls back to ollama
        var result = factory.GetClient(null);
        result.Should().Be(_ollamaClient.Object);
    }

    // =====================================================================
    // GetClient — unknown provider → fallback to Ollama
    // =====================================================================

    [TestMethod]
    public void GetClient_UnknownProvider_FallsBackToOllama()
    {
        var factory = CreateFactory();
        var result = factory.GetClient("azure");
        result.Should().Be(_ollamaClient.Object);
    }

    [TestMethod]
    public void GetClient_GibberishProvider_FallsBackToOllama()
    {
        var factory = CreateFactory();
        var result = factory.GetClient("xyz-not-a-provider");
        result.Should().Be(_ollamaClient.Object);
    }

    // =====================================================================
    // GetClient — well-known provider constants
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
    // GetClient — logging on fallback
    // =====================================================================

    [TestMethod]
    public void GetClient_UnknownProvider_LogsWarning()
    {
        var factory = CreateFactory();
        factory.GetClient("unknown");
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
    public void GetClient_OpenRouterWithoutClient_LogsWarning()
    {
        var factory = new ChatClientFactory(
            _ollamaClient.Object,
            openRouterClient: null,
            defaultProvider: "ollama",
            _logger.Object);
        factory.GetClient("openrouter");
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