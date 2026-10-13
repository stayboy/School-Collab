using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// The mobile assignment detail screen's state and behaviour — the device's replacement for the desktop
/// detail page's <c>@code</c> block, so the load, the lifecycle actions, the approval decision and the
/// per-student submission read are unit-testable without a device.
/// </summary>
/// <remarks>
/// <para>
/// It mirrors <c>src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Detail.razor</c>
/// for the surfaces this slice ships: the item read, the two independently failure-degraded
/// enrichments (recipients, submissions), the lifecycle actions' reload-after-act shape, the approval
/// decision's acting-teacher claim read, and the read-only per-student submission view. The authoring
/// overview, the review/grade forms and the sign-off/notification tabs are out of slice scope (OD-2 and
/// the round's scope list).
/// </para>
/// <para>
/// The enrichment reads degrade rather than fail the page: a recipients or submissions failure is logged
/// and leaves that section empty while the assignment itself stays loaded — the desktop page's behaviour,
/// preserved so a thin endpoint can never take the whole screen down.
/// </para>
/// </remarks>
public sealed class AssignmentDetailViewModel(
    MobileAssignmentsApiClient api,
    AuthenticationStateProvider authState,
    ILogger<AssignmentDetailViewModel> logger) : IDisposable
{
    /// <summary>The acting-teacher claim the realm's mappers emit and the approval request carries.</summary>
    private const string TeacherIdClaim = "teacher_id";

    private readonly CancellationTokenSource _loadCts = new();
    private volatile bool _disposed;

    /// <summary>Raised after every state mutation, so the rendering component can re-render.</summary>
    public event Action? Changed;

    /// <summary>The assignment currently loaded; <see langword="null"/> until the read answers.</summary>
    public AssignmentSummaryDto? Item { get; private set; }

    /// <summary>The assignment id the actions act on, set by <see cref="LoadAsync"/>.</summary>
    public Guid AssignmentId { get; private set; }

    /// <summary>The publish recipients, or <see langword="null"/> when the read failed or 404'd.</summary>
    public AssignmentRecipientDto[]? Recipients { get; private set; }

    /// <summary>The submissions list, or <see langword="null"/> when the read failed or 404'd.</summary>
    public SubmissionForReviewDto[]? Submissions { get; private set; }

    /// <summary>The per-student submission the user opened, if any.</summary>
    public SubmissionDetailDto? SelectedSubmission { get; private set; }

    /// <summary>Whether the main read is in flight.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Whether a lifecycle or approval action is in flight.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Whether the per-student submission read is in flight.</summary>
    public bool IsLoadingSubmission { get; private set; }

    /// <summary>The assignment read's failure — the only failure that owns the screen's error state.</summary>
    public string? Error { get; private set; }

    /// <summary>The last failed lifecycle action's message, or <see langword="null"/>.</summary>
    /// <remarks>
    /// Reported apart from <see cref="Error"/> on purpose: the error branch replaces the whole screen, so
    /// an action failure routed through it unmounted the very screen the button lived on. It renders as a
    /// bar above the still-rendered assignment instead.
    /// </remarks>
    public string? ActionError { get; private set; }

    /// <summary>The action-failure bar's title, e.g. "Could not publish" (set with <see cref="ActionError"/>).</summary>
    public string? ActionErrorTitle { get; private set; }

    /// <summary>
    /// Set when an action succeeded but the re-read that follows it failed: the screen is showing pre-action
    /// state and says so rather than silently diverging from what the server now holds.
    /// </summary>
    public string? ReloadWarning { get; private set; }

    /// <summary>
    /// Whether this session can record an approval decision — i.e. it carries a usable <c>teacher_id</c>.
    /// The decision is attributed to that claim, and the server rejects a <see cref="Guid.Empty"/> approver
    /// from a real principal, so without it Approve/Reject are guaranteed failures and are not offered.
    /// </summary>
    public bool CanDecideApproval { get; private set; }

    /// <summary>The approval surface's own failure, so an approval problem never masks the page.</summary>
    public string? ApprovalError { get; private set; }

    /// <summary>The per-student submission read's own failure.</summary>
    public string? SubmissionError { get; private set; }

    /// <summary>True when the read answered and the assignment does not exist (or is out of scope).</summary>
    public bool NotFound => !IsLoading && Error is null && Item is null;

    /// <summary>
    /// Loads the assignment and its two enrichments. A 404 leaves <see cref="Item"/> null (see
    /// <see cref="NotFound"/>); a cancelled request is a silent no-op; any other main-read failure is
    /// reported through <see cref="Error"/>.
    /// </summary>
    /// <param name="id">The assignment to load.</param>
    /// <param name="ct">An explicit token; when it cannot be cancelled the view model's own token is used.</param>
    public async Task LoadAsync(Guid id, CancellationToken ct = default)
    {
        AssignmentId = id;
        IsLoading = true;
        NotifyChanged();

        try
        {
            logger.LogInformation("Loading assignment {AssignmentId}", id);
            Item = await api.GetByIdAsync(id, LoadToken(ct));
            if (_disposed)
            {
                return;
            }

            CanDecideApproval = await GetActingTeacherIdAsync() != Guid.Empty;
            ClearFailures();

            if (Item is not null)
            {
                await LoadRecipientsAsync(id, ct);
                await LoadSubmissionsAsync(id, ct);
            }

            logger.LogInformation("Loaded assignment {AssignmentId}", id);
        }
        catch (OperationCanceledException)
        {
            // The component navigated away.
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            logger.LogError(ex, "Failed to load assignment {AssignmentId}", id);
            Error = ex.Message;
        }
        finally
        {
            if (!_disposed)
            {
                IsLoading = false;
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// Re-reads the item after a lifecycle action — the desktop page's <c>ReloadItemAsync</c>. A failed
    /// reload is logged and leaves the previous item in place, and it reports itself through
    /// <see cref="ReloadWarning"/>, because the action it follows has already been accepted by the server.
    /// </summary>
    public async Task ReloadAsync()
    {
        if (_disposed || AssignmentId == Guid.Empty)
        {
            return;
        }

        try
        {
            Item = await api.GetByIdAsync(AssignmentId, _loadCts.Token);
            if (_disposed)
            {
                return;
            }

            ReloadWarning = null;
        }
        catch (OperationCanceledException)
        {
            // Cancelled: keep the item the screen already renders.
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            logger.LogWarning(ex, "Failed to reload assignment {AssignmentId}", AssignmentId);
            ReloadWarning = "The action was accepted, but this screen could not re-read the assignment, "
                + "so it may still show its previous state.";
        }
        finally
        {
            NotifyChanged();
        }
    }

    /// <summary>Publishes the assignment, then re-reads it.</summary>
    public Task PublishAsync() =>
        RunActionAsync("publish", ActionSurface.Page, ct => api.PublishAsync(AssignmentId, ct));

    /// <summary>Returns the assignment to draft, then re-reads it.</summary>
    public Task UnpublishAsync() =>
        RunActionAsync("unpublish", ActionSurface.Page, ct => api.UnpublishAsync(AssignmentId, ct));

    /// <summary>Closes the assignment, then re-reads it.</summary>
    public Task CloseAsync() =>
        RunActionAsync("close", ActionSurface.Page, ct => api.CloseAsync(AssignmentId, ct));

    /// <summary>
    /// Submits the assignment for approval, then re-reads it. The failure surfaces on the approval
    /// panel (the desktop page's <c>_approvalError</c>), because that is the button's own surface.
    /// </summary>
    public Task SubmitForApprovalAsync() =>
        RunActionAsync("submit for approval", ActionSurface.Approval, ct => api.SubmitForApprovalAsync(AssignmentId, ct));

    /// <summary>
    /// Approves or rejects the pending assignment, attributing the decision to the session's
    /// <c>teacher_id</c> claim, then re-reads the assignment. A failure surfaces in
    /// <see cref="ApprovalError"/> and never throws through the UI.
    /// </summary>
    /// <param name="approve"><see langword="true"/> to approve, <see langword="false"/> to reject.</param>
    public async Task SubmitApprovalDecisionAsync(bool approve)
    {
        if (_disposed || Item is null)
        {
            return;
        }

        IsBusy = true;
        ApprovalError = null;
        NotifyChanged();

        try
        {
            var approverId = await GetActingTeacherIdAsync();
            if (approve)
            {
                await api.ApproveAsync(AssignmentId, approverId, _loadCts.Token);
            }
            else
            {
                await api.RejectAsync(AssignmentId, approverId, _loadCts.Token);
            }

            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            // Cancelled: the panel keeps its previous state.
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            logger.LogError(ex, "Failed to record the approval decision for assignment {AssignmentId}", AssignmentId);
            ApprovalError = ex.Message;
        }
        finally
        {
            if (!_disposed)
            {
                IsBusy = false;
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// Loads one student's submission (version history + review result) into
    /// <see cref="SelectedSubmission"/>. The review/grade forms are out of slice scope (OD-2).
    /// </summary>
    public async Task SelectSubmissionAsync(Guid studentId)
    {
        if (_disposed)
        {
            return;
        }

        IsLoadingSubmission = true;
        SubmissionError = null;
        NotifyChanged();

        try
        {
            SelectedSubmission = await api.GetSubmissionAsync(AssignmentId, studentId, _loadCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancelled: nothing to show.
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            logger.LogError(ex, "Failed to load submission for {AssignmentId}/{StudentId}", AssignmentId, studentId);
            SubmissionError = ex.Message;
        }
        finally
        {
            if (!_disposed)
            {
                IsLoadingSubmission = false;
                NotifyChanged();
            }
        }
    }

    /// <summary>Returns the submissions section to its list view.</summary>
    public void ClearSelectedSubmission()
    {
        SelectedSubmission = null;
        SubmissionError = null;
        NotifyChanged();
    }

    /// <summary>Marks the view model dead and cancels its in-flight reads.</summary>
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
    }

    /// <summary>
    /// The lifecycle shape every action shares: run the call, re-read the item, and report a failure on
    /// the surface that owns the button.
    /// </summary>
    private async Task RunActionAsync(string action, ActionSurface surface, Func<CancellationToken, Task> call)
    {
        if (_disposed || Item is null)
        {
            return;
        }

        IsBusy = true;
        SetSurfaceError(surface, action, null);
        NotifyChanged();

        try
        {
            await call(_loadCts.Token);
            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            // Cancelled: the screen keeps what it already shows.
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            logger.LogError(ex, "Failed to {Action} assignment {AssignmentId}", action, AssignmentId);
            SetSurfaceError(surface, action, ex.Message);
        }
        finally
        {
            if (!_disposed)
            {
                IsBusy = false;
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// The acting teacher's id from the circuit principal's <c>teacher_id</c> claim — the same claim the
    /// realm's mappers emit and the desktop page reads. No claim yields <see cref="Guid.Empty"/>, which
    /// the server honours only under TestAuth (a real principal always wins).
    /// </summary>
    private async Task<Guid> GetActingTeacherIdAsync()
    {
        var state = await authState.GetAuthenticationStateAsync();
        var claim = state.User.FindFirst(TeacherIdClaim)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Writes (or clears) the failure on the surface that owns the button — the approval panel for an
    /// approval action, the action bar above the still-rendered assignment for a lifecycle one. Never
    /// <see cref="Error"/>, which owns the screen-replacing load branch.
    /// </summary>
    private void SetSurfaceError(ActionSurface surface, string action, string? message)
    {
        if (surface == ActionSurface.Approval)
        {
            ApprovalError = message;
            return;
        }

        ActionError = message;
        ActionErrorTitle = message is null ? null : $"Could not {action}";
    }

    /// <summary>
    /// Clears every failure surface a successful load supersedes: a message outliving the state it
    /// described is what kept a reloaded screen behind its error branch.
    /// </summary>
    private void ClearFailures()
    {
        Error = null;
        ActionError = null;
        ActionErrorTitle = null;
        ReloadWarning = null;
    }

    /// <summary>An explicit caller token wins; otherwise the view model's own token is used.</summary>
    private CancellationToken LoadToken(CancellationToken ct) => ct.CanBeCanceled ? ct : _loadCts.Token;

    private async Task LoadRecipientsAsync(Guid id, CancellationToken ct)
    {
        try
        {
            Recipients = await api.GetRecipientsAsync(id, LoadToken(ct));
        }
        catch (OperationCanceledException)
        {
            // Cancelled with the page.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load recipients for {AssignmentId}", id);
        }
    }

    private async Task LoadSubmissionsAsync(Guid id, CancellationToken ct)
    {
        try
        {
            Submissions = await api.ListSubmissionsAsync(id, LoadToken(ct));
        }
        catch (OperationCanceledException)
        {
            // Cancelled with the page.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load submissions for {AssignmentId}", id);
        }
    }

    private void NotifyChanged() => Changed?.Invoke();

    /// <summary>Which surface owns a failed lifecycle action's error message.</summary>
    private enum ActionSurface
    {
        /// <summary>The page-level error bar (publish / unpublish / close).</summary>
        Page,

        /// <summary>The approval panel's own error bar (submit-for-approval / approve / reject).</summary>
        Approval,
    }
}
