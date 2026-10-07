using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
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
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListSubmissionsByAssignment;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.Ward;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Constants;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round <c>teacher-scope-auth</c> D4 / D6.5 / [P1-1] — the reader policy and, above all, its
/// <b>route coverage</b>. Two layers:
///
/// <list type="number">
/// <item>the endpoint-metadata matrix: the six covered GETs carry a disjunctive role policy that
/// every one of teacher / staff / user-admin / platform-admin satisfies while a role-less or
/// anonymous principal does not; and every explicitly NOT-covered route carries no role
/// requirement at all, so a role-less principal is still admitted (the Families ward/guardian
/// flows and the create-wizard reads).</item>
/// <item>HTTP status on the container-free TestServer harness (no Postgres / RabbitMQ): 401
/// anonymous, 403 role-less, 200 for a teacher whose scope is threaded onto the query, and the
/// [P2-2] direct-id gate — an out-of-scope id is a 404 on every id-addressed covered read.</item>
/// </list>
///
/// <para>Discriminating: pre-fix, none of the covered routes carried a role requirement and
/// <c>AuthorizeAsync</c> for a role-less principal succeeded everywhere.</para>
/// </summary>
[TestClass]
public class AssignmentReaderPolicyRouteTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TeacherClaimId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GradeId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly string[] AssignmentReaderRoles =
        [RealmRoleNames.Teacher, RealmRoleNames.Staff, RealmRoleNames.UserAdmin, RealmRoleNames.PlatformAdmin];

    /// <summary>The Covered table: exactly these seven routes get the reader policy.</summary>
    private static readonly (string Method, string Pattern)[] CoveredRoutes =
    [
        ("GET", "assignments/"),
        ("GET", "assignments/{id:guid}"),
        ("GET", "assignments/{id:guid}/submissions"),
        ("GET", "assignments/{id:guid}/submissions/review-queue"),
        ("GET", "assignments/{id:guid}/students/{studentId:guid}/submission"),
        ("GET", "assignments/{id:guid}/sign-off-statuses"),
        ("GET", "assignments/effective-policy"),
    ];

    /// <summary>
    /// The Explicitly NOT covered table: the ward/guardian reads, the create-wizard reads, the
    /// sibling ward + activity-group groups, and every write.
    /// </summary>
    private static readonly (string Method, string Pattern)[] NotCoveredRoutes =
    [
        ("GET", "students/{studentId:guid}/assignments"),
        ("GET", "assignments/{id:guid}/students/{studentId:guid}/modules"),
        ("GET", "assignments/{id:guid}/students/{studentId:guid}/sign-off"),
        ("GET", "assignments/{id:guid}/gates/student/{studentId:guid}"),
        ("GET", "assignments/{id:guid}/authoring"),
        ("GET", "assignments/{id:guid}/questions-draft"),
        ("GET", "assignments/{id:guid}/recipients"),
        ("GET", "assignments/signature-consent-text"),
        ("GET", "assignments/ai-prompt-policy"),
        ("GET", "assignments/recipient-preview"),
        ("GET", "activity-groups/{groupId:guid}/assignments"),
        ("POST", "assignments/"),
        ("PUT", "assignments/{id:guid}"),
        ("DELETE", "assignments/{id:guid}"),
        ("POST", "assignments/{id:guid}/publish"),
        ("POST", "assignments/{id:guid}/approve"),
        ("POST", "assignments/{id:guid}/students/{studentId:guid}/submission"),
        ("POST", "assignments/{id:guid}/students/{studentId:guid}/submit-on-behalf"),
        ("POST", "assignments/{id:guid}/students/{studentId:guid}/guardian-review"),
        ("POST", "assignments/{id:guid}/students/{studentId:guid}/sign-off"),
        ("POST", "assignments/{id:guid}/recipients/{contactId:guid}/opened"),
    ];

    private sealed class StubFeatureFlags(bool disableOidcAuth, bool enableActivityGroups = true) : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey switch
        {
            FeatureFlagKeys.DisableOIDCAuth => disableOidcAuth,
            FeatureFlagKeys.EnableActivityGroups => enableActivityGroups,
            _ => false,
        };

        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(IsEnabled(featureKey));
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? TeacherId => TeacherClaimId;
        public TenantContext CurrentTenant => new(Guid.Empty, "Test", TenantType.School);
    }

    /// <summary>D19/D7: the stub claims port the moved portal-session handler authenticates through
    /// on this host — its canned answer is never consulted by the assertions here (bearer callers
    /// only), but the registration itself is what the reader policy's gateway needs to exist.</summary>
    private sealed class StubPortalSessionClaimsReader : IPortalSessionClaimsReader
    {
        public ValueTask<PortalClaims?> ReadClaimsAsync(string sessionId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<PortalClaims?>(null);
    }

    private sealed class StubScopeProvider : ITeacherScopeProvider
    {
        public List<Guid> RequestedTeacherIds { get; } = [];
        public TeacherScope Result { get; set; } =
            TeacherScope.ForTeacher(TeacherClaimId, [new TeacherSubjectGrade(GradeId, null, null)]);

        public Task<TeacherScope> GetScopeAsync(Guid teacherId, CancellationToken cancellationToken = default)
        {
            RequestedTeacherIds.Add(teacherId);
            return Task.FromResult(Result);
        }
    }

    private sealed class CapturingListHandler : IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]>
    {
        public ListAssignmentsQuery? LastQuery { get; private set; }

        public Task<AssignmentSummaryDto[]> HandleAsync(ListAssignmentsQuery query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(Array.Empty<AssignmentSummaryDto>());
        }
    }

    /// <summary>
    /// The bearer scheme, authenticating per request from headers so ONE host can act as any
    /// principal: <c>x-test-roles</c> comma-separated role names, <c>x-test-teacher-id</c>, and
    /// <c>x-test-anonymous: 1</c> for an unauthenticated caller.
    /// </summary>
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

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "test-user") };
            foreach (var role in Request.Headers["x-test-roles"].ToString()
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            if (Request.Headers.TryGetValue("x-test-teacher-id", out var teacherId) && Guid.TryParse(teacherId, out var parsed))
            {
                claims.Add(new Claim("teacher_id", parsed.ToString()));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private static async Task<WebApplication> StartHostAsync(
        bool disableOidcAuth,
        Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags(disableOidcAuth));
        builder.Services.AddAuthentication(AuthTenancyExtensions.BearerScheme)
            .AddScheme<AuthenticationSchemeOptions, HeaderPrincipalAuthHandler>(
                AuthTenancyExtensions.BearerScheme, _ => { })
            // D19/D7 (portal-session-adoption): the reader policy names the GATEWAY scheme, and a
            // policy-named scheme without a handler is a 500 — never a 401 — so this host registers
            // the gateway exactly as Program.cs does, in BOTH flag states, over a stubbed claims
            // port (the moved handler's required ctor dependency; an unregistered dependency is a
            // DI failure, not a 401). Bearer callers keep authenticating through the pinned
            // fallback, unchanged.
            .AddPortalSessionAuthentication(AuthTenancyExtensions.BearerScheme);
        builder.Services.AddSingleton<IPortalSessionClaimsReader>(new StubPortalSessionClaimsReader());
        builder.Services.AddAuthorization();
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app, string? roles = null, bool anonymous = false)
    {
        var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();
        if (roles is not null)
        {
            client.DefaultRequestHeaders.Add("x-test-roles", roles);
        }

        if (anonymous)
        {
            client.DefaultRequestHeaders.Add("x-test-anonymous", "1");
        }

        return client;
    }

    // ── Layer 1: the endpoint-metadata matrix ─────────────────────────────

    private static RouteEndpoint Find(WebApplication app, string method, string pattern)
    {
        var wanted = Normalize(pattern);
        var match = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => Normalize(e.RoutePattern.RawText) == wanted)
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true)
            .ToList();

        match.Should().ContainSingle(
            $"exactly one {method} route should match '{wanted}' — the policy coverage test enumerates real endpoints");
        return match[0];
    }

    private static string Normalize(string? rawText) => (rawText ?? string.Empty).Trim('/');

    private static ClaimsPrincipal Principal(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "TestBearer"));

    private static ClaimsPrincipal RoleLess() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "role-less")], "TestBearer"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static async Task<bool> IsAuthorizedAsync(WebApplication app, AuthorizationPolicy policy, ClaimsPrincipal user) =>
        (await app.Services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(user, resource: null, policy: policy)).Succeeded;

    [TestMethod]
    public async Task CoveredRoutes_CarryTheDisjunctiveReaderPolicy()
    {
        await using var app = await StartHostAsync(disableOidcAuth: false);

        foreach (var (method, pattern) in CoveredRoutes)
        {
            var endpoint = Find(app, method, pattern);
            var policies = endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>();

            policies.Should().HaveCount(2,
                $"{method} /{pattern} carries the group's authenticated-user policy AND the reader policy");

            var reader = policies.Where(p => p.Requirements.OfType<RolesAuthorizationRequirement>().Any()).ToList();
            reader.Should().ContainSingle($"{method} /{pattern} must carry exactly one role-bearing policy");
            reader[0].Requirements.OfType<RolesAuthorizationRequirement>().Single().AllowedRoles.Should()
                .BeEquivalentTo(AssignmentReaderRoles,
                    $"{method} /{pattern} is the disjunctive teacher ∨ staff ∨ user-admin ∨ platform-admin policy");
            reader[0].AuthenticationSchemes.Should().Equal([PortalSessionAuthenticationHandler.GatewaySchemeName],
                "the reader policy names the portal-session gateway (D19): the single scheme registered "
                + "in every flag state — portal callers authenticate through it, bearer callers through "
                + "the pinned Bearer fallback");
        }
    }

    [TestMethod]
    public async Task CoveredRoutes_TeacherStaffAndBothAdmins_Pass()
    {
        await using var app = await StartHostAsync(disableOidcAuth: false);

        foreach (var (method, pattern) in CoveredRoutes)
        {
            var endpoint = Find(app, method, pattern);
            foreach (var role in AssignmentReaderRoles)
            {
                (await AuthorizeEndpointAsync(app, endpoint, Principal(role))).Should().BeTrue(
                    $"{method} /{pattern} must admit a caller holding '{role}'");
            }

            (await AuthorizeEndpointAsync(app, endpoint, Principal(RealmRoleNames.UserAdmin, RealmRoleNames.PlatformAdmin)))
                .Should().BeTrue("an admin-only principal is the P1-2 compatibility case the rollout must not break");
            (await AuthorizeEndpointAsync(app, endpoint, RoleLess())).Should().BeFalse(
                $"{method} /{pattern} must reject an authenticated principal with no recognised role");
            (await AuthorizeEndpointAsync(app, endpoint, Anonymous())).Should().BeFalse(
                $"{method} /{pattern} must reject an unauthenticated caller");
        }
    }

    [TestMethod]
    public async Task NotCoveredRoutes_CarryNoRoleRequirement_AndAdmitARoleLessPrincipal()
    {
        await using var app = await StartHostAsync(disableOidcAuth: false);

        foreach (var (method, pattern) in NotCoveredRoutes)
        {
            var endpoint = Find(app, method, pattern);
            var policies = endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>();

            policies.Should().HaveCount(1,
                $"{method} /{pattern} keeps only the group's authenticated-user policy");
            policies.SelectMany(p => p.Requirements).Should().NotContain(r => r is RolesAuthorizationRequirement,
                $"{method} /{pattern} is NOT covered by the reader policy ([P1-1] — a group-wide policy "
                + "would break the Families ward/guardian flows and the create wizard)");
            (await AuthorizeEndpointAsync(app, endpoint, RoleLess())).Should().BeTrue(
                $"{method} /{pattern} must still admit a role-less principal");
        }
    }

    /// <summary>Endpoint authorization as the middleware applies it: EVERY policy attached to
    /// the endpoint must pass (the reader policy is additive, not a replacement).</summary>
    private static async Task<bool> AuthorizeEndpointAsync(WebApplication app, RouteEndpoint endpoint, ClaimsPrincipal user)
    {
        var policies = endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>();
        policies.Should().NotBeEmpty("every endpoint under test is authorized in real-auth mode");

        foreach (var policy in policies)
        {
            if (!await IsAuthorizedAsync(app, policy, user))
            {
                return false;
            }
        }

        return true;
    }

    // ── Layer 2: HTTP status ─────────────────────────────────────────────

    [TestMethod]
    public async Task Http_TeacherRole_IsAdmitted_AndItsScopeRidesTheListQuery()
    {
        var listHandler = new CapturingListHandler();
        var scopeProvider = new StubScopeProvider();
        await using var app = await StartHostAsync(
            disableOidcAuth: false,
            s =>
            {
                s.AddSingleton<IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]>>(listHandler);
                s.AddSingleton<ITeacherScopeProvider>(scopeProvider);
                s.AddSingleton<ICurrentUser, StubCurrentUser>();
            });
        using var client = CreateClient(app, roles: RealmRoleNames.Teacher);

        var response = await client.GetAsync("/assignments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        scopeProvider.RequestedTeacherIds.Should().Equal([TeacherClaimId],
            "the endpoint resolves the scope from the principal's teacher_id claim");
        listHandler.LastQuery.Should().NotBeNull();
        listHandler.LastQuery!.Scope.Should().BeSameAs(scopeProvider.Result,
            "D3: the scope is resolved at the ENDPOINT and passed on the query, never fetched in a Core handler");
    }

    [TestMethod]
    public async Task Http_RoleLessPrincipal_IsForbidden_AndAnonymousIsUnauthorized()
    {
        await using var app = await StartHostAsync(
            disableOidcAuth: false,
            s =>
            {
                s.AddSingleton<IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]>>(new CapturingListHandler());
                s.AddSingleton<ITeacherScopeProvider>(new StubScopeProvider());
                s.AddSingleton<ICurrentUser, StubCurrentUser>();
            });

        using var roleLess = CreateClient(app, roles: null);
        (await roleLess.GetAsync("/assignments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = CreateClient(app, anonymous: true);
        (await anonymous.GetAsync("/assignments")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task Http_WardListAndAWrite_StillAdmitARoleLessPrincipal()
    {
        await using var app = await StartHostAsync(
            disableOidcAuth: false,
            s =>
            {
                s.AddSingleton<IQueryHandler<ListWardAssignments, WardAssignmentListItemDto[]>>(new StubWardListHandler());
                s.AddSingleton<ICommandHandler<PublishAssignmentCommand>>(new StubPublishHandler());
            });

        using var client = CreateClient(app, roles: null);

        (await client.GetAsync($"/students/{StudentId}/assignments")).StatusCode
            .Should().Be(HttpStatusCode.OK, "the Families ward list is explicitly NOT covered (P1-1)");
        (await client.PostAsync($"/assignments/{AssignmentId}/publish", content: null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent, "writes are explicitly NOT covered (P1-1)");
    }

    /// <summary>[PR — regression guard] With FEATURE:DisableOIDCAuth ON the policy block is
    /// skipped, so a covered route is no longer 401 for an unauthenticated caller. This is TRUE
    /// PRE-FIX as well (the block sits inside the same <c>if</c>), so it is a regression guard,
    /// not a discriminator.</summary>
    [TestMethod]
    public async Task Http_DisableOidcAuthOn_NoGroupRequiresAuthorization_RegressionGuard()
    {
        await using var app = await StartHostAsync(
            disableOidcAuth: true,
            s =>
            {
                s.AddSingleton<IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]>>(new CapturingListHandler());
                s.AddSingleton<ITeacherScopeProvider>(new StubScopeProvider());
                s.AddSingleton<ICurrentUser, StubCurrentUser>();
            });

        using var anonymous = CreateClient(app, anonymous: true);
        (await anonymous.GetAsync("/assignments")).StatusCode.Should().Be(
            HttpStatusCode.OK,
            "regression guard: with the flag ON the reader policy is not applied at all, which is "
            + "already true pre-fix — the flag is what the operational prerequisite gates");
    }

    [TestMethod]
    public async Task Http_OutOfScopeDirectIdReads_Are404_AndTheScopeGateIsConsulted()
    {
        var foreignId = Guid.NewGuid();
        var detailGate = new ScopeAwareDetailHandler(
        [
            new AssignmentRow(AssignmentId, TeacherClaimId, [GradeId], null),
            new AssignmentRow(foreignId, Guid.NewGuid(), [Guid.NewGuid()], null),
        ]);
        await using var app = await StartHostAsync(
            disableOidcAuth: false,
            s =>
            {
                s.AddSingleton<IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>>(detailGate);
                s.AddSingleton<IQueryHandler<ListSubmissionsByAssignment, SubmissionForReviewDto[]>>(new StubSubmissionsHandler());
                s.AddSingleton<IQueryHandler<GetSubmission, SubmissionDetailDto?>>(new StubSubmissionDetailHandler());
                s.AddSingleton<ITeacherScopeProvider>(new StubScopeProvider());
                s.AddSingleton<ICurrentUser, StubCurrentUser>();
            });
        using var client = CreateClient(app, roles: RealmRoleNames.Teacher);

        // [P2-2] the three id-addressed reads are gated by the scope-aware detail read, so an
        // out-of-scope id answers exactly like an unknown one.
        (await client.GetAsync($"/assignments/{foreignId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/assignments/{foreignId}/submissions")).StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "the per-assignment submissions read is reachable by id and must apply the same visibility rule");
        (await client.GetAsync($"/assignments/{foreignId}/students/{StudentId}/submission")).StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "the per-ward submission read is reachable by id and must apply the same visibility rule");

        detailGate.RequestedIds.Should().Contain(foreignId,
            "the gate is the scope-aware detail read, consulted with the id the caller asked for");

        // …and the in-scope id still resolves.
        (await client.GetAsync($"/assignments/{AssignmentId}/submissions")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/assignments/{AssignmentId}/students/{StudentId}/submission")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// [REWORK — closed residual] <c>GET /assignments/{id}/sign-off-statuses</c> is reachable by
    /// id and the D4 reader policy admits a <c>teacher</c>, so the read must apply the same [P2-2]
    /// visibility gate as <c>GET /{id}/submissions</c>: a teacher-only caller gets the per-ward
    /// rows for an assignment they created or whose grade they teach grade-wide, and <b>404</b> —
    /// indistinguishable from an unknown id — for one they cannot see. Staff (unrestricted) is
    /// unaffected.
    ///
    /// <para>Discriminating: pre-fix the route resolved no scope at all and passed the raw id to
    /// <c>ListSignOffStatusesQuery</c>, which answered <c>200</c> with the stub's rows for the
    /// foreign id — so the first assertion below fails.</para>
    /// </summary>
    [TestMethod]
    public async Task Http_SignOffStatuses_OutOfScopeId_Is404_AndTheInScopeIdsReadTheRows()
    {
        var ownId = Guid.NewGuid();
        var taughtGradeWideId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var foreignTeacherId = Guid.NewGuid();
        var foreignGradeId = Guid.NewGuid();

        var detailGate = new ScopeAwareDetailHandler(
        [
            // The caller's own creation, in a grade they do not teach.
            new AssignmentRow(ownId, TeacherClaimId, [foreignGradeId], null),
            // A colleague's grade-wide creation in a grade the caller teaches (TopicId null ⇒
            // the whole grade — the [P1-5] leg).
            new AssignmentRow(taughtGradeWideId, foreignTeacherId, [GradeId], null),
            // A colleague's creation in a grade the caller does not teach.
            new AssignmentRow(foreignId, foreignTeacherId, [foreignGradeId], null),
        ]);
        var signOffHandler = new StubSignOffStatusesHandler([SignOffRow()]);

        await using var app = await StartHostAsync(
            disableOidcAuth: false,
            s =>
            {
                s.AddSingleton<IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>>(detailGate);
                s.AddSingleton<IQueryHandler<ListSignOffStatusesQuery, IReadOnlyList<SignOffStatusDto>>>(signOffHandler);
                s.AddSingleton<ITeacherScopeProvider>(new StubScopeProvider());
                s.AddSingleton<ICurrentUser, StubCurrentUser>();
            });

        using var teacher = CreateClient(app, roles: RealmRoleNames.Teacher);

        var foreign = await teacher.GetAsync($"/assignments/{foreignId}/sign-off-statuses");
        foreign.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "a teacher-only caller must not read another teacher's per-ward sign-off rows by id — the "
            + "out-of-scope id answers like an unknown one, with no distinct status confirming it exists");
        detailGate.RequestedIds.Should().Contain(foreignId,
            "the route applies the [P2-2] gate rather than handing the raw id to the handler");
        signOffHandler.RequestedAssignmentIds.Should().NotContain(foreignId,
            "the rows behind an out-of-scope id are never even read");

        var own = await teacher.GetAsync($"/assignments/{ownId}/sign-off-statuses");
        own.StatusCode.Should().Be(HttpStatusCode.OK, "the caller's own creation stays readable");
        (await own.Content.ReadFromJsonAsync<List<SignOffStatusDto>>())
            .Should().ContainSingle("the in-scope read still returns the per-ward rows");

        (await teacher.GetAsync($"/assignments/{taughtGradeWideId}/sign-off-statuses")).StatusCode.Should().Be(
            HttpStatusCode.OK,
            "a grade-wide taught row ([P1-5]) is in scope, so its per-ward rows are still returned");

        using var staff = CreateClient(app, roles: RealmRoleNames.Staff);
        var staffRead = await staff.GetAsync($"/assignments/{foreignId}/sign-off-statuses");
        staffRead.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "an unrestricted (staff/admin) caller is unaffected by the gate");
        (await staffRead.Content.ReadFromJsonAsync<List<SignOffStatusDto>>()).Should().ContainSingle();
    }

    private static SignOffStatusDto SignOffRow() =>
        new(StudentId, "Ward One", null, null, null, null, SignOffStateDto.AwaitingSignature,
            null, null, Delivered: false, Opened: false, CurrentVersionNumber: 1, Score: null, Passed: null);

    private sealed class StubSignOffStatusesHandler(IReadOnlyList<SignOffStatusDto> rows)
        : IQueryHandler<ListSignOffStatusesQuery, IReadOnlyList<SignOffStatusDto>>
    {
        public List<Guid> RequestedAssignmentIds { get; } = [];

        public Task<IReadOnlyList<SignOffStatusDto>> HandleAsync(
            ListSignOffStatusesQuery query, CancellationToken ct = default)
        {
            RequestedAssignmentIds.Add(query.AssignmentId);
            return Task.FromResult(rows);
        }
    }

    /// <summary>
    /// A faithful stand-in for the [P2-2] gate. <c>GetAssignmentByIdQueryHandler</c> applies the
    /// real D3 rule (<see cref="TeacherScope.AllowsAnyTargetGrade"/>) to the row the tenant-wide
    /// cache returned and answers <c>null</c> for an out-of-scope id; this stub applies the same
    /// call to a fixed row set, so a route test observes the production rule without Postgres.
    /// The row's grade scope is its authored grade TARGETS (round <c>drop-primary-grade</c>).
    /// </summary>
    private sealed class ScopeAwareDetailHandler(IReadOnlyList<AssignmentRow> rows)
        : IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?>
    {
        private readonly Dictionary<Guid, AssignmentRow> _rows = rows.ToDictionary(r => r.Id);

        public List<Guid> RequestedIds { get; } = [];

        public Task<AssignmentSummaryDto?> HandleAsync(GetAssignmentByIdQuery query, CancellationToken ct = default)
        {
            RequestedIds.Add(query.Id);

            if (!_rows.TryGetValue(query.Id, out var row))
            {
                return Task.FromResult<AssignmentSummaryDto?>(null);
            }

            var visible = query.Scope is not { IsUnrestricted: false } scope
                || scope.AllowsAnyTargetGrade(row.CreatedByTeacherId, row.TargetGradeIds, row.TopicId);

            return Task.FromResult(visible ? Summary(row) : null);
        }

        private static AssignmentSummaryDto Summary(AssignmentRow row) => new(
            row.Id, "Scoped assignment", null, AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded,
            TargetAudienceTypeDto.AllStudents, row.TopicId ?? Guid.Empty, null,
            AssignmentStatusDto.Draft, null, null, false, row.CreatedByTeacherId,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            TargetGradeIds: row.TargetGradeIds);
    }

    private sealed record AssignmentRow(
        Guid Id, Guid CreatedByTeacherId, IReadOnlyList<Guid> TargetGradeIds, Guid? TopicId);

    private sealed class StubSubmissionsHandler : IQueryHandler<ListSubmissionsByAssignment, SubmissionForReviewDto[]>
    {
        public Task<SubmissionForReviewDto[]> HandleAsync(ListSubmissionsByAssignment query, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<SubmissionForReviewDto>());
    }

    private sealed class StubSubmissionDetailHandler : IQueryHandler<GetSubmission, SubmissionDetailDto?>
    {
        public Task<SubmissionDetailDto?> HandleAsync(GetSubmission query, CancellationToken ct = default) =>
            Task.FromResult<SubmissionDetailDto?>(new SubmissionDetailDto(
                Guid.NewGuid(), AssignmentId, StudentId, 1, ReviewStateDto.Pending, DateTimeOffset.UtcNow, [], null));
    }

    private sealed class StubWardListHandler : IQueryHandler<ListWardAssignments, WardAssignmentListItemDto[]>
    {
        public Task<WardAssignmentListItemDto[]> HandleAsync(ListWardAssignments query, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<WardAssignmentListItemDto>());
    }

    private sealed class StubPublishHandler : ICommandHandler<PublishAssignmentCommand>
    {
        public Task HandleAsync(PublishAssignmentCommand command, CancellationToken ct = default) => Task.CompletedTask;
    }
}
