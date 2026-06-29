using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.AI.Services;

namespace SchoolCollab.CodedValues.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ChatModelResolver"/> — verifies that each
/// provider (Ollama and OpenRouter) resolves to the correct working model,
/// that configured models take precedence, and that sensible defaults apply
/// when configuration is missing.
/// </summary>
[TestClass]
public class ChatModelResolverTests
{
    // Working models configured per provider.
    private const string OllamaModel = "gemma4:31b-cloud";
    // Stable, paid OpenRouter model. Avoid the :free aliases — they are heavily
    // rate-limited and intermittently return empty responses, which makes the
    // live integration tests flake.
    private const string OpenRouterModel = "google/gemma-3-12b-it";

    // =====================================================================
    // Constants
    // =====================================================================

    [TestMethod]
    public void DefaultOllamaModel_IsGemma4_31b_Cloud()
    {
        ChatModelResolver.DefaultOllamaModel.Should().Be(OllamaModel);
    }

    [TestMethod]
    public void DefaultOpenRouterModel_IsGoogleGemma3_12b_It()
    {
        ChatModelResolver.DefaultOpenRouterModel.Should().Be(OpenRouterModel);
    }

    // =====================================================================
    // Ollama provider — configured model is returned
    // =====================================================================

    [TestMethod]
    public void Resolve_OllamaWithConfiguredModel_ReturnsOllamaProviderAndModel()
    {
        var (provider, model) = ChatModelResolver.Resolve(
            provider: "ollama",
            ollamaModel: OllamaModel,
            openRouterModel: OpenRouterModel);

        provider.Should().Be("ollama");
        model.Should().Be(OllamaModel);
    }

    [TestMethod]
    public void Resolve_Ollama_IgnoresOpenRouterModel()
    {
        var (_, model) = ChatModelResolver.Resolve(
            provider: "ollama",
            ollamaModel: OllamaModel,
            openRouterModel: OpenRouterModel);

        model.Should().Be(OllamaModel, "Ollama must use the Ollama model, not the OpenRouter one");
    }

    // =====================================================================
    // OpenRouter provider — configured model is returned
    // =====================================================================

    [TestMethod]
    public void Resolve_OpenRouterWithConfiguredModel_ReturnsOpenRouterProviderAndModel()
    {
        var (provider, model) = ChatModelResolver.Resolve(
            provider: "openrouter",
            ollamaModel: OllamaModel,
            openRouterModel: OpenRouterModel);

        provider.Should().Be("openrouter");
        model.Should().Be(OpenRouterModel);
    }

    [TestMethod]
    public void Resolve_OpenRouter_IgnoresOllamaModel()
    {
        var (_, model) = ChatModelResolver.Resolve(
            provider: "openrouter",
            ollamaModel: OllamaModel,
            openRouterModel: OpenRouterModel);

        model.Should().Be(OpenRouterModel, "OpenRouter must use the OpenRouter model, not the Ollama one");
    }

    // =====================================================================
    // Missing configured model → provider default
    // =====================================================================

    [TestMethod]
    public void Resolve_OllamaMissingModel_FallsBackToDefaultOllamaModel()
    {
        var (provider, model) = ChatModelResolver.Resolve(
            provider: "ollama",
            ollamaModel: null,
            openRouterModel: OpenRouterModel);

        provider.Should().Be("ollama");
        model.Should().Be(ChatModelResolver.DefaultOllamaModel);
    }

