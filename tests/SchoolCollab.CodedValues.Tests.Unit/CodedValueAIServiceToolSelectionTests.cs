using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.AI.Services;

namespace SchoolCollab.CodedValues.Tests.Unit;

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
        var service = BuildService();
        var tools = service.SelectToolsForPrompt([new ChatMessage(ChatRole.User, "add music genres under code 'GENRES'")]);

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
        var service = BuildService();
        var tools = service.SelectToolsForPrompt([new ChatMessage(ChatRole.User, "update description for CNTRY")]);

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
        var service = BuildService();
        var tools = service.SelectToolsForPrompt([new ChatMessage(ChatRole.User, "disable HSPTL")]);

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
        var service = BuildService();
        var tools = service.SelectToolsForPrompt([new ChatMessage(ChatRole.User, "enable CNTRY")]);

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
        var service = BuildService();
        var tools = service.SelectToolsForPrompt([new ChatMessage(ChatRole.User, "list all categories")]);

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
        var service = BuildService();
        var tools = service.SelectToolsForPrompt([new ChatMessage(ChatRole.User, "hello there")]);

        var names = tools.Select(t => t.Name).ToList();
        names.Should().BeEquivalentTo(AllToolNames,
            "the safety fallback must ship ALL tools for prompts the classifier doesn't recognise");
    }

    [TestMethod]
    public void SelectToolsForPrompt_EmptyHistory_FallsBackToAllTools()
    {
        var service = BuildService();
        var tools = service.SelectToolsForPrompt([]);

        tools.Select(t => t.Name).Should().BeEquivalentTo(AllToolNames);
    }

    [TestMethod]
    public void SelectToolsForPrompt_ClassifiesByMostRecentUserMessage_IgnoringPriorAssistantTurn()
    {
        // The classifier looks at the most recent USER message, not the latest
        // message in history overall. An assistant turn + a follow-up user
        // prompt that says "disable it" must classify as disable, not whatever
        // the prior turn was about (e.g. add).
        var service = BuildService();
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "add music genres under code 'GENRES'"),
            new(ChatRole.Assistant, "Would you like me to create 10 music genres?"),
            new(ChatRole.User, "actually disable that for now"),
        };
        var tools = service.SelectToolsForPrompt(history);

        var names = tools.Select(t => t.Name).ToList();
        names.Should().BeEquivalentTo(
            "list_coded_value_categories",
            "get_coded_value_by_code",
            "disable_coded_value",
            "enable_coded_value");
    }

    // --- Helpers ---

    private static CodedValueAIService BuildService()
    {
        var mockFactory = new Mock<IChatClientFactory>();
        // The tool-selection tests don't drive ChatAsync, but
        // IChatClientFactory is required by the constructor. Returning null
        // is fine — SelectToolsForPrompt only inspects _toolsByName which is
        // built eagerly in the constructor.
        mockFactory.Setup(f => f.GetClient()).Returns(() => null!);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["codedvalue-ai-provider"] = "ollama",
                ["Ollama:DefaultModel"] = "test-model",
                ["OpenRouter:DefaultModel"] = "test-model"
            })
            .Build();

        return new CodedValueAIService(
            mockFactory.Object,
            new Mock<ICodedValuesApiClient>().Object,
            NullLogger<CodedValueAIService>.Instance,
            mockEnv.Object,
            config);
    }
}
