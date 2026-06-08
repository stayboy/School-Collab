using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.CodedValues.AI.Services;

namespace SchoolCollab.CodedValues.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ChatClientFactory"/> routing logic.
/// </summary>
[TestClass]
public class ChatClientFactoryTests
{
    private readonly Mock<IChatClient> _localClient = new();
    private readonly Mock<IChatClient> _cloudClient = new();
    private readonly Mock<ILogger<ChatClientFactory>> _logger = new();

    private static readonly string[] DefaultPrefixes =
        ["openai/", "anthropic/", "google/", "meta-llama/", "mistralai/", "deepseek/"];

    private ChatClientFactory CreateFactory(
        IChatClient? local = null,
        IChatClient? cloud = null,
        string[]? prefixes = null)
        => new(
            local ?? _localClient.Object,
            cloud ?? _cloudClient.Object,
            prefixes ?? DefaultPrefixes,
            _logger.Object);

    // =====================================================================
    // GetClient — null or empty model → local
    // =====================================================================

    [TestMethod]
    public void GetClient_NullModel_ReturnsLocalClient()
    {
        var factory = CreateFactory();
        var result = factory.GetClient(null);
        result.Should().Be(_localClient.Object);
    }

    [TestMethod]
    public void GetClient_EmptyStringModel_ReturnsLocalClient()
    {
        var factory = CreateFactory();
        var result = factory.GetClient("");
        result.Should().Be(_localClient.Object);
    }

    // =====================================================================
    // GetClient — local model names → local
    // =====================================================================

    [TestMethod]
    public void GetClient_LocalOllamaModel_ReturnsLocalClient()
    {
        var factory = CreateFactory();
        var result = factory.GetClient("llama3.2");
        result.Should().Be(_localClient.Object);
    }

    [TestMethod]
    public void GetClient_LocalModelWithSimilarPrefix_ReturnsLocalClient()
    {
        // "deepseek-r1" is a local Ollama model name, should NOT match "deepseek/" prefix
        var factory = CreateFactory();
        var result = factory.GetClient("deepseek-r1");
        result.Should().Be(_localClient.Object);
    }

    [TestMethod]
    [DataRow("gpt-4o-mini")]
    [DataRow("claude-3-haiku")]
    [DataRow("phi3")]
    public void GetClient_LocalModelNames_ReturnsLocalClient(string model)
    {
        var factory = CreateFactory();
        var result = factory.GetClient(model);
        result.Should().Be(_localClient.Object);
    }

    // =====================================================================
    // GetClient — cloud model names → cloud
    // =====================================================================

    [TestMethod]
    [DataRow("openai/gpt-4o-mini")]
    [DataRow("openai/gpt-4o")]
    [DataRow("anthropic/claude-3.5-haiku")]
    [DataRow("anthropic/claude-sonnet-4")]
    [DataRow("google/gemini-2.0-flash")]
    [DataRow("meta-llama/llama-3.3-70b")]
    [DataRow("mistralai/mistral-small")]
    [DataRow("deepseek/deepseek-chat")]
    public void GetClient_CloudModelNames_ReturnsCloudClient(string model)
    {
        var factory = CreateFactory();
        var result = factory.GetClient(model);
        result.Should().Be(_cloudClient.Object);
    }

    // =====================================================================
    // GetClient — case-insensitive prefix matching
    // =====================================================================

    [TestMethod]
    public void GetClient_CloudPrefixCaseInsensitive_RoutesToCloud()
    {
        var factory = CreateFactory();
        var result = factory.GetClient("OpenAI/gpt-4o-mini");
        result.Should().Be(_cloudClient.Object);
    }

    [TestMethod]
    public void GetClient_CloudPrefixMixedCase_RoutesToCloud()
    {
        var factory = CreateFactory();
        var result = factory.GetClient("Anthropic/claude-3.5-haiku");
        result.Should().Be(_cloudClient.Object);
    }

    // =====================================================================
    // GetClient — no cloud client configured
    // =====================================================================

    [TestMethod]
    public void GetClient_CloudModelWithoutCloudClient_FallsBackToLocal()
    {
        var factory = new ChatClientFactory(
            _localClient.Object,
            cloudClient: null,
            DefaultPrefixes,
            _logger.Object);
        var result = factory.GetClient("openai/gpt-4o-mini");
        result.Should().Be(_localClient.Object);
    }

    [TestMethod]
    public void GetClient_LocalModelWithoutCloudClient_ReturnsLocalClient()
    {
        var factory = new ChatClientFactory(
            _localClient.Object,
            cloudClient: null,
            DefaultPrefixes,
            _logger.Object);
        var result = factory.GetClient("llama3.2");
        result.Should().Be(_localClient.Object);
    }

    // =====================================================================
    // GetClient — custom prefix configuration
    // =====================================================================

    [TestMethod]
    public void GetClient_CustomPrefixes_RespectConfiguration()
    {
        var factory = CreateFactory(prefixes: ["custom/", "my-"]);
        var result = factory.GetClient("custom/model-x");
        result.Should().Be(_cloudClient.Object);
    }

    [TestMethod]
    public void GetClient_CustomPrefixes_NonMatchingModel_ReturnsLocal()
    {
        var factory = CreateFactory(prefixes: ["custom/", "my-"]);
        var result = factory.GetClient("openai/gpt-4o-mini");
        result.Should().Be(_localClient.Object);
    }

    // =====================================================================
    // GetClient — empty prefixes collection
    // =====================================================================

    [TestMethod]
    public void GetClient_EmptyPrefixes_AllModelsGoToLocal()
    {
        var factory = CreateFactory(prefixes: []);
        // Even a model that looks like a cloud model goes to local
        var result = factory.GetClient("openai/gpt-4o-mini");
        result.Should().Be(_localClient.Object);
    }

    // =====================================================================
    // IsCloudModel — boundary cases
    // =====================================================================

    [TestMethod]
    public void GetClient_PrefixExactMatch_RoutesToCloud()
    {
        // Model name that is exactly the prefix (e.g., "openai/")
        var factory = CreateFactory();
        var result = factory.GetClient("openai/");
        result.Should().Be(_cloudClient.Object);
    }

    [TestMethod]
    public void GetClient_ModelNameStartsWithPrefixButNoSlash_RoutesLocal()
    {
        // "openai-gpt4" does NOT match prefix "openai/" — no slash
        var factory = CreateFactory();
        var result = factory.GetClient("openai-gpt4");
        result.Should().Be(_localClient.Object);
    }
}