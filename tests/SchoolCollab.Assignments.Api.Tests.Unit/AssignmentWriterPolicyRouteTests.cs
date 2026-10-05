using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmission;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Constants;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round <c>portal-submission-grade</c> D1 / AC1 / AC2 — the <b>writer policy</b> and the grade
/// route it holds. Two layers, the <c>PortalSessionReaderPolicyTests</c> / 
/// <c>AssignmentReaderPolicyRouteTests</c> shape:
///
/// <list type="number">
/// <item>endpoint metadata: the grade POST carries the group's authenticated-user policy AND
/// exactly one role-bearing writer policy, naming the portal-session GATEWAY and the reader's own
/// four-role disjunction; every sibling write keeps the group policy alone, so no assignment-level
/// write moved (the round's explicit non-goal);</item>
/// <item>HTTP status over the container-free TestServer harness: a live portal session
/// authorizes the grade POST <b>through the gateway</b> (the assertion that proves the write path
/// works at all — the parent group's Bearer opt-in and the writer policy's gateway opt-in are
/// COMBINED by the authorization middleware, and only the union authenticates a session caller);
/// a role-less session is 403; an unknown session is a bare 401; a bearer caller with a role is
/// unchanged; and the moved route's four catch arms plus its 404-before-dispatch are asserted
/// (AC2's verbatim-parity tripwire).</item>
/// </list>
///
/// <para>Discriminating: pre-fix the grade POST carried no writer policy at all — a portal session
/// could not reach it (the group's Bearer pin 401s the header-less caller) — and the route's
/// catch arms lived in <c>MapAssignmentRoutes</c>.</para>
/// </summary>
[TestClass]
public class AssignmentWriterPolicyRouteTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TeacherClaimId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string GradePattern = "assignments/{id:guid}/students/{studentId:guid}/submission/review";
    private const string GradeUrl = "/assignments/11111111-1111-1111-1111-111111111111/students/22222222-2222-2222-2222-222222222222/submission/review";

    private static readonly string[] WriterRoles =
        [RealmRoleNames.Teacher, RealmRoleNames.Staff, RealmRoleNames.UserAdmin, RealmRoleNames.PlatformAdmin];

    /// <summary>The sibling writes the round explicitly leaves alone: only the group policy.</summary>
    private static readonly (string Method, string Pattern)[] UnmovedWrites =
    [
        ("POST", "assignments/{id:guid}/review"),
        ("POST", "assignments/{id:guid}/students/{studentId:guid}/override-attempts"),
        ("POST", "assignments/{id:guid}/students/{studentId:guid}/enable-submission"),
        ("POST", "assignments/{id:guid}/students/{studentId:guid}/sign-off"),
        ("POST", "assignments/{id:guid}/publish"),
    ];

    private static readonly string GradeBody =
        $$"""{"teacherId":"{{TeacherClaimId}}","score":90,"grade":"A","comments":"Good work"}""";

    private sealed class StubFeatureFlags(bool disableOidcAuth) : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey switch
        {
            FeatureFlagKeys.DisableOIDCAuth => disableOidcAuth,
            _ => false,
        };

        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(IsEnabled(featureKey));
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    /// <summary>The canned claim set the stub claims port answers — the D9 contract the
    /// portal-session handler materializes, with the roles the case under test needs.</summary>
    private sealed class StubClaimsReader : IPortalSessionClaimsReader
    {
        public PortalClaims? NextClaims { get; set; } =
            new("00000000-0000-0000-0000-000000000002", "Dev School", "School", TeacherClaimId.ToString(), [RealmRoleNames.Teacher]);

        public string? LastSessionId { get; private set; }

        public ValueTask<PortalClaims?> ReadClaimsAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            LastSessionId = sessionId;
            return ValueTask.FromResult(NextClaims);
        }
    }

    private sealed class StubSubmissionRepository : ISubmissionRepository
    {
        public AssignmentSubmission? Submission { get; set; }

        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
            => Task.FromResult(Submission);

        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken ct = default)
            => Task.FromResult(Submission);

        public void Add(AssignmentSubmission submission) { }
        public void Update(AssignmentSubmission submission) { }
        public void Add(AssignmentSubmissionVersion version) { }
        public void Add(SubmissionReview review) { }
        public void Add(SubmissionAnswer answer) { }
        public void Add(AssignmentRecipient recipient) { }
        public void Update(AssignmentRecipient recipient) { }
        public void Add(GuardianSubmissionGate gate) { }
        public void Update(GuardianSubmissionGate gate) { }
        public void Add(SignatureEvent signatureEvent) { }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<AssignmentRecipient?> GetRecipientAsync(Guid assignmentId, Guid contactId, CancellationToken ct = default) => Task.FromResult<AssignmentRecipient?>(null);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid gateId, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default) => Task.FromResult<SignatureEvent?>(null);
        public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmission>());
        public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(new List<AssignmentRecipient>());
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> submissionIds, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmissionVersion>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid teacherId, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
    }

    /// <summary>Records the command it was handed and answers whatever the case under test wants.</summary>
    private sealed class ScriptedReviewHandler(Func<ReviewSubmissionCommand, Exception?>? outcome = null)
        : ICommandHandler<ReviewSubmissionCommand>
    {
        public ReviewSubmissionCommand? LastCommand { get; private set; }

        public int Calls { get; private set; }

        public Task HandleAsync(ReviewSubmissionCommand command, CancellationToken ct = default)
        {
            Calls++;
            LastCommand = command;
            var failure = outcome?.Invoke(command);
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }

    /// <summary>The bearer scheme, authenticating per request from headers: <c>x-test-roles</c>
    /// comma-separated role names and <c>x-test-anonymous: 1</c> for an unauthenticated caller.</summary>
    private sealed class HeaderPrincipalAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers["x-test-anonymous"] == "1")
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var roles = Request.Headers["x-test-roles"].ToString();
            if (string.IsNullOrWhiteSpace(roles))
            {
                // No roles header ⇒ no presented credential (the reader-host shape): the portal
                // session case must authorize through the gateway, never through this handler.
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "test-user") };
            foreach (var role in roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private static async Task<WebApplication> StartHostAsync(
        bool disableOidcAuth,
        StubClaimsReader? claimsReader = null,
        ISubmissionRepository? repository = null,
        ICommandHandler<ReviewSubmissionCommand>? reviewHandler = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags(disableOidcAuth));
        builder.Services.AddAuthentication(AuthTenancyExtensions.BearerScheme)
            .AddScheme<AuthenticationSchemeOptions, HeaderPrincipalAuthHandler>(
                AuthTenancyExtensions.BearerScheme, _ => { })
            // D19/D7: the writer policy names the GATEWAY scheme, and a policy-named scheme
            // without a handler is a 500 — never a 401 — so this host registers the gateway
            // exactly as Program.cs does, in BOTH flag states, over a stubbed claims port.
            .AddPortalSessionAuthentication(AuthTenancyExtensions.BearerScheme);
        builder.Services.AddSingleton<IPortalSessionClaimsReader>(claimsReader ?? new StubClaimsReader());
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<ISubmissionRepository>(
            repository ?? new StubSubmissionRepository());
        builder.Services.AddSingleton<ICommandHandler<ReviewSubmissionCommand>>(
            reviewHandler ?? new ScriptedReviewHandler());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    private static HttpRequestMessage GradeRequest(string? sessionId, string? roles = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GradeUrl)
        {
            Content = new StringContent(GradeBody, Encoding.UTF8, "application/json"),
        };
        if (sessionId is not null)
        {
            request.Headers.Add(PortalSessionAuthenticationHandler.SessionHeaderName, sessionId);
        }

        if (roles is not null)
        {
            request.Headers.Add("x-test-roles", roles);
        }

        return request;
    }

    private static async Task<HttpResponseMessage> PostGradeAsync(
        WebApplication app, string sessionId, string? roles = null)
    {
        using var client = CreateClient(app);
        return await client.SendAsync(GradeRequest(sessionId, roles));
    }

    /// <summary>A host with a gradeable submission and a no-op review handler.</summary>
    private static Task<WebApplication> StartGradeableHostAsync(
        StubClaimsReader claimsReader,
        Func<ReviewSubmissionCommand, Exception?>? outcome = null)
    {
        var repository = new StubSubmissionRepository
        {
            Submission = AssignmentSubmission.Create(Guid.NewGuid(), AssignmentId, StudentId, null),
        };
        return StartHostAsync(false, claimsReader, repository, new ScriptedReviewHandler(outcome));
    }

    // ── Layer 1: endpoint metadata ────────────────────────────────────────

    private static RouteEndpoint Find(WebApplication app, string method, string pattern)
    {
        var wanted = Normalize(pattern);
        var match = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => Normalize(e.RoutePattern.RawText) == wanted)
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true)
            .ToList();

        match.Should().ContainSingle($"exactly one {method} route should match '{wanted}'");
        return match[0];
    }

    private static string Normalize(string? rawText) => (rawText ?? string.Empty).Trim('/');

    [TestMethod]
    public async Task TheGradePost_CarriesTheWriterPolicy_OnTheGatewayScheme()
    {
        await using var app = await StartHostAsync(disableOidcAuth: false);

        var endpoint = Find(app, "POST", GradePattern);
        var policies = endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>();

        policies.Should().HaveCount(2,
            "the grade POST carries the group's authenticated-user policy AND the writer policy");
        var writer = policies.Where(p => p.Requirements.OfType<RolesAuthorizationRequirement>().Any()).ToList();
        writer.Should().ContainSingle("the grade POST carries exactly one role-bearing policy");
        writer[0].AuthenticationSchemes.Should().Equal([PortalSessionAuthenticationHandler.GatewaySchemeName],
            "the writer policy names the portal-session gateway (D19): the single scheme registered in " +
            "every flag state — portal callers authenticate through it, bearer callers through the " +
            "pinned Bearer fallback, and the two listed schemes are COMBINED into the endpoint's union");
        writer[0].AuthenticationSchemes.Should().NotContain(AuthTenancyExtensions.BearerScheme);
        writer[0].Requirements.OfType<RolesAuthorizationRequirement>().Single().AllowedRoles.Should()
            .BeEquivalentTo(WriterRoles,
                "the writer policy restates the reader's four-role disjunction — one disjunction per module");
    }

    [TestMethod]
    public async Task TheSiblingWrites_KeepTheGroupPolicyAlone()
    {
        // The round's explicit non-goal: only the submission-grade route moves. A role-bearing
        // policy on any of these would be a scope regression (the read policy's [P1-1] reason).
        await using var app = await StartHostAsync(disableOidcAuth: false);

        foreach (var (method, pattern) in UnmovedWrites)
        {
            var endpoint = Find(app, method, pattern);
            var policies = endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>();

            policies.Should().HaveCount(1, $"{method} /{pattern} keeps only the group's policy");
            policies.SelectMany(p => p.Requirements).Should().NotContain(r => r is RolesAuthorizationRequirement,
                $"{method} /{pattern} must NOT be covered by the writer policy");
        }
    }

    // ── Layer 2: HTTP status (the union that makes the write path work) ────

    [TestMethod]
    public async Task APortalSession_AuthorizesTheGradePost_ThroughTheGateway()
    {
        // THE assertion the watch item asks for: asserted over the real HTTP pipeline (the
        // middleware combines the group's Bearer opt-in with the writer policy's gateway opt-in
        // and authenticates the session through the gateway), not per-policy in isolation.
        var claimsReader = new StubClaimsReader();
        await using var app = await StartGradeableHostAsync(claimsReader);

        var response = await PostGradeAsync(app, "a-live-session-id");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a live portal session carrying the teacher role authorizes the grade POST");
        claimsReader.LastSessionId.Should().Be("a-live-session-id",
            "the handler resolved the request's session id through the claims port");
    }

    [TestMethod]
    public async Task APortalSessionWithoutAWriterRole_IsForbidden()
    {
        var claimsReader = new StubClaimsReader
        {
            NextClaims = new("00000000-0000-0000-0000-000000000002", "Dev School", "School", TeacherClaimId.ToString(), ["guardian"]),
        };
        await using var app = await StartGradeableHostAsync(claimsReader);

        var response = await PostGradeAsync(app, "a-live-session-id");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "T4: the API is the security boundary — a live session without a writer role is the " +
            "policy's own 403, never a Python role list");
    }

    [TestMethod]
    public async Task AnUnknownSession_IsABare401()
    {
        var claimsReader = new StubClaimsReader { NextClaims = null };
        await using var app = await StartGradeableHostAsync(claimsReader);

        var response = await PostGradeAsync(app, "not-a-live-session");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an unknown or expired portal session fails closed with the gateway's bare 401");
        response.Headers.Location.Should().BeNull("the challenge is never a redirect");
    }

    [TestMethod]
    public async Task ABearerCaller_IsUnchanged()
    {
        var repository = new StubSubmissionRepository
        {
            Submission = AssignmentSubmission.Create(Guid.NewGuid(), AssignmentId, StudentId, null),
        };
        await using var app = await StartHostAsync(disableOidcAuth: false, repository: repository);
        using var client = CreateClient(app);

        var response = await client.SendAsync(GradeRequest(sessionId: null, roles: RealmRoleNames.Teacher));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "an existing bearer caller authorizes exactly as before the writer policy landed");
    }

    [TestMethod]
    public async Task AnAnonymousCaller_Is401()
    {
        await using var app = await StartHostAsync(disableOidcAuth: false);
        using var client = CreateClient(app);

        var response = await client.PostAsync(
            GradeUrl, new StringContent(GradeBody, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>[Regression guard] With FEATURE:DisableOIDCAuth ON the policy block is skipped, so
    /// the grade route is reachable without any principal — today's dev posture, unchanged.</summary>
    [TestMethod]
    public async Task UnderDisableOidcAuth_NoWriterPolicyApplies_RegressionGuard()
    {
        var repository = new StubSubmissionRepository { Submission = null };
        await using var app = await StartHostAsync(disableOidcAuth: true, repository: repository);
        using var client = CreateClient(app);

        var response = await client.PostAsync(
            GradeUrl, new StringContent(GradeBody, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "with the flag ON there is no policy at all, so the request reaches the route — which " +
            "answers 404 for the unknown submission, not 401");
    }

    // ── AC2: the moved route's verbatim behaviour ─────────────────────────

    [TestMethod]
    public async Task AnUnknownSubmission_Is404_BeforeTheHandlerIsDispatched()
    {
        var repository = new StubSubmissionRepository { Submission = null };
        var handler = new ScriptedReviewHandler();
        await using var app = await StartHostAsync(false, new StubClaimsReader(), repository, handler);
        using var client = CreateClient(app);

        var response = await client.SendAsync(GradeRequest("a-live-session-id"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the route resolves (assignment, student) -> submission and 404s before dispatching");
        handler.Calls.Should().Be(0, "the command is never dispatched for an unknown submission");
    }

    [TestMethod]
    public async Task TheFourCatchArms_AreUnchangedByTheMove()
    {
        var submission = AssignmentSubmission.Create(Guid.NewGuid(), AssignmentId, StudentId, null);

        (HttpStatusCode, Exception)[] cases =
        [
            (HttpStatusCode.NotFound, new SubmissionNotFoundException(submission.Id)),
            (HttpStatusCode.Forbidden, new UnauthorizedAccessException("only the creating teacher can review")),
            (HttpStatusCode.Forbidden, new MissingTeacherPrincipalException(nameof(ReviewSubmissionCommand))),
            (HttpStatusCode.Forbidden, new TeacherTenantMismatchException(
                AssignmentId, "assignment", Guid.NewGuid(), Guid.NewGuid())),
        ];

        foreach (var (expected, failure) in cases)
        {
            var repository = new StubSubmissionRepository { Submission = submission };
            var handler = new ScriptedReviewHandler(_ => failure);
            await using var app = await StartHostAsync(false, new StubClaimsReader(), repository, handler);
            using var client = CreateClient(app);

            var response = await client.SendAsync(GradeRequest("a-live-session-id"));

            response.StatusCode.Should().Be(expected,
                $"{failure.GetType().Name} maps to {expected} exactly as it did inside MapAssignmentRoutes");
        }
    }

    [TestMethod]
    public async Task TheHappyPath_DispatchesTheSubmissionId_AndTheWireTeacherId()
    {
        var submission = AssignmentSubmission.Create(Guid.NewGuid(), AssignmentId, StudentId, null);
        var repository = new StubSubmissionRepository { Submission = submission };
        var handler = new ScriptedReviewHandler();
        await using var app = await StartHostAsync(false, new StubClaimsReader(), repository, handler);
        using var client = CreateClient(app);

        var response = await client.SendAsync(GradeRequest("a-live-session-id"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        handler.Calls.Should().Be(1);
        handler.LastCommand!.SubmissionId.Should().Be(submission.Id,
            "the route hands the resolved submission's id to the command (not the path id)");
        handler.LastCommand.TeacherId.Should().Be(TeacherClaimId,
            "the body's teacherId is threaded through verbatim — the handler's claim-wins rule is untouched");
    }
}
