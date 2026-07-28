# Student View: Contacts In Grid + "View all" Anchor Dialog

**Date:** 2026-07-27
**Branch:** `feature/guardian-link-from-student-edit-gradelevel-wizard`
**Status:** Spec
**Related plans:**
- [2026-07-25-guardian-grid-ux-refinement-plan.md](./2026-07-25-guardian-grid-ux-refinement-plan.md) — built the shared `GuardianGrid` with C1/C2/C3 contact columns.
- [2026-07-25-guardian-link-from-student-edit-gradelevel-wizard.md](./2026-07-25-guardian-link-from-student-edit-gradelevel-wizard.md)

---

## 1. Goal

The student **view** page (`Detail.razor`) currently has two redundant
surfaces for guardian contacts:

1. The **Guardians** grid (`StudentGuardiansList` → `GuardianGrid` Linked
   mode) already renders up to 3 contacts per row in the C1/C2/C3 columns
   (channel subtitle + value).
2. A separate **Contacts** section below it (`<GuardianContactsList
   StudentId="Id" />`) re-fetches and re-renders the same guardians'
   contacts as a stack of cards — a second round-trip and a second visual
   telling of the same story.

This plan **removes the standalone Contacts section** and makes the
guardians grid the single source of "how do I reach this student's
guardians?". Because the grid only shows the top 3 contacts, guardians
with **more than 3 contacts** get a small **anchor** affordance — a
`View all (N) contacts` link rendered **underneath the guardian's name**
in the Name cell — that opens a **dialog** displaying the guardian's full
contact list (all channels, ordered by `DisplayOrder`, with verified
badges). Guardians with ≤ 3 contacts have everything inline; the anchor is
not shown.

### 1.1 Requirements (verbatim from the user)

1. **Remove the Contacts section on the student view page** — contacts are
   now contained in the guardians grid.
2. Where a guardian has more than 3 contacts, show a **`View all (N)
   contacts`** anchor **underneath the name** (NOT a separate column) that
   reaches the full contact list.
3. The anchor opens a **dialog** that displays the full contacts of that
   guardian (not a navigation to `GuardianDetail.razor`).
4. The anchor must **never appear in the guardian picker dialog** — it is
   Linked mode only.

## 2. Non-Goals

- Do **not** change the picker (`GuardianPickerDialog` / `GuardianGrid`
  Picker mode). The anchor is **Linked mode only** (the per-student list on
  the view page). Picker search results keep the 4-column Name + C1/C2/C3
  layout, and the picker's Name cell has **no anchor** (the anchor markup
  lives in the Linked-mode Name branch only).
- Do **not** add a new column. The anchor is a second line **inside the
  existing Name cell**, so Linked mode stays at **7 columns** (Name,
  Relationship, C1, C2, C3, Primary, Actions). `LinkedSettings` column
  template is unchanged.
- Do **not** make the contacts dialog an editor. It is **read-only display**
  (channel, value, verified badge, label). Editing a guardian's contacts
  still happens on `GuardianDetail.razor` via the shared
  `ContactsEditor OwnerType=Guardian` (unchanged). A future plan can add an
  "Edit" button inside the dialog that navigates to `GuardianDetail`; not
  in scope here.
- Do **not** change `Edit.razor`. The student **edit** page keeps its own
  layout (it has no standalone Contacts section today — the student's own
  contacts live in the Profile card via `ContactsEditor OwnerType=Student`,
  which is unrelated to *guardian* contacts).
- Do **not** change the `Contact` domain / schema. `DisplayOrder` is already
  the ordering key; this plan only adds a count to an existing projection.

## 3. Design Overview

