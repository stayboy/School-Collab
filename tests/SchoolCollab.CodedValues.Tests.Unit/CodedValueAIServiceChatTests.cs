using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.CodedValues.AI;
using SchoolCollab.CodedValues.AI.Services;

namespace SchoolCollab.CodedValues.Tests.Unit;

/// <summary>
/// Integration-style tests for CodedValueAIService.ChatAsync that simulate
/// the full multi-round tool-call flow for common user prompts.
/// Uses a mock IChatClient to drive predictable AI behaviour.
/// </summary>
[TestClass]
public class CodedValueAIServiceChatTests
{
    /// <summary>
    /// Simulates the prompt "Add hospitals to code values under HSPTL code".
    /// The AI should call get_coded_value_by_code, then create_bulk_values,
    /// and finally produce a human-readable text response listing the hospitals.
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_AddHospitalsUnderHsptl_ReturnsHospitalList()
    {
        // Arrange
        var parentHsptl = new CodedValueDto(
            Id: Guid.NewGuid(),
            Code: "HSPTL",
            Name: "Hospital Type",
            Description: "Hospital categories",
            ParentId: null,
            ParentCode: null,
            IsDisabled: false,
            DisplayOrder: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            Attributes: [],
            AttributeDefinitions: [],
            ChildrenCount: 0);

        var mockApi = new Mock<ICodedValuesApiClient>();
        mockApi.Setup(a => a.GetByCodeAsync("HSPTL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentHsptl);
        mockApi.Setup(a => a.CreateAsync(It.IsAny<CreateCodedValueRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Simulate a 3-round AI conversation:
        // Round 1: AI calls get_coded_value_by_code with code=HSPTL
        // Round 2: AI calls create_bulk_values with parentCode=HSPTL and hospital children
        // Round 3: AI produces final text (no tool calls)
        // Round 1: AI calls get_coded_value_by_code with code=HSPTL
        var round1Args = new Dictionary<string, object?> { ["code"] = "HSPTL" };
        // Round 2: AI calls create_bulk_values with parentCode=HSPTL and hospital children
        var round2Args = new Dictionary<string, object?>
        {
            ["parentCode"] = "HSPTL",
            ["children"] = JsonSerializer.Deserialize<JsonElement>(
                """[{"code":"GH","name":"General Hospital","description":"General medical facility"},{"code":"TH","name":"Teaching Hospital","description":"Academic medical center"},{"code":"CH","name":"Children's Hospital","description":"Pediatric care facility"}]""")
        };

        // Round 3: AI produces final text (no tool calls)
        var finalTextUpdate = new ChatResponseUpdate(ChatRole.Assistant, "I've added 3 hospital types under the HSPTL category:\n\n| Code | Name | Description |\n|------|------|-------------|\n| GH | General Hospital | General medical facility |\n| TH | Teaching Hospital | Academic medical center |\n| CH | Children's Hospital | Pediatric care facility |\n\nAll 3 coded values have been created successfully.");
        var chatClient = new MockChatClient(
        [
            new List<ChatResponseUpdate>
            {
                new(ChatRole.Assistant, [new FunctionCallContent("call_1", "get_coded_value_by_code", round1Args)])
            },
            new List<ChatResponseUpdate>
            {
                new(ChatRole.Assistant, [new FunctionCallContent("call_2", "create_bulk_values", round2Args)])
            },
            new List<ChatResponseUpdate> { finalTextUpdate }
        ]);

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetClient(It.IsAny<string?>())).Returns(chatClient);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        var service = new CodedValueAIService(
            mockFactory.Object,
            mockApi.Object,
            new TestLogger<CodedValueAIService>(),
            mockEnv.Object);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Add hospitals to code values under HSPTL code")
        };

        // Act
        var updates = new List<ChatUpdate>();
        await foreach (var update in service.ChatAsync(history, null, CancellationToken.None))
        {
            updates.Add(update);
        }

        // Assert: verify the full ChatUpdate stream
        // 1. Tool call starts and ends for get_coded_value_by_code
        updates.OfType<ChatUpdate.ToolCallStart>().Should().Contain(
            tcs => tcs.FriendlyName == "Get By Code" && tcs.ArgsSummary == "code: HSPTL",
            "AI should look up the HSPTL parent first");
        updates.OfType<ChatUpdate.ToolCallEnd>().Should().Contain(
            tce => tce.FriendlyName == "Get By Code" && tce.Success,
            "Get By Code should succeed");

        // 2. Tool call starts and ends for create_bulk_values
        updates.OfType<ChatUpdate.ToolCallStart>().Should().Contain(
            tcs => tcs.FriendlyName == "Create Bulk Values" && tcs.ArgsSummary == "parent: HSPTL",
            "AI should create bulk values under HSPTL");
        updates.OfType<ChatUpdate.ToolCallEnd>().Should().Contain(
            tce => tce.FriendlyName == "Create Bulk Values" && tce.Success,
            "Bulk create should succeed");

        // 3. Final text should contain the hospital list
        var textChunks = updates.OfType<ChatUpdate.TextChunk>().ToList();
        textChunks.Should().HaveCount(1, "only the final round should produce visible text");
        textChunks[0].Text.Should().Contain("GH", "response should list General Hospital code");
        textChunks[0].Text.Should().Contain("General Hospital", "response should list hospital names");
        textChunks[0].Text.Should().Contain("Teaching Hospital", "response should list hospital names");
        textChunks[0].Text.Should().Contain("Children's Hospital", "response should list hospital names");

        // 4. No errors
        updates.Should().NotContain(u => u is ChatUpdate.Error, "no errors should occur");

        // 5. Verify API was called correctly
        mockApi.Verify(a => a.GetByCodeAsync("HSPTL", It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "AI service should look up HSPTL parent code");
        mockApi.Verify(a => a.CreateAsync(It.IsAny<CreateCodedValueRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3),
            "3 hospital coded values should be created");
    }

    /// <summary>
    /// Verifies that when the AI produces a final text response containing leaked tool-call
    /// syntax, the CodedValueAIService cleans it before yielding to the UI.
    /// This is the primary regression test for "tool calling leaking into responses".
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_FinalResponseWithToolCallLeakage_CleansBeforeYielding()
    {
        var mockApi = new Mock<ICodedValuesApiClient>();
        mockApi.Setup(a => a.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CodedValueDto?)null);
        mockApi.Setup(a => a.CreateAsync(It.IsAny<CreateCodedValueRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Simulate AI that produces final text with leaked tool-call syntax
        var round1Update = new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call_1", "list_coded_value_categories", new Dictionary<string, object?>())]);
        var round2Update = new ChatResponseUpdate(ChatRole.Assistant, "I'll list the categories.\n\nlist_coded_value_categories()\n\nHere are the categories found.");
        var chatClient = new MockChatClient(
        [
            new List<ChatResponseUpdate> { round1Update },
            new List<ChatResponseUpdate> { round2Update }
        ]);

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetClient(It.IsAny<string?>())).Returns(chatClient);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        var service = new CodedValueAIService(
            mockFactory.Object,
            mockApi.Object,
            new TestLogger<CodedValueAIService>(),
            mockEnv.Object);

        var history = new List<ChatMessage> { new(ChatRole.User, "show me categories") };

        var updates = new List<ChatUpdate>();
        await foreach (var update in service.ChatAsync(history, null, CancellationToken.None))
        {
            updates.Add(update);
        }

        var textChunks = updates.OfType<ChatUpdate.TextChunk>().ToList();
        textChunks.Should().HaveCount(1);
        textChunks[0].Text.Should().NotContain("list_coded_value_categories",
            "leaked tool-call names must be stripped from final display text");
        textChunks[0].Text.Should().Contain("categories found",
            "legitimate text content must be preserved");
    }

    /// <summary>
    /// Verifies that empty JSON objects/braces are stripped from the AI's final response.
    /// Regression test for "Response is coming out as empty json tags".
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_FinalResponseWithEmptyJson_CleansBeforeYielding()
    {
        var mockApi = new Mock<ICodedValuesApiClient>();
        mockApi.Setup(a => a.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CodedValueDto?)null);

        // AI response with empty JSON objects mixed in
        var textUpdate = new ChatResponseUpdate(ChatRole.Assistant, "Here are the results.\n\n{}\n\nThe hospitals are listed above.");
        var chatClient = new MockChatClient(
        [
            new List<ChatResponseUpdate> { textUpdate }
        ]);

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetClient(It.IsAny<string?>())).Returns(chatClient);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        var service = new CodedValueAIService(
            mockFactory.Object,
            mockApi.Object,
            new TestLogger<CodedValueAIService>(),
            mockEnv.Object);

        var history = new List<ChatMessage> { new(ChatRole.User, "show hospitals") };

        var updates = new List<ChatUpdate>();
        await foreach (var update in service.ChatAsync(history, null, CancellationToken.None))
        {
            updates.Add(update);
        }

        var textChunks = updates.OfType<ChatUpdate.TextChunk>().ToList();
        textChunks.Should().HaveCount(1);
        textChunks[0].Text.Should().NotContain("{}",
            "empty JSON objects must be stripped from final display text");
        textChunks[0].Text.Should().Contain("hospitals are listed",
            "legitimate text must be preserved after empty JSON is removed");
    }

    // --- Helpers ---

    /// <summary>
    /// Mock IChatClient that replays a sequence of rounds.
    /// Each round returns a pre-defined list of ChatResponseUpdate items.
    /// After each round, the mock captures the updated message list
    /// (including tool results added by CodedValueAIService).
    /// </summary>
    private class MockChatClient : IChatClient
    {
        private readonly List<List<ChatResponseUpdate>> _rounds;
        private int _currentRound;

        public MockChatClient(List<List<ChatResponseUpdate>> rounds)
        {
            _rounds = rounds;
            _currentRound = 0;
        }

        public ChatClientMetadata Metadata => new("mock", new Uri("http://localhost"), "mock-model");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Use GetStreamingResponseAsync for this mock.");
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_currentRound >= _rounds.Count)
                throw new InvalidOperationException($"MockChatClient: no more rounds configured (requested round {_currentRound + 1} of {_rounds.Count})");

            var round = _rounds[_currentRound++];
            foreach (var update in round)
            {
                yield return update;
            }

            await Task.Yield();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;

        public void Dispose() { }
    }

    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}