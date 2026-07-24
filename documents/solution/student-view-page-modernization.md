# Student view page modernization

**Status:** Proposed
**Owner:** Students module
**Branch:** `feat/student-view-modernize` (new)
**Affected files:**

- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Detail.razor` — rewrite
- `src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Detail.razor.css` — rewrite (merge)
- `src/Students/SchoolCollab.Students.Admin/Components/Students/EnrollStudentDialog.razor` — new
- `src/Students/SchoolCollab.Students.Admin/Components/Students/EnrollStudentModel.cs` — new
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardiansTab.razor` — minor edit (inline "Link" form → collapsible disclosure)
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardiansTab.razor.css` — minor edit (collapsible styling)
- `src/Students/SchoolCollab.Students.Admin/Components/Students/WithdrawEnrollmentDialog.razor` — new (optional, see §6)
- `src/Students/SchoolCollab.Students.Admin/Components/Students/WithdrawEnrollmentModel.cs` — new (optional)
- `tests/SchoolCollab.Admin.Tests.Unit/StudentDetailSectionsTests.cs` — new bUnit tests

**Out of scope:** editing the student's profile fields inline (still goes to `/students/{id}/edit`); the `Edit.razor` page itself is untouched.

---

## 1. Goals & constraints

### 1.1 User goals
1. **No tabs.** All student information visible on one scrollable page. Each
   information group is a stacked, headed section (like `GuardianDetail.razor`).
2. **Enrollment & transfer on the page.** Two new actions visible on the
   "Enrollments" section:
   - **Enroll** — open a dialog, pick period + grade, save.
   - **Transfer** — reuse the existing `StudentTransferDialog` from the
     landing page (already wired in `Index.razor`).
   - **Withdraw** (optional, see §6) — close the active enrollment with an
     exit date.
3. **Consistency.** Match the visual language of `GuardianDetail.razor`
   (single-page, `FluentCard` sections, `<h3>` headers, `.assigned-list`
   lists, `.wizard-section-header` pattern, `.detail-card` popup-clipping
   fix).
4. **No regression.** All current behavior preserved: TenantGate fallback,
   soft-delete badge, created/updated timestamps, contacts editor, all four
   state-management hygiene rules (`CancellationTokenSource` on every async
   path, `_disposed` guards, single-flight load).

### 1.2 Hard constraints
- **No `FluentTabs`.** Section, do not tab. (User-stated.)
- **No `FluentPaginator` on the enrollments list** unless the list ever
  exceeds ~20 rows. Today it is bounded by the number of periods a student
  has been enrolled in (typically 1–12). If pagination is ever needed, use
  the `LandingPage` / `FluentPaginator` pattern.
- **Reuse, don't fork.** `GuardiansTab` is already a self-contained
  component with its own loader, sort-by-role, and link form. Embed it
  inline (not inside a tab). Same for `ContactsEditor`.
- **Single `OnInitializedAsync` load.** Add to the existing
  parallel-load pattern (student + enrollments) rather than refactoring the
  loader. The new sections either piggyback on existing data or load
  lazily.
- **CSS merge, not full-file overwrite.** `Detail.razor.css` already has
  the `.page-container`, `.title-row`, `.action-bar`, `.spinner-container`,
  `.form-container`, `.form-field` rules. All must be preserved (per
  `dialog-ui` skill §3 — scoped CSS hazard).

---

## 2. Information architecture (top-to-bottom, single page)

```
┌──────────────────────────────────────────────────────────────────────┐
│ [PageTitle]                                                          │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  Title row:  "Juan dela Cruz"  [Edit] [⋯ menu]                  │  │
│  │  Subtitle:   Student # 2025-0007 · Active · Current: Grade 3    │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                      │
│  ┌─── Profile (always-on, FluentCard) ────────────────────────────┐  │
│  │   Student # | First name | Last name | Date of birth | Gender  │  │
│  │   Status (badge) | Created | Updated                            │  │
│  │   (same fields as the old Overview tab, but in a profile-grid) │  │
│  └─────────────────────────────────────────────────────────────────┘  │
│                                                                      │
│  ┌─── Enrollments (FluentCard) ───────────────────────────────────┐  │
│  │  [Enroll]  [Transfer active]  [Withdraw active]                │  │
│  │   ┌─ table ─────────────────────────────────────────────────┐  │  │
│  │   │ Period | Grade | Enrolled on | Exit date | Status | ⋯   │  │  │
│  │   └──────────────────────────────────────────────────────────┘  │  │
│  │   (Inline empty state if no enrollments)                       │  │
│  └─────────────────────────────────────────────────────────────────┘  │
│                                                                      │
│  ┌─── Guardians (embed <GuardiansTab StudentId=… />) ──────────────┐  │
│  │   Grouped by role, link form                                  │  │
│  └─────────────────────────────────────────────────────────────────┘  │
│                                                                      │
│  ┌─── Contacts (<ContactsEditor OwnerType=Student OwnerId=… />) ───┐  │
│  └─────────────────────────────────────────────────────────────────┘  │
│                                                                      │
│  [Back to Students]                                                  │
└──────────────────────────────────────────────────────────────────────┘
```

### 2.1 Section ordering rationale
1. **Profile** — who is this student? (identify at a glance)
2. **Enrollments** — what grade/period are they currently in? (the
   operational state that drives everything else)
3. **Guardians** — who looks after them? (operational, edits common)
4. **Contacts** — how do we reach them? (reference data)

This ordering matches the read-then-act pattern: confirm identity →
see current status → act on related people → act on related contact info.

### 2.2 Title row enhancement
Replace the existing "StudentNumber ··· [Edit]" with a richer header:

```razor
<div class="title-row">
    <div class="title-block">
        <h1 class="page-title">@_student.FirstName @_student.LastName</h1>
        <p class="page-subtitle">
            <code>@_student.StudentNumber</code>
            <span class="title-sep">·</span>
            @StatusBadge
            <span class="title-sep">·</span>
            Current: <strong>@(_currentGradeName ?? "—")</strong>
        </p>
    </div>
    <FluentButton Appearance="Appearance.Outline"
                  OnClick='() => Nav.NavigateTo($"/students/{Id}/edit")'>
        Edit
    </FluentButton>