| Surface | Current | Proposed |
|---|---|---|
| `Detail.razor` Contacts section | `<GuardianContactsList StudentId="Id" />` under an `<h3>Contacts</h3>` header | **Removed.** |
| `GuardianGrid` Linked Name cell | name + Emergency badge inline (`<span class="guardian-name-cell">…</span>`) | **Stacked:** name + Emergency badge on the first line, then a `View all (N) contacts` lightweight button on a second line, shown **only when `TotalContactCount > 3`** (and `OnViewAllContacts` is wired). The button raises `OnViewAllContacts`. |
| `GuardianGrid` Linked column count | 7 cols | **7 cols (unchanged)** — the anchor is inside the Name cell, not a new column. |
| `GuardianGrid` Picker Name cell | name only | **Unchanged** — no anchor, no badge (the anchor lives in the Linked branch only). |
| Full contact list | `GuardianContactsList` cards on the view page | A **dialog** (`GuardianContactsDialog.razor`) opened by the page, scoped to one guardian, loading via `IContactsClient.ListContactsAsync(ContactOwnerType.Guardian, guardianId)`. |
| `StudentGuardianViewDto` | `Contacts` (top 3, `IReadOnlyList<GuardianContactViewDto>`) | Add `int TotalContactCount` (all non-deleted contacts for the guardian). `HasMoreContacts => TotalContactCount > 3`. |
| `ListGuardiansByStudentHandler` | groups contacts, `.Take(3)` | Same query (already loads all non-deleted contacts into memory); project `TotalContactCount = group.Count()` **before** `Take(3)`. |
| `GuardianContactsList.razor` + `.css` + tests | Used only by `Detail.razor` | **Deleted** (replaced by the dialog). |

### 3.1 Why a count on the DTO (not derived from `Contacts.Length`)

`Contacts.Length == 3` is **ambiguous**: it is returned both for guardians
with exactly 3 contacts and for guardians with more than 3 (the projection
caps at 3). The grid therefore cannot tell whether a full C3 row has
additional siblings. An explicit `TotalContactCount` removes the ambiguity
and also lets the anchor render a precise `View all (N) contacts` label.

### 3.2 Why under the name (not a column)

A dedicated "All" column would be empty (em-dash) for the common case of ≤ 3
contacts, adding a mostly-empty column to a grid that already has 7.
Putting the anchor on a second line inside the Name cell keeps the column
count stable, groups the anchor visually with the guardian it belongs to,
and keeps the Picker's Name cell untouched (the anchor markup is in the
Linked branch only — satisfying requirement #4 with no mode flag needed).

## 4. Detailed Design

### 4.1 Remove the Contacts section (`Detail.razor`)

Delete the block at `Detail.razor:203–211`:

```razor
@* ── Contacts (embedded, not in a tab) — guardians & their
   contacts. … Edits to a guardian's contact happen on GuardianDetail.razor. *@
<div class="section-header"><h3>Contacts</h3></div>
<GuardianContactsList StudentId="Id" />
```

The Guardians section (immediately above) becomes the last section in the
detail card. No layout/CSS change is expected beyond removing the block;
the `min-height` rule added to `Detail.razor.css` for the guardians grid is
unaffected (the enrollments-vs-guardians equal-length rule still applies).

### 4.2 DTO + handler: `TotalContactCount`

**`StudentGuardianViewDto.cs`** — add an `init`-only property (keeps the
positional constructor back-compat; existing construction sites that omit
it get `0`, which is correct for any caller that does not set it):

```csharp
/// <summary>
/// Total number of non-deleted contacts for this guardian (NOT capped at
/// 3). Used by the guardians grid to decide whether to show the "View all
/// (N) contacts" anchor (<see cref="HasMoreContacts"/> =
/// <c>TotalContactCount > 3</c>). <see cref="Contacts"/> carries only the
/// top 3; this count is the authoritative "are there more?" signal
/// (Contacts.Length == 3 is ambiguous between exactly-3 and more-than-3).
/// </summary>
public int TotalContactCount { get; init; }

/// <summary>True when the guardian has more than the 3 contacts shown
/// inline in the grid. Convenience over <see cref="TotalContactCount"/>.
/// </summary>
public bool HasMoreContacts => TotalContactCount > 3;
```