    [TestMethod]
    public void Resolve_OpenRouterMissingModel_FallsBackToDefaultOpenRouterModel()
    {
        var (provider, model) = ChatModelResolver.Resolve(
            provider: "openrouter",
            ollamaModel: OllamaModel,
            openRouterModel: null);

        provider.Should().Be("openrouter");
        model.Should().Be(ChatModelResolver.DefaultOpenRouterModel);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Resolve_OllamaBlankModel_FallsBackToDefault(string? blank)
    {
        var (_, model) = ChatModelResolver.Resolve(
            provider: "ollama",
            ollamaModel: blank,
            openRouterModel: OpenRouterModel);

        model.Should().Be(ChatModelResolver.DefaultOllamaModel);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Resolve_OpenRouterBlankModel_FallsBackToDefault(string? blank)
    {
        var (_, model) = ChatModelResolver.Resolve(
            provider: "openrouter",
            ollamaModel: OllamaModel,
            openRouterModel: blank);

        model.Should().Be(ChatModelResolver.DefaultOpenRouterModel);
    }

    // =====================================================================
    // Whitespace in configured model is trimmed
    // =====================================================================

    [TestMethod]
    public void Resolve_OllamaModel_IsTrimmed()
    {
        var (_, model) = ChatModelResolver.Resolve(
            provider: "ollama",
            ollamaModel: $"  {OllamaModel}  ",
            openRouterModel: OpenRouterModel);

        model.Should().Be(OllamaModel);
    }

    [TestMethod]
    public void Resolve_OpenRouterModel_IsTrimmed()
    {
        var (_, model) = ChatModelResolver.Resolve(
            provider: "openrouter",
            ollamaModel: OllamaModel,
            openRouterModel: $"\t{OpenRouterModel}\t");

        model.Should().Be(OpenRouterModel);
    }

    // =====================================================================
    // Missing / unknown provider → defaults to Ollama
    // =====================================================================

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Resolve_MissingProvider_DefaultsToOllamaAndOllamaModel(string? provider)
    {
        var (resolvedProvider, model) = ChatModelResolver.Resolve(
            provider: provider,
            ollamaModel: OllamaModel,
            openRouterModel: OpenRouterModel);

        resolvedProvider.Should().Be("ollama");
        model.Should().Be(OllamaModel);
    }

    [TestMethod]
    [DataRow("azure")]
    [DataRow("foo")]
    [DataRow("claude")]
    public void Resolve_UnknownProvider_DefaultsToOllamaAndOllamaModel(string provider)
    {
        var (resolvedProvider, model) = ChatModelResolver.Resolve(
            provider: provider,
            ollamaModel: OllamaModel,
            openRouterModel: OpenRouterModel);

        resolvedProvider.Should().Be("ollama");
        model.Should().Be(OllamaModel);
    }

    // =====================================================================
    // Provider name is case-insensitive
    // =====================================================================

    [TestMethod]
    [DataRow("Ollama")]
    [DataRow("OLLAMA")]
    [DataRow("Ollama")]
    public void Resolve_OllamaCaseInsensitive_ReturnsOllamaModel(string provider)
    {
        var (resolvedProvider, model) = ChatModelResolver.Resolve(
            provider: provider,
            ollamaModel: OllamaModel,
            openRouterModel: OpenRouterModel);

        resolvedProvider.Should().Be("ollama");
        model.Should().Be(OllamaModel);
    }

    [TestMethod]
    [DataRow("OpenRouter")]
    [DataRow("OPENROUTER")]
    [DataRow("OpenRouter")]
    public void Resolve_OpenRouterCaseInsensitive_ReturnsOpenRouterModel(string provider)
    {
        var (resolvedProvider, model) = ChatModelResolver.Resolve(
            provider: provider,
            ollamaModel: OllamaModel,
            openRouterModel: OpenRouterModel);

        resolvedProvider.Should().Be("openrouter");
        model.Should().Be(OpenRouterModel);
    }

    [TestMethod]
    public void Resolve_ProviderWithSurroundingWhitespace_IsTrimmed()
    {
        var (resolvedProvider, _) = ChatModelResolver.Resolve(
            provider: "  openrouter  ",
            ollamaModel: OllamaModel,
            openRouterModel: OpenRouterModel);

        resolvedProvider.Should().Be("openrouter");
    }

    // =====================================================================
    // NormalizeProvider
    // =====================================================================

    [TestMethod]
    [DataRow("ollama", "ollama")]
    [DataRow("openrouter", "openrouter")]
    [DataRow("OpenRouter", "openrouter")]
    [DataRow("OLLAMA", "ollama")]
    [DataRow(null, "ollama")]
    [DataRow("", "ollama")]
    [DataRow("unknown", "ollama")]
    public void NormalizeProvider_ReturnsExpected(string? input, string expected)
    {
        ChatModelResolver.NormalizeProvider(input).Should().Be(expected);
    }
}