</div>
```

`@_currentGradeName` is computed from the active enrollment's
`GradeLevelId` resolved against a small `_gradeLevelNames` dictionary
(see §4.3).

---

## 3. Component changes (Detail.razor)

### 3.1 Markup (high-level structure)

```razor
@page "/students/{id:guid}"
@using SchoolCollab.Students.Admin.Components.Students
@using SchoolCollab.Students.Admin.Services
@using SchoolCollab.Students.Core.Domain
@using SchoolCollab.Students.Core.DTOs
@using SchoolCollab.Admin.Shared.Components
@using Microsoft.FluentUI.AspNetCore.Components
@inject StudentsApiClient Api
@inject NavigationManager Nav
@inject IDialogService DialogService
@inject ILogger<Detail> Logger
@inject VisibleTenantService VisibleTenant
@implements IDisposable

<PageTitle>Student — @(_student?.FirstName ?? "Loading…") @_student?.LastName</PageTitle>

<ErrorBoundary>
    <ChildContent>
        <FluentStack Orientation="Orientation.Vertical" class="page-container">
            <TenantGate>
                <ChildContent>
                    @if (_student is null) { <FluentProgressRing /> }
                    else
                    {
                        <!-- 1. Title row -->
                        <div class="title-row"> … </div>

                        <!-- 2. Profile -->
                        <FluentCard class="detail-card">
                            <div class="profile-grid">
                                <div class="profile-row"><span class="profile-label">Student #</span><span class="profile-value">@_student.StudentNumber</span></div>
                                <div class="profile-row"><span class="profile-label">Full name</span><span class="profile-value">@_student.FirstName @_student.LastName</span></div>
                                <div class="profile-row"><span class="profile-label">Date of birth</span><span class="profile-value">@(_student.DateOfBirth?.ToString("d") ?? "—")</span></div>
                                <div class="profile-row"><span class="profile-label">Gender</span><span class="profile-value">@(_student.GenderName ?? "—")</span></div>
                                <div class="profile-row"><span class="profile-label">Status</span><span class="profile-value">@StatusBadge</span></div>
                                <div class="profile-row"><span class="profile-label">Created</span><span class="profile-value">@_student.CreatedAt.ToLocalTime().ToString("g")</span></div>
                                <div class="profile-row"><span class="profile-label">Updated</span><span class="profile-value">@student.UpdatedAt.ToLocalTime().ToString("g")</span></div>
                            </div>
                        </FluentCard>

                        <!-- 3. Enrollments -->
                        <div class="section-header">
                            <h3>Enrollments</h3>
                            <FluentStack Orientation="Orientation.Horizontal" Spacing="6">
                                <FluentButton Appearance="Appearance.Accent" OnClick="OnEnrollAsync"
                                              Disabled="@_savingEnrollment">
                                    <FluentIcon Icon="@(Icons.Regular.Size16.Add)" Slot="start" />
                                    Enroll
                                </FluentButton>
                                <FluentButton Appearance="Appearance.Outline" OnClick="OnTransferAsync"
                                              Disabled="@(HasNoActiveEnrollment || _savingEnrollment)">
                                    Transfer
                                </FluentButton>
                                <FluentButton Appearance="Appearance.Outline" OnClick="OnWithdrawAsync"
                                              Disabled="@(HasNoActiveEnrollment || _savingEnrollment)">
                                    Withdraw
                                </FluentButton>
                            </FluentStack>
                        </div>
                        @if (_enrollments is null) { <FluentProgressRing /> }
                        else if (_enrollments.Length == 0) { <FluentMessageBar Intent="MessageIntent.Info">No enrollments yet — use Enroll to add the first one.</FluentMessageBar> }
                        else { <EnrollmentsTable Enrollments="_enrollments" GradeNames="_gradeLevelNames" PeriodNames="_periodNames" /> }

                        <!-- 4. Guardians -->
                        <div class="section-header"><h3>Guardians</h3></div>
                        <GuardiansTab StudentId="Id" />

                        <!-- 5. Contacts -->
                        <div class="section-header"><h3>Contacts</h3></div>
                        <ContactsEditor OwnerType="ContactOwnerType.Student" OwnerId="Id" />
                    }
                </ChildContent>
                <Fallback> … (unchanged) … </Fallback>
            </TenantGate>

            <div class="action-bar">
                <FluentButton Appearance="Appearance.Outline" OnClick='() => Nav.NavigateTo("/students")'>Back to Students</FluentButton>
            </div>
            @if (!string.IsNullOrEmpty(_error)) { <FluentMessageBar Intent="MessageIntent.Error" class="mt-3">@_error</FluentMessageBar> }
        </FluentStack>
    </ChildContent>
    <ErrorContent Context="ex"> … (unchanged) … </ErrorContent>
