using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// The mobile assignments list screen's state and behaviour — the device's replacement for the desktop
/// page's <c>@code</c> block, so the load, the search, the status filter and the optimistic lifecycle
/// actions are unit-testable without a device.
/// </summary>
/// <remarks>
/// <para>
/// Every method mirrors the desktop page it replaces
/// (<c>src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor</c>):
/// same API calls, same optimistic-then-rollback shape for the lifecycle actions, same client-side
/// title filter for search, same cancellation semantics. What is new is only the place it lives — the
/// view model sits in <c>Mobile.Logic</c> (plain <c>net10.0</c>), per the round's OD3 rule that
/// anything CI has to see lives there, not in the head.
/// </para>
/// <para>
/// <see cref="Changed"/> is the render notification: the component subscribes and calls
/// <c>StateHasChanged</c>. Without it an optimistic flip performed inside a view-model method could not
/// paint before the round-trip completes, which is the whole point of the optimistic shape.
/// </para>
/// </remarks>
public sealed class AssignmentsListViewModel(
    MobileAssignmentsApiClient api,
    ILogger<AssignmentsListViewModel> logger) : IDisposable
{
    /// <summary>The load-failure bar's title.</summary>
    private const string LoadErrorTitle = "Could not load assignments";

    /// <summary>The search-failure bar's title — the same bar, naming what actually failed.</summary>
    private const string SearchErrorTitle = "Could not search assignments";

    private CancellationTokenSource _loadCts = new();
    private CancellationTokenSource _searchCts = new();
    private readonly HashSet<Guid> _pendingActions = [];
    private Func<Task>? _retryAction;
    private int _activity;
    private bool _errorIsFromSearch;
    private volatile bool _disposed;

    /// <summary>Raised after every state mutation, so the rendering component can re-render.</summary>
    public event Action? Changed;

    /// <summary>The loaded rows, or <see langword="null"/> while the first load is in flight.</summary>
    public AssignmentSummaryDto[]? Items { get; private set; }

    /// <summary>The client-side search result set; <see langword="null"/> while no search is active.</summary>
    public AssignmentSummaryDto[]? SearchResults { get; private set; }

    /// <summary>The active status filter; <see langword="null"/> lists every status.</summary>
    public AssignmentStatusDto? StatusFilter { get; private set; }

    /// <summary>The search box's text, trimmed.</summary>
    public string SearchText { get; private set; } = string.Empty;

    /// <summary>Whether a list fetch (load, filter reload or search fetch) is in flight.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>The last <b>fetch</b> failure's message, or <see langword="null"/>.</summary>
    /// <remarks>
    /// A failed row action is deliberately NOT reported here: the screen renders its whole list only
    /// while <see cref="Error"/> is clear, so routing an action failure through this property unmounted
    /// the list the action came from. Action failures carry <see cref="ActionError"/> instead.
    /// </remarks>
    public string? Error { get; private set; }

    /// <summary>The load-failure bar's title, naming what actually failed (set with <see cref="Error"/>).</summary>
    public string? ErrorTitle { get; private set; }

    /// <summary>The last failed row action's message, or <see langword="null"/>.</summary>
    public string? ActionError { get; private set; }

    /// <summary>The action-failure bar's title, e.g. "Could not publish" (set with <see cref="ActionError"/>).</summary>
    public string? ActionErrorTitle { get; private set; }

    /// <summary>Whether a non-empty search is narrowing the list.</summary>
    public bool IsSearchActive => SearchText.Length > 0;

    /// <summary>
    /// Whether the row's <b>own</b> action is in flight — the card's disabled state. Deliberately not
    /// <see cref="IsLoading"/>: that flag flips on every keystroke's search fetch and would grey out
    /// every card's buttons while the user types.
    /// </summary>
    public bool IsActionPending(Guid id) => _pendingActions.Contains(id);

    /// <summary>
    /// The rows the screen should render: the search result set while a search is active, otherwise the
    /// loaded list — in both cases narrowed to the active <see cref="StatusFilter"/>.
    /// </summary>
    /// <remarks>
    /// The status filter is applied to BOTH slices. The search fetch itself asks for every status
    /// (<see cref="SearchAsync"/>), and the loaded slice comes back already filtered from the server —
    /// but an optimistic action can move a row out of the active filter, and a chip tap while a search
    /// was active used to change nothing on screen. Filtering here is what keeps the rendered rows from
    /// contradicting the chip.
    /// </remarks>
    public AssignmentSummaryDto[]? DisplayItems
    {
        get
        {
            var rows = IsSearchActive ? SearchResults : Items;
            if (rows is null || StatusFilter is null)
            {
                return rows;
            }

            return [.. rows.Where(row => row.Status == StatusFilter)];
        }
    }

    /// <summary>The empty-state message: search-aware, and honest about a filter that emptied the list.</summary>
    public string EmptyMessage => IsSearchActive
        ? $"No assignments match '{SearchText}'."
        : StatusFilter is { } filter
            ? $"No {filter} assignments."
            : "No assignments yet.";

    /// <summary>
    /// Loads the list for the active <see cref="StatusFilter"/>. A cancelled request is a silent no-op;
    /// any other failure is reported through <see cref="Error"/>.
    /// </summary>
    /// <param name="ct">An explicit token (a caller that owns its own cancellation); when it cannot be
    /// cancelled the view model's own superseding token is used.</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var activity = BeginActivity();

        try
        {
            logger.LogInformation("Loading assignments list");
            var items = await api.ListAsync(StatusFilter, LoadToken(ct));
            if (_disposed)
            {
                return;
            }

            Items = items;
            ClearFailures();
            logger.LogInformation("Loaded {Count} assignments", Items?.Length ?? 0);
        }
        catch (OperationCanceledException)
        {
            // The component navigated away, or a newer load superseded this one.
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            logger.LogError(ex, "Failed to load assignments");
            Error = ex.Message;
            ErrorTitle = LoadErrorTitle;
            _errorIsFromSearch = false;
        }
        finally
        {
            EndActivity(activity);
        }
    }

    /// <summary>
    /// Applies a status filter and reloads, cancelling the load it supersedes (the desktop page's
    /// <c>OnStatusFilterChangedAsync</c>).
    /// </summary>
    /// <param name="filter">The new filter; <see langword="null"/> lists every status.</param>
    /// <param name="ct">See <see cref="LoadAsync"/>.</param>
    public async Task SetStatusFilterAsync(AssignmentStatusDto? filter, CancellationToken ct = default)
    {
        if (_disposed)
        {
            // A tap can still arrive after the screen navigated away and disposed this view model —
            // and its token sources with it, so touching them here would throw.
            return;
        }

        StatusFilter = filter;

        _loadCts.Cancel();
        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();

        await LoadAsync(ct);
    }

    /// <summary>
    /// Applies the search text. A non-empty search fetches every status and filters titles client-side
    /// (case-insensitive), cancelling the search it supersedes; an empty search clears the results (the
    /// desktop page's <c>OnSearchValueChanged</c>).
    /// </summary>
    /// <param name="text">The raw search box text; whitespace is trimmed.</param>
    /// <param name="ct">See <see cref="LoadAsync"/>.</param>
    public async Task SearchAsync(string? text, CancellationToken ct = default)
    {
        if (_disposed)
        {
            return;
        }

        SearchText = text?.Trim() ?? string.Empty;

        if (SearchText.Length == 0)
        {
            // The box was cleared: drop the results and invalidate any fetch still in flight, so a
            // superseded search can neither repopulate them nor switch the spinner off under a load.
            _activity++;
            SearchResults = null;
            IsLoading = false;
            if (_errorIsFromSearch && Items is not null)
            {
                // The search that failed no longer exists, so its message no longer describes any state:
                // the loaded list is what the screen shows again. The guard matters — with no loaded list
                // to fall back to, the failure is still the truth about the screen.
                Error = null;
                ErrorTitle = null;
                _errorIsFromSearch = false;
            }

            NotifyChanged();
            return;
        }

        _searchCts.Cancel();
        _searchCts.Dispose();
        _searchCts = new CancellationTokenSource();

        var activity = BeginActivity();

        try
        {
            var results = await api.ListAsync(null, SearchToken(ct));
            if (_disposed)
            {
                return;
            }

            SearchResults = results?
                .Where(row => row.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            ClearFailures();

            logger.LogInformation(
                "Search returned {Count} results for '{SearchText}'", SearchResults?.Length ?? 0, SearchText);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke superseded this search.
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            logger.LogError(ex, "Search failed for '{SearchText}'", SearchText);
            Error = ex.Message;
            ErrorTitle = SearchErrorTitle;
            _errorIsFromSearch = true;
        }
        finally
        {
            EndActivity(activity);
        }
    }

    /// <summary>Publishes the row optimistically, rolling it back when the API call fails.</summary>
    public Task PublishAsync(Guid id, CancellationToken ct = default) =>
        ApplyStatusChangeAsync(
            "publish",
            id,
            row => row with { Status = AssignmentStatusDto.Published },
            (callCt) => api.PublishAsync(id, callCt),
            ct);

    /// <summary>Returns the row to draft optimistically, rolling it back when the API call fails.</summary>
    public Task UnpublishAsync(Guid id, CancellationToken ct = default) =>
        ApplyStatusChangeAsync(
            "unpublish",
            id,
            row => row with { Status = AssignmentStatusDto.Draft },
            (callCt) => api.UnpublishAsync(id, callCt),
            ct);

    /// <summary>Closes the row optimistically, rolling it back when the API call fails.</summary>
    public Task CloseAsync(Guid id, CancellationToken ct = default) =>
        ApplyStatusChangeAsync(
            "close",
            id,
            row => row with { Status = AssignmentStatusDto.Closed },
            (callCt) => api.CloseAsync(id, callCt),
            ct);

    /// <summary>
    /// Marks the row's approval status pending optimistically, rolling it back when the API call fails.
    /// </summary>
    public Task SubmitForApprovalAsync(Guid id, CancellationToken ct = default) =>
        ApplyStatusChangeAsync(
            "submit for approval",
            id,
            row => row with { ApprovalStatus = ApprovalStatusDto.Pending },
            (callCt) => api.SubmitForApprovalAsync(id, callCt),
            ct);

    /// <summary>
    /// Deletes the assignment and drops the row (and its search-result twin) on success.
    /// </summary>
    /// <remarks>
    /// A row the user confirmed that is no longer in the collection — a reload or a concurrent delete
    /// removed it while the confirmation was open — is a no-op, never an error: the API is not called for
    /// a target the screen no longer holds, and no failure is reported over an action with nothing to act
    /// on.
    /// </remarks>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (_disposed || !ContainsRow(id))
        {
            return;
        }

        BeginRowAction(id);
        ClearActionError();

        try
        {
            await api.DeleteAsync(id, LoadToken(ct));
            if (_disposed)
            {
                return;
            }

            if (Items is not null)
            {
                Items = [.. Items.Where(row => row.Id != id)];
            }

            if (SearchResults is not null)
            {
                SearchResults = [.. SearchResults.Where(row => row.Id != id)];
            }

            logger.LogInformation("Deleted assignment {AssignmentId}", id);
        }
        catch (OperationCanceledException)
        {
            // Cancelled: nothing was deleted from the server's point of view we can act on.
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            logger.LogError(ex, "Failed to delete assignment {AssignmentId}", id);
            ReportActionFailure("delete", ex.Message, () => DeleteAsync(id));
        }
        finally
        {
            EndRowAction(id);
        }
    }

    /// <summary>Re-runs the action whose failure is on the action bar, if there is one.</summary>
    public async Task RetryActionAsync()
    {
        var retry = _retryAction;
        if (_disposed || retry is null)
        {
            return;
        }

        // Cleared first: a retry that fails again re-arms itself through the same failure path.
        _retryAction = null;
        await retry();
    }

    /// <summary>Marks the view model dead and cancels its in-flight loads and searches.</summary>
    /// <remarks>Idempotent: the screen disposes the view model when it navigates away, and the container
    /// also disposes any transient it created when the provider shuts down — a second
    /// <see cref="CancellationTokenSource.Cancel"/> on an already-disposed source would throw.</remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _loadCts.Cancel();
        _loadCts.Dispose();
        _searchCts.Cancel();
        _searchCts.Dispose();
    }

    /// <summary>
    /// The optimistic lifecycle shape every status action shares: stamp the new row locally, call the
    /// API, restore the previous row and report through <see cref="Error"/> on failure (the desktop
    /// page's per-action pattern).
    /// </summary>
    private async Task ApplyStatusChangeAsync(
        string action,
        Guid id,
        Func<AssignmentSummaryDto, AssignmentSummaryDto> optimistic,
        Func<CancellationToken, Task> call,
        CancellationToken ct)
    {
        if (_disposed || Items is null)
        {
            return;
        }

        var index = Array.FindIndex(Items, row => row.Id == id);
        if (index < 0)
        {
            // The row left the collection while the tap was in flight: nothing to flip, nothing to call.
            return;
        }

        var previous = Items[index];
        Items[index] = optimistic(previous);
        SetSearchResult(id, Items[index]);
        BeginRowAction(id);
        ClearActionError();
        NotifyChanged();

        try
        {
            await call(LoadToken(ct));
        }
        catch (OperationCanceledException)
        {
            // Cancelled: the row keeps its optimistic value; a later load re-reads the truth.
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            logger.LogError(ex, "Failed to {Action} assignment {AssignmentId}", action, id);

            var restoreIndex = Array.FindIndex(Items, row => row.Id == id);
            if (restoreIndex >= 0)
            {
                Items[restoreIndex] = previous;
            }

            SetSearchResult(id, previous);
            ReportActionFailure(
                action,
                ex.Message,
                () => ApplyStatusChangeAsync(action, id, optimistic, call, default));
        }
        finally
        {
            EndRowAction(id);
        }
    }

    /// <summary>
    /// Records a failed row action on the action bar — never on <see cref="Error"/>, which owns the
    /// load-failure branch and therefore the whole list — and remembers how to retry it.
    /// </summary>
    private void ReportActionFailure(string action, string message, Func<Task> retry)
    {
        ActionError = message;
        ActionErrorTitle = $"Could not {action}";
        _retryAction = retry;
    }

    /// <summary>Whether the row is in either rendered collection.</summary>
    private bool ContainsRow(Guid id) =>
        Items?.Any(row => row.Id == id) == true || SearchResults?.Any(row => row.Id == id) == true;

    /// <summary>Marks the row's own action as in flight, so only that row's buttons grey out.</summary>
    private void BeginRowAction(Guid id)
    {
        _pendingActions.Add(id);
        NotifyChanged();
    }

    /// <summary>Clears the row's action flag and repaints.</summary>
    private void EndRowAction(Guid id)
    {
        _pendingActions.Remove(id);
        NotifyChanged();
    }

    private void ClearActionError()
    {
        ActionError = null;
        ActionErrorTitle = null;
        _retryAction = null;
    }

    /// <summary>
    /// Clears every failure surface after a successful fetch. A message outliving the state it described
    /// is what kept a reloaded list hidden behind the load-failure branch.
    /// </summary>
    private void ClearFailures()
    {
        Error = null;
        ErrorTitle = null;
        _errorIsFromSearch = false;
        ClearActionError();
    }

    /// <summary>
    /// Mirrors one row substitution into the search result set: the screen renders that set while a
    /// search is active, so an unmirrored flip (or rollback) would never be visible.
    /// </summary>
    private void SetSearchResult(Guid id, AssignmentSummaryDto row)
    {
        if (SearchResults is null)
        {
            return;
        }

        var index = Array.FindIndex(SearchResults, candidate => candidate.Id == id);
        if (index >= 0)
        {
            SearchResults[index] = row;
        }
    }

    /// <summary>An explicit caller token wins; otherwise the superseding load token is used.</summary>
    private CancellationToken LoadToken(CancellationToken ct) => ct.CanBeCanceled ? ct : _loadCts.Token;

    /// <summary>An explicit caller token wins; otherwise the superseding search token is used.</summary>
    private CancellationToken SearchToken(CancellationToken ct) => ct.CanBeCanceled ? ct : _searchCts.Token;

    /// <summary>
    /// Starts a list operation (a load or a search fetch) and claims the loading flag for it. Loads
    /// supersede each other — a status-filter change cancels the load it replaces, and each keystroke
    /// the search before it — so the flag is owned by the NEWEST operation: the returning one carries
    /// its claim token and only that one is allowed to switch the spinner off. Without this the
    /// cancelled predecessor's completion clears the flag under its successor and the screen flashes
    /// its empty state while the new request is still in flight.
    /// </summary>
    private int BeginActivity()
    {
        IsLoading = true;
        NotifyChanged();
        return ++_activity;
    }

    /// <summary>Releases the loading flag if <paramref name="activity"/> still owns it.</summary>
    private void EndActivity(int activity)
    {
        if (_disposed || activity != _activity)
        {
            return;
        }

        IsLoading = false;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