**`ListGuardiansByStudentHandler.cs`** — the handler already loads **all**
non-deleted contacts for the linked guardians into memory
(`db.Contacts … ToListAsync`), groups by owner, then `.Take(3)`. Project
the group count **before** the `Take(3)`:

```csharp
var contactsByOwner = contacts
    .GroupBy(c => c.OwnerId)
    .ToDictionary(
        g => g.Key,
        g => new {
            Top = g.OrderBy(c => c.DisplayOrder)
                   .Take(3)
                   .Select(c => new GuardianContactViewDto(c.Channel, c.Value, c.CountryCode))
                   .ToList(),
            Total = g.Count(),           // all non-deleted contacts for this guardian
        });

return rows.Select(r =>
{
    var entry = contactsByOwner.TryGetValue(r.GuardianId, out var e) ? e : null;
    var list = entry?.Top;
    var c = list?.FirstOrDefault();
    return new StudentGuardianViewDto(
        r.GuardianId, r.StudentId, r.Role, r.RelationshipCodedValueId,
        r.IsEmergencyContact, r.FirstName, r.LastName, r.DisplayName, r.TitleCodedValueId,
        c?.Channel, c?.Value, c?.CountryCode)
    {
        Contacts = (IReadOnlyList<GuardianContactViewDto>?)list?.AsReadOnly()
                   ?? System.Array.Empty<GuardianContactViewDto>(),
        TotalContactCount = entry?.Total ?? 0,
    };
}).ToArray();
```

No DB schema change, no new query — the count is computed from the already-
materialized group. Cache tags are unchanged (`["guardians"]`).

`ListGuardiansHandler` (the landing/picker list) is **not** changed: the
picker does not render the anchor. Its `StudentGuardianViewDto` projections
simply leave `TotalContactCount` at the default `0` (the picker never reads
it).

### 4.3 `GuardianGrid` Linked Name cell: the "View all (N) contacts" anchor

The anchor is a **second line inside the existing Linked-mode Name cell** —
no new column, no change to `LinkedSettings.GridTemplateColumns` (stays
7 columns). The Picker Name cell is untouched (the anchor markup lives in
the Linked branch only, so the picker cannot inherit it).

New accessor + callback on `GuardianGrid<TItem>`:

```csharp
/// <summary>Total non-deleted contacts for the row's guardian. Used by
/// the Linked-mode Name cell to decide whether to render the "View all
/// (N) contacts" anchor (shown only when TotalContactCount > 3).
/// Null/0 = no anchor. Picker mode never reads this.</summary>
[Parameter] public Func<TItem, int>? GetTotalContactCount { get; set; }

/// <summary>Raised when the user clicks the per-row "View all (N)
/// contacts" anchor in the Linked-mode Name cell. The parent opens
/// <c>GuardianContactsDialog</c> for the row's guardian. Not delegated to
/// → the anchor is hidden. The grid does NOT open the dialog itself — it
/// stays a dumb presenter (no IDialogService injection), consistent with
/// the existing OnEdit / OnRemove pattern. Picker mode never wires this.
/// </summary>
[Parameter] public EventCallback<TItem> OnViewAllContacts { get; set; }
```

Linked-mode Name cell markup (replaces the current
`<span class="guardian-name-cell">…</span>` block):

```razor
<TemplateColumn TGridItem="TItem" Title="Name" Context="row">
    @{
        var total = GetTotalContactCount?.Invoke(row) ?? 0;
        var showAnchor = total > 3 && OnViewAllContacts.HasDelegate;
    }
    <div class="guardian-name-cell">
        <div class="guardian-name-line">
            <span>@(GetName?.Invoke(row) ?? "")</span>
            @if (GetIsEmergencyContact?.Invoke(row) == true)
            {
                <FluentBadge Appearance="Appearance.Accent">Emergency</FluentBadge>
            }
        </div>
        @if (showAnchor)
        {
            <FluentButton Appearance="Appearance.Hypertext"
                          Size="ButtonSize.Small"
                          Class="guardian-view-all-contacts"
                          Title="@($"View all {total} contacts for {GetName?.Invoke(row)}")"
                          OnClick="@(() => OnViewAllContacts.InvokeAsync(row))">
                View all (@total) contacts
            </FluentButton>
        }
    </div>
</TemplateColumn>
```

