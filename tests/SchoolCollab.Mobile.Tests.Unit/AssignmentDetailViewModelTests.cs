using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Tests.Unit;

/// <summary>
/// Covers <see cref="AssignmentDetailViewModel"/> — the detail screen's load (including its two
/// independently failure-degraded enrichments), the lifecycle actions' reload-after-act shape, the
/// approval decision's acting-teacher attribution and the read-only per-student submission view. The
/// degradation rule is the subtle one: a thin recipients endpoint must leave the assignment on screen,
/// not blank it, and only an assertion on both the item and the section catches a regression.
/// </summary>
[TestClass]
public class AssignmentDetailViewModelTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TeacherId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly string ItemPath = $"/assignments/{AssignmentId}";

    /// <summary>Built from <see cref="UriBuilder"/>, never typed as a literal — base addresses are
    /// configuration (D8), and the test suite is not an exception to that rule.</summary>
    private static readonly Uri BaseAddress =
        new UriBuilder(Uri.UriSchemeHttps, "assignments.example", 7199).Uri;

    [TestMethod]
    public async Task LoadAsync_LoadsTheItemAndBothEnrichments()
    {
        var fixture = CreateViewModel(Routes(requiresApproval: true, approvalStatus: ApprovalStatusDto.Pending));

        await fixture.ViewModel.LoadAsync(AssignmentId);

        fixture.ViewModel.Item.Should().NotBeNull();
        fixture.ViewModel.NotFound.Should().BeFalse();
        fixture.ViewModel.Error.Should().BeNull();
        fixture.ViewModel.Recipients.Should().ContainSingle();
        fixture.ViewModel.Submissions.Should().ContainSingle();
        fixture.Handler.Requests.Should().HaveCount(3, "the item read plus one call per enrichment.");
        fixture.Handler.Requests[0].Path.Should().Be(ItemPath);
    }

    [TestMethod]
    public async Task LoadAsync_SetsNotFound_WhenTheItemReadAnswers404()
    {
        var fixture = CreateViewModel(_ => Failure(HttpStatusCode.NotFound));

        await fixture.ViewModel.LoadAsync(AssignmentId);

        fixture.ViewModel.Item.Should().BeNull();
        fixture.ViewModel.NotFound.Should().BeTrue();
        fixture.ViewModel.Error.Should().BeNull("a 404 is an answer, not a failure.");
        fixture.Handler.Requests.Should().ContainSingle("a missing assignment has no enrichments to read.");
    }

    [TestMethod]
    public async Task LoadAsync_KeepsTheAssignment_WhenTheRecipientsReadFails()
    {
        var routes = Routes();
        var fixture = CreateViewModel(request => IsPath(request, "/recipients")
            ? Failure(HttpStatusCode.InternalServerError)
            : routes(request));

        await fixture.ViewModel.LoadAsync(AssignmentId);

        fixture.ViewModel.Item.Should().NotBeNull("a failed enrichment must never blank the assignment.");
        fixture.ViewModel.Recipients.Should().BeNull();
        fixture.ViewModel.Submissions.Should().ContainSingle("the second enrichment is independent of the first.");
        fixture.ViewModel.Error.Should().BeNull("the page-level error stays clear — the section degrades instead.");
        fixture.ViewModel.NotFound.Should().BeFalse();
    }

    [TestMethod]
    public async Task PublishAsync_ReloadsTheAssignmentAfterTheCall()
    {
        var routes = Routes();
        var itemReads = 0;
        var fixture = CreateViewModel(request =>
        {
            if (request.Method != HttpMethod.Get)
            {
                return Json(string.Empty);
            }

            if (IsPath(request, ItemPath))
            {
                itemReads++;
                return Json(SummaryJson(
                    itemReads == 1 ? AssignmentStatusDto.Draft : AssignmentStatusDto.Published));
            }

            return routes(request);
        });

        await fixture.ViewModel.LoadAsync(AssignmentId);
        fixture.ViewModel.Item!.Status.Should().Be(AssignmentStatusDto.Draft);

        await fixture.ViewModel.PublishAsync();

        fixture.ViewModel.Item!.Status.Should().Be(AssignmentStatusDto.Published,
            "the screen re-reads the assignment after a lifecycle action instead of guessing the new state.");
        fixture.ViewModel.Error.Should().BeNull();
        fixture.Handler.Requests.Last().Path.Should().Be(ItemPath);
        fixture.Handler.Requests.Should().Contain(request => request.Path == $"/assignments/{AssignmentId}/publish");
    }

    [TestMethod]
    public async Task SubmitApprovalDecisionAsync_ApprovesWithTheSessionsTeacherClaim_AndReloads()
    {
        var routes = Routes(requiresApproval: true, approvalStatus: ApprovalStatusDto.Pending);
        var approved = false;
        var fixture = CreateViewModel(
            request =>
            {
                if (request.Method != HttpMethod.Get)
                {
                    approved = true;
                    return Json(string.Empty);
                }

                return IsPath(request, ItemPath)
                    ? Json(SummaryJson(
                        AssignmentStatusDto.Draft,
                        requiresApproval: true,
                        approvalStatus: approved ? ApprovalStatusDto.Approved : ApprovalStatusDto.Pending))
                    : routes(request);
            },
            new Claim("teacher_id", TeacherId.ToString()));

        await fixture.ViewModel.LoadAsync(AssignmentId);
        await fixture.ViewModel.SubmitApprovalDecisionAsync(approve: true);

        var approval = fixture.Handler.Requests.Last(request => request.Method == HttpMethod.Post);
        approval.Path.Should().Be($"/assignments/{AssignmentId}/approve");
        approval.Body.Should().Be($"{{\"approverId\":\"{TeacherId}\"}}",
            "the acting teacher comes from the session's teacher_id claim — the same claim the realm's mappers emit.");
        fixture.ViewModel.Item!.ApprovalStatus.Should().Be(ApprovalStatusDto.Approved);
        fixture.ViewModel.ApprovalError.Should().BeNull();
    }

    [TestMethod]
    public async Task SubmitApprovalDecisionAsync_RejectsWhenAsked()
    {
        var fixture = CreateViewModel(
            Routes(requiresApproval: true, approvalStatus: ApprovalStatusDto.Pending),
            new Claim("teacher_id", TeacherId.ToString()));

        await fixture.ViewModel.LoadAsync(AssignmentId);
        await fixture.ViewModel.SubmitApprovalDecisionAsync(approve: false);

        fixture.Handler.Requests.Last(request => request.Method == HttpMethod.Post).Path
            .Should().Be($"/assignments/{AssignmentId}/reject");
    }

    [TestMethod]
    public async Task SubmitApprovalDecisionAsync_UsesAnEmptyGuid_WhenTheSessionCarriesNoTeacherClaim()
    {
        var fixture = CreateViewModel(Routes(requiresApproval: true, approvalStatus: ApprovalStatusDto.Pending));

        await fixture.ViewModel.LoadAsync(AssignmentId);
        await fixture.ViewModel.SubmitApprovalDecisionAsync(approve: true);

        fixture.Handler.Requests.Last(request => request.Method == HttpMethod.Post).Body
            .Should().Be("{\"approverId\":\"00000000-0000-0000-0000-000000000000\"}",
                "no claim means the dev/TestAuth fallback — a real principal always wins server-side.");
    }

    [TestMethod]
    public async Task SubmitApprovalDecisionAsync_ReportsFailureOnTheApprovalSurface_AndKeepsThePage()
    {
        var routes = Routes(requiresApproval: true, approvalStatus: ApprovalStatusDto.Pending);
        var fixture = CreateViewModel(
            request => request.Method == HttpMethod.Post ? Failure(HttpStatusCode.InternalServerError) : routes(request),
            new Claim("teacher_id", TeacherId.ToString()));

        await fixture.ViewModel.LoadAsync(AssignmentId);
        await fixture.ViewModel.SubmitApprovalDecisionAsync(approve: true);

        fixture.ViewModel.ApprovalError.Should().NotBeNullOrEmpty();
        fixture.ViewModel.Error.Should().BeNull("an approval failure is the panel's to report, not the page's.");
        fixture.ViewModel.Item.Should().NotBeNull();
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [TestMethod]
    public async Task SelectSubmissionAsync_LoadsTheStudentSubmission_AndClearReturnsToList()
    {
        var fixture = CreateViewModel(Routes());

        await fixture.ViewModel.LoadAsync(AssignmentId);
        fixture.ViewModel.Submissions.Should().NotBeNull();

        await fixture.ViewModel.SelectSubmissionAsync(StudentId);

        fixture.ViewModel.SelectedSubmission.Should().NotBeNull();
        fixture.ViewModel.SelectedSubmission!.StudentId.Should().Be(StudentId);
        fixture.ViewModel.SubmissionError.Should().BeNull();
        fixture.Handler.Requests.Last(request => request.Method == HttpMethod.Get).Path
            .Should().Be($"/assignments/{AssignmentId}/students/{StudentId}/submission");

        fixture.ViewModel.ClearSelectedSubmission();

        fixture.ViewModel.SelectedSubmission.Should().BeNull();
    }

    [TestMethod]
    public async Task ReloadAsync_KeepsTheLoadedItem_WhenTheReReadFails()
    {
        var routes = Routes();
        var itemReads = 0;
        var fixture = CreateViewModel(request =>
        {
            if (!IsPath(request, ItemPath))
            {
                return routes(request);
            }

            itemReads++;
            return itemReads == 1
                ? Json(SummaryJson(AssignmentStatusDto.Draft))
                : Failure(HttpStatusCode.InternalServerError);
        });

        await fixture.ViewModel.LoadAsync(AssignmentId);
        await fixture.ViewModel.ReloadAsync();

        fixture.ViewModel.Item.Should().NotBeNull();
        fixture.ViewModel.Item!.Status.Should().Be(AssignmentStatusDto.Draft,
            "a failed reload keeps what the screen already renders.");
        fixture.ViewModel.Error.Should().BeNull();
    }

    [TestMethod]
    public async Task LoadAsync_ClearsTheErrorOfTheFailedLoadItSupersedes()
    {
        var routes = Routes();
        var itemReads = 0;
        var fixture = CreateViewModel(request =>
        {
            if (!IsPath(request, ItemPath))
            {
                return routes(request);
            }

            itemReads++;
            return itemReads == 1
                ? Failure(HttpStatusCode.InternalServerError)
                : Json(SummaryJson(AssignmentStatusDto.Draft));
        });

        await fixture.ViewModel.LoadAsync(AssignmentId);
        fixture.ViewModel.Error.Should().NotBeNullOrEmpty();
        fixture.ViewModel.Item.Should().BeNull();

        await fixture.ViewModel.LoadAsync(AssignmentId);

        fixture.ViewModel.Error.Should().BeNull(
            "a stale message outliving the state it described is what kept a reloaded screen behind its "
            + "error branch.");
        fixture.ViewModel.Item.Should().NotBeNull();
        fixture.ViewModel.NotFound.Should().BeFalse();
    }

    [TestMethod]
    public async Task PublishAsync_ReportsAFailedAction_WithoutCollapsingTheAssignment()
    {
        var routes = Routes();
        var fixture = CreateViewModel(request => request.Method == HttpMethod.Post
            ? Failure(HttpStatusCode.InternalServerError)
            : routes(request));

        await fixture.ViewModel.LoadAsync(AssignmentId);
        await fixture.ViewModel.PublishAsync();

        fixture.ViewModel.Item.Should().NotBeNull("a failed action must never take the assignment off the screen.");
        fixture.ViewModel.ActionError.Should().NotBeNullOrEmpty();
        fixture.ViewModel.ActionErrorTitle.Should().Be("Could not publish",
            "the bar names the action that failed.");
        fixture.ViewModel.Error.Should().BeNull(
            "the error branch replaces the screen, so it stays reserved for the assignment read.");
        fixture.ViewModel.IsBusy.Should().BeFalse();
    }

    [TestMethod]
    public async Task PublishAsync_SurfacesANonBlockingWarning_WhenThePostActionReloadFails()
    {
        var routes = Routes();
        var itemReads = 0;
        var fixture = CreateViewModel(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(string.Empty);
            }

            if (!IsPath(request, ItemPath))
            {
                return routes(request);
            }

            itemReads++;
            return itemReads == 1
                ? Json(SummaryJson(AssignmentStatusDto.Draft))
                : Failure(HttpStatusCode.InternalServerError);
        });

        await fixture.ViewModel.LoadAsync(AssignmentId);
        await fixture.ViewModel.PublishAsync();

        fixture.ViewModel.Item!.Status.Should().Be(AssignmentStatusDto.Draft,
            "a failed re-read keeps what the screen already renders.");
        fixture.ViewModel.ReloadWarning.Should().NotBeNullOrEmpty(
            "the action was accepted server-side, so the screen must say it is showing pre-action state "
            + "instead of diverging silently.");
        fixture.ViewModel.ActionError.Should().BeNull("the action itself succeeded — only the re-read failed.");
        fixture.ViewModel.Error.Should().BeNull();
    }

    [TestMethod]
    public async Task ReloadAsync_ClearsTheWarning_WhenTheReReadSucceeds()
    {
        var routes = Routes();
        var itemReads = 0;
        var fixture = CreateViewModel(request =>
        {
            if (!IsPath(request, ItemPath))
            {
                return routes(request);
            }

            itemReads++;
            return itemReads == 2
                ? Failure(HttpStatusCode.InternalServerError)
                : Json(SummaryJson(AssignmentStatusDto.Published));
        });

        await fixture.ViewModel.LoadAsync(AssignmentId);
        await fixture.ViewModel.ReloadAsync();
        fixture.ViewModel.ReloadWarning.Should().NotBeNullOrEmpty();

        await fixture.ViewModel.ReloadAsync();

        fixture.ViewModel.ReloadWarning.Should().BeNull("the re-read that succeeded cleared the divergence.");
    }

    [TestMethod]
    public async Task LoadAsync_AllowsTheApprovalDecision_WhenTheSessionCarriesATeacherId()
    {
        var fixture = CreateViewModel(
            Routes(requiresApproval: true, approvalStatus: ApprovalStatusDto.Pending),
            new Claim("teacher_id", TeacherId.ToString()));

        await fixture.ViewModel.LoadAsync(AssignmentId);

        fixture.ViewModel.CanDecideApproval.Should().BeTrue(
            "the decision is attributed to the session's teacher_id, which this session has.");
    }

    [TestMethod]
    public async Task LoadAsync_WithholdsTheApprovalDecision_WhenTheSessionCarriesNoTeacherId()
    {
        var fixture = CreateViewModel(Routes(requiresApproval: true, approvalStatus: ApprovalStatusDto.Pending));

        await fixture.ViewModel.LoadAsync(AssignmentId);

        fixture.ViewModel.CanDecideApproval.Should().BeFalse(
            "approving would attribute the decision to Guid.Empty, which the server rejects — so the screen "
            + "must not offer the button.");
        fixture.ViewModel.Item.Should().NotBeNull("the assignment itself still loads and renders.");
    }

    /// <summary>
    /// One fixture that serves the item, its two enrichments and the per-student read by path — so a
    /// test only has to override the route it is actually about.
    /// </summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Routes(
        AssignmentStatusDto status = AssignmentStatusDto.Published,
        bool requiresApproval = false,
        ApprovalStatusDto? approvalStatus = null) =>
        request =>
        {
            if (IsPath(request, "/recipients"))
            {
                return Json(RecipientsJson());
            }

            if (IsPath(request, "/submissions"))
            {
                return Json(SubmissionsJson());
            }

            if (request.RequestUri!.AbsolutePath.Contains("/students/", StringComparison.Ordinal))
            {
                return Json(SubmissionDetailJson());
            }

            return Json(SummaryJson(status, requiresApproval, approvalStatus));
        };

    private static bool IsPath(HttpRequestMessage request, string suffix) =>
        request.RequestUri!.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal);

    private static ViewModelFixture CreateViewModel(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        params Claim[] claims)
    {
        var handler = new ScriptedHandler(respond);
        var client = new MobileAssignmentsApiClient(
            new HttpClient(handler) { BaseAddress = BaseAddress },
            NullLogger<MobileAssignmentsApiClient>.Instance);

        var viewModel = new AssignmentDetailViewModel(
            client,
            new StubAuthenticationStateProvider(claims),
            NullLogger<AssignmentDetailViewModel>.Instance);

        return new ViewModelFixture(viewModel, handler);
    }

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Failure(HttpStatusCode status) => new(status)
    {
        Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
    };

    private static string SummaryJson(
        AssignmentStatusDto status,
        bool requiresApproval = false,
        ApprovalStatusDto? approvalStatus = null) =>
        "{" +
        $"\"id\":\"{AssignmentId}\"," +
        "\"title\":\"Fractions quiz\"," +
        "\"description\":\"Equivalent fractions.\"," +
        "\"assignmentType\":\"digital\"," +
        "\"gradingFormat\":\"teacherGraded\"," +
        "\"targetAudienceType\":\"allStudents\"," +
        $"\"topicId\":\"{Guid.Empty}\"," +
        "\"topicName\":null," +
        $"\"status\":\"{status}\"," +
        "\"dueDate\":null," +
        "\"maxScore\":25," +
        "\"mandatoryReview\":false," +
        $"\"createdByTeacherId\":\"{Guid.Empty}\"," +
        "\"createdAt\":\"2026-10-13T00:00:00+00:00\"," +
        "\"updatedAt\":\"2026-10-13T00:00:00+00:00\"," +
        $"\"requiresApproval\":{requiresApproval.ToString().ToLowerInvariant()}," +
        $"\"approvalStatus\":{(approvalStatus is null ? "null" : $"\"{approvalStatus}\"")}" +
        "}";

    private static string RecipientsJson() =>
        "[" +
        "{" +
        $"\"id\":\"{Guid.NewGuid()}\"," +
        $"\"assignmentId\":\"{AssignmentId}\"," +
        "\"ownerType\":\"guardian\"," +
        $"\"ownerId\":\"{Guid.NewGuid()}\"," +
        "\"wardStudentId\":null," +
        $"\"contactId\":\"{Guid.NewGuid()}\"," +
        "\"channel\":\"email\"," +
        "\"role\":\"primary\"," +
        "\"notifyOnBroadcast\":true," +
        "\"subscriptionActive\":true" +
        "}" +
        "]";

    private static string SubmissionsJson() =>
        "[" +
        "{" +
        $"\"submissionId\":\"{Guid.NewGuid()}\"," +
        $"\"assignmentId\":\"{AssignmentId}\"," +
        "\"assignmentTitle\":\"Fractions quiz\"," +
        $"\"studentId\":\"{StudentId}\"," +
        "\"currentVersionNumber\":1," +
        "\"reviewState\":\"pending\"," +
        "\"lastSubmittedAt\":\"2026-10-13T09:00:00+00:00\"" +
        "}" +
        "]";

    private static string SubmissionDetailJson() =>
        "{" +
        $"\"submissionId\":\"{Guid.NewGuid()}\"," +
        $"\"assignmentId\":\"{AssignmentId}\"," +
        $"\"studentId\":\"{StudentId}\"," +
        "\"currentVersionNumber\":1," +
        "\"reviewState\":\"reviewed\"," +
        "\"lastSubmittedAt\":\"2026-10-13T09:00:00+00:00\"," +
        "\"versions\":[" +
        "{" +
        $"\"id\":\"{Guid.NewGuid()}\"," +
        "\"versionNumber\":1," +
        "\"source\":\"student\"," +
        "\"content\":\"12/16\"," +
        "\"submittedByGuardianId\":null," +
        "\"submittedAt\":\"2026-10-13T09:00:00+00:00\"," +
        "\"score\":20," +
        "\"passed\":true" +
        "}" +
        "]," +
        "\"review\":null," +
        "\"signOffState\":\"none\"" +
        "}";

    private sealed record ViewModelFixture(AssignmentDetailViewModel ViewModel, ScriptedHandler Handler);

    /// <summary>The session as the view model sees it: a principal whose claims are exactly what the
    /// test supplied (no claim at all is the no-teacher-claim case).</summary>
    private sealed class StubAuthenticationStateProvider(params Claim[] claims) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
    }

    /// <summary>A scripted <see cref="HttpMessageHandler"/> (the repo's HTTP-testing seam — never a
    /// mocking library): it records every request and answers with the test's own factory.</summary>
    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery, body));

            return respond(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Body);
}