</ErrorBoundary>
```

### 3.2 Code block — load, state, action handlers

```razor
@code {
    [Parameter] public Guid Id { get; set; }

    // State
    private StudentDto? _student;
    private StudentEnrollmentDto[]? _enrollments;
    private Dictionary<Guid, string> _gradeLevelNames = new();
    private Dictionary<Guid, string> _periodNames = new();
    private string? _error;
    private bool _savingEnrollment;
    private bool _isRealTenant;
    private CancellationTokenSource? _loadCts;
    private volatile bool _disposed;

    // Derived
    private StudentEnrollmentDto? ActiveEnrollment =>
        _enrollments?.FirstOrDefault(e => e.Status == "Active" && e.ExitDate is null)
        ?? _enrollments?.FirstOrDefault(e => e.ExitDate is null);
    private string? CurrentGradeName =>
        ActiveEnrollment is { } e && _gradeLevelNames.TryGetValue(e.GradeLevelId, out var n) ? n : null;
    private bool HasNoActiveEnrollment => ActiveEnrollment is null;
    private RenderFragment StatusBadge => __builder =>
    {
        if (_student is null) return;
        if (_student.IsDeleted) <FluentBadge Appearance="Appearance.Neutral">Deleted</FluentBadge>;
        else <FluentBadge Appearance="Appearance.Accent">Active</FluentBadge>;
    };

    protected override async Task OnInitializedAsync()
    {
        var scope = await VisibleTenant.GetScopeAsync();
        _isRealTenant = scope.IsRealTenant;
        if (!_isRealTenant) return;

        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            // Original two parallel loads…
            var studentTask = Api.GetStudentByIdAsync(Id, ct);
            var enrollmentsTask = Api.ListEnrollmentsByStudentAsync(Id, ct);
            await Task.WhenAll(studentTask, enrollmentsTask);
            if (_disposed) return;

            _student = studentTask.Result;
            _enrollments = enrollmentsTask.Result;
            if (_student is null) _error = $"Student {Id} not found.";

            // NEW: build the grade + period name lookup dicts from the data
            // the new section needs. Single round-trip per call because the
            // collections are small (typically ≤ 12 entries).
            var gradeIds = (_enrollments ?? Array.Empty<StudentEnrollmentDto>())
                .Select(e => e.GradeLevelId).Distinct().ToArray();
            var periodIds = (_enrollments ?? Array.Empty<StudentEnrollmentDto>())
                .Select(e => e.PeriodId).Distinct().ToArray();

            var gradesTask = gradeIds.Length > 0 ? Api.ListGradeLevelsAsync(ct) : Task.FromResult<GradeLevelDto[]?>(null);
            var periodsTask = periodIds.Length > 0 ? Api.ListPeriodsAsync(ct) : Task.FromResult<PeriodDto[]?>(null);
            await Task.WhenAll(gradesTask, periodsTask);
            if (_disposed) return;

            _gradeLevelNames = (gradesTask.Result ?? Array.Empty<GradeLevelDto>())
                .ToDictionary(g => g.Id, g => g.Name);
            _periodNames = (periodsTask.Result ?? Array.Empty<PeriodDto>())
                .ToDictionary(p => p.Id, p => p.Name);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_disposed) return;
            Logger.LogError(ex, "Failed to load student {StudentId}", Id);
            _error = ex.Message;
        }
    }

    // ── Enrollment / transfer / withdraw actions ──
    private async Task OnEnrollAsync()
    {
        if (_disposed) return;
        var model = new EnrollStudentModel(
            StudentId: Id,
            SuggestedPeriodId: _periodNames.Keys.FirstOrDefault(),
            SuggestedGradeLevelId: _gradeLevelNames.Keys.FirstOrDefault());
        var result = await DialogService.ShowShellDialogAsync<EnrollStudentDialog, EnrollStudentModel, EnrollStudentResult>(
            model, "Enroll student", DialogSize.Medium);
        if (result is { Success: true }) await ReloadEnrollmentsAsync();
    }

    private async Task OnTransferAsync()
    {
        if (_disposed) return;
        var result = await DialogService.ShowShellDialogAsync<StudentTransferDialog, StudentTransferModel, StudentTransferResult>(
            new StudentTransferModel(Id), "Transfer student", DialogSize.Medium);
        if (result is { Success: true }) await ReloadEnrollmentsAsync();
    }

    private async Task OnWithdrawAsync() { /* §6, optional */ }

    private async Task ReloadEnrollmentsAsync()
    {
        if (_disposed) return;
        _savingEnrollment = true;
        try
        {
            _enrollments = await Api.ListEnrollmentsByStudentAsync(Id);
            if (_disposed) return;
            // Rebuild lookup dicts in case the new enrollment introduced
            // a fresh period / grade.
            var gradeIds = _enrollments.Select(e => e.GradeLevelId).Distinct().ToArray();
            var periodIds = _enrollments.Select(e => e.PeriodId).Distinct().ToArray();
            if (gradeIds.Except(_gradeLevelNames.Keys).Any())
                foreach (var g in await Api.ListGradeLevelsAsync() ?? Array.Empty<GradeLevelDto>())
                    _gradeLevelNames[g.Id] = g.Name;
            if (periodIds.Except(_periodNames.Keys).Any())
                foreach (var p in await Api.ListPeriodsAsync() ?? Array.Empty<PeriodDto>())
                    _periodNames[p.Id] = p.Name;
        }
        catch (Exception ex)
        {
            if (_disposed) return;
            Logger.LogError(ex, "Failed to reload enrollments for student {StudentId}", Id);
            _error = ex.Message;
        }
        finally
        {
            _savingEnrollment = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }
}
```

### 3.3 Why embed `GuardiansTab` and `ContactsEditor` instead of reimplementing
- **Zero regression risk.** Both components are already battle-tested,
  have their own cancellation/load lifecycle, and are unit-tested.
- **Single source of truth.** Linking a guardian or editing a contact from
  the student page must be the *same code path* as the corresponding
  actions on the landing page; the tab is already wired correctly.
- **Reuses the popup-clipping CSS workaround** that `GuardiansTab.razor.css`
  carries (`.detail-card { contain: none !important }`).

The trade-off — we lose the ability to put a custom "Linked" badge inside
the section header — is acceptable because the section header is
`<h3>Guardians</h3>` with a count, matching the `GuardianDetail.razor` "Wards"
section exactly.

---

## 4. New components

### 4.1 `EnrollStudentDialog.razor` (new)

Lives next to `StudentTransferDialog.razor`. Shape mirrors the transfer
dialog (per `dialog-ui` skill §1 — `DialogShellBase<TModel, TResult>` +
`DialogShellFooter`).

```razor
@inherits DialogShellBase<EnrollStudentModel, EnrollStudentResult>
@using SchoolCollab.Students.Admin.Services
@using SchoolCollab.Students.Core.DTOs
@inject StudentsApiClient Api
@inject ILogger<EnrollStudentDialog> Logger

