using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.CodedValues.Tests.Unit;

/// <summary>
/// Unit tests for provider-aware model name resolution logic
/// used in the AI chat model dropdown.
/// </summary>
[TestClass]
public class ModelNameResolutionTests
{
    // =====================================================================
    // ResolveModelNameKey — maps provider name to attribute key
    // =====================================================================

    [TestMethod]
    [DataRow("ollama", "ollamaModelName")]
    [DataRow("Ollama", "ollamaModelName")]
    [DataRow("OLLAMA", "ollamaModelName")]
    [DataRow("openrouter", "openrouterModelName")]
    [DataRow("OpenRouter", "openrouterModelName")]
    [DataRow("OPENROUTER", "openrouterModelName")]
    public void ResolveModelNameKey_MapsProviderToAttributeKey(string provider, string expectedKey)
    {
        var key = ResolveModelNameKey(provider);
        key.Should().Be(expectedKey);
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("")]
    [DataRow("azure")]
    public void ResolveModelNameKey_UnknownProvider_DefaultsToOllama(string provider)
    {
        var key = ResolveModelNameKey(provider);
        key.Should().Be("ollamaModelName");
    }

    // =====================================================================
    // ResolveModelName — picks value from correct attribute, falls back to Name
    // =====================================================================

    [TestMethod]
    public void ResolveModelName_OllamaProvider_UsesOllamaAttribute()
    {
        var codedValue = new TestCodedValue(
            "llama3",
            [new("ollamaModelName", "llama3.1:8b"), new("openrouterModelName", "meta-llama/llama-3.1-8b-instruct")]);

        var result = ResolveModelNameForProvider(codedValue, "ollama");
        result.Should().Be("llama3.1:8b");
    }

    [TestMethod]
    public void ResolveModelName_OpenRouterProvider_UsesOpenRouterAttribute()
    {
        var codedValue = new TestCodedValue(
            "gpt4o",
            [new("ollamaModelName", ""), new("openrouterModelName", "openai/gpt-4o")]);

        var result = ResolveModelNameForProvider(codedValue, "openrouter");
        result.Should().Be("openai/gpt-4o");
    }

    [TestMethod]
    public void ResolveModelName_MissingProviderAttribute_FallsBackToName()
    {
        var codedValue = new TestCodedValue(
            "MyModel",
            [new("ollamaModelName", "my-model:7b")]);

        var result = ResolveModelNameForProvider(codedValue, "openrouter");
        result.Should().Be("MyModel");
    }

    [TestMethod]
    public void ResolveModelName_EmptyAttributeValue_FallsBackToName()
    {
        var codedValue = new TestCodedValue(
            "MyModel",
            [new("ollamaModelName", ""), new("openrouterModelName", "")]);

        var result = ResolveModelNameForProvider(codedValue, "ollama");
        result.Should().Be("MyModel");
    }

    [TestMethod]
    public void ResolveModelName_NoAttributes_FallsBackToName()
    {
        var codedValue = new TestCodedValue("Phi35", []);

        var result = ResolveModelNameForProvider(codedValue, "ollama");
        result.Should().Be("Phi35");
    }

    [TestMethod]
    public void ResolveModelName_WhitespaceAttributeValue_FallsBackToName()
    {
        var codedValue = new TestCodedValue(
            "MyModel",
            [new("ollamaModelName", "   ")]);

        var result = ResolveModelNameForProvider(codedValue, "ollama");
        result.Should().Be("MyModel");
    }

    // =====================================================================
    // Full resolution pipeline — provider → attribute key → value → fallback
    // =====================================================================

    [TestMethod]
    public void FullResolution_OllamaProvider_PicksOllamaAttribute()
    {
        var codedValue = new TestCodedValue(
            "Llama31",
            [new("ollamaModelName", "llama3.1:8b"), new("openrouterModelName", "meta-llama/llama-3.1-8b")]);

        var key = ResolveModelNameKey("ollama");
        var result = ResolveModelName(codedValue, key);
        result.Should().Be("llama3.1:8b");
    }

    [TestMethod]
    public void FullResolution_OpenRouterProvider_PicksOpenRouterAttribute()
    {
        var codedValue = new TestCodedValue(
            "GPT4o",
            [new("ollamaModelName", ""), new("openrouterModelName", "openai/gpt-4o")]);

        var key = ResolveModelNameKey("openrouter");
        var result = ResolveModelName(codedValue, key);
        result.Should().Be("openai/gpt-4o");
    }

    [TestMethod]
    public void FullResolution_UnknownProvider_DefaultsToOllama()
    {
        var codedValue = new TestCodedValue(
            "Mistral",
            [new("ollamaModelName", "mistral:7b"), new("openrouterModelName", "mistralai/mistral-7b-instruct")]);

        var key = ResolveModelNameKey("azure"); // unknown → defaults to ollama
        var result = ResolveModelName(codedValue, key);
        result.Should().Be("mistral:7b");
    }

    // =====================================================================
    // Test helpers — mirror the logic from CodedValuesChat.razor
    // =====================================================================

    /// <summary>
    /// Maps a provider name to the model-name attribute key.
    /// Mirrors the logic in CodedValuesChat.OnInitializedAsync.
    /// </summary>
    private static string ResolveModelNameKey(string provider) =>
        provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase)
            ? "openrouterModelName"
            : "ollamaModelName";

    /// <summary>
    /// Resolves the display model name from a coded value's attributes,
    /// falling back to the coded value's Name when the attribute is missing or empty.
    /// </summary>
    private static string ResolveModelName(TestCodedValue cv, string attributeKey)
    {
        var attrValue = cv.Attributes.FirstOrDefault(a => a.Key == attributeKey)?.Value;
        return !string.IsNullOrWhiteSpace(attrValue) ? attrValue : cv.Name;
    }

    private static string ResolveModelNameForProvider(TestCodedValue cv, string provider) =>
        ResolveModelName(cv, ResolveModelNameKey(provider));

    /// <summary>
    /// Lightweight test double for a coded value DTO with Name and Attributes.
    /// </summary>
    private record TestCodedValue(string Name, TestAttribute[] Attributes);

    private record TestAttribute(string Key, string Value);
}
