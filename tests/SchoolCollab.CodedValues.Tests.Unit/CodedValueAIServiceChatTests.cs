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
        mockApi.Setup(a => a.GetByCodeAsync("HSPTL", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentHsptl);
        mockApi.Setup(a => a.BulkCreateAsync(parentHsptl.Id, It.IsAny<IEnumerable<CreateCodedValueRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkCreateResult(3, Array.Empty<string>(), parentHsptl.Id));

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
        mockFactory.Setup(f => f.GetClient()).Returns(chatClient);

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
        mockApi.Verify(a => a.GetByCodeAsync("HSPTL", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "AI service should look up HSPTL parent code");
        mockApi.Verify(a => a.BulkCreateAsync(parentHsptl.Id, It.IsAny<IEnumerable<CreateCodedValueRequest>>(), It.IsAny<CancellationToken>()), Times.Once,
            "bulk create should be called once with all children");
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
        mockApi.Setup(a => a.GetByCodeAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
        mockFactory.Setup(f => f.GetClient()).Returns(chatClient);

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
        mockApi.Setup(a => a.GetByCodeAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CodedValueDto?)null);

        // AI response with empty JSON objects mixed in
        var textUpdate = new ChatResponseUpdate(ChatRole.Assistant, "Here are the results.\n\n{}\n\nThe hospitals are listed above.");
        var chatClient = new MockChatClient(
        [
            new List<ChatResponseUpdate> { textUpdate }
        ]);

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetClient()).Returns(chatClient);

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

    /// <summary>
    /// Verifies that descriptions in bulk-create children are passed through
    /// to the Coded Values API via CreateCodedValueRequest.
    /// Regression test for "descriptions from AI prompts are not getting saved".
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_BulkCreateWithDescription_PassesDescriptionToApi()
    {
        // Arrange
        var parentDiseases = new CodedValueDto(
            Id: Guid.NewGuid(),
            Code: "DISEASES",
            Name: "Diseases",
            Description: "Disease categories",
            ParentId: null,
            ParentCode: null,
            IsDisabled: false,
            DisplayOrder: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            Attributes: [],
            AttributeDefinitions: [],
            ChildrenCount: 0);

        var capturedRequests = new List<CreateCodedValueRequest>();

        var mockApi = new Mock<ICodedValuesApiClient>();
        mockApi.Setup(a => a.GetByCodeAsync("DISEASES", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentDiseases);
        mockApi.Setup(a => a.BulkCreateAsync(parentDiseases.Id, It.IsAny<IEnumerable<CreateCodedValueRequest>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IEnumerable<CreateCodedValueRequest>, CancellationToken>((_, children, _) => capturedRequests.AddRange(children))
            .ReturnsAsync(new BulkCreateResult(3, Array.Empty<string>(), parentDiseases.Id));

        // Simulate: AI calls get_coded_value_by_code, then create_bulk_values with descriptions
        var round1Args = new Dictionary<string, object?> { ["code"] = "DISEASES" };
        var round2Args = new Dictionary<string, object?>
        {
            ["parentCode"] = "DISEASES",
            ["children"] = JsonSerializer.Deserialize<JsonElement>(
                """[{"code":"MALARIA","name":"Malaria","description":"Mosquito-borne infectious disease","displayOrder":1},{"code":"TUBERCULOSIS","name":"Tuberculosis","description":"Bacterial infection affecting lungs","displayOrder":2},{"code":"DIABETES","name":"Diabetes","description":"Metabolic disorder with high blood sugar","displayOrder":3}]""")
        };

        var finalTextUpdate = new ChatResponseUpdate(ChatRole.Assistant,
            "I've added 3 diseases under the DISEASES category.");
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
        mockFactory.Setup(f => f.GetClient()).Returns(chatClient);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        var service = new CodedValueAIService(
            mockFactory.Object,
            mockApi.Object,
            new TestLogger<CodedValueAIService>(),
            mockEnv.Object);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Add diseases to code 'DISEASES' with description")
        };

        // Act
        var updates = new List<ChatUpdate>();
        await foreach (var update in service.ChatAsync(history, null, CancellationToken.None))
        {
            updates.Add(update);
        }

        // Assert: descriptions must flow through to API calls
        capturedRequests.Should().HaveCount(3, "3 disease coded values should be created");
        capturedRequests[0].Code.Should().Be("MALARIA");
        capturedRequests[0].Description.Should().Be("Mosquito-borne infectious disease",
            "description from AI function call must be passed to the API");
        capturedRequests[1].Code.Should().Be("TUBERCULOSIS");
        capturedRequests[1].Description.Should().Be("Bacterial infection affecting lungs",
            "description from AI function call must be passed to the API");
        capturedRequests[2].Code.Should().Be("DIABETES");
        capturedRequests[2].Description.Should().Be("Metabolic disorder with high blood sugar",
            "description from AI function call must be passed to the API");

        // No errors
        updates.Should().NotContain(u => u is ChatUpdate.Error, "no errors should occur");
    }

    /// <summary>
    /// Verifies that bulk-create without descriptions still works (null descriptions).
    /// This ensures the pipeline doesn't crash when the model omits descriptions.
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_BulkCreateWithoutDescription_NullDescriptionsAccepted()
    {
        // Arrange
        var parentDiseases = new CodedValueDto(
            Id: Guid.NewGuid(),
            Code: "DISEASES",
            Name: "Diseases",
            Description: "Disease categories",
            ParentId: null,
            ParentCode: null,
            IsDisabled: false,
            DisplayOrder: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            Attributes: [],
            AttributeDefinitions: [],
            ChildrenCount: 0);

        var capturedRequests = new List<CreateCodedValueRequest>();

        var mockApi = new Mock<ICodedValuesApiClient>();
        mockApi.Setup(a => a.GetByCodeAsync("DISEASES", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentDiseases);
        mockApi.Setup(a => a.BulkCreateAsync(parentDiseases.Id, It.IsAny<IEnumerable<CreateCodedValueRequest>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IEnumerable<CreateCodedValueRequest>, CancellationToken>((_, children, _) => capturedRequests.AddRange(children))
            .ReturnsAsync(new BulkCreateResult(2, Array.Empty<string>(), parentDiseases.Id));

        // Simulate: model creates children WITHOUT descriptions
        var round1Args = new Dictionary<string, object?> { ["code"] = "DISEASES" };
        var round2Args = new Dictionary<string, object?>
        {
            ["parentCode"] = "DISEASES",
            ["children"] = JsonSerializer.Deserialize<JsonElement>(
                """[{"code":"MALARIA","name":"Malaria","displayOrder":1},{"code":"TUBERCULOSIS","name":"Tuberculosis","displayOrder":2}]""")
        };

        var finalTextUpdate = new ChatResponseUpdate(ChatRole.Assistant,
            "I've added 2 diseases under the DISEASES category.");
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
        mockFactory.Setup(f => f.GetClient()).Returns(chatClient);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        var service = new CodedValueAIService(
            mockFactory.Object,
            mockApi.Object,
            new TestLogger<CodedValueAIService>(),
            mockEnv.Object);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Add diseases to code 'DISEASES'")
        };

        // Act
        var updates = new List<ChatUpdate>();
        await foreach (var update in service.ChatAsync(history, null, CancellationToken.None))
        {
            updates.Add(update);
        }

        // Assert: pipeline should handle null descriptions gracefully
        capturedRequests.Should().HaveCount(2);
        capturedRequests[0].Code.Should().Be("MALARIA");
        capturedRequests[0].Description.Should().BeNull("model omitted description — null is valid");
        capturedRequests[1].Code.Should().Be("TUBERCULOSIS");
        capturedRequests[1].Description.Should().BeNull("model omitted description — null is valid");

        updates.Should().NotContain(u => u is ChatUpdate.Error, "no errors should occur");
    }

    /// <summary>
    /// Verifies that bulk-create under PKTYPES with descriptions passes all
    /// descriptions through to the API, and that the parent's existing children
    /// data is loaded correctly. Simulates the prompt:
    /// "add packaging types for commerce under code PKTYPES"
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_BulkCreateUnderPktypes_WithDescriptions_PassesAllFieldsToApi()
    {
        // Arrange — PKTYPES parent already exists with some children
        var parentPktypes = new CodedValueDto(
            Id: Guid.NewGuid(),
            Code: "PKTYPES",
            Name: "Packaging Types",
            Description: "Packaging types for commerce",
            ParentId: null,
            ParentCode: null,
            IsDisabled: false,
            DisplayOrder: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            Attributes: [],
            AttributeDefinitions: [],
            ChildrenCount: 0);

        var capturedRequests = new List<CreateCodedValueRequest>();

        var mockApi = new Mock<ICodedValuesApiClient>();
        mockApi.Setup(a => a.GetByCodeAsync("PKTYPES", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentPktypes);
        mockApi.Setup(a => a.BulkCreateAsync(parentPktypes.Id, It.IsAny<IEnumerable<CreateCodedValueRequest>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IEnumerable<CreateCodedValueRequest>, CancellationToken>((_, children, _) => capturedRequests.AddRange(children))
            .ReturnsAsync(new BulkCreateResult(4, Array.Empty<string>(), parentPktypes.Id));

        // Round 1: AI calls get_coded_value_by_code to look up PKTYPES
        var round1Args = new Dictionary<string, object?> { ["code"] = "PKTYPES" };

        // Round 2: AI calls create_bulk_values with packaging type children, each with a description
        var round2Args = new Dictionary<string, object?>
        {
            ["parentCode"] = "PKTYPES",
            ["children"] = JsonSerializer.Deserialize<JsonElement>(
                """[{"code":"BOX","name":"Box","description":"Rigid container made of cardboard or corrugated material","displayOrder":1},{"code":"BAG","name":"Bag","description":"Flexible container made of paper or plastic","displayOrder":2},{"code":"CRATE","name":"Crate","description":"Wooden container for heavy or bulky items","displayOrder":3},{"code":"DRUM","name":"Drum","description":"Cylindrical container for liquids and powders","displayOrder":4}]""")
        };

        // Round 3: AI confirms creation
        var finalTextUpdate = new ChatResponseUpdate(ChatRole.Assistant,
            "I've added 4 packaging types under PKTYPES.\n\n| Code | Name | Description |\n|------|------|-------------|\n| BOX | Box | Rigid container |\n| BAG | Bag | Flexible container |\n| CRATE | Crate | Wooden container |\n| DRUM | Drum | Cylindrical container |");

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
        mockFactory.Setup(f => f.GetClient()).Returns(chatClient);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        var service = new CodedValueAIService(
            mockFactory.Object,
            mockApi.Object,
            new TestLogger<CodedValueAIService>(),
            mockEnv.Object);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Add packaging types for commerce under code PKTYPES")
        };

        // Act
        var updates = new List<ChatUpdate>();
        await foreach (var update in service.ChatAsync(history, null, CancellationToken.None))
        {
            updates.Add(update);
        }

        // Assert: all 4 children were created with descriptions
        capturedRequests.Should().HaveCount(4, "4 packaging types should be created under PKTYPES");
        capturedRequests[0].Code.Should().Be("BOX");
        capturedRequests[0].Description.Should().Be("Rigid container made of cardboard or corrugated material",
            "description must flow through from AI function call to API request");
        capturedRequests[1].Code.Should().Be("BAG");
        capturedRequests[1].Description.Should().Be("Flexible container made of paper or plastic");
        capturedRequests[2].Code.Should().Be("CRATE");
        capturedRequests[2].Description.Should().Be("Wooden container for heavy or bulky items");
        capturedRequests[3].Code.Should().Be("DRUM");
        capturedRequests[3].Description.Should().Be("Cylindrical container for liquids and powders");

        // ParentId must be set to the PKTYPES parent's Id
        capturedRequests.Should().OnlyContain(r => r.ParentId == parentPktypes.Id,
            "all children must reference the PKTYPES parent");

        // No errors
        updates.Should().NotContain(u => u is ChatUpdate.Error, "no errors should occur");
    }

    /// <summary>
    /// Verifies that the update workflow correctly updates descriptions for
    /// both a parent and its children. Simulates the prompt:
    /// "update description of PKTYPES and its children"
    /// The AI should call get_coded_value_by_code for PKTYPES (which returns
    /// the parent with children), then update_coded_value for the parent and
    /// each child with new descriptions.
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_UpdateDescriptionsForParentAndChildren_UpdatesAll()
    {
        // Arrange — PKTYPES parent with 3 children that have descriptions
        var parentId = Guid.NewGuid();
        var childBoxId = Guid.NewGuid();
        var childBagId = Guid.NewGuid();
        var childCrateId = Guid.NewGuid();

        var parentPktypes = new CodedValueDto(
            Id: parentId,
            Code: "PKTYPES",
            Name: "Packaging Types",
            Description: "Old description",
            ParentId: null,
            ParentCode: null,
            IsDisabled: false,
            DisplayOrder: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            Attributes: [],
            AttributeDefinitions: [],
            ChildrenCount: 3);

        var children = new[]
        {
            new CodedValueDto(childBoxId, "BOX", "Box", "Old box desc", parentId, "PKTYPES", false, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [], 0),
            new CodedValueDto(childBagId, "BAG", "Bag", "Old bag desc", parentId, "PKTYPES", false, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [], 0),
            new CodedValueDto(childCrateId, "CRATE", "Crate", "Old crate desc", parentId, "PKTYPES", false, 3, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [], 0),
        };

        var mockApi = new Mock<ICodedValuesApiClient>();
        mockApi.Setup(a => a.GetByCodeAsync("PKTYPES", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentPktypes);
        mockApi.Setup(a => a.GetChildrenAsync(parentPktypes.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(children);

        // Track update calls
        var updateCalls = new List<(Guid Id, UpdateCodedValueRequest Req)>();
        mockApi.Setup(a => a.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateCodedValueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdateCodedValueRequest, CancellationToken>((id, req, _) => updateCalls.Add((id, req)))
            .Returns(Task.CompletedTask);

        // Also need GetByCodeAsync to return individual children when AI updates them
        mockApi.Setup(a => a.GetByCodeAsync("BOX", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(children[0]);
        mockApi.Setup(a => a.GetByCodeAsync("BAG", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(children[1]);
        mockApi.Setup(a => a.GetByCodeAsync("CRATE", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(children[2]);

        // Round 1: AI calls get_coded_value_by_code for PKTYPES (returns parent + loads children)
        var round1Args = new Dictionary<string, object?> { ["code"] = "PKTYPES" };

        // Round 2: AI calls update_coded_value for PKTYPES parent (update description)
        var round2Args = new Dictionary<string, object?>
        {
            ["code"] = "PKTYPES",
            ["description"] = "Updated: packaging types for commerce"
        };

        // Round 3: AI calls update_coded_value for BOX child (update description)
        var round3Args = new Dictionary<string, object?>
        {
            ["code"] = "BOX",
            ["description"] = "Updated: rigid container made of cardboard"
        };

        // Round 4: AI calls update_coded_value for BAG child (update description)
        var round4Args = new Dictionary<string, object?>
        {
            ["code"] = "BAG",
            ["description"] = "Updated: flexible container made of paper or plastic"
        };

        // Round 5: AI calls update_coded_value for CRATE child (update description)
        var round5Args = new Dictionary<string, object?>
        {
            ["code"] = "CRATE",
            ["description"] = "Updated: wooden container for heavy or bulky items"
        };

        // Round 6: Final text confirmation
        var finalTextUpdate = new ChatResponseUpdate(ChatRole.Assistant,
            "Updated descriptions for PKTYPES and all 3 children.");

        var chatClient = new MockChatClient(
        [
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_1", "get_coded_value_by_code", round1Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_2", "update_coded_value", round2Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_3", "update_coded_value", round3Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_4", "update_coded_value", round4Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_5", "update_coded_value", round5Args)]) },
            new List<ChatResponseUpdate> { finalTextUpdate }
        ]);

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetClient()).Returns(chatClient);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        var service = new CodedValueAIService(
            mockFactory.Object,
            mockApi.Object,
            new TestLogger<CodedValueAIService>(),
            mockEnv.Object);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Update description of PKTYPES and its children")
        };

        // Act
        var updates = new List<ChatUpdate>();
        await foreach (var update in service.ChatAsync(history, null, CancellationToken.None))
        {
            updates.Add(update);
        }

        // Assert: 5 update calls — 1 parent + 3 children = 4 (but the test has 5 rounds for updates)
        // Actually we have: 1 get + 4 update + 1 final = 6 rounds total, 4 update API calls
        updateCalls.Should().HaveCount(4, "should update parent PKTYPES and 3 children");

        // Verify parent was updated
        updateCalls.Should().Contain(c => c.Id == parentId,
            "parent PKTYPES should be updated");
        var parentUpdate = updateCalls.First(c => c.Id == parentId);
        parentUpdate.Req.Description.Should().Be("Updated: packaging types for commerce",
            "parent description should be updated via API");

        // Verify children were updated
        updateCalls.Should().Contain(c => c.Id == childBoxId,
            "BOX child should be updated");
        updateCalls.Should().Contain(c => c.Id == childBagId,
            "BAG child should be updated");
        updateCalls.Should().Contain(c => c.Id == childCrateId,
            "CRATE child should be updated");

        // No errors
        updates.Should().NotContain(u => u is ChatUpdate.Error, "no errors should occur");

        // Verify children were loaded (GetChildrenAsync called)
        mockApi.Verify(a => a.GetChildrenAsync(parentPktypes.Id, It.IsAny<CancellationToken>()), Times.Once,
            "children should be loaded when parent has ChildrenCount > 0");
    }

    /// <summary>
    /// Verifies that JsonSerializer.Serialize correctly roundtrips function call arguments
    /// containing nested JSON arrays with description fields. This tests the critical
    /// serialization step at line 179-180 of CodedValueAIService.cs.
    /// </summary>
    [TestMethod]
    public void JsonSerializer_SerializeFunctionCallArgs_RoundtripsChildrenWithDescription()
    {
        // Simulate what M.E.AI creates: IDictionary<string, object?> with JsonElement values
        var args = new Dictionary<string, object?>
        {
            ["parentCode"] = "DISEASES",
            ["children"] = JsonSerializer.Deserialize<JsonElement>(
                """[{"code":"MALARIA","name":"Malaria","description":"Mosquito-borne infectious disease","displayOrder":1}]""")
        };

        // Act: serialize then re-parse (same flow as CodedValueAIService)
        var json = JsonSerializer.Serialize(args);

        // Assert: the children array must survive roundtrip with description intact
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("children", out var children).Should().BeTrue();
        children.ValueKind.Should().Be(JsonValueKind.Array);
        children.GetArrayLength().Should().Be(1);

        var child = children[0];
        child.TryGetProperty("code", out var code).Should().BeTrue();
        code.GetString().Should().Be("MALARIA");
        child.TryGetProperty("description", out var desc).Should().BeTrue();
        desc.GetString().Should().Be("Mosquito-borne infectious disease",
            "description must survive JsonSerializer.Serialize roundtrip");
    }

    /// <summary>
    /// Verifies that set_attribute can find child coded values by code
    /// (not just root values). Simulates prompt:
    /// "add schools in ghana to code SCHOOLS. Use city and region of school as attributes"
    /// The AI should: get parent SCHOOLS, create_bulk_values for children,
    /// set_attribute_definition on parent for city/region,
    /// and set_attribute on each child for city/region.
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_SetAttributeOnChildren_FindsChildByCode()
    {
        // Arrange — SCHOOLS parent with 2 children
        var parentId = Guid.NewGuid();
        var presecId = Guid.NewGuid();
        var achimotaId = Guid.NewGuid();

        var parentSchools = new CodedValueDto(
            Id: parentId,
            Code: "SCHOOLS",
            Name: "Schools in Ghana",
            Description: "Ghanaian schools",
            ParentId: null,
            ParentCode: null,
            IsDisabled: false,
            DisplayOrder: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            Attributes: [],
            AttributeDefinitions: [],
            ChildrenCount: 0);

        // Children returned by BulkCreateAsync (with descriptions)
        var presecDto = new CodedValueDto(
            presecId, "PRESEC", "Presbyterian Boys' SHS", "A boys' senior high school in Accra",
            parentId, "SCHOOLS", false, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [], [], 0);
        var achimotaDto = new CodedValueDto(
            achimotaId, "ACHIMOTA", "Achimota School", "A co-educational school in Accra",
            parentId, "SCHOOLS", false, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [], [], 0);

        var mockApi = new Mock<ICodedValuesApiClient>();

        // Step 1: get_coded_value_by_code for SCHOOLS → parent
        mockApi.Setup(a => a.GetByCodeAsync("SCHOOLS", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentSchools);

        // Step 2: create_bulk_values → returns 2 created
        mockApi.Setup(a => a.BulkCreateAsync(parentId, It.IsAny<IEnumerable<CreateCodedValueRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkCreateResult(2, Array.Empty<string>(), parentId));

        // Step 3: get_coded_value_by_code for PRESEC (child) → MUST find it (the fix!)
        mockApi.Setup(a => a.GetByCodeAsync("PRESEC", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(presecDto);

        // Step 4: get_coded_value_by_code for ACHIMOTA (child) → MUST find it (the fix!)
        mockApi.Setup(a => a.GetByCodeAsync("ACHIMOTA", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(achimotaDto);

        // Step 5: get_by_id for parent (to check attribute definitions exist)
        mockApi.Setup(a => a.GetByIdAsync(parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentSchools with
            {
                AttributeDefinitions =
                [
                    new("city", "City", 0, null, false, false, null, null, null),
                    new("region", "Region", 0, null, false, false, null, null, null)
                ]
            });

        // Track set_attribute_definition calls
        var attrDefCalls = new List<(Guid Id, string Key, AttributeDefinitionRequest Req)>();
        mockApi.Setup(a => a.SetAttributeDefinitionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<AttributeDefinitionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, AttributeDefinitionRequest, CancellationToken>((id, key, req, _) => attrDefCalls.Add((id, key, req)))
            .Returns(Task.CompletedTask);

        // Track set_attribute calls
        var attrCalls = new List<(Guid Id, string Key, string Value)>();
        mockApi.Setup(a => a.SetAttributeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, CancellationToken>((id, key, value, _) => attrCalls.Add((id, key, value)))
            .Returns(Task.CompletedTask);

        // Round 1: AI calls get_coded_value_by_code for SCHOOLS
        var round1Args = new Dictionary<string, object?> { ["code"] = "SCHOOLS" };

        // Round 2: AI calls create_bulk_values with children
        var round2Args = new Dictionary<string, object?>
        {
            ["parentCode"] = "SCHOOLS",
            ["children"] = JsonSerializer.Deserialize<JsonElement>(
                """[{"code":"PRESEC","name":"Presbyterian Boys' SHS","description":"A boys' senior high school in Accra"},{"code":"ACHIMOTA","name":"Achimota School","description":"A co-educational school in Accra"}]""")
        };

        // Round 3: AI calls set_attribute_definition for "city" on SCHOOLS
        var round3Args = new Dictionary<string, object?>
        {
            ["parentCode"] = "SCHOOLS",
            ["key"] = "city",
            ["displayName"] = "City",
            ["dataType"] = 0
        };

        // Round 4: AI calls set_attribute_definition for "region" on SCHOOLS
        var round4Args = new Dictionary<string, object?>
        {
            ["parentCode"] = "SCHOOLS",
            ["key"] = "region",
            ["displayName"] = "Region",
            ["dataType"] = 0
        };

        // Round 5: AI calls set_attribute on PRESEC for city
        var round5Args = new Dictionary<string, object?>
        {
            ["code"] = "PRESEC",
            ["key"] = "city",
            ["value"] = "Accra"
        };

        // Round 6: AI calls set_attribute on PRESEC for region
        var round6Args = new Dictionary<string, object?>
        {
            ["code"] = "PRESEC",
            ["key"] = "region",
            ["value"] = "Greater Accra"
        };

        // Round 7: AI calls set_attribute on ACHIMOTA for city
        var round7Args = new Dictionary<string, object?>
        {
            ["code"] = "ACHIMOTA",
            ["key"] = "city",
            ["value"] = "Accra"
        };

        // Round 8: AI calls set_attribute on ACHIMOTA for region
        var round8Args = new Dictionary<string, object?>
        {
            ["code"] = "ACHIMOTA",
            ["key"] = "region",
            ["value"] = "Greater Accra"
        };

        // Round 9: Final text confirmation
        var finalTextUpdate = new ChatResponseUpdate(ChatRole.Assistant,
            "Added 2 schools under SCHOOLS with city and region attributes.");

        var chatClient = new MockChatClient(
        [
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_1", "get_coded_value_by_code", round1Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_2", "create_bulk_values", round2Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_3", "set_attribute_definition", round3Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_4", "set_attribute_definition", round4Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_5", "set_attribute", round5Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_6", "set_attribute", round6Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_7", "set_attribute", round7Args)]) },
            new List<ChatResponseUpdate> { new(ChatRole.Assistant, [new FunctionCallContent("call_8", "set_attribute", round8Args)]) },
            new List<ChatResponseUpdate> { finalTextUpdate }
        ]);

        var mockFactory = new Mock<IChatClientFactory>();
        mockFactory.Setup(f => f.GetClient()).Returns(chatClient);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        var service = new CodedValueAIService(
            mockFactory.Object,
            mockApi.Object,
            new TestLogger<CodedValueAIService>(),
            mockEnv.Object);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "add schools in ghana to code SCHOOLS. Use city and region of school as attributes")
        };

        // Act
        var updates = new List<ChatUpdate>();
        await foreach (var update in service.ChatAsync(history, null, CancellationToken.None))
        {
            updates.Add(update);
        }

        // Assert: set_attribute_definition called for city and region on parent
        attrDefCalls.Should().HaveCount(2, "should define city and region attributes on parent");
        attrDefCalls.Should().Contain(c => c.Id == parentId && c.Key == "city",
            "city attribute definition should be on SCHOOLS parent");
        attrDefCalls.Should().Contain(c => c.Id == parentId && c.Key == "region",
            "region attribute definition should be on SCHOOLS parent");

        // Assert: set_attribute called for each child's city and region
        attrCalls.Should().HaveCount(4, "should set city and region on 2 children = 4 attribute calls");
        attrCalls.Should().Contain(c => c.Id == presecId && c.Key == "city" && c.Value == "Accra",
            "PRESEC city should be set");
        attrCalls.Should().Contain(c => c.Id == presecId && c.Key == "region" && c.Value == "Greater Accra",
            "PRESEC region should be set");
        attrCalls.Should().Contain(c => c.Id == achimotaId && c.Key == "city" && c.Value == "Accra",
            "ACHIMOTA city should be set");
        attrCalls.Should().Contain(c => c.Id == achimotaId && c.Key == "region" && c.Value == "Greater Accra",
            "ACHIMOTA region should be set");

        // Verify child codes were found (the critical fix!)
        mockApi.Verify(a => a.GetByCodeAsync("PRESEC", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "set_attribute should find child code PRESEC via global search");
        mockApi.Verify(a => a.GetByCodeAsync("ACHIMOTA", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "set_attribute should find child code ACHIMOTA via global search");

        // No errors
        updates.Should().NotContain(u => u is ChatUpdate.Error, "no errors should occur");
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