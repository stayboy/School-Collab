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
/// Covers <see cref="AssignmentsListViewModel"/> — the list screen's load, search, status filter and
/// optimistic lifecycle behaviour, moved out of the desktop page's code-behind so it can be tested on
/// ubuntu without a device. The optimistic path is the reason this suite exists: a flip that is not
/// rolled back on failure shows a status the server never accepted, and only an assertion on the row
/// (not on the request) catches that.
/// </summary>
[TestClass]
public class AssignmentsListViewModelTests
{
    private static readonly Guid DraftId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PublishedId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Built from <see cref="UriBuilder"/>, never typed as a literal — base addresses are
    /// configuration (D8), and the test suite is not an exception to that rule.</summary>
    private static readonly Uri BaseAddress =
        new UriBuilder(Uri.UriSchemeHttps, "assignments.example", 7199).Uri;

    [TestMethod]
    public async Task LoadAsync_MapsTheLoadedRowsOntoItems()
    {
        var fixture = CreateViewModel(request => Json(SummariesJson(
            SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft),
            SummaryJson(PublishedId, "Decimals quiz", AssignmentStatusDto.Published))));

        await fixture.ViewModel.LoadAsync();

        fixture.ViewModel.Items.Should().HaveCount(2);
        fixture.ViewModel.DisplayItems.Should().BeSameAs(fixture.ViewModel.Items,
            "with no search active the screen renders the loaded list itself.");
        fixture.ViewModel.Error.Should().BeNull();
        fixture.ViewModel.IsLoading.Should().BeFalse();
    }

    [TestMethod]
    public async Task LoadAsync_IsASilentNoOpWhenTheRequestIsCancelled()
    {
        var fixture = CreateViewModel(request => Json(SummariesJson(
            SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft))));

        await fixture.ViewModel.LoadAsync(new CancellationToken(canceled: true));

        fixture.ViewModel.Items.Should().BeNull("the send was cancelled before it produced anything.");
        fixture.ViewModel.Error.Should().BeNull("a cancellation is navigation away, not a failure to report.");
        fixture.ViewModel.IsLoading.Should().BeFalse();
    }

    [TestMethod]
    public async Task SetStatusFilterAsync_ReloadsWithTheStatusQuery()
    {
        var fixture = CreateViewModel(request => Json(SummariesJson(
            SummaryJson(PublishedId, "Decimals quiz", AssignmentStatusDto.Published))));

        await fixture.ViewModel.SetStatusFilterAsync(AssignmentStatusDto.Published);

        fixture.ViewModel.StatusFilter.Should().Be(AssignmentStatusDto.Published);
        var request = fixture.Handler.Requests.Should().ContainSingle().Subject;
        request.Path.Should().Be("/assignments?status=Published");
        fixture.ViewModel.Items.Should().ContainSingle().Subject.Status.Should().Be(AssignmentStatusDto.Published);
    }

    [TestMethod]
    public async Task SearchAsync_FiltersTitlesCaseInsensitively()
    {
        var fixture = CreateViewModel(request => Json(SummariesJson(
            SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft),
            SummaryJson(PublishedId, "Decimals quiz", AssignmentStatusDto.Published))));

        await fixture.ViewModel.SearchAsync("FRACTIONS");

        fixture.ViewModel.IsSearchActive.Should().BeTrue();
        fixture.ViewModel.SearchResults.Should().ContainSingle().Subject.Id.Should().Be(DraftId);
        fixture.ViewModel.DisplayItems.Should().BeSameAs(fixture.ViewModel.SearchResults);
        fixture.Handler.Requests.Should().ContainSingle().Subject.Path.Should().Be(
            "/assignments", "search fetches all statuses and narrows client-side, exactly like the desktop page.");
    }

    [TestMethod]
    public async Task SearchAsync_WithEmptyText_ClearsTheResults()
    {
        var fixture = CreateViewModel(request => Json(SummariesJson(
            SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft))));

        await fixture.ViewModel.LoadAsync();
        await fixture.ViewModel.SearchAsync("fractions");
        await fixture.ViewModel.SearchAsync("   ");

        fixture.ViewModel.SearchText.Should().BeEmpty();
        fixture.ViewModel.SearchResults.Should().BeNull();
        fixture.ViewModel.DisplayItems.Should().BeSameAs(fixture.ViewModel.Items);
    }

    [TestMethod]
    public async Task PublishAsync_FlipsTheLoadedRowToPublished()
    {
        var fixture = CreateViewModel(request => request.Method == HttpMethod.Post
            ? Ok()
            : Json(SummariesJson(SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft))));

        await fixture.ViewModel.LoadAsync();
        await fixture.ViewModel.PublishAsync(DraftId);

        fixture.ViewModel.Items.Should().ContainSingle().Subject.Status.Should().Be(AssignmentStatusDto.Published);
        fixture.ViewModel.Error.Should().BeNull();
        fixture.Handler.Requests.Should().HaveCount(2);
        fixture.Handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        fixture.Handler.Requests[1].Path.Should().Be($"/assignments/{DraftId}/publish");
    }

    [TestMethod]
    public async Task SubmitForApprovalAsync_FlipsTheRowsApprovalStatusToPending()
    {
        var fixture = CreateViewModel(request => request.Method == HttpMethod.Post
            ? Ok()
            : Json(SummariesJson(
                SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft, requiresApproval: true))));

        await fixture.ViewModel.LoadAsync();
        await fixture.ViewModel.SubmitForApprovalAsync(DraftId);

        fixture.ViewModel.Items.Should().ContainSingle().Subject.ApprovalStatus.Should().Be(ApprovalStatusDto.Pending);
        fixture.Handler.Requests[1].Path.Should().Be($"/assignments/{DraftId}/submit-for-approval");
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesTheRowOnSuccess()
    {
        var fixture = CreateViewModel(request => request.Method == HttpMethod.Delete
            ? Ok()
            : Json(SummariesJson(
                SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft),
                SummaryJson(PublishedId, "Decimals quiz", AssignmentStatusDto.Published))));

        await fixture.ViewModel.LoadAsync();
        await fixture.ViewModel.DeleteAsync(DraftId);

        fixture.ViewModel.Items.Should().ContainSingle().Subject.Id.Should().Be(PublishedId);
        fixture.ViewModel.Error.Should().BeNull();
        fixture.Handler.Requests.Should().HaveCount(2);
        fixture.Handler.Requests[1].Method.Should().Be(HttpMethod.Delete);
        fixture.Handler.Requests[1].Path.Should().Be($"/assignments/{DraftId}");
    }

    [TestMethod]
    public async Task PublishAsync_DoesNothingWhenNoRowIsLoaded()
    {
        var fixture = CreateViewModel(request => Json("[]"));

        await fixture.ViewModel.PublishAsync(DraftId);

        fixture.Handler.Requests.Should().BeEmpty("there is no row to flip, so no call is made.");
    }

    [TestMethod]
    public async Task LoadAsync_KeepsTheSpinnerOn_WhenANewerLoadSupersedesTheOldOne()
    {
        var firstStarted = new TaskCompletionSource();
        var secondStarted = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        var releaseSecond = new TaskCompletionSource();

        var fixture = CreateViewModel(
            request => Json(SummariesJson(SummaryJson(PublishedId, "Decimals quiz", AssignmentStatusDto.Published))),
            beforeRespond: async (index, cancellationToken) =>
            {
                if (index == 1)
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task;
                    // The superseding filter change cancelled this load's own token.
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else if (index == 2)
                {
                    secondStarted.SetResult();
                    await releaseSecond.Task;
                }
            });

        var supersededLoad = fixture.ViewModel.LoadAsync();
        await firstStarted.Task;

        var currentLoad = fixture.ViewModel.SetStatusFilterAsync(AssignmentStatusDto.Published);
        await secondStarted.Task;

        releaseFirst.SetResult();
        await supersededLoad;

        fixture.ViewModel.IsLoading.Should().BeTrue(
            "the superseded load must not switch the spinner off under the load that replaced it — " +
            "that flash is what the loading flag's ownership rules out.");

        releaseSecond.SetResult();
        await currentLoad;

        fixture.ViewModel.IsLoading.Should().BeFalse();
        fixture.ViewModel.Items.Should().ContainSingle().Subject.Status.Should().Be(AssignmentStatusDto.Published);
    }

    [TestMethod]
    public async Task EveryOperationIsANoOpAfterDispose()
    {
        var fixture = CreateViewModel(request => Json(SummariesJson(
            SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft))));

        await fixture.ViewModel.LoadAsync();
        fixture.ViewModel.Dispose();

        var afterDispose = async () =>
        {
            await fixture.ViewModel.SetStatusFilterAsync(AssignmentStatusDto.Published);
            await fixture.ViewModel.SearchAsync("fractions");
            await fixture.ViewModel.PublishAsync(DraftId);
            await fixture.ViewModel.DeleteAsync(DraftId);
        };

        await afterDispose.Should().NotThrowAsync(
            "a queued tap can arrive after the screen navigated away and disposed the view model — its " +
            "cancelled token sources included — so no operation may touch them.");
    }

    [TestMethod]
    public async Task PublishAsync_RollsTheRowBackAndReportsAnActionError()
    {
        var fixture = CreateViewModel(request => request.Method == HttpMethod.Post
            ? Failure(HttpStatusCode.InternalServerError)
            : Json(SummariesJson(SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft))));

        await fixture.ViewModel.LoadAsync();
        await fixture.ViewModel.PublishAsync(DraftId);

        fixture.ViewModel.Items.Should().ContainSingle().Subject.Status.Should().Be(AssignmentStatusDto.Draft,
            "the optimistically painted status must not survive a rejected publish.");
        fixture.ViewModel.ActionError.Should().NotBeNullOrEmpty();
        fixture.ViewModel.ActionErrorTitle.Should().Be("Could not publish",
            "the bar names the action that failed, not the fetch the screen happens to be doing.");
        fixture.ViewModel.Error.Should().BeNull(
            "a failed action owns the action bar; the load-error branch would take the whole list away.");
        fixture.ViewModel.DisplayItems.Should().ContainSingle("the rows the action came from stay on screen.");
    }

    [TestMethod]
    public async Task PublishAsync_RollbackKeepsTheRowUnderItsChip()
    {
        var fixture = CreateViewModel(request => request.Method == HttpMethod.Post
            ? Failure(HttpStatusCode.InternalServerError)
            : Json(SummariesJson(SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft))));

        await fixture.ViewModel.SetStatusFilterAsync(AssignmentStatusDto.Draft);
        await fixture.ViewModel.PublishAsync(DraftId);

        fixture.ViewModel.DisplayItems.Should().ContainSingle(
            "the rollback restored the Draft row, which is what the Draft chip shows.");
    }

    [TestMethod]
    public async Task RetryActionAsync_ReRunsTheFailedAction_AndClearsItsBar()
    {
        var publishAttempts = 0;
        var fixture = CreateViewModel(request =>
        {
            if (request.Method != HttpMethod.Post)
            {
                return Json(SummariesJson(SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft)));
            }

            publishAttempts++;
            return publishAttempts == 1 ? Failure(HttpStatusCode.InternalServerError) : Ok();
        });

        await fixture.ViewModel.LoadAsync();
        await fixture.ViewModel.PublishAsync(DraftId);
        fixture.ViewModel.ActionError.Should().NotBeNullOrEmpty();

        await fixture.ViewModel.RetryActionAsync();

        fixture.ViewModel.Items.Should().ContainSingle().Subject.Status.Should().Be(AssignmentStatusDto.Published,
            "the retry re-runs the action the bar is reporting on, not a fresh load.");
        fixture.ViewModel.ActionError.Should().BeNull();
        fixture.ViewModel.ActionErrorTitle.Should().BeNull();
    }

    [TestMethod]
    public async Task DeleteAsync_IsANoOp_WhenTheConfirmedRowIsNoLongerLoaded()
    {
        var deleteAttempts = 0;
        var fixture = CreateViewModel(request =>
        {
            if (request.Method != HttpMethod.Delete)
            {
                return Json(SummariesJson(
                    SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft),
                    SummaryJson(PublishedId, "Decimals quiz", AssignmentStatusDto.Published)));
            }

            // The second delete is the stale one: the server answers 404 because the row is gone.
            deleteAttempts++;
            return deleteAttempts == 1 ? Ok() : Failure(HttpStatusCode.NotFound);
        });

        await fixture.ViewModel.LoadAsync();
        await fixture.ViewModel.DeleteAsync(DraftId);
        await fixture.ViewModel.DeleteAsync(DraftId);

        fixture.Handler.Requests.Should().HaveCount(2,
            "the confirmation referred to a row the list no longer holds, so the second delete has no target "
            + "and makes no call — never a 404 the user cannot act on.");
        fixture.ViewModel.ActionError.Should().BeNull();
        fixture.ViewModel.Error.Should().BeNull();
    }

    [TestMethod]
    public async Task LoadAsync_ClearsTheErrorOfTheFailedLoadItSupersedes()
    {
        var attempts = 0;
        var fixture = CreateViewModel(request =>
        {
            attempts++;
            return attempts == 1
                ? Failure(HttpStatusCode.InternalServerError)
                : Json(SummariesJson(SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft)));
        });

        await fixture.ViewModel.LoadAsync();
        fixture.ViewModel.Error.Should().NotBeNullOrEmpty();

        await fixture.ViewModel.LoadAsync();

        fixture.ViewModel.Error.Should().BeNull(
            "a stale message outliving the state it described is what kept a reloaded list hidden behind "
            + "the error branch.");
        fixture.ViewModel.ErrorTitle.Should().BeNull();
        fixture.ViewModel.DisplayItems.Should().ContainSingle();
    }

    [TestMethod]
    public async Task SetStatusFilterAsync_ClearsTheErrorOfTheFailedFilterItSupersedes()
    {
        var attempts = 0;
        var fixture = CreateViewModel(request =>
        {
            attempts++;
            return attempts == 1
                ? Failure(HttpStatusCode.InternalServerError)
                : Json(SummariesJson(SummaryJson(PublishedId, "Decimals quiz", AssignmentStatusDto.Published)));
        });

        await fixture.ViewModel.LoadAsync();
        await fixture.ViewModel.SetStatusFilterAsync(AssignmentStatusDto.Published);

        fixture.ViewModel.Error.Should().BeNull();
        fixture.ViewModel.Items.Should().ContainSingle();
    }

    [TestMethod]
    public async Task SearchAsync_ClearsTheActionErrorOfTheStateItReplaces()
    {
        var fixture = CreateViewModel(request => request.Method == HttpMethod.Post
            ? Failure(HttpStatusCode.InternalServerError)
            : Json(SummariesJson(SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft))));

        await fixture.ViewModel.LoadAsync();
        await fixture.ViewModel.PublishAsync(DraftId);
        fixture.ViewModel.ActionError.Should().NotBeNullOrEmpty();

        await fixture.ViewModel.SearchAsync("fractions");

        fixture.ViewModel.ActionError.Should().BeNull(
            "the fetched state replaces the rows the action was about, so its message is no longer true.");
        fixture.ViewModel.ActionErrorTitle.Should().BeNull();
        fixture.ViewModel.Error.Should().BeNull();
        fixture.ViewModel.SearchResults.Should().ContainSingle();
    }

    [TestMethod]
    public async Task SearchAsync_WithEmptyText_ClearsASearchFailure()
    {
        var attempts = 0;
        var fixture = CreateViewModel(request =>
        {
            attempts++;
            return attempts == 1
                ? Json(SummariesJson(SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft)))
                : Failure(HttpStatusCode.InternalServerError);
        });

        await fixture.ViewModel.LoadAsync();
        await fixture.ViewModel.SearchAsync("fractions");
        fixture.ViewModel.ErrorTitle.Should().Be("Could not search assignments",
            "the bar names the fetch that actually failed.");

        await fixture.ViewModel.SearchAsync("   ");

        fixture.ViewModel.Error.Should().BeNull(
            "the search that failed is gone with the box, so the loaded list is what the screen shows again.");
        fixture.ViewModel.DisplayItems.Should().ContainSingle();
    }

    [TestMethod]
    public async Task SearchAsync_DoesNotMarkAnyRowsOwnActionPending()
    {
        var searchStarted = new TaskCompletionSource();
        var releaseSearch = new TaskCompletionSource();
        var fixture = CreateViewModel(
            request => Json(SummariesJson(SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft))),
            beforeRespond: async (index, cancellationToken) =>
            {
                if (index == 2)
                {
                    searchStarted.SetResult();
                    await releaseSearch.Task;
                    cancellationToken.ThrowIfCancellationRequested();
                }
            });

        await fixture.ViewModel.LoadAsync();

        var search = fixture.ViewModel.SearchAsync("fractions");
        await searchStarted.Task;

        fixture.ViewModel.IsLoading.Should().BeTrue();
        fixture.ViewModel.IsActionPending(DraftId).Should().BeFalse(
            "a keystroke's search fetch is not an action on the row: binding the cards to the fetch flag is "
            + "what greyed every button out while the user typed.");

        releaseSearch.SetResult();
        await search;
    }

    [TestMethod]
    public async Task PublishAsync_MarksOnlyTheActingRowPending()
    {
        var publishStarted = new TaskCompletionSource();
        var releasePublish = new TaskCompletionSource();
        var fixture = CreateViewModel(
            request => request.Method == HttpMethod.Post
                ? Ok()
                : Json(SummariesJson(
                    SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft),
                    SummaryJson(PublishedId, "Decimals quiz", AssignmentStatusDto.Published))),
            beforeRespond: async (index, cancellationToken) =>
            {
                if (index == 2)
                {
                    publishStarted.SetResult();
                    await releasePublish.Task;
                    cancellationToken.ThrowIfCancellationRequested();
                }
            });

        await fixture.ViewModel.LoadAsync();

        var publish = fixture.ViewModel.PublishAsync(DraftId);
        await publishStarted.Task;

        fixture.ViewModel.IsActionPending(DraftId).Should().BeTrue();
        fixture.ViewModel.IsActionPending(PublishedId).Should().BeFalse(
            "an action on one row must never disable another row's buttons.");
        fixture.ViewModel.IsLoading.Should().BeFalse("a row action is not a fetch: the list keeps rendering under it.");

        releasePublish.SetResult();
        await publish;

        fixture.ViewModel.IsActionPending(DraftId).Should().BeFalse();
    }

    [TestMethod]
    public async Task DisplayItems_DropsARowThatNoLongerMatchesTheActiveFilter()
    {
        var fixture = CreateViewModel(request => request.Method == HttpMethod.Post
            ? Ok()
            : Json(SummariesJson(SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft))));

        await fixture.ViewModel.SetStatusFilterAsync(AssignmentStatusDto.Draft);
        await fixture.ViewModel.PublishAsync(DraftId);

        fixture.ViewModel.Items.Should().ContainSingle().Subject.Status.Should().Be(AssignmentStatusDto.Published,
            "the row keeps the status the action produced.");
        fixture.ViewModel.DisplayItems.Should().BeEmpty(
            "the Draft chip must never render a Published row.");
    }

    [TestMethod]
    public async Task SetStatusFilterAsync_NarrowsTheActiveSearchSlice()
    {
        var fixture = CreateViewModel(request => IsPublishedFilter(request)
            ? Json(SummariesJson(SummaryJson(PublishedId, "Decimals quiz", AssignmentStatusDto.Published)))
            : Json(SummariesJson(
                SummaryJson(DraftId, "Fractions quiz", AssignmentStatusDto.Draft),
                SummaryJson(PublishedId, "Decimals quiz", AssignmentStatusDto.Published))));

        await fixture.ViewModel.SearchAsync("quiz");
        fixture.ViewModel.DisplayItems.Should().HaveCount(2);

        await fixture.ViewModel.SetStatusFilterAsync(AssignmentStatusDto.Published);

        fixture.ViewModel.IsSearchActive.Should().BeTrue("the chip narrows the results; it does not clear the search.");
        fixture.ViewModel.DisplayItems.Should().ContainSingle().Subject.Id.Should().Be(PublishedId,
            "a chip tap while a search is active must change what is rendered.");
    }

    [TestMethod]
    public async Task EmptyMessage_NamesTheFilter_WhenTheFilterEmptiedTheList()
    {
        var fixture = CreateViewModel(request => Json("[]"));

        await fixture.ViewModel.SetStatusFilterAsync(AssignmentStatusDto.Archived);

        fixture.ViewModel.EmptyMessage.Should().Be("No Archived assignments.",
            "with no search active the empty state is the filter's doing, not the search's.");
    }

    private static bool IsPublishedFilter(HttpRequestMessage request) =>
        request.RequestUri!.PathAndQuery.Contains("status=Published", StringComparison.Ordinal);

    private static ViewModelFixture CreateViewModel(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        Func<int, CancellationToken, Task>? beforeRespond = null)
    {
        var handler = new ScriptedHandler(respond, beforeRespond);
        var client = new MobileAssignmentsApiClient(
            new HttpClient(handler) { BaseAddress = BaseAddress },
            NullLogger<MobileAssignmentsApiClient>.Instance);

        return new ViewModelFixture(
            new AssignmentsListViewModel(client, NullLogger<AssignmentsListViewModel>.Instance),
            handler);
    }

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Ok() => Json(string.Empty);

    private static HttpResponseMessage Failure(HttpStatusCode status) => new(status)
    {
        Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
    };

    private static string SummariesJson(params string[] rows) => $"[{string.Join(",", rows)}]";

    private static string SummaryJson(
        Guid id,
        string title,
        AssignmentStatusDto status,
        bool requiresApproval = false,
        ApprovalStatusDto? approvalStatus = null) =>
        "{" +
        $"\"id\":\"{id}\"," +
        $"\"title\":\"{title}\"," +
        "\"description\":null," +
        "\"assignmentType\":\"digital\"," +
        "\"gradingFormat\":\"teacherGraded\"," +
        "\"targetAudienceType\":\"allStudents\"," +
        $"\"topicId\":\"{Guid.Empty}\"," +
        "\"topicName\":null," +
        $"\"status\":\"{status}\"," +
        "\"dueDate\":null," +
        "\"maxScore\":null," +
        "\"mandatoryReview\":false," +
        $"\"createdByTeacherId\":\"{Guid.Empty}\"," +
        "\"createdAt\":\"2026-10-13T00:00:00+00:00\"," +
        "\"updatedAt\":\"2026-10-13T00:00:00+00:00\"," +
        $"\"requiresApproval\":{requiresApproval.ToString().ToLowerInvariant()}," +
        $"\"approvalStatus\":{(approvalStatus is null ? "null" : $"\"{approvalStatus}\"")}" +
        "}";

    private sealed record ViewModelFixture(AssignmentsListViewModel ViewModel, ScriptedHandler Handler);

    /// <summary>A scripted <see cref="HttpMessageHandler"/> (the repo's HTTP-testing seam — never a
    /// mocking library): it records every request, optionally pauses before answering (so a test can
    /// hold one request in flight while another starts) and answers with the test's own factory.</summary>
    private sealed class ScriptedHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        Func<int, CancellationToken, Task>? beforeRespond = null) : HttpMessageHandler
    {
        private int _count;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery, body));

            if (beforeRespond is not null)
            {
                await beforeRespond(++_count, cancellationToken);
            }

            return respond(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Body);
}