<div class="enroll-dialog">
    @if (_loading) { <FluentProgressRing /> }
    else if (_error is not null) { <FluentMessageBar Intent="MessageIntent.Error">@_error</FluentMessageBar> }
    else if (_periods.Length == 0) { <FluentMessageBar Intent="MessageIntent.Warning">No periods are configured yet — create one before enrolling.</FluentMessageBar> }
    else if (_gradeLevels.Length == 0) { <FluentMessageBar Intent="MessageIntent.Warning">No grade levels are configured yet — create one before enrolling.</FluentMessageBar> }
    else
    {
        <EditForm Model="_form" OnValidSubmit="HandleSubmitAsync">
            <FluentStack Orientation="Orientation.Vertical" Spacing="3">
                <FluentSelect TOption="PeriodDto" Items="_periods"
                              @bind-SelectedOption="_selectedPeriod"
                              OptionText="@(p => $"{p.Name} ({p.StartDate:yyyy-MM-dd} – {p.EndDate:yyyy-MM-dd})")"
                              OptionValue="@(p => p.Id.ToString())"
                              Label="Period" Required="true" />
                <FluentSelect TOption="GradeLevelDto" Items="_gradeLevels"
                              @bind-SelectedOption="_selectedGrade"
                              OptionText="@(g => g.Name)"
                              OptionValue="@(g => g.Id.ToString())"
                              Label="Grade level" Required="true" />
                <FluentDatePicker @bind-Value="_enrolledOn" Label="Enrolled on" Required="true" />
            </FluentStack>
            <DialogShellFooter Saving="Saving" Error="Error"
                               SubmitText="Enroll" SavingText="Enrolling…"
                               OnCancel="HandleCancelAsync" />
        </EditForm>
    }
