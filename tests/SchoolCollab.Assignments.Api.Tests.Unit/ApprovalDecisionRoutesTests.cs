using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DenyAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RejectAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round <c>assignment-approval-workflow</c> (spec §3.1/§3.2/§5, WS2 + WS5) — the route layer:
/// <list type="bullet">
///   <item><c>POST /assignments/{id}/reject</c> carries the approver's optional reason to the
///   command;</item>
///   <item><c>POST /assignments/{id}/deny</c> exists and carries it too;</item>
///   <item>the pending-approval edit hold maps to <b>409</b> on the routes that enforce it (the
///   update route is the representative; the hold is one typed exception, one mapping shape);</item>
///   <item>a cross-tenant denial is 403 (the ar-24 posture), exactly like a cross-tenant
///   approval.</item>
/// </list>
/// </summary>
[TestClass]
public class ApprovalDecisionRoutesTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ApproverId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static async Task<WebApplication> StartHostAsync(Action<IServiceCollection> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        builder.Services.AddSingleton<ICurrentUser, TestAuthCurrentUser>();
        configure(builder.Services);

        var app = builder.Build();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    // ── Reject ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Reject_CarriesTheOptionalReason_ToTheCommand()
    {
        var handler = new ScriptedRejectHandler();
        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<RejectAssignmentCommand>>(handler));

        using var client = CreateClient(app);
        var response = await client.PostAsync(
            $"/assignments/{AssignmentId}/reject",
            Json($$"""{"approverId":"{{ApproverId}}","reason":"Add the rubric"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        handler.LastCommand!.Reason.Should().Be("Add the rubric");
        handler.LastCommand.ApproverId.Should().Be(ApproverId);
    }

    [TestMethod]
    public async Task Reject_WithoutAReason_StaysValid()
    {
        var handler = new ScriptedRejectHandler();
        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<RejectAssignmentCommand>>(handler));

        using var client = CreateClient(app);
        var response = await client.PostAsync(
            $"/assignments/{AssignmentId}/reject",
            Json($$"""{"approverId":"{{ApproverId}}"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        handler.LastCommand!.Reason.Should().BeNull();
    }

    // ── Deny (new route) ────────────────────────────────────────────────

    [TestMethod]
    public async Task Deny_IsMapped_AndCarriesTheOptionalReason()
    {
        var handler = new ScriptedDenyHandler();
        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<DenyAssignmentCommand>>(handler));

        using var client = CreateClient(app);
        var response = await client.PostAsync(
            $"/assignments/{AssignmentId}/deny",
            Json($$"""{"approverId":"{{ApproverId}}","reason":"Out of scope"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        handler.LastCommand!.Reason.Should().Be("Out of scope");
    }

    [TestMethod]
    public async Task Deny_CrossTenant_Is403_TheAr24Posture()
    {
        var handler = new ScriptedDenyHandler(() => new TeacherTenantMismatchException(
            AssignmentId, "assignment", Guid.NewGuid(), Guid.NewGuid()));
        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<DenyAssignmentCommand>>(handler));

        using var client = CreateClient(app);
        var response = await client.PostAsync(
            $"/assignments/{AssignmentId}/deny",
            Json($$"""{"approverId":"{{ApproverId}}"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a foreign-tenant denial is never a 500");
    }

    [TestMethod]
    public async Task Deny_RealAuthWithoutATeacherClaim_Is403()
    {
        var handler = new ScriptedDenyHandler(() => new MissingTeacherPrincipalException("DenyAssignmentCommand"));
        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<DenyAssignmentCommand>>(handler));

        using var client = CreateClient(app);
        var response = await client.PostAsync(
            $"/assignments/{AssignmentId}/deny",
            Json($$"""{"approverId":"{{ApproverId}}"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task Deny_UnknownAssignment_Is404()
    {
        var handler = new ScriptedDenyHandler(() => new AssignmentNotFoundException(AssignmentId));
        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<DenyAssignmentCommand>>(handler));

        using var client = CreateClient(app);
        var response = await client.PostAsync(
            $"/assignments/{AssignmentId}/deny",
            Json($$"""{"approverId":"{{ApproverId}}"}"""));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── The hold's 409 mapping (representative: the authoring save) ──────

    [TestMethod]
    public async Task Update_WhilePending_Is409_NotA500()
    {
        var handler = new ScriptedUpdateHandler(() => new AssignmentPendingApprovalException(AssignmentId));
        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<UpdateAssignmentCommand>>(handler));

        using var client = CreateClient(app);
        var response = await client.PutAsync(
            $"/assignments/{AssignmentId}",
            // Numeric enum values: this minimal host registers no JSON enum converters (the real
            // Program.cs does) — the body only has to bind so the handler's 409 arm is reached.
            Json($$"""
                {"title":"Renamed","assignmentType":0,"gradingFormat":1,
                 "targetAudienceType":0,"topicId":"00000000-0000-0000-0000-000000000002"}
                """));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the hold is 409 — the request is legal, the row is locked until an approver decides");
    }

    // ── Fakes ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SubmitForApproval_OnADeniedRow_Is400_WithTheRulesOwnMessage()
    {
        // Denied is terminal: the domain raises the typed AssignmentApprovalStateException, which the
        // route maps to 400 explicitly (never the generic status-guard mapping).
        var handler = new ScriptedSubmitHandler(() => new SchoolCollab.Assignments.Core.Domain.Exceptions.AssignmentApprovalStateException(
            "A denied assignment cannot be submitted again; duplicate it to start a new draft."));
        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentForApprovalCommand.SubmitAssignmentForApprovalCommand>>(handler));

        using var client = CreateClient(app);
        var response = await client.PostAsync($"/assignments/{AssignmentId}/submit-for-approval", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("denied assignment cannot be submitted again");
    }

    private sealed class ScriptedSubmitHandler(Func<Exception?>? outcome = null)
        : ICommandHandler<SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentForApprovalCommand.SubmitAssignmentForApprovalCommand>
    {
        public Task HandleAsync(
            SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentForApprovalCommand.SubmitAssignmentForApprovalCommand command,
            CancellationToken cancellationToken = default)
        {
            var failure = outcome?.Invoke();
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    private sealed class ScriptedRejectHandler(Func<Exception?>? outcome = null) : ICommandHandler<RejectAssignmentCommand>
    {
        public RejectAssignmentCommand? LastCommand { get; private set; }

        public Task HandleAsync(RejectAssignmentCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            var failure = outcome?.Invoke();
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    private sealed class ScriptedDenyHandler(Func<Exception?>? outcome = null) : ICommandHandler<DenyAssignmentCommand>
    {
        public DenyAssignmentCommand? LastCommand { get; private set; }

        public Task HandleAsync(DenyAssignmentCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            var failure = outcome?.Invoke();
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    private sealed class ScriptedUpdateHandler(Func<Exception?>? outcome = null) : ICommandHandler<UpdateAssignmentCommand>
    {
        public Task HandleAsync(UpdateAssignmentCommand command, CancellationToken cancellationToken = default)
        {
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

        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool> { [FeatureFlagKeys.DisableOIDCAuth] = true });
    }

    private sealed class TestAuthCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? TeacherId => null;
        public TenantContext CurrentTenant => new(Guid.Empty, "Test", TenantType.School);
    }
}
