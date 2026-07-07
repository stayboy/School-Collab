using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.AI.Tools.CodedValues;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Regression-guard for <see cref="CodedValueAIService.SelectToolsForPrompt"/> —
/// the per-prompt-shape tool filter that ships only the relevant subset of
/// the 9 tools to OpenRouter, instead of always sending all 9.
///
/// Each test pins one prompt shape and asserts the exact tool-name set the
/// service selects for it. If a future refactor re-shapes the intent
/// classifier, these tests force the developer to revisit the prompt-shape
/// classification explicitly.
/// </summary>
[TestClass]
public class CodedValueAIServiceToolSelectionTests
{
    private static readonly string[] AllToolNames =
    [
        "list_coded_value_categories",
        "get_coded_value_by_code",
        "create_coded_value",
        "create_bulk_values",
        "update_coded_value",
        "disable_coded_value",
        "enable_coded_value",
        "set_attribute_definition",
        "set_attribute",
    ];

    [TestMethod]
    public void SelectToolsForPrompt_AddGenresUnderCode_ShipsReadCreateAndBulkTools()
    {
        // The original "smoking-gun" prompt that surfaced the gemma-3 → gemma-4
        // root-cause investigation: this is the user-observed case that shipped
        // only 5 tools after the trim, instead of 9.
        var provider = BuildProvider();
        var tools = SelectTools(provider, [new ChatMessage(ChatRole.User, "add music genres under code 'GENRES'")]);

        var names = tools.Select(t => t.Name).ToList();
        names.Should().BeEquivalentTo(
            "list_coded_value_categories",
            "get_coded_value_by_code",
            "create_coded_value",
            "create_bulk_values",
            "set_attribute_definition",
            "set_attribute");
        // Sanity: explicit denials — no update / disable / enable for an "add" prompt.
        names.Should().NotContain("update_coded_value");
        names.Should().NotContain("disable_coded_value");
        names.Should().NotContain("enable_coded_value");
    }

    [TestMethod]
    public void SelectToolsForPrompt_UpdateDescription_ShipsReadAndUpdateOnly()
    {
        var provider = BuildProvider();
        var tools = SelectTools(provider, [new ChatMessage(ChatRole.User, "update description for CNTRY")]);

        var names = tools.Select(t => t.Name).ToList();
        names.Should().BeEquivalentTo(
            "list_coded_value_categories",
            "get_coded_value_by_code",
            "update_coded_value");
        names.Should().NotContain("create_bulk_values");
        names.Should().NotContain("disable_coded_value");
    }

    [TestMethod]
    public void SelectToolsForPrompt_DisableCode_ShipsReadAndDisableEnableOnly()
    {
        var provider = BuildProvider();
        var tools = SelectTools(provider, [new ChatMessage(ChatRole.User, "disable HSPTL")]);

        var names = tools.Select(t => t.Name).ToList();
        names.Should().BeEquivalentTo(
            "list_coded_value_categories",
            "get_coded_value_by_code",
            "disable_coded_value",
            "enable_coded_value");
        // Disable/enable intents must NOT include create/update tools to prevent
        // the model from re-creating something the user wants disabled.
        names.Should().NotContain("create_coded_value");
        names.Should().NotContain("create_bulk_values");
        names.Should().NotContain("update_coded_value");
    }

    [TestMethod]
    public void SelectToolsForPrompt_EnableCode_ShipsReadAndDisableEnableOnly()
    {
        var provider = BuildProvider();
        var tools = SelectTools(provider, [new ChatMessage(ChatRole.User, "enable CNTRY")]);

        var names = tools.Select(t => t.Name).ToList();
        names.Should().BeEquivalentTo(
            "list_coded_value_categories",
            "get_coded_value_by_code",
            "disable_coded_value",
            "enable_coded_value");
    }

    [TestMethod]
    public void SelectToolsForPrompt_ReadOnlyListPrompt_ShipsReadOnlyTools()
    {
        var provider = BuildProvider();
        var tools = SelectTools(provider, [new ChatMessage(ChatRole.User, "list all categories")]);

        var names = tools.Select(t => t.Name).ToList();
        names.Should().BeEquivalentTo(
            "list_coded_value_categories",
            "get_coded_value_by_code");
        // Read-only intent must never include write tools.
        names.Should().NotContain("create_bulk_values");
        names.Should().NotContain("update_coded_value");
        names.Should().NotContain("disable_coded_value");
    }

    [TestMethod]
    public void SelectToolsForPrompt_UnknownPrompt_FallsBackToAllTools()
    {
        // The safety fallback: if the classifier doesn't recognise any intent
        // keywords, ship everything. This is the contract that protects against
        // over-aggressive filtering on prompts the heuristic doesn't cover
        // (e.g. "hello", "what's the weather", or anything weird that still
        // needs the full tool surface).
        var provider = BuildProvider();
        var tools = SelectTools(provider, [new ChatMessage(ChatRole.User, "hello there")]);

        var names = tools.Select(t => t.Name).ToList();
        names.Should().BeEquivalentTo(AllToolNames,
            "the safety fallback must ship ALL tools for prompts the classifier doesn't recognise");
    }

    [TestMethod]
    public void SelectToolsForPrompt_EmptyHistory_FallsBackToAllTools()
    {
        var provider = BuildProvider();
        var tools = SelectTools(provider, []);

        tools.Select(t => t.Name).Should().BeEquivalentTo(AllToolNames);
    }

    [TestMethod]
    public void SelectToolsForPrompt_ClassifiesByMostRecentUserMessage_IgnoringPriorAssistantTurn()
    {
        // The classifier looks at the most recent USER message, not the latest
        // message in history overall. An assistant turn + a follow-up user
        // prompt that says "disable it" must classify as disable, not whatever
        // the prior turn was about (e.g. add).
        var provider = BuildProvider();
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "add music genres under code 'GENRES'"),
            new(ChatRole.Assistant, "Would you like me to create 10 music genres?"),
            new(ChatRole.User, "actually disable that for now"),
        };
        var tools = SelectTools(provider, history);

        var names = tools.Select(t => t.Name).ToList();
        names.Should().BeEquivalentTo(
            "list_coded_value_categories",
            "get_coded_value_by_code",
            "disable_coded_value",
            "enable_coded_value");
    }

    // --- Helpers ---

    /// <summary>
    /// Builds a CodedValuesToolProvider against a no-op Coded Values API mock.
    /// The tool-selection tests don't drive ChatAsync; they only assert the
    /// per-prompt tool subset that CreateTools (the engine's per-turn tool
    /// source) returns — which runs the same SelectToolsForPrompt intent
    /// classifier the former CodedValueAIService used.
    /// </summary>
    private static CodedValuesToolProvider BuildProvider()
    {
        var mockApi = new Mock<ICodedValuesApiClient>();
        return new CodedValuesToolProvider(mockApi.Object, NullLogger<CodedValuesToolProvider>.Instance);
    }

    /// <summary>
    /// Convenience wrapper: invokes the provider’s per-turn CreateTools (which
    /// runs SelectToolsForPrompt) with a no-op logger, returning the narrowed
    /// AITool list. Assertions stay identical to the pre-refactor suite — only
    /// the call target changed (CodedValueAIService.SelectToolsForPrompt →
    /// CodedValuesToolProvider.CreateTools).
    /// </summary>
    private static IReadOnlyList<AITool> SelectTools(CodedValuesToolProvider provider, IReadOnlyList<ChatMessage> history)
        => provider.CreateTools(history, NullLogger<CodedValuesToolProvider>.Instance);
}