</div>

@code {
    private sealed class FormState
    {
        public DateOnly? EnrolledOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    }
    private readonly FormState _form = new();
    private DateOnly? _enrolledOn => _form.EnrolledOn;
    private bool _loading = true;
    private string? _error;
    private PeriodDto[] _periods = Array.Empty<PeriodDto>();
    private GradeLevelDto[] _gradeLevels = Array.Empty<GradeLevelDto>();
    private PeriodDto? _selectedPeriod;
    private GradeLevelDto? _selectedGrade;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var periodsTask = Api.ListPeriodsAsync(CancellationToken.None);
            var gradesTask = Api.ListGradeLevelsAsync(CancellationToken.None);
            await Task.WhenAll(periodsTask, gradesTask);
            _periods = periodsTask.Result ?? Array.Empty<PeriodDto>();
            _gradeLevels = gradesTask.Result ?? Array.Empty<GradeLevelDto>();
            _selectedPeriod = _periods.FirstOrDefault(p => p.Id == Model.SuggestedPeriodId) ?? _periods.FirstOrDefault();
            _selectedGrade = _gradeLevels.FirstOrDefault(g => g.Id == Model.SuggestedGradeLevelId) ?? _gradeLevels.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load enroll data for student {StudentId}", Model.StudentId);
            _error = ex.Message;
        }
        finally { _loading = false; }
    }

    protected override async Task<EnrollStudentResult?> SubmitAsync(EnrollStudentModel model)
    {
        if (_selectedPeriod is null || _selectedGrade is null || _form.EnrolledOn is null) return null;
        try
        {
            await Api.EnrollStudentAsync(new EnrollStudentRequest(
                model.StudentId, _selectedPeriod.Id, _selectedGrade.Id, _form.EnrolledOn));
            return new EnrollStudentResult(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to enroll student {StudentId}", model.StudentId);
            Error = ex.Message;
            return null;
        }
    }
}
```

### 4.2 `EnrollStudentModel.cs` (new)

```csharp
namespace SchoolCollab.Students.Admin.Components.Students;

/// <summary>Dialog model for the new-enrollment dialog.</summary>
/// <param name="StudentId">Student to enroll (caller fills).</param>
/// <param name="SuggestedPeriodId">Pre-select the active period (best-effort).</param>
/// <param name="SuggestedGradeLevelId">Pre-select the most recent grade (best-effort).</param>
public sealed record EnrollStudentModel(
    Guid StudentId,
    Guid? SuggestedPeriodId = null,
    Guid? SuggestedGradeLevelId = null);

/// <summary>Result of the new-enrollment dialog.</summary>
public sealed record EnrollStudentResult(bool Success);
```

### 4.3 Enrollments table component (inline, in Detail.razor)

Not extracted as a separate `.razor` file because (a) it's a thin
property-grid and (b) extracting it would create a one-off file that
nobody else uses. Rendered inline as:

```razor
<FluentDataGrid Items="@_enrollments.AsQueryable()" TGridItem="StudentEnrollmentDto" MultiLine="true" Class="enrollments-grid">
    <TemplateColumn Title="Period">
        @(_periodNames.TryGetValue(context.PeriodId, out var pn) ? pn : context.PeriodId.ToString()[..8])
    </TemplateColumn>
    <TemplateColumn Title="Grade">
        @(_gradeLevelNames.TryGetValue(context.GradeLevelId, out var gn) ? gn : context.GradeLevelId.ToString()[..8])
    </TemplateColumn>
    <TemplateColumn Title="Enrolled on">@context.EnrolledOn.ToString("d")</TemplateColumn>
    <TemplateColumn Title="Exit date">@(context.ExitDate?.ToString("d") ?? "—")</TemplateColumn>
    <TemplateColumn Title="Status">
        @{
            var isActive = context.ExitDate is null && string.Equals(context.Status, "Active", StringComparison.OrdinalIgnoreCase);
            if (isActive) { <FluentBadge Appearance="Appearance.Accent">Active</FluentBadge>; }
            else { <FluentBadge Appearance="Appearance.Neutral">@context.Status</FluentBadge>; }
        }
    </TemplateColumn>
