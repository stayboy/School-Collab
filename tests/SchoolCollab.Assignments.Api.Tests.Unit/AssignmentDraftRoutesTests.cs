using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Api.Endpoints;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.QuestionsDraft;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-B2 draft-route wiring (round-10 binding list) — exercised against a minimal
/// in-process host (TestServer) that maps ONLY
/// <see cref="AssignmentEndpoints.MapAssignmentEndpoints"/> with mocked draft CQRS
/// handlers. Program is never started, so no Postgres / RabbitMQ / Settings-api is
/// needed. Covers the four questions-draft routes' status mapping: GET 204-when-none,
/// PUT stages (204), confirm 200 + 409 (missing/corrupt blob), DELETE 204.
/// Reuses the ar-9 <c>SignOffRoutesTests</c> minimal-host harness rather than
/// inventing a new one.
/// </summary>
[TestClass]
public class AssignmentDraftRoutesTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string DraftUrl(Guid id) => $"/assignments/{id}/questions-draft";

    private static async Task<WebApplication> StartHostAsync(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    [TestMethod]
    public async Task GetDraft_204WhenNone()
    {
        var queryHandler = new Mock<IQueryHandler<GetQuestionsDraftQuery, IReadOnlyList<NewQuestionDto>?>>();
        queryHandler.Setup(h => h.HandleAsync(It.IsAny<GetQuestionsDraftQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<NewQuestionDto>?)null);

        await using var app = await StartHostAsync(s => s.AddSingleton(queryHandler.Object));
        var client = CreateClient(app);

        var response = await client.GetAsync(DraftUrl(AssignmentId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "GET with no staged draft returns 204 (the ApiClient maps it to null)");
    }

    [TestMethod]
    public async Task PutDraft_Stages()
    {
        StageQuestionsDraftCommand? captured = null;
        var handler = new Mock<ICommandHandler<StageQuestionsDraftCommand>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<StageQuestionsDraftCommand>(), It.IsAny<CancellationToken>()))
            .Callback<StageQuestionsDraftCommand, CancellationToken>((c, _) => captured = c)
            .Returns(Task.CompletedTask);

        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<StageQuestionsDraftCommand>>(handler.Object));
        var client = CreateClient(app);

        var body = new StageQuestionsDraftBody(new[]
        {
            new NewQuestionDto("Q1", QuestionTypeDto.MultipleChoice, 0, new[]
            {
                new NewQuestionOptionDto("A", true),
                new NewQuestionOptionDto("B", false),
            }),
        });
        var response = await client.PutAsJsonAsync(DraftUrl(AssignmentId), body);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().NotBeNull();
        captured!.AssignmentId.Should().Be(AssignmentId);
        captured.Questions.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task ConfirmDraft_200And409Paths()
    {
        var confirmHandler = new Mock<ICommandHandler<ConfirmQuestionsDraftCommand>>();
        confirmHandler.Setup(h => h.HandleAsync(It.IsAny<ConfirmQuestionsDraftCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var readHandler = new Mock<IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>>();
        readHandler.Setup(h => h.HandleAsync(It.IsAny<GetAssignmentByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AssignmentSummaryDto?)SampleSummary());

        await using (var app = await StartHostAsync(s =>
        {
            s.AddSingleton<ICommandHandler<ConfirmQuestionsDraftCommand>>(confirmHandler.Object);
            s.AddSingleton<IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>>(readHandler.Object);
        }))
        {
            var client = CreateClient(app);
            var ok = await client.PostAsync($"{DraftUrl(AssignmentId)}/confirm", null);
            ok.StatusCode.Should().Be(HttpStatusCode.OK,
                "a confirmable draft returns 200 + the refreshed summary");
            var summary = await ok.Content.ReadFromJsonAsync<AssignmentSummaryDto>();
            summary.Should().NotBeNull();
        }

        // 409 path: the confirm handler throws the typed missing/corrupt-blob
        // exception → the route maps it to 409 (not 500). The route still binds the
        // GetAssignmentById read handler as a parameter, so it must be registered.
        var failingConfirm = new Mock<ICommandHandler<ConfirmQuestionsDraftCommand>>();
        failingConfirm.Setup(h => h.HandleAsync(It.IsAny<ConfirmQuestionsDraftCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException(new InvalidQuestionsDraftException("no blob staged")));
        var unusedRead = new Mock<IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>>();
        unusedRead.Setup(h => h.HandleAsync(It.IsAny<GetAssignmentByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AssignmentSummaryDto?)null);

        await using (var failApp = await StartHostAsync(s =>
        {
            s.AddSingleton<ICommandHandler<ConfirmQuestionsDraftCommand>>(failingConfirm.Object);
            s.AddSingleton<IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>>(unusedRead.Object);
        }))
        {
            var client = CreateClient(failApp);
            var conflict = await client.PostAsync($"{DraftUrl(AssignmentId)}/confirm", null);
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "a missing/corrupt blob surfaces 409 (InvalidQuestionsDraftException)");
        }
    }

    [TestMethod]
    public async Task DeleteDraft_204()
    {
        var handler = new Mock<ICommandHandler<DiscardQuestionsDraftCommand>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<DiscardQuestionsDraftCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<DiscardQuestionsDraftCommand>>(handler.Object));
        var client = CreateClient(app);

        var response = await client.DeleteAsync(DraftUrl(AssignmentId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static AssignmentSummaryDto SampleSummary() => new(
        Id: AssignmentId,
        Title: "Sample",
        Description: null,
        AssignmentType: AssignmentTypeDto.Digital,
        GradingFormat: GradingFormatDto.AutoGraded,
        TargetAudienceType: TargetAudienceTypeDto.AllStudents,
        TopicId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        TopicName: "Photosynthesis",
        GradeLevelId: null,
        GradeName: null,
        Status: AssignmentStatusDto.Draft,
        DueDate: null,
        MaxScore: null,
        MandatoryReview: false,
        CreatedByTeacherId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    /// <summary>OIDC auth is disabled so the mapped group needs no authorization
    /// scheme (the ar-9 TestAuth posture).</summary>
    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth;

        public IDictionary<string, bool> GetAllFlags() =>
            new Dictionary<string, bool> { [FeatureFlagKeys.DisableOIDCAuth] = true };

        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled(featureKey));
    }
}
