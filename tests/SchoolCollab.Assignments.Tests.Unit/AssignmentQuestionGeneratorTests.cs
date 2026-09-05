using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.Assignments.Application.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Tests for <see cref="AssignmentQuestionGenerator"/>: the HTTP seam the
/// Assignments wizard uses to call the AI host's question-generation
/// endpoint. Uses <see cref="MockHttpMessageHandler"/> (the repo's
/// scripted-handler pattern from <c>AssignmentIndexBunitTests</c>).
/// </summary>
[TestClass]
public class AssignmentQuestionGeneratorTests
{
    private const string EndpointUrl = "http://localhost/api/ai/assignments/questions";

    [TestMethod]
    public async Task GenerateAsync_200WithValidResponse_ReturnsQuestions()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, EndpointUrl)
            .Respond(HttpStatusCode.OK, "application/json", """
                {"questions":[
                    {"text":"Q1?","type":"multipleChoice","options":[{"text":"A","isCorrect":true},{"text":"B","isCorrect":false}],"modelAnswer":null},
                    {"text":"Q2?","type":"trueFalse","options":[{"text":"True","isCorrect":true},{"text":"False","isCorrect":false}],"modelAnswer":null},
                    {"text":"Q3?","type":"shortAnswer","options":null,"modelAnswer":"Glucose"}
                ]}
                """);

        var http = handler.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        var sut = NewSut(http);

        var request = NewValidRequest();

        // Act
        var questions = await sut.GenerateAsync(request, CancellationToken.None);

        // Assert
        questions.Should().HaveCount(3);
        questions[0].Type.Should().Be(GeneratedQuestionType.MultipleChoice);
        questions[0].Options!.Single(o => o.IsCorrect).Text.Should().Be("A");
        questions[1].Type.Should().Be(GeneratedQuestionType.TrueFalse);
        questions[2].Type.Should().Be(GeneratedQuestionType.ShortAnswer);
        questions[2].ModelAnswer.Should().Be("Glucose");
    }

    [TestMethod]
    public async Task GenerateAsync_502WithErrorBody_ThrowsWithBodyMessage()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, EndpointUrl)
            .Respond(HttpStatusCode.BadGateway, "application/json", """{"error":"The AI provider rate-limited the request."}""");

        var http = handler.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        var sut = NewSut(http);

        // Act
        var act = () => sut.GenerateAsync(NewValidRequest(), CancellationToken.None);

        // Assert
        var assertion = await act.Should().ThrowAsync<QuestionGenerationFailed>();
        assertion.Which.Message.Should().Be("The AI provider rate-limited the request.");
    }

    [TestMethod]
    public async Task GenerateAsync_400WithErrorBody_ThrowsWithBodyMessage()
    {
        // Arrange — any non-success status is a failure (decision (c))
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, EndpointUrl)
            .Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"A topic name is required to generate questions."}""");

        var http = handler.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        var sut = NewSut(http);

        // Act
        var act = () => sut.GenerateAsync(NewValidRequest(), CancellationToken.None);

        // Assert
        var assertion = await act.Should().ThrowAsync<QuestionGenerationFailed>();
        assertion.Which.Message.Should().Be("A topic name is required to generate questions.");
    }

    [TestMethod]
    public async Task GenerateAsync_500WithoutErrorBody_ThrowsWithStatusFallback()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, EndpointUrl)
            .Respond(HttpStatusCode.InternalServerError);

        var http = handler.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        var sut = NewSut(http);

        // Act
        var act = () => sut.GenerateAsync(NewValidRequest(), CancellationToken.None);

        // Assert — the body had no `{"error":...}` so the fallback message wins.
        var assertion = await act.Should().ThrowAsync<QuestionGenerationFailed>();
        assertion.Which.Message.Should().Be("Question generation failed (HTTP 500).");
    }

    [TestMethod]
    public async Task GenerateAsync_HttpRequestException_ThrowsWithFriendlyMessage()
    {
        // Arrange — simulate a transport-level failure (DNS, connection refused, etc.)
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, EndpointUrl)
            .Throw(new HttpRequestException("connection refused"));

        var http = handler.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        var sut = NewSut(http);

        // Act
        var act = () => sut.GenerateAsync(NewValidRequest(), CancellationToken.None);

        // Assert
        var assertion = await act.Should().ThrowAsync<QuestionGenerationFailed>();
        assertion.Which.Message.Should().Contain("unreachable");
    }

    [TestMethod]
    public async Task GenerateAsync_200WithMalformedBody_ThrowsUnreadableMessage()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, EndpointUrl)
            .Respond(HttpStatusCode.OK, "application/json", "this is not JSON at all");

        var http = handler.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        var sut = NewSut(http);

        // Act
        var act = () => sut.GenerateAsync(NewValidRequest(), CancellationToken.None);

        // Assert
        var assertion = await act.Should().ThrowAsync<QuestionGenerationFailed>();
        assertion.Which.Message.Should().Contain("unreadable");
    }

    [TestMethod]
    public async Task GenerateAsync_200WithEmptyBody_ThrowsUnreadableMessage()
    {
        // Arrange — a 200 with no body deserialises to null.
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, EndpointUrl)
            .Respond(HttpStatusCode.OK, "application/json", "");

        var http = handler.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        var sut = NewSut(http);

        // Act
        var act = () => sut.GenerateAsync(NewValidRequest(), CancellationToken.None);

        // Assert
        var assertion = await act.Should().ThrowAsync<QuestionGenerationFailed>();
        assertion.Which.Message.Should().Contain("unreadable");
    }

    [TestMethod]
    public async Task GenerateAsync_CancelledToken_RethrowsOperationCanceledException()
    {
        // Arrange — never respond; cancel before the call returns.
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, EndpointUrl)
            .Respond(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var http = handler.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        var sut = NewSut(http);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert — cancellation must NOT be wrapped as QuestionGenerationFailed (EC-2).
        var act = () => sut.GenerateAsync(NewValidRequest(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static AssignmentQuestionGenerator NewSut(HttpClient http)
    {
        return new AssignmentQuestionGenerator(
            http,
            Mock.Of<ILogger<AssignmentQuestionGenerator>>());
    }

    private static QuestionGenerationRequest NewValidRequest() =>
        new(TopicId: Guid.NewGuid(), TopicName: "Photosynthesis", QuestionCount: 3);
}