</FluentDataGrid>
```

(Replaces the raw `PropertyColumn` lines that currently show GUIDs.)

### 4.4 Withdraw (optional, see §6)

`WithdrawEnrollmentDialog.razor` mirrors `EnrollStudentDialog` with a
single `FluentDatePicker` (exit date) and a confirmation text area.
SubmitAsync calls `Api.WithdrawStudentAsync(enrollmentId, new WithdrawStudentRequest(exitDate))`.

---

## 5. CSS changes — `Detail.razor.css` (merge, not overwrite)

The current file is 8 lines. After the rewrite, it must contain **at
least** every class used in the new markup, plus a verbatim copy of the
**`detail-card` popup-clipping fix** from `GuardianDetail.razor.css` and
`GuardiansTab.razor.css` (per §6 of `dialog-ui` skill — that fix is
replicated into every file that hosts a `FluentCard` with
`FluentSelect` / `CodedValueDropdown`).

```css
/* ── Layout ──────────────────────────────────────────────────────── */
.page-container { height: 100%; gap: 0; }
.title-row { flex-shrink: 0; display: flex; align-items: flex-start; gap: 0.75rem; padding-top: 0.25rem; }
.title-block { display: flex; flex-direction: column; gap: 0.15rem; min-width: 0; }
.page-title { margin: 0; }
.page-subtitle { margin: 0; color: var(--neutral-foreground-hint); font-size: 0.9rem; display: flex; flex-wrap: wrap; gap: 0.4rem; align-items: center; }
.title-sep { opacity: 0.6; }
.action-bar { flex-shrink: 0; display: flex; align-items: center; gap: 0.75rem; padding-bottom: 0.75rem; }
.spinner-container { flex: 1; display: flex; align-items: center; justify-content: center; }

/* ── Section header (h3 + inline action bar) ─────────────────────── */
.section-header {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    margin: 1.5rem 0 0.5rem;
}
.section-header h3 { margin: 0; flex: 1; min-width: 0; }

/* ── Profile grid (mirror of GuardianDetail.razor.css) ───────────── */
.profile-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 8px 24px; }
.profile-row { display: flex; gap: 8px; }
.profile-label { font-weight: 600; min-width: 110px; color: var(--neutral-foreground-hint); }
.profile-value { word-break: break-word; }
.muted { color: var(--neutral-foreground-hint); }

/* ── Enrollments table ───────────────────────────────────────────── */
.enrollments-grid { width: 100%; }

/* ── detail-card popup-clipping fix (REQUIRED for FluentSelect +     ──
   CodedValueDropdown inside the profile / enrollments cards) ────── */
