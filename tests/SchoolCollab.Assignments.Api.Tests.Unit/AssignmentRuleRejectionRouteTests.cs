using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// A7 (round <c>question-response-types</c>, Q4 as settled): the two <b>typed rule rejections</b> the
/// authoring rules throw — <see cref="QuestionResponseKindValidationException"/> (QR/Q5: a media kind
/// cannot be auto-scored) and <see cref="AssignmentTypeGradingValidationException"/> (D15: an Offline
/// assignment is always Teacher Marked) — reach the client as a <c>400</c> carrying <b>the rule's own
/// message</b>, on <b>both</b> write surfaces.
///
/// <para>Why a route test: the guards live in the command handlers, so the only thing between a rule
/// rejection and an unhandled <c>500</c> is the pair of catch arms in <c>AssignmentRoutes</c> —
/// duplicated on <c>POST /assignments</c> and <c>PUT /assignments/{id}</c>, which is exactly the kind
/// of wiring one of the two copies loses. The handlers here are scripted to throw the
/// <b>production</b> exception, built from the <b>production</b> message producer, so the assertion
/// tracks the rule rather than a copy of its text.</para>
///
/// <para>Discriminating: every rejection case is paired with a control where the same body and the
/// same scripted-handler shape complete normally (<c>201</c> / <c>204</c>) — proving the <c>400</c>s
/// come from the catch arms and not from body binding or a mistyped route, which a bare status
/// assertion could never tell apart.</para>
///
/// <para>The harness is <see cref="SignOffRoutesTests"/>'s: a minimal in-process TestServer mapping
/// only <see cref="AssignmentEndpoints.MapAssignmentEndpoints"/> over a
/// <c>FEATURE:DisableOIDCAuth</c>-ON flag stub, so no Postgres, RabbitMQ, Keycloak or settings-api is
/// needed.</para>
/// </summary>
[TestClass]
public class AssignmentRuleRejectionRouteTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string CreateUrl = "/assignments";

    private static string UpdateUrl => $"/assignments/{AssignmentId}";

    /// <summary>The smallest body that binds: <c>Title</c> and <c>Description</c> are the request's
    /// only non-defaulted members, so no enum converter is involved and the payload never has a say in
    /// the outcome — the scripted handler decides it alone.</summary>
    private const string EmptyDraftBody = """{"title":"Rule rejection probe","description":null}""";

    /// <summary>The message <see cref="QuestionResponseKindRules"/> really throws for a media kind on
    /// Auto Scored — produced by calling the guard, never copied from its source.</summary>
    private static string MediaKindRejection()
    {
        try
        {
            QuestionResponseKindRules.EnsurePermitted(
                GradingFormatDto.AutoGraded, [QuestionResponseKindDto.Video]);
            throw new AssertFailedException(
                "premise: the guard must reject a media response kind on an Auto Scored assignment");
        }
        catch (QuestionResponseKindValidationException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The message <see cref="AssignmentTypeGradingRules"/> really throws for an Offline
    /// (Manual) assignment on Auto Scored — again produced by the guard itself.</summary>
    private static string TypeGradingRejection()
    {
        try
        {
            AssignmentTypeGradingRules.EnsurePermitted(
                AssignmentTypeDto.Manual, GradingFormatDto.AutoGraded);
            throw new AssertFailedException(
                "premise: the guard must reject Auto Scored on an Offline (Manual) assignment");
        }
        catch (AssignmentTypeGradingValidationException ex)
        {
            return ex.Message;
        }
    }

    // ── the harness ───────────────────────────────────────────────────────

    private static async Task<WebApplication> StartHostAsync(Action<IServiceCollection> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        // The dependency surface the group's endpoint filters resolve (the SignOffRoutesTests
        // completion) — this host is role-less on purpose: authorisation is not what is under test.
        builder.Services.AddSingleton<ICurrentUser, TestAuthCurrentUser>();
        configure(builder.Services);

        var app = builder.Build();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    /// <summary>Asserts the <c>400</c>, and that the body carries the rule's own message under the
    /// <c>message</c> property the authoring client reads.</summary>
    private static async Task AssertRejectionAsync(HttpResponseMessage response, string expectedMessage)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("message", out var message).Should().BeTrue(
            $"the 400 body must expose the rule's message as 'message' — got: {body}");
        message.GetString().Should().Be(expectedMessage,
            "the route surfaces the rule's own user-facing message, not a generic one");
    }

    /// <summary>Records the dispatch and answers whatever the case under test wants.</summary>
    private sealed class ScriptedCreateHandler(Func<Exception?>? outcome = null)
        : ICommandHandler<CreateAssignmentCommand, Guid>
    {
        public int Calls { get; private set; }

        public CreateAssignmentCommand? LastCommand { get; private set; }

        public Task<Guid> HandleAsync(CreateAssignmentCommand command, CancellationToken ct = default)
        {
            Calls++;
            LastCommand = command;
            var failure = outcome?.Invoke();
            return failure is null ? Task.FromResult(AssignmentId) : Task.FromException<Guid>(failure);
        }
    }

    /// <summary>The update twin — the second catch-arm site.</summary>
    private sealed class ScriptedUpdateHandler(Func<Exception?>? outcome = null)
        : ICommandHandler<UpdateAssignmentCommand>
    {
        public int Calls { get; private set; }

        public UpdateAssignmentCommand? LastCommand { get; private set; }

        public Task HandleAsync(UpdateAssignmentCommand command, CancellationToken ct = default)
        {
            Calls++;
            LastCommand = command;
            var failure = outcome?.Invoke();
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    /// <summary>OIDC disabled: the mapped group then needs no authorization scheme.</summary>
    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth;

        public IDictionary<string, bool> GetAllFlags() =>
            new Dictionary<string, bool> { [FeatureFlagKeys.DisableOIDCAuth] = true };

        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled(featureKey));
    }

    private sealed class TestAuthCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? TeacherId => null;
        public TenantContext CurrentTenant => new(Guid.Empty, "Test", TenantType.School);
    }

    /// <summary>The JSON body content every case sends.</summary>
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    // ── QR/Q5 — a media kind cannot be auto-scored: BOTH write surfaces ───

    [TestMethod]
    public async Task Create_MediaKindRejection_Is400_WithTheRulesOwnMessage()
    {
        var rejection = MediaKindRejection();
        var handler = new ScriptedCreateHandler(
            () => new QuestionResponseKindValidationException(rejection));
        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<CreateAssignmentCommand, Guid>>(handler));
        using var client = CreateClient(app);

        var response = await client.PostAsync(CreateUrl, Json(EmptyDraftBody));

        await AssertRejectionAsync(response, rejection);
        handler.Calls.Should().Be(1, "the request reached the handler — the 400 is the catch arm");
    }

    [TestMethod]
    public async Task Update_MediaKindRejection_Is400_WithTheRulesOwnMessage()
    {
        var rejection = MediaKindRejection();
        var handler = new ScriptedUpdateHandler(
            () => new QuestionResponseKindValidationException(rejection));
        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<UpdateAssignmentCommand>>(handler));
        using var client = CreateClient(app);

        var response = await client.PutAsync(UpdateUrl, Json(EmptyDraftBody));

        await AssertRejectionAsync(response, rejection);
        handler.Calls.Should().Be(1, "the update surface grew its own catch arm — it must hold too");
    }

    // ── D15 — an Offline assignment is always Teacher Marked ─────────────

    [TestMethod]
    public async Task Create_OfflineAutoScoredRejection_Is400_WithTheRulesOwnMessage()
    {
        var rejection = TypeGradingRejection();
        var handler = new ScriptedCreateHandler(
            () => new AssignmentTypeGradingValidationException(rejection));
        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<CreateAssignmentCommand, Guid>>(handler));
        using var client = CreateClient(app);

        var response = await client.PostAsync(CreateUrl, Json(EmptyDraftBody));

        await AssertRejectionAsync(response, rejection);
        handler.Calls.Should().Be(1);
    }

    [TestMethod]
    public async Task Update_OfflineAutoScoredRejection_Is400_WithTheRulesOwnMessage()
    {
        var rejection = TypeGradingRejection();
        var handler = new ScriptedUpdateHandler(
            () => new AssignmentTypeGradingValidationException(rejection));
        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<UpdateAssignmentCommand>>(handler));
        using var client = CreateClient(app);

        var response = await client.PutAsync(UpdateUrl, Json(EmptyDraftBody));

        await AssertRejectionAsync(response, rejection);
        handler.Calls.Should().Be(1);
    }

    // ── the controls that make the 400s mean something ───────────────────

    [TestMethod]
    public async Task Create_ACleanHandler_Is201_SoThe400sAreTheCatchArms()
    {
        var handler = new ScriptedCreateHandler();
        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<CreateAssignmentCommand, Guid>>(handler));
        using var client = CreateClient(app);

        var response = await client.PostAsync(CreateUrl, Json(EmptyDraftBody));

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the same body and the same route succeed when the handler does not reject — so the 400s " +
            "above are the typed-exception mapping, never body binding or a mistyped route");
        handler.LastCommand!.Title.Should().Be("Rule rejection probe",
            "the body really bound into the command the route built");
    }

    [TestMethod]
    public async Task Update_ACleanHandler_Is204_SoThe400sAreTheCatchArms()
    {
        var handler = new ScriptedUpdateHandler();
        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<UpdateAssignmentCommand>>(handler));
        using var client = CreateClient(app);

        var response = await client.PutAsync(UpdateUrl, Json(EmptyDraftBody));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        handler.LastCommand!.Id.Should().Be(AssignmentId,
            "the route threads the path id into the command");
    }
}
