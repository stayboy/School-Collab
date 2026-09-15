using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateStudentSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RecordModuleProgress;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.Ward;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-D1/WS-A5 route wiring (round-doc binding list) — exercised against the
/// same minimal TestServer harness as <see cref="SignOffRoutesTests"/> with
/// mocked handlers (Program never starts). Covers the progress upsert (204/404),
/// the ward assignment view (200/404 with per-module progress + the
/// questions-unlocked flag), the ward list (published, HasLockedModules), and
/// the module-gate 409 on submission.
/// </summary>
[TestClass]
public class WardRoutesTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ModuleId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter<ModuleTypeDto>(),
            new JsonStringEnumConverter<WardSubmissionStateDto>(),
        }
    };

    private static string ProgressUrl => $"/assignments/{AssignmentId}/students/{StudentId}/modules/{ModuleId}/progress";
    private static string WardViewUrl => $"/assignments/{AssignmentId}/students/{StudentId}/modules";
    private static string WardListUrl => $"/students/{StudentId}/assignments";
    private static string SubmissionUrl => $"/assignments/{AssignmentId}/students/{StudentId}/submission";

    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth;
        public IDictionary<string, bool> GetAllFlags() =>
            new Dictionary<string, bool> { [FeatureFlagKeys.DisableOIDCAuth] = true };
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled(featureKey));
    }

    private static async Task<WebApplication> StartHostAsync(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ModuleTypeDto>());
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<WardSubmissionStateDto>());
        });
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    private static WardAssignmentViewDto SampleView(bool unlocked = true)
    {
        var module = new WardModuleViewDto(ModuleId, ModuleTypeDto.Video, "Watch me", "https://v",
            0, 80, true, unlocked ? 100 : 40, unlocked ? DateTimeOffset.UtcNow : null);
        return new WardAssignmentViewDto(AssignmentId, "Gated", DateTimeOffset.UtcNow, unlocked, [module]);
    }

    [TestMethod]
    public async Task ProgressPost_Returns204_WhenRecorded()
    {
        var handler = new Mock<ICommandHandler<RecordModuleProgressCommand, bool>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<RecordModuleProgressCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<RecordModuleProgressCommand, bool>>(handler.Object));
        var client = CreateClient(app);

        var response = await client.PostAsJsonAsync(ProgressUrl, new RecordModuleProgressRequest(50));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [TestMethod]
    public async Task ProgressPost_Returns404_WhenModuleMissing()
    {
        var handler = new Mock<ICommandHandler<RecordModuleProgressCommand, bool>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<RecordModuleProgressCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<RecordModuleProgressCommand, bool>>(handler.Object));
        var client = CreateClient(app);

        var response = await client.PostAsJsonAsync(ProgressUrl, new RecordModuleProgressRequest(50));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task WardView_Returns200_WithPerModuleProgressAndUnlockFlag()
    {
        var handler = new Mock<IQueryHandler<GetWardAssignmentView, WardAssignmentViewDto?>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<GetWardAssignmentView>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleView(unlocked: true));

        await using var app = await StartHostAsync(s => s.AddSingleton<
            IQueryHandler<GetWardAssignmentView, WardAssignmentViewDto?>>(handler.Object));
        var client = CreateClient(app);

        var view = await client.GetFromJsonAsync<WardAssignmentViewDto>(WardViewUrl, ReadOptions);
        view.Should().NotBeNull();
        view!.QuestionsUnlocked.Should().BeTrue();
        view.Modules.Should().ContainSingle();
        view.Modules[0].PercentComplete.Should().Be(100);
        view.Modules[0].CompletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task WardView_Returns404_WhenAssignmentMissing()
    {
        var handler = new Mock<IQueryHandler<GetWardAssignmentView, WardAssignmentViewDto?>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<GetWardAssignmentView>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WardAssignmentViewDto?)null);

        await using var app = await StartHostAsync(s => s.AddSingleton<
            IQueryHandler<GetWardAssignmentView, WardAssignmentViewDto?>>(handler.Object));
        var client = CreateClient(app);

        var response = await client.GetAsync(WardViewUrl);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task WardList_Returns200_WithHasLockedModules()
    {
        var handler = new Mock<IQueryHandler<ListWardAssignments, WardAssignmentListItemDto[]>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<ListWardAssignments>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WardAssignmentListItemDto(AssignmentId, "Gated", DateTimeOffset.UtcNow,
                WardSubmissionStateDto.InProgress, HasLockedModules: true)]);

        await using var app = await StartHostAsync(s => s.AddSingleton<
            IQueryHandler<ListWardAssignments, WardAssignmentListItemDto[]>>(handler.Object));
        var client = CreateClient(app);

        var items = await client.GetFromJsonAsync<WardAssignmentListItemDto[]>(WardListUrl, ReadOptions);
        items.Should().ContainSingle();
        items[0].HasLockedModules.Should().BeTrue();
        items[0].State.Should().Be(WardSubmissionStateDto.InProgress);
    }

    [TestMethod]
    public async Task Submission_Returns409_WhenRequiredModuleIncomplete()
    {
        var handler = new Mock<ICommandHandler<CreateStudentSubmissionCommand, SubmissionFeedbackDto?>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<CreateStudentSubmissionCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequiredModuleIncompleteException("required module x not completed"));

        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<CreateStudentSubmissionCommand, SubmissionFeedbackDto?>>(handler.Object));
        var client = CreateClient(app);

        var response = await client.PostAsJsonAsync(SubmissionUrl, new { Content = "work", Answers = Array.Empty<SubmissionAnswerDto>() });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
