using System.ClientModel;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.AI.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Tests for <see cref="AssignmentQuestionGenerationService"/>: happy path,
/// provider-error mapping (EC-8), malformed-model mapping (EC-1), and
/// request validation (400).
/// </summary>
[TestClass]
public class AssignmentQuestionGenerationServiceTests
{
    [TestMethod]
    public async Task GenerateAsync_HappyPath_ReturnsValidatedResponse_AndResolvesModelFromConfig()
    {
        // Arrange
        var request = new QuestionGenerationRequest(
            TopicId: Guid.NewGuid(),
            TopicName: "Photosynthesis",
            QuestionCount: 2);

        var expectedModel = "test-model";
        var config = BuildConfiguration(("codedvalue-ai-provider", "ollama"),
            ("Ollama:DefaultModel", expectedModel));

        ChatOptions? capturedOptions = null;
        List<ChatMessage>? capturedMessages = null;
        var chatClient = new DelegateChatClient(
            (messages, options, _) =>
            {
                capturedMessages = messages.ToList();
                capturedOptions = options;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, """
                    {"questions":[
                        {"text":"Q1?","type":"shortAnswer","options":null,"modelAnswer":"A1"},
                        {"text":"Q2?","type":"shortAnswer","options":null,"modelAnswer":"A2"}
                    ]}
                    """));
            });

        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(chatClient);

        var promptProvider = BuildPromptProvider("Production");
        var service = new AssignmentQuestionGenerationService(
            promptProvider,
            factory.Object,
            config,
            new TestLogger<AssignmentQuestionGenerationService>());

        // Act
        var response = await service.GenerateAsync(request, CancellationToken.None);

        // Assert
        response.Questions.Should().HaveCount(2);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.ModelId.Should().Be(expectedModel,
            "the service must resolve the model from configuration (decision (a))");
        capturedMessages.Should().NotBeNull();
        capturedMessages!.Should().HaveCount(2);
        capturedMessages[0].Role.Should().Be(ChatRole.System);
        capturedMessages[1].Role.Should().Be(ChatRole.User);
        capturedMessages[1].Text.Should().Contain("Photosynthesis",
            "the request payload must be serialised into the user message");
    }

    [TestMethod]
    public async Task GenerateAsync_WithPromptOverride_AddsThirdUserMessage()
    {
        var request = new QuestionGenerationRequest(
            TopicId: Guid.NewGuid(),
            TopicName: "Photosynthesis",
            PromptOverride: "Focus on cellular biology.");

        var config = BuildConfiguration(("codedvalue-ai-provider", "ollama"),
            ("Ollama:DefaultModel", "test-model"));

        List<ChatMessage>? capturedMessages = null;
        var chatClient = new DelegateChatClient(
            (messages, _, _) =>
            {
                capturedMessages = messages.ToList();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    """{"questions":[{"text":"Q?","type":"shortAnswer","options":null,"modelAnswer":"A"}]}"""));
            });

        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(chatClient);

        var service = new AssignmentQuestionGenerationService(
            BuildPromptProvider("Production"),
            factory.Object,
            config,
            new TestLogger<AssignmentQuestionGenerationService>());

        await service.GenerateAsync(request, CancellationToken.None);

        capturedMessages.Should().HaveCount(3, "PromptOverride must add a third user message (EC-9)");
        capturedMessages![2].Role.Should().Be(ChatRole.User);
        capturedMessages[2].Text.Should().Contain("Focus on cellular biology.");
    }

    [TestMethod]
    public async Task GenerateAsync_Provider401_SurfacesAsFriendlyMessage502()
    {
        var chatClient = new DelegateChatClient(
            (_, _, _) => throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));

        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(chatClient);

        var service = BuildService(factory.Object);

        var act = () => service.GenerateAsync(NewValidRequest(), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<AssignmentQuestionGenerationException>();
        assertion.Which.StatusCode.Should().Be(502);
        assertion.Which.Message.Should().Contain("unauthorised");
        assertion.Which.Message.Should().Contain("OpenRouter:ApiKey");
    }

    [TestMethod]
    public async Task GenerateAsync_Provider403_SurfacesAsFriendlyMessage502()
    {
        var chatClient = new DelegateChatClient(
            (_, _, _) => throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden));

        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(chatClient);

        var service = BuildService(factory.Object);

        var act = () => service.GenerateAsync(NewValidRequest(), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<AssignmentQuestionGenerationException>();
        assertion.Which.StatusCode.Should().Be(502);
        assertion.Which.Message.Should().Contain("unauthorised");
    }

    [TestMethod]
    public async Task GenerateAsync_Provider429_SurfacesAsRateLimitMessage502()
    {
        var chatClient = new DelegateChatClient(
            (_, _, _) => throw new HttpRequestException("Rate-limited", null, HttpStatusCode.TooManyRequests));

        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(chatClient);

        var service = BuildService(factory.Object);

        var act = () => service.GenerateAsync(NewValidRequest(), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<AssignmentQuestionGenerationException>();
        assertion.Which.StatusCode.Should().Be(502);
        assertion.Which.Message.Should().Contain("rate-limited");
    }

    [TestMethod]
    public async Task GenerateAsync_Provider500_SurfacesAsServerErrorMessage502()
    {
        var chatClient = new DelegateChatClient(
            (_, _, _) => throw new HttpRequestException("Server error", null, HttpStatusCode.InternalServerError));

        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(chatClient);

        var service = BuildService(factory.Object);

        var act = () => service.GenerateAsync(NewValidRequest(), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<AssignmentQuestionGenerationException>();
        assertion.Which.StatusCode.Should().Be(502);
        assertion.Which.Message.Should().Contain("server error");
    }

    [TestMethod]
    public async Task GenerateAsync_ClientResultException_SurfacesAs502()
    {
        // ClientResultException surfaces from OpenAI SDK on provider errors; it
        // carries an HTTP status code. The public ctors need a PipelineResponse,
        // so we construct via the uninitialised-object helper and stamp the
        // private _status backing field with reflection. The Formatter (ex.Status)
        // reads the same field, so the service's mapping sees 401.
        var exception = (ClientResultException)System.Runtime.CompilerServices
            .RuntimeHelpers.GetUninitializedObject(typeof(ClientResultException));
        var statusField = typeof(ClientResultException).GetField("_status",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        statusField!.SetValue(exception, (int)HttpStatusCode.Unauthorized);
        var chatClient = new DelegateChatClient((_, _, _) => throw exception);

        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(chatClient);

        var service = BuildService(factory.Object);

        var act = () => service.GenerateAsync(NewValidRequest(), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<AssignmentQuestionGenerationException>();
        assertion.Which.StatusCode.Should().Be(502);
        assertion.Which.Message.Should().Contain("unauthorised");
    }

    [TestMethod]
    public async Task GenerateAsync_GarbageModelText_Throws502()
    {
        var chatClient = new DelegateChatClient(
            (_, _, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, "totally not json")));

        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(chatClient);

        var service = BuildService(factory.Object);

        var act = () => service.GenerateAsync(NewValidRequest(), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<AssignmentQuestionGenerationException>();
        assertion.Which.StatusCode.Should().Be(502);
        assertion.Which.Message.Should().Contain("invalid question set");
    }

    [TestMethod]
    public async Task GenerateAsync_BlankTopicName_Throws400()
    {
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(new DelegateChatClient((_, _, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))));

        var service = BuildService(factory.Object);

        var request = new QuestionGenerationRequest(
            TopicId: Guid.NewGuid(),
            TopicName: "");

        var act = () => service.GenerateAsync(request, CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<AssignmentQuestionGenerationException>();
        assertion.Which.StatusCode.Should().Be(400);
        assertion.Which.Message.Should().Contain("topic name");
    }

    [TestMethod]
    public async Task GenerateAsync_QuestionCountOutOfRange_Throws400()
    {
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(new DelegateChatClient((_, _, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))));

        var service = BuildService(factory.Object);

        var request = new QuestionGenerationRequest(
            TopicId: Guid.NewGuid(),
            TopicName: "Photosynthesis",
            QuestionCount: 100);

        var act = () => service.GenerateAsync(request, CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<AssignmentQuestionGenerationException>();
        assertion.Which.StatusCode.Should().Be(400);
        assertion.Which.Message.Should().Contain("Question count");
    }

    [TestMethod]
    public async Task GenerateAsync_OversizedPromptOverride_Throws400()
    {
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(new DelegateChatClient((_, _, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))));

        var service = BuildService(factory.Object);

        var request = new QuestionGenerationRequest(
            TopicId: Guid.NewGuid(),
            TopicName: "Photosynthesis",
            PromptOverride: new string('a', AssignmentQuestionGenerationService.MaxPromptOverrideLength + 1));

        var act = () => service.GenerateAsync(request, CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<AssignmentQuestionGenerationException>();
        assertion.Which.StatusCode.Should().Be(400);
        assertion.Which.Message.Should().Contain("Prompt override");
    }

    [TestMethod]
    public async Task GenerateAsync_CancelledToken_RethrowsOperationCanceledException()
    {
        // Arrange
        var chatClient = new DelegateChatClient((_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, ""));
        });

        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetClient()).Returns(chatClient);

        var service = BuildService(factory.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert: cancellation must NOT be mapped to 502 (EC-2).
        var act = () => service.GenerateAsync(NewValidRequest(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static AssignmentQuestionGenerationService BuildService(IChatClientFactory factory)
    {
        var config = BuildConfiguration(("codedvalue-ai-provider", "ollama"),
            ("Ollama:DefaultModel", "test-model"));
        return new AssignmentQuestionGenerationService(
            BuildPromptProvider("Production"),
            factory,
            config,
            new TestLogger<AssignmentQuestionGenerationService>());
    }

    private static AssignmentQuestionGenerationSystemPromptProvider BuildPromptProvider(string environment)
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(environment);
        return new AssignmentQuestionGenerationSystemPromptProvider(
            env.Object,
            new TestLogger<AssignmentQuestionGenerationSystemPromptProvider>());
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => (string?)p.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static QuestionGenerationRequest NewValidRequest() =>
        new(TopicId: Guid.NewGuid(), TopicName: "Photosynthesis");

    /// <summary>
    /// Local <see cref="IChatClient"/> that drives <c>GetResponseAsync</c> via
    /// a caller-supplied delegate. Keeps the legacy streaming mock untouched.
    /// </summary>
    private sealed class DelegateChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, ChatResponse> _handler;

        public DelegateChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, ChatResponse> handler)
        {
            _handler = handler;
        }

        public ChatClientMetadata Metadata => new("delegate", new Uri("http://localhost"), "delegate-model");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_handler(chatMessages, options, cancellationToken));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;

        public void Dispose() { }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