.detail-card {
    overflow: visible;
    contain: none !important;
}
::deep .detail-card .fluent-popup-body,
::deep .detail-card .fluent-listbox {
    position: fixed !important;
}
```

### 5.1 Why this CSS
- **Profile grid uses `auto-fit, minmax(260px, 1fr)`** so it collapses
  gracefully on narrow viewports (mobile) without media queries.
- **`section-header` flexbox** holds the `<h3>` on the left and the
  action buttons on the right; the `<h3>` has `flex: 1` so it consumes
  the leftover space, and `min-width: 0` so the long "Guardians" header
  can wrap without breaking the action group.
- **`.detail-card` popup-clipping fix is the same workaround** the
  GuardianDetail / GuardiansTab / GradeLevelWizard all carry, verbatim.
  Without it, the new `FluentSelect` controls in `EnrollStudentDialog`
  (rendered via `FluentMenuProvider` to the layout root) would still
  work, but the page's inline `FluentSelect` for the filter dropdown
  (none in the new design) would clip. We add it anyway as a
  defensive carry-over because future inline selects will rely on it.

### 5.2 Merge-not-overwrite checklist
Before committing, grep the new `Detail.razor` markup for every class
referenced in the CSS and confirm they exist:

```bash
grep -oE 'class="[^"]*"' Detail.razor | sort -u > /tmp/markup.txt
grep -oE '^\.[a-z-]+' Detail.razor.css | sort -u > /tmp/css.txt
# Each class in markup must have a matching rule in CSS.
```

---

## 6. Scope: Withdraw dialog (deferred to follow-up?)

The user asked for "enrollment/transfer." Withdraw is technically
**close-enrollment**, not enroll-or-transfer. Two options:

**Option A (recommended): Include Withdraw in this change.**
- Adds one more button + one new 30-line dialog. Negligible extra scope.
- Closes the "what do I do when a student leaves the school?" gap on
  the same page (otherwise admins have to fall back to the API or
  another tool).
- The "Transfer" button is already half-redundant with Withdraw
  (transfer is "enroll in a new grade + close the old enrollment");
  Withdraw is just "close the old enrollment without opening a new
  one").

**Option B (defer): Skip Withdraw this PR.**
- Smaller diff, easier review.
- The student can still be soft-deleted via the "Delete" action on
  the landing page (which sets `IsDeleted` but does not touch
  enrollments).

**Decision request for user:** include Withdraw in the initial
modernization, or defer to a follow-up?

---

## 7. `GuardiansTab.razor` minor edits

The "Link a guardian" form is currently always visible at the bottom
of the tab. On a long single-page student view, this can push the
Contacts section far below the fold. Plan:

- Wrap the "Link a guardian" `FluentCard` in a `<FluentAccordion>` (or
  an inline `<details>` element) with the summary "Add a guardian
  link", default `closed`.
- Same edit applies to `GuardianDetail.razor`'s "Link a ward" card, but
  the request is for the student view only — leave GuardianDetail
  alone unless the user asks.

This is a **3-line markup change** plus 4 lines of CSS in
`GuardiansTab.razor.css`. Carrying it in the same PR keeps the
modernization cohesive; deferring it is also fine.

---

## 8. Tests

### 8.1 New: `StudentDetailSectionsTests.cs`

bUnit tests that mirror the `EntityGridTests.cs` / `StudentLandingColumnsTests.cs` pattern. Each test instantiates a fake `IStudentsClient` (same `Fake*Client` pattern as the other Admin.Tests.Unit files), renders the new `Detail` component, and asserts the markup contains every section in §2.

Test list:

| # | Name | Asserts |
|---|------|---------|
| 1 | `Renders_All_Five_Sections_On_Single_Page` | Page contains `Profile`, `Enrollments`, `Guardians`, `Contacts` headings, and the action bar; **no `FluentTabs` element** in the markup. |
| 2 | `Profile_Shows_All_Eight_Fields` | `Student #`, `Full name`, `Date of birth`, `Gender`, `Status`, `Created`, `Updated` all rendered. |
| 3 | `Enrollments_Section_Has_Three_Action_Buttons` | `Enroll`, `Transfer`, `Withdraw` buttons are present. |
| 4 | `Enrollments_Section_Transfer_Disabled_When_No_Active_Enrollment` | When `_enrollments` is empty, `Transfer` is `Disabled`. |
| 5 | `Enrollments_Grid_Resolves_Period_And_Grade_Names` | Period/Grade cells show names (not GUID prefixes) when the lookup dicts are populated. |
| 6 | `Guardians_Section_Renders_Embedded_GuardiansTab` | Embedded `<GuardiansTab StudentId="..." />` is in the markup. |
| 7 | `Contacts_Section_Renders_Embedded_ContactsEditor` | Embedded `<ContactsEditor OwnerType="..." OwnerId="..." />` is in the markup. |
| 8 | `Page_Does_Not_Use_FluentTabs` | Regression: any future reintroduction of `<FluentTabs>` fails this test. |
| 9 | `Title_Row_Shows_FullName_And_StudentNumber` | The `<h1>` contains the full name, the subtitle contains the student number, and the Edit button navigates to `/students/{id}/edit`. |
| 10 | `Enroll_Action_Opens_Dialog_And_Reloads_On_Success` | Stub `IDialogService.ShowShellDialogAsync` to return `Success=true`; assert `Api.ListEnrollmentsByStudentAsync` is called twice (initial load + reload). |
| 11 | `Transfer_Action_Passes_StudentId_To_Existing_Dialog` | Stub `IDialogService.ShowShellDialogAsync` to capture the `StudentTransferModel`; assert `model.StudentId == Id`. |
| 12 | `Withdraw_Action_Disabled_When_Has_Active_Enrollment` | (if §6 Option A) |
| 13 | `Cancellation_On_Dispose_Does_Not_Throw` | Render → `ctx.Dispose()` mid-load → no `ObjectDisposedException` (per `ContactsEditorTests` pattern). |

The regression test for "no tabs" (#1, #8) is the most important: it
directly encodes the user's constraint and would catch a future
contributor who reaches for `FluentTabs` to "tidy up" a section.

### 8.2 New: `EnrollStudentDialogTests.cs` (small, optional)

- `SubmitAsync_Valid_Form_Calls_EnrollStudentAsync_With_Request`
- `SubmitAsync_No_Selected_Grade_Returns_Null_And_Keeps_Open`
- `SubmitAsync_Api_Throws_Surfaces_In_Footer`

### 8.3 Existing tests not to regress
- `LandingPageTests` — `Detail.razor` is downstream of `Index.razor`; the
  landing page tests don't touch the detail page, but a build break
  would surface them. Run them after the change.
- `ContactsEditorTests` — `Detail.razor` embeds `ContactsEditor`; if
  embedding changes the cascading context, these tests would break.
  Mitigated by not changing the `OwnerType`/`OwnerId` API.

### 8.4 Manual verification (Playwright)
- `LayoutRenderingTests`-style test in `tests/SchoolCollab.Students.Tests.Playwright/`:
  navigate to `/students/{id}`, assert all four section headings are
  visible without scrolling the user, and the action buttons
  (`Enroll`/`Transfer`/`Withdraw`) are clickable.

---

## 9. Risks & mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Embedding `<GuardiansTab>` and `<ContactsEditor>` adds cascade-context conflicts | Med | Both components are self-contained; the new `Detail.razor` provides the same `OwnerId`/`OwnerType` parameters they already accept. |
| `DialogService.ShowShellDialogAsync` for the enroll dialog needs a tenant-scoped ApiClient | Low | `EnrollStudentDialog` injects `StudentsApiClient` directly (same as `StudentTransferDialog`); the HTTP client already carries the tenant header. |
| `EnrollStudentRequest` requires a period that the student is not already actively enrolled in (server-side uniqueness invariant) | Med | Pre-validate client-side: if `ActiveEnrollment` exists for the chosen period, surface a `FluentMessageBar` warning before submit. |
| New `Withdrawal` mutation may not exist in the current API | Med | `WithdrawStudentAsync` **does** exist (`StudentsApiClient.cs:562`). Verified. If the server doesn't yet support it, the dialog is dead UI; flagged for follow-up. |
| Title row change breaks the "Edit" button position expectation | Low | The Edit button keeps the same `OnClick` handler and `Appearance`. Only the surrounding `.title-row` markup is enriched. |
| `.detail-card` popup-clipping fix breaks scrolling | Low | Already used by `GuardianDetail`, `GuardiansTab`, `GradeLevelWizard`; same rule. If a regression occurs, it would have already been caught there. |
| bUnit test stubs don't cover the `ShowShellDialogAsync` call site | Low | Use the `BunitContext` + fake `IDialogService` pattern that `DialogShellTests` already establishes. |
| CI build is flaky on `SchoolCollab.Admin.dll` (file lock) | Low | Already documented in the prior session summary; retry the build, no code change. |

---

## 10. Step-by-step implementation order

1. **Branch + skeleton.** Create `feat/student-view-modernize` from
   `main`. Copy `Detail.razor` to a new file (don't edit in place yet)
   so the old behavior is a known fallback.
2. **CSS merge first.** Add the new rules to `Detail.razor.css` while
   keeping all old rules. Run `grep` cross-check per §5.2.
3. **Rewrite `Detail.razor` markup** to the §2 structure — no logic
   changes yet, only markup. Build must compile.
4. **Add `EnrollStudentModel.cs` and `EnrollStudentDialog.razor`.**
   Build must compile.
5. **Add `_gradeLevelNames` / `_periodNames` to `OnInitializedAsync`
   (§3.2).** Build must compile; manual smoke test loads the page.
6. **Wire `OnEnrollAsync` + `ReloadEnrollmentsAsync`.** Manual smoke
   test: click Enroll, complete dialog, verify new row appears.
7. **Wire `OnTransferAsync`** (reuses existing dialog). Manual smoke
   test.
8. **Wire `OnWithdrawAsync`** (if §6 Option A) and create
   `WithdrawEnrollmentDialog`.
9. **Collapse the "Link a guardian" form in `GuardiansTab.razor`** (§7).
   Manual smoke test: scroll, add link, confirm visual rhythm.
10. **Add bUnit tests** (§8.1). Full `dotnet test` green.
11. **Add Playwright test** (if applicable).
12. **Full solution build** + `dotnet test` (Admin + Students + Settings).
13. **Commit + PR** with `SCHOOLCOLLAB_ALLOW_PUSH=1`.

---

## 11. Open questions for the user

1. **§6 — Include Withdraw in this PR, or defer?** (Option A vs B.)
2. **§7 — Also collapse the "Link a ward" form on `GuardianDetail.razor`**
   for consistency, or leave GuardianDetail untouched?
3. **Empty state copy** — when a student has zero enrollments, the
   current copy is `"No enrollments yet."` Should the new copy be the
   action-oriented `"No enrollments yet — use Enroll to add the first
   one."`? (Stronger nudge but slightly more verbose.)
4. **Title row** — keep the current minimal title (`StudentNumber` only,
   no name) or use the enriched header (§2.2) showing
   `FirstName LastName` as `<h1>` and `StudentNumber · Status · Current
   grade` as the subtitle?

---

## 12. Verification (post-implementation)

- [ ] `dotnet build` → 0 errors, 0 new warnings.
- [ ] `dotnet test tests/SchoolCollab.Admin.Tests.Unit/` → all pass
      (current 70 + ~13 new = 83).
- [ ] `dotnet test tests/SchoolCollab.Students.Tests.Unit/` → all pass
      (existing 67; no new).
- [ ] Manual: navigate to `/students/{id}`, hard-refresh, scroll
      through all four sections, click Enroll → dialog opens → submit
      → new row appears, current grade subtitle updates.
- [ ] Manual: click Transfer → existing transfer dialog opens → submit
      → row updates, subtitle updates.
- [ ] Manual: click Withdraw → confirm dialog → submit → active
      enrollment shows exit date, subtitle reverts to "—".
- [ ] Manual: soft-delete a student from the landing page → return
      to the detail page → profile badge reads "Deleted", all sections
      still render.
- [ ] Manual: no tenant (DevTenantSwitcher to "(none)") → TenantGate
      fallback message renders, sections do not.
- [ ] Visual: page scrolls smoothly, no layout shift between sections,
      no `FluentTabs` visible in DevTools, no console errors.
- [ ] `git grep "<FluentTab" src/Students/SchoolCollab.Students.Admin/Components/Pages/Students/Detail.razor`
      → 0 matches (regression guard).
