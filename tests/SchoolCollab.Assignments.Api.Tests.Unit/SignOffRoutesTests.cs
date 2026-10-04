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
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-C1/C2 route wiring (round-doc binding list) — exercised against a minimal
/// in-process host (TestServer) that maps ONLY
/// <see cref="AssignmentEndpoints.MapAssignmentEndpoints"/> with mocked
/// handlers. Program is never started, so no Postgres / RabbitMQ / Settings-api
/// is needed. Covers the status-code mapping (200 / 204 / 404 / 409 / 400) and
/// the endpoint-level IP + user-agent capture that threads into the sign-off
/// command (spec §3.2 line 53 / §6 auditability line 116).
/// </summary>
[TestClass]
public class SignOffRoutesTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GuardianId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>Client-side read options — must carry the enum converters the
    /// minimal host serializes with (the ApiClient does the same).</summary>
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter<SignOffStateDto>(),
            new JsonStringEnumConverter<SignatureTypeDto>(),
        }
    };

    private static string SignOffUrl => $"/assignments/{AssignmentId}/students/{StudentId}/sign-off";

    private static async Task<WebApplication> StartHostAsync(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        // Harness completion (round teacher-scope-auth rework): `GET /assignments/{id}/sign-off-statuses`
        // is an id-addressed reader read and now applies the [P2-2] scope gate, so it resolves the
        // caller, their taught set and the assignment detail. This host (FEATURE:DisableOIDCAuth ON,
        // role-less) never had to register them, so the request failed with "No service for type
        // 'ICurrentUser' has been registered" instead of exercising the route. The three registrations
        // below supply that dependency surface; no assertion in this file changed.
        builder.Services.AddSingleton<ICurrentUser, TestAuthCurrentUser>();
        builder.Services.AddSingleton<ITeacherScopeProvider, UnrestrictedScopeProvider>();
        builder.Services.AddSingleton<IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>, StubAssignmentDetailHandler>();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<SignOffStateDto>());
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<SignatureTypeDto>());
        });
        configure?.Invoke(builder.Services);

        var app = builder.Build();

        // Deterministic caller IP — the route reads
        // HttpContext.Connection.RemoteIpAddress and TestServer leaves it null.
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
            return next(context);
        });

        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    private static Mock<ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto>> SignOffHandler(
        Func<SignOffSubmissionCommand, CancellationToken, Task<SignOffStatusDto>>? onHandle = null)
    {
        var handler = new Mock<ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<SignOffSubmissionCommand>(), It.IsAny<CancellationToken>()))
            .Returns((SignOffSubmissionCommand c, CancellationToken ct) =>
                onHandle?.Invoke(c, ct) ?? Task.FromResult(SampleStatus()));
        return handler;
    }

    private static SignOffStatusDto SampleStatus() =>
        new(StudentId, "Ward One", GuardianId, "Jane Doe", GuardianId, "Jane Doe",
            SignOffStateDto.Signed, DateTimeOffset.UtcNow, null, true, true, 2, 88m, true);

    [TestMethod]
    public async Task SignOff_Returns200_AndCapturesIpAndAgent()
    {
        SignOffSubmissionCommand? captured = null;
        var handler = SignOffHandler((c, _) =>
        {
            captured = c;
            return Task.FromResult(SampleStatus());
        });

        await using var app = await StartHostAsync(s => s.AddSingleton<
            ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto>>(handler.Object));
        var client = CreateClient(app);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("unit-test-agent/1.0");

        var response = await client.PostAsJsonAsync(SignOffUrl,
            new SignOffSubmissionRequest(GuardianId, SignatureTypeDto.Typed, "Jane Doe"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        captured.Should().NotBeNull();
        captured!.AssignmentId.Should().Be(AssignmentId);
        captured.StudentId.Should().Be(StudentId);
        captured.GuardianId.Should().Be(GuardianId);
        captured.SignatureType.Should().Be(SignatureType.Typed);
        captured.TypedSignature.Should().Be("Jane Doe");
        captured.IpAddress.Should().Be("203.0.113.7", "the endpoint captures the caller IP for the audit event");
        captured.UserAgent.Should().Be("unit-test-agent/1.0", "the endpoint captures the user-agent for the audit event");

        var body = await response.Content.ReadFromJsonAsync<SignOffStatusDto>(ReadOptions);
        body!.SignOffState.Should().Be(SignOffStateDto.Signed);
    }

    [TestMethod]
    public async Task SignOff_Maps409s()
    {
        (HttpStatusCode Expected, Exception Error)[] cases =
        {
            (HttpStatusCode.Conflict, new SubmissionAlreadySignedException(StudentId)),
            (HttpStatusCode.Conflict, new SubmissionSignOffStateException("This assignment does not require a guardian signature.")),
            (HttpStatusCode.Conflict, new SubmissionLockedException(StudentId)),
            (HttpStatusCode.Conflict, new GuardianNotAuthorizedException(StudentId, GuardianId)),
            (HttpStatusCode.BadRequest, new ArgumentException("A typed signature is required.")),
            (HttpStatusCode.NotFound, new SubmissionNotFoundException(StudentId)),
            (HttpStatusCode.NotFound, new AssignmentNotFoundException(AssignmentId)),
        };

        foreach (var (expected, error) in cases)
        {
            var handler = new Mock<ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto>>();
            handler.Setup(h => h.HandleAsync(It.IsAny<SignOffSubmissionCommand>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromException<SignOffStatusDto>(error));

            await using var app = await StartHostAsync(s => s.AddSingleton<
                ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto>>(handler.Object));
            var client = CreateClient(app);

            var response = await client.PostAsJsonAsync(SignOffUrl,
                new SignOffSubmissionRequest(GuardianId, SignatureTypeDto.Click));

            response.StatusCode.Should().Be(expected, "the route maps a thrown {0}", error.GetType().Name);
        }
    }

    [TestMethod]
    public async Task Reassign_Returns204()
    {
        ReassignSignOffCommand? captured = null;
        var handler = new Mock<ICommandHandler<ReassignSignOffCommand>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<ReassignSignOffCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ReassignSignOffCommand, CancellationToken>((c, _) => captured = c)
            .Returns(Task.CompletedTask);

        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<ReassignSignOffCommand>>(handler.Object));
        var client = CreateClient(app);

        var response = await client.PostAsJsonAsync($"{SignOffUrl}/reassign",
            new ReassignSignOffRequest(GuardianId));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().NotBeNull();
        captured!.NewGuardianId.Should().Be(GuardianId);
    }

    [TestMethod]
    public async Task Finalize_Returns204()
    {
        FinalizeSignOffCommand? captured = null;
        var handler = new Mock<ICommandHandler<FinalizeSignOffCommand>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<FinalizeSignOffCommand>(), It.IsAny<CancellationToken>()))
            .Callback<FinalizeSignOffCommand, CancellationToken>((c, _) => captured = c)
            .Returns(Task.CompletedTask);

        await using var app = await StartHostAsync(s => s.AddSingleton<ICommandHandler<FinalizeSignOffCommand>>(handler.Object));
        var client = CreateClient(app);

        var response = await client.PostAsync($"{SignOffUrl}/finalize", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().NotBeNull();
        captured!.StudentId.Should().Be(StudentId);
    }

    [TestMethod]
    public async Task Statuses_Returns200List()
    {
        var handler = new Mock<IQueryHandler<ListSignOffStatusesQuery, IReadOnlyList<SignOffStatusDto>>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<ListSignOffStatusesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SignOffStatusDto> { SampleStatus() });

        await using var app = await StartHostAsync(s => s.AddSingleton<
            IQueryHandler<ListSignOffStatusesQuery, IReadOnlyList<SignOffStatusDto>>>(handler.Object));
        var client = CreateClient(app);

        var response = await client.GetAsync($"/assignments/{AssignmentId}/sign-off-statuses");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<SignOffStatusDto>>(ReadOptions);
        rows.Should().ContainSingle();
        rows![0].StudentId.Should().Be(StudentId);
    }

    [TestMethod]
    public async Task ConsentText_Always200()
    {
        await using var app = await StartHostAsync(s => s.AddSingleton<ISignatureConsentTextResolver>(new StubConsentResolver()));
        var client = CreateClient(app);

        var response = await client.GetAsync("/assignments/signature-consent-text");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("consent-text-from-resolver");
    }

    /// <summary>OIDC auth is disabled so the mapped group needs no authorization
    /// scheme (the Students.Tests.Integration TestAuth posture).</summary>
    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth;

        public IDictionary<string, bool> GetAllFlags() =>
            new Dictionary<string, bool> { [FeatureFlagKeys.DisableOIDCAuth] = true };

        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled(featureKey));
    }

    private sealed class StubConsentResolver : ISignatureConsentTextResolver
    {
        public Task<string> ResolveConsentTextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("consent-text-from-resolver");
    }

    /// <summary>
    /// The principal this host's requests carry: FEATURE:DisableOIDCAuth is ON and the minimal host
    /// maps no authentication scheme, so there is no role and no <c>teacher_id</c> claim —
    /// <see cref="ICurrentUser.TeacherId"/> is null, matching the TestAuth default posture.
    /// </summary>
    private sealed class TestAuthCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? TeacherId => null;
        public TenantContext CurrentTenant => new(Guid.Empty, "Test", TenantType.School);
    }

    /// <summary>
    /// [P1-4] the posture of a principal carrying no recognised role: tenant-wide (this host's
    /// caller is role-less, so <c>AssignmentScopeResolver</c> never consults the port — the
    /// unrestricted default is the honest value here, never a scope crafted to suit an assertion).
    /// </summary>
    private sealed class UnrestrictedScopeProvider : ITeacherScopeProvider
    {
        public Task<TeacherScope> GetScopeAsync(Guid teacherId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TeacherScope.Unrestricted);
    }

    /// <summary>
    /// The [P2-2] gate's detail read for this host. Its premise is that the assignment the route is
    /// asked about exists — the sign-off list handler answers that same id with a row — so the
    /// honest stand-in is "the row exists", exactly as the production detail read answers for a
    /// caller whose scope is unrestricted.
    /// </summary>
    private sealed class StubAssignmentDetailHandler : IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>
    {
        public Task<AssignmentSummaryDto?> HandleAsync(GetAssignmentByIdQuery query, CancellationToken ct = default) =>
            Task.FromResult<AssignmentSummaryDto?>(new AssignmentSummaryDto(
                query.Id, "Assignment", null, AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded,
                TargetAudienceTypeDto.AllStudents, Guid.Empty, null, null, null, AssignmentStatusDto.Draft,
                null, null, false, Guid.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }
}
