using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Tests.Unit;

/// <summary>
/// Covers <see cref="MobileAssignmentsApiClient"/> — the slice-scoped, twelve-method client the mobile
/// assignments screens call. Three contracts are pinned per method: the exact <b>route and verb</b> the
/// server-side client uses (a second code path for the same wire shapes is only safe while it is
/// mirrored, not invented), the <b>body</b> shape the API's commands bind, and the <b>string-enum
/// tolerance</b> the API host's converter block requires — a missing converter makes every read of that
/// payload throw rather than degrade, so the round-trip is asserted, not assumed.
/// </summary>
[TestClass]
public class MobileAssignmentsApiClientTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ApproverId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>The client's base address in these tests is built from <see cref="UriBuilder"/>, never
    /// typed as a literal — the D8 rule (base addresses are configuration) has no exception for a test.</summary>
    private static readonly Uri BaseAddress =
        new UriBuilder(Uri.UriSchemeHttps, "assignments.example", 7199).Uri;

    [TestMethod]
    public async Task ListAsync_SendsAGetToTheAssignmentsRoute()
    {
        var handler = new StubHandler(payload: "[]");

        var result = await CreateClient(handler).ListAsync();

        result.Should().NotBeNull();
        result!.Should().BeEmpty();
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Path.Should().Be("/assignments");
    }

    [TestMethod]
    public async Task ListAsync_AppendsTheStatusAsItsEnumName()
    {
        var handler = new StubHandler(payload: "[]");

        await CreateClient(handler).ListAsync(AssignmentStatusDto.Published);

        handler.Requests.Should().ContainSingle().Subject.Path.Should().Be("/assignments?status=Published");
    }

    [TestMethod]
    public async Task ListAsync_ReadsTheStringEnumNamesTheApiHostEmits()
    {
        var handler = new StubHandler(payload: SummaryJson(AssignmentStatusDto.Published));

        var result = await CreateClient(handler).ListAsync();

        var assignment = result.Should().ContainSingle().Subject;
        assignment.Status.Should().Be(AssignmentStatusDto.Published,
            "the API host registers AssignmentStatusDto as a name, so the client's converter set is what makes this read possible.");
        assignment.AssignmentType.Should().Be(AssignmentTypeDto.SemiManual);
        assignment.GradingFormat.Should().Be(GradingFormatDto.AutoGraded);
        assignment.TargetAudienceType.Should().Be(TargetAudienceTypeDto.SelectedGrades);
        assignment.MaxScore.Should().Be(25m);
    }

    [TestMethod]
    public async Task GetByIdAsync_SendsAGetToTheAssignmentRoute()
    {
        var handler = new StubHandler(payload: SummaryObjectJson(AssignmentStatusDto.Draft));

        var result = await CreateClient(handler).GetByIdAsync(AssignmentId);

        result!.Id.Should().Be(AssignmentId);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Path.Should().Be($"/assignments/{AssignmentId}");
    }

    [TestMethod]
    public async Task GetByIdAsync_ReturnsNullWhenTheApiAnswers404()
    {
        var handler = new StubHandler(status: HttpStatusCode.NotFound);

        var result = await CreateClient(handler).GetByIdAsync(AssignmentId);

        result.Should().BeNull("a deleted or out-of-scope assignment is an expected answer, not a failure.");
    }

    [TestMethod]
    public async Task PublishAsync_PostsToThePublishRouteWithANullBody()
    {
        var handler = new StubHandler();

        await CreateClient(handler).PublishAsync(AssignmentId);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be($"/assignments/{AssignmentId}/publish");
        request.Body.Should().Be("null",
            "the server client posts a nullable PublishAssignmentRequest, and the command binds null as \"all contacts\".");
    }

    [TestMethod]
    public async Task UnpublishAsync_PostsToTheUnpublishRoute()
    {
        var handler = new StubHandler();

        await CreateClient(handler).UnpublishAsync(AssignmentId);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be($"/assignments/{AssignmentId}/unpublish");
    }

    [TestMethod]
    public async Task CloseAsync_PostsToTheCloseRoute()
    {
        var handler = new StubHandler();

        await CreateClient(handler).CloseAsync(AssignmentId);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be($"/assignments/{AssignmentId}/close");
    }

    [TestMethod]
    public async Task SubmitForApprovalAsync_PostsToTheSubmitForApprovalRoute()
    {
        var handler = new StubHandler();

        await CreateClient(handler).SubmitForApprovalAsync(AssignmentId);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be($"/assignments/{AssignmentId}/submit-for-approval");
    }

    [TestMethod]
    public async Task ApproveAsync_PostsTheApproverToTheApproveRoute()
    {
        var handler = new StubHandler();

        await CreateClient(handler).ApproveAsync(AssignmentId, ApproverId);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be($"/assignments/{AssignmentId}/approve");
        request.Body.Should().Be($"{{\"approverId\":\"{ApproverId}\"}}");
    }

    [TestMethod]
    public async Task RejectAsync_PostsTheApproverToTheRejectRoute()
    {
        var handler = new StubHandler();

        await CreateClient(handler).RejectAsync(AssignmentId, ApproverId);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be($"/assignments/{AssignmentId}/reject");
        request.Body.Should().Be($"{{\"approverId\":\"{ApproverId}\"}}");
    }

    [TestMethod]
    public async Task DeleteAsync_SendsADeleteToTheAssignmentRoute()
    {
        var handler = new StubHandler();

        await CreateClient(handler).DeleteAsync(AssignmentId);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Delete);
        request.Path.Should().Be($"/assignments/{AssignmentId}");
    }

    [TestMethod]
    public async Task GetRecipientsAsync_ReadsTheRecipientsRouteWithTheApiEnumNames()
    {
        var handler = new StubHandler(payload: RecipientJson());

        var result = await CreateClient(handler).GetRecipientsAsync(AssignmentId);

        var recipient = result.Should().ContainSingle().Subject;
        recipient.OwnerType.Should().Be(ContactOwnerTypeDto.Guardian);
        recipient.Channel.Should().Be(ContactChannelDto.Email);
        recipient.Role.Should().Be(GuardianRoleDto.Primary);
        recipient.SubscriptionActive.Should().BeTrue();
        handler.Requests.Should().ContainSingle().Subject.Path.Should().Be($"/assignments/{AssignmentId}/recipients");
    }

    [TestMethod]
    public async Task ListSubmissionsAsync_ReadsTheSubmissionsRoute()
    {
        var handler = new StubHandler(payload: SubmissionsJson());

        var result = await CreateClient(handler).ListSubmissionsAsync(AssignmentId);

        var submission = result.Should().ContainSingle().Subject;
        submission.StudentId.Should().Be(StudentId);
        submission.ReviewState.Should().Be(ReviewStateDto.Graded);
        handler.Requests.Should().ContainSingle().Subject.Path.Should().Be($"/assignments/{AssignmentId}/submissions");
    }

    [TestMethod]
    public async Task GetSubmissionAsync_ReadsTheStudentSubmissionRoute_AndItsSignOffStateName()
    {
        var handler = new StubHandler(payload: SubmissionDetailJson());

        var result = await CreateClient(handler).GetSubmissionAsync(AssignmentId, StudentId);

        result.Should().NotBeNull();
        var submission = result!;
        submission.ReviewState.Should().Be(ReviewStateDto.Reviewed);
        submission.SignOffState.Should().Be(SignOffStateDto.AwaitingSignature,
            "SubmissionDetailDto.SignOffState is non-nullable and the host emits it as a name, so its converter is required for every read of this route.");
        submission.Versions.Should().ContainSingle().Subject.Source.Should().Be(SubmissionSourceDto.GuardianOnBehalf);
        handler.Requests.Should().ContainSingle().Subject.Path
            .Should().Be($"/assignments/{AssignmentId}/students/{StudentId}/submission");
    }

    [TestMethod]
    public async Task GetSubmissionAsync_ReturnsNullWhenTheApiAnswers404()
    {
        var handler = new StubHandler(status: HttpStatusCode.NotFound);

        var result = await CreateClient(handler).GetSubmissionAsync(AssignmentId, StudentId);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task PublishAsync_ThrowsWhenTheApiRejectsTheCall()
    {
        var handler = new StubHandler(status: HttpStatusCode.InternalServerError);

        var publish = () => CreateClient(handler).PublishAsync(AssignmentId);

        await publish.Should().ThrowAsync<HttpRequestException>(
            "a failed lifecycle call must surface, so the view model can roll its optimistic row back.");
    }

    private static MobileAssignmentsApiClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = BaseAddress }, NullLogger<MobileAssignmentsApiClient>.Instance);

    private static string SummaryJson(AssignmentStatusDto status) => $"[{SummaryObjectJson(status)}]";

    private static string SummaryObjectJson(AssignmentStatusDto status) =>
        "{" +
        $"\"id\":\"{AssignmentId}\"," +
        "\"title\":\"Fractions quiz\"," +
        "\"description\":null," +
        "\"assignmentType\":\"semiManual\"," +
        "\"gradingFormat\":\"autoGraded\"," +
        "\"targetAudienceType\":\"selectedGrades\"," +
        $"\"topicId\":\"{Guid.Empty}\"," +
        "\"topicName\":null," +
        $"\"status\":\"{status}\"," +
        "\"dueDate\":null," +
        "\"maxScore\":25," +
        "\"mandatoryReview\":false," +
        $"\"createdByTeacherId\":\"{Guid.Empty}\"," +
        "\"createdAt\":\"2026-10-13T00:00:00+00:00\"," +
        "\"updatedAt\":\"2026-10-13T00:00:00+00:00\"" +
        "}";

    private static string RecipientJson() =>
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
        "\"currentVersionNumber\":2," +
        "\"reviewState\":\"graded\"," +
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
        "\"source\":\"guardianOnBehalf\"," +
        "\"content\":\"12/16\"," +
        "\"submittedByGuardianId\":null," +
        "\"submittedAt\":\"2026-10-13T09:00:00+00:00\"," +
        "\"score\":20," +
        "\"passed\":true" +
        "}" +
        "]," +
        "\"review\":null," +
        "\"signOffState\":\"awaitingSignature\"" +
        "}";

    /// <summary>The far end of the client's pipeline: records the method, the path+query and the body,
    /// and answers with the configured payload or status (a scripted handler, never a mocking library).</summary>
    private sealed class StubHandler(string? payload = null, HttpStatusCode status = HttpStatusCode.OK)
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

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(payload ?? string.Empty, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Body);
}