**CSS (`GuardianGrid.razor.css`)** — the existing `.guardian-name-cell` is
`display:flex; align-items:center; gap:8px` (a single inline row). Change
it to a vertical stack so the anchor sits on its own line beneath the
name+badge row:

```css
.guardian-name-cell {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 4px;
}
.guardian-name-line {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-wrap: wrap;
}
.guardian-view-all-contacts {
    /* hypertext button: looks like a link, not a button */
    min-height: 0;
    padding: 0;
    font-size: 0.8rem;
}
```

> **Open question (§6.1):** button `Appearance="Hypertext"` vs a real
> `<a>`/`<FluentAnchor>`. Hypertext keeps it keyboard-focusable and
> consistent with the repo's `FluentButton` usage; an anchor is more
> semantically "link". The spec uses Hypertext; confirm at implementation.

The Picker Name cell (`Title="Name"` in the Picker branch) is **not**
modified — it stays a single `<span>@(GetName?.Invoke(row) ?? "")</span>`,
so the picker never shows the anchor (requirement #4).

### 4.4 `StudentGuardiansList`: forward the callback

`StudentGuardiansList.razor` is presentational only (no dialog opens, no
API calls) — it forwards a new `EventCallback<StudentGuardianViewDto>` to
`GuardianGrid`, and the page (`Detail.razor`) owns the dialog open.

```csharp
/// <summary>Fired when the user clicks the per-row "View all (N)
/// contacts" anchor in the Name cell (only for guardians with > 3
/// contacts). The page opens <c>GuardianContactsDialog</c> for the row's
/// guardian. Not delegated to → the anchor is hidden.</summary>
[Parameter] public EventCallback<StudentGuardianViewDto> OnViewAllContacts { get; set; }
```

Wire it through to `GuardianGrid`:

```razor
<GuardianGrid …
              GetTotalContactCount="@(g => g.TotalContactCount)"
              OnViewAllContacts="OnViewAllContacts" />
```

### 4.5 `GuardianContactsDialog.razor` — the full-contacts dialog

A new **read-only** dialog component that displays every contact for one
guardian. It is opened by `Detail.razor` via `IDialogService`.

**File:** `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianContactsDialog.razor`

**Parameters:**

```csharp
[Parameter, EditorRequired] public Guid GuardianId { get; set; }
[Parameter] public string? GuardianName { get; set; }   // dialog title
[Parameter] public string? Subtitle { get; set; }       // e.g. "Father · Primary"
```

**Data load:** inject `IContactsClient`; on `OnParametersSetAsync` call

```csharp
_contacts = await ContactsApi.ListContactsAsync(ContactOwnerType.Guardian, GuardianId, _cts.Token);
```

Order by `DisplayOrder` ascending (the API already returns ordered, but
sort client-side defensively). Cancellation: a single `CTS` guarded by a
`_disposed` flag, mirroring `ContactsEditor` / `GuardianContactsList`.

**Render:** a `FluentDialog` with:
- **Header:** `GuardianName` (and `Subtitle` when present) — e.g.
  `"Mr. John Smith — Father · Primary"`.
- **Body:** one row per contact (reuses the visual language of the deleted
  `GuardianContactsList` card row): channel glyph + channel name, formatted
  value (`[+CC] value`), `Label` when present, and a `Verified` badge when
  `IsVerified`. The first row (lowest `DisplayOrder`) is the visual anchor
  (slightly emphasized); non-preferred rows are de-emphasized, exactly as
  `GuardianContactsList` did. Empty state: a `FluentMessageBar` info "No
  contacts on file for this guardian."
- **Footer:** a single **Close** button (no OK/Cancel — this is not a form,
  so the `DialogShellBase` / `ShowShellDialogAsync` form pattern does NOT
  apply; use `IDialogService.ShowAsync<GuardianContactsDialog>(parameters)`
  with a `DialogParameters` carrying `Title = GuardianName` and
  `PrimaryAction`/`SecondaryAction` disabled, OR a self-contained
  `<FluentDialog>` that calls `DialogService.Cancel()`/close on the Close
  button).

**Styling:** extract the shared contact-row markup into a small partial or
duplicate the compact row CSS into `GuardianContactsDialog.razor.css`. The
deleted `GuardianContactsList.razor.css` is the source for the row styling
(channel glyph, value, verified badge, de-emphasized non-primary). Do
**not** keep `GuardianContactsList.razor` around solely for its CSS — copy
what is needed.

**Opening from `Detail.razor`:**

```csharp
private async Task OnViewAllContactsAsync(StudentGuardianViewDto g)
{
    var subtitle = string.IsNullOrWhiteSpace(ResolveRelName(g))
        ? g.Role.ToString()
        : $"{ResolveRelName(g)} · {g.Role}";
    await DialogService.ShowAsync<GuardianContactsDialog>(
        title: FormatGuardianName(g),        // "Mr. John Smith"
        parameters: new()
        {
            { nameof(GuardianContactsDialog.GuardianId), g.GuardianId },
            { nameof(GuardianContactsDialog.GuardianName), FormatGuardianName(g) },
            { nameof(GuardianContactsDialog.Subtitle), subtitle },
        },
        options: new() { DialogSize = DialogSize.Medium });
}
```

> The exact `ShowAsync` signature varies by FluentUI version; verify against
> the `GuardianPickerDialog` / `EnrollStudentDialog` call sites in
> `Detail.razor` (`DialogService.ShowShellDialogAsync<…>` is the form-dialog
> helper; for a read-only dialog use the lower-level `ShowAsync`). If the
> repo has no read-only `ShowAsync` precedent, add a thin
> `DialogServiceExtensions.ShowReadonlyDialogAsync` helper so the pattern is
> reusable. **(§6.2 open question.)**

Wire `OnViewAllContacts="OnViewAllContactsAsync"` on the
`<StudentGuardiansList>` element in `Detail.razor`.

### 4.6 Delete `GuardianContactsList`

After the Contacts section is removed, `GuardianContactsList.razor` has
exactly one consumer (`Detail.razor`) and that consumer is gone. Delete:

- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianContactsList.razor`
- `src/Students/SchoolCollab.Students.Admin/Components/Students/GuardianContactsList.razor.css`
- `tests/SchoolCollab.Admin.Tests.Unit/GuardianContactsListTests.cs`

Update `tests/SchoolCollab.Admin.Tests.Unit/StudentDetailSectionsTests.cs`:
remove / replace the assertions that require the Contacts section and the
`<GuardianContactsList>` tag (lines ~271, 289–290, 322–343). Add a
negative assertion: `Detail.razor` should **no longer** contain
`<GuardianContactsList` or an `<h3>Contacts</h3>` section header for the
guardian-contacts block (careful: the student's **own** Profile card may
still mention "Contacts" — scope the assertion to the guardian-contacts
region or to the literal `<GuardianContactsList` tag absence).

## 5. Test Plan

**Unit (source-level, `SchoolCollab.Admin.Tests.Unit`):**
- `GuardianGridTests.cs`:
  - Linked mode still renders **7** columns (Name, Relationship, C1, C2, C3,
    Primary, Actions) — the anchor is **not** a column. `LinkedSettings`
    column template is unchanged.
  - The Linked Name cell contains the `View all (@total) contacts` button
    text, gated by `GetTotalContactCount` (> 3) and `OnViewAllContacts.HasDelegate`.
  - `GetTotalContactCount` + `OnViewAllContacts` parameters exist.
  - **Picker mode is unchanged:** its Name cell contains neither
    `guardian-view-all-contacts` nor `OnViewAllContacts` (regression guard
    for requirement #4). One way to assert: the picker Name branch does not
    contain `guardian-view-all-contacts`, and `OnViewAllContacts` appears
    only inside the Linked branch.
- `StudentDetailSectionsTests.cs`:
  - `Detail.razor` no longer contains `<GuardianContactsList`.
  - The guardian-contacts `<h3>Contacts</h3>` section is gone.
  - `<StudentGuardiansList … OnViewAllContacts="OnViewAllContactsAsync" />`
    is wired.
  - Enrollments + Guardians equal-`min-height` rule still present in
    `Detail.razor.css` (regression guard for the prior change).
- New `GuardianContactsDialogTests.cs` (source-level):
  - Component injects `IContactsClient`, calls
    `ListContactsAsync(ContactOwnerType.Guardian, GuardianId, …)` in
    `OnParametersSetAsync`.
  - Renders one row per contact, ordered by `DisplayOrder`, with a
    `Verified` badge when `IsVerified`.
  - Empty-state message when the API returns null/empty.
  - Cancellation `CTS` + `_disposed` flag present (mirrors
    `ContactsEditor` pattern).
- Delete `GuardianContactsListTests.cs`.

**Unit (CQRS, `SchoolCollab.Students.Tests.Unit`):**
- `GuardianContactsCqrsTests.cs` (or a new `ListGuardiansByStudentCountTests`):
  - A guardian with 5 non-deleted contacts → `Contacts.Count == 3` **and**
    `TotalContactCount == 5` and `HasMoreContacts == true`.
  - A guardian with exactly 3 contacts → `TotalContactCount == 3`,
    `HasMoreContacts == false`.
  - A guardian with 2 contacts → `TotalContactCount == 2`,
    `Contacts.Count == 2`.
  - Soft-deleted contacts are excluded from `TotalContactCount` (the query
    filters `!c.IsDeleted`).

**Build:** build `SchoolCollab.Students.Admin` and
`SchoolCollab.Students.Core`; run both unit test projects with
`-p:BuildProjectReferences=false` to avoid file-lock issues while VS / API /
Worker processes are running.

## 6. Open Questions / Decisions

1. **Anchor control type:** `FluentButton Appearance="Hypertext"` (spec
   default — keyboard-focusable, consistent with repo button usage) vs a
   real `<FluentAnchor>`/`<a>` (more semantically a "link"). **Recommend
   Hypertext** for consistency; switch to anchor if a11y/semantics review
   prefers it.
2. **Read-only dialog open helper:** does the repo already have a
   `ShowAsync`-based read-only dialog precedent, or should a small
   `DialogServiceExtensions.ShowReadonlyDialogAsync` helper be added?
   Check `EnrollStudentDialog` / `GuardianFormDialog` (form dialogs via
   `ShowShellDialogAsync`) and any info-only dialog in the repo before
   deciding. The dialog itself is **not** a `DialogShellBase` form (no
   model/result/OK-Cancel) — keep it a plain `FluentDialog` with a Close
   button.
3. **Anchor trigger condition:** the spec shows the anchor only when
   `TotalContactCount > 3` (matches the user's "where contacts are more
   than 3" wording). The label example `View all (3) contacts` could be read
   as "also show at exactly 3". **Recommend > 3** (a guardian with 3
   contacts has nothing more than what C1–C3 already show, so a "view all"
   that opens the same 3 contacts is redundant). If the user wants the full
   list (with verified badges) reachable even at ≤ 3, switch to
   `TotalContactCount > 0` — one-line change. **Confirm.**
4. **Reuse vs duplicate `GuardianContactsList` row markup:** the dialog
   needs the same channel/value/verified row. Extract a shared
   `GuardianContactRow.razor` partial (used by the dialog) vs duplicating
   the small amount of markup/CSS. **Recommend extract** if the row markup
   is more than ~15 lines; otherwise duplicate. Decide at implementation.

## 7. Suggested Implementation Order

1. **DTO + handler** (`§4.2`) — add `TotalContactCount` to
   `StudentGuardianViewDto`; project the group count in
   `ListGuardiansByStudentHandler`. Add CQRS unit tests (`§5`). Low risk,
   no UI change yet.
2. **`GuardianGrid` Name-cell anchor** (`§4.3`) — add `GetTotalContactCount`
   + `OnViewAllContacts` parameters and the `View all (N) contacts` line
   inside the **Linked** Name cell (only). Update `.guardian-name-cell` CSS
   to a vertical stack. Do **not** touch the Picker Name cell. Update
   `GuardianGridTests` (7 columns; anchor in Linked Name cell; picker
   unchanged).
3. **`GuardianContactsDialog`** (`§4.5`) — build the read-only dialog
   component + its unit tests. Verify the `IContactsClient.ListContactsAsync`
   call and the cancellation pattern.
4. **Wire `StudentGuardiansList` + `Detail.razor`** (`§4.4`, `§4.5`) —
   forward `OnViewAllContacts`; add `OnViewAllContactsAsync` to `Detail.razor`
   opening the dialog; wire it on the `<StudentGuardiansList>` element.
5. **Remove the Contacts section** (`§4.1`) + **delete `GuardianContactsList`**
   (`§4.6`) — delete the section markup, the component, its CSS, and its
   tests; update `StudentDetailSectionsTests`.
6. **Full build + test pass.**

Steps 1 and 2 can land before the dialog exists (the anchor raises a
callback the parent does not yet wire → `OnViewAllContacts.HasDelegate` is
false → the anchor is hidden, which is the ≤-3 behaviour anyway). Step 5
is best done last so the old surface remains available while the new one
is wired.

## 8. Risks

- **`StudentGuardianViewDto` is a positional record.** Adding
  `TotalContactCount` as an `init` property (not a constructor parameter)
  is non-breaking — existing callers compile unchanged and get the default
  `0`. Verify no caller constructs the DTO via object-initializer in a way
  that would now conflict. The handler is the only setter.
- **`ListGuardiansByStudentHandler` memory:** it already materializes all
  contacts per guardian; adding `g.Count()` does not change the query
  shape. No new N+1 risk.
- **Read-only dialog pattern:** if the repo has no `ShowAsync` precedent,
  the dialog-open code in `Detail.razor` needs care to match FluentUI 4.14.x
  `IDialogService` API. Mitigate by mirroring an existing dialog call site;
  if none is read-only, add the `ShowReadonlyDialogAsync` helper (§6.2) and
  unit-test it lightly.
- **Picker contamination:** the anchor must not leak into Picker mode. The
  spec puts the anchor markup in the **Linked Name branch only** and adds a
  regression test that the picker Name cell does not contain
  `guardian-view-all-contacts`. Verify the two Name `<TemplateColumn>`s stay
  separate (they already differ — Linked has the Emergency badge, Picker
  does not).
- **Test churn:** `StudentDetailSectionsTests` and
  `GuardianContactsListTests` both assert on the deleted section/component.
  Sequence the deletion (step 5) after the new dialog is wired so there is
  no window where the view page has neither surface.
- **`.guardian-name-cell` CSS change:** switching it from a single inline
  row to a vertical stack affects only the Linked Name cell (the picker
  uses a bare `<span>`, not `.guardian-name-cell`). Verify the Emergency
  badge still sits inline with the name on the first line
  (`.guardian-name-line` wraps name + badge).