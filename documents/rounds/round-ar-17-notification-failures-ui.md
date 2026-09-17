Provider: pi/ollama profile (exact ids: orchestrator ollama-cloud/glm-5.3-flash, worker ollama/deepseek-v4-flash:0731-cloud, reviewer ollama-cloud/deepseek-v4.1-flash, ui-tester ollama/minimax-m3:cloud) — Tier 3 FULL FOUR-AGENT, Phase 4 slice E2b (notification failure surfacing UI)

# Round ar-17-notification-failures-ui (WS-E2b)

**Round base:** branch `stack/17-ar-17-notification-failures-ui`, cut by the parent at **`c7c5b4ac`** — the tip of `stack/16-ar-16-channel-delivery` (PR **#240**, green, **unmerged**). The round is therefore **stacked on ar-16**, not on `main`: it consumes ar-16's read side (`GET /{id}/notification-failures` + `NotificationFailureDto`), which does **not** exist on `main` (verified: 0 occurrences of `NotificationFailureDto` at `main`). **Owner decision (2026-09-17): path B — keep the stack.** ar-17's PR targets `stack/16-ar-16-channel-delivery` (#240); when #240 merges, this nested single-layer stack does **not** auto-retarget, so the ar-17 PR must be retargeted to `main` manually (`gh pr edit <n> --base main`) and the branch rebased. **F5 (`Failures (N)` tab count) — owner decision: carried as a follow-up, not delivered in this round.** The final PR base is confirmed at PR time.

**Owner decision on residual (7), the unpublish→Draft gate hole: OPTION A — FIXED in-round after acceptance** (see the final section of this document).

**Fact-check corrections applied by the parent after planning:** (i) base line above; (ii) both planning assumptions verified true on disk — `StudentsApiClient.ListSubscribedContactsAsync(…)` exists (`src/Students/SchoolCollab.Students.Application/Services/StudentsApiClient.cs:1931`, returns `SubscribedContactDto[]?`) and `Detail.razor` **already injects both** `AssignmentsApiClient Api` and `StudentsApiClient StudentsApi` (`Detail.razor:10-11`), so decision (b)'s primary route needs **no new DI** and **no contract change** — the fallback additive `RecipientLabel` route is expected to stay unused. The worker still re-confirms the id-universe question at step 2.

**Execution mode:** Tier 3 FULL FOUR-AGENT — parent plans + accepts, one worker implements, one static reviewer verifies, UI tester bug-hunts the delivered UI.

**Context:** ar-16 (`rounds/round-ar-16-channel-delivery.md`, CLOSED) shipped the delivery core + the read side: tenant-scoped `NotificationLog`, and `GET /assignments/{id}/notification-failures` returning `NotificationFailureDto[]` (failed rows only). This round is the **UI half (E2b)** — purely presentational over that endpoint.

**Sources read & verified at plan time:** `round-ar-16-channel-delivery.md`; breakdown `assignment-request-go-forward-breakdown.md` §Phase 4 (E2b row + E2 acceptance clause "bounced/failed sends surface on the author dashboard"); impl-details `assignment-request-implementation-details.md` §2 WS-E; code on disk: `ContractTypes.cs` (`ContactChannelDto` Email/SMS/WhatsApp; `NotificationKindDto` Publish/Reminder/Completion/Overdue; **`NotificationFailureDto(Guid RecipientId, Guid ContactId, ContactChannelDto Channel, NotificationKindDto Kind, int Attempt, string? FailureReason, DateTimeOffset? NextRetryAt)` — verified, NO duplicated `RecipientId`**; note: no recipient display label, only GUIDs), `AssignmentRoutes.cs` (`group.MapGet("/{id:guid}/notification-failures", ...)` inside the `MapAssignmentRoutes` group — endpoint-grouping rule honoured; plain 200 DTO array, no feature flag, 404 not modelled — the query returns an empty array for an unknown id), `GetNotificationFailures.cs` (handler projects failed rows only, tenant-scoped by the global query filter, ordered RecipientId → Attempt), `AssignmentsApiClient.cs` (no failures method yet; `JsonStringEnumConverter` already registered for `ContactChannelDto` but **NOT** for `NotificationKindDto` — the client must add it), `Detail.razor` (FluentTabs: Overview / Recipients / Submissions / Guardian Sign-off; `SignOffSection` self-loads inside its tab; `_loadCts` + `_disposed` posture; recipients/submissions loaded after the main load), `SignOffSection.razor` (the established self-loading section pattern: gate flag → busy → error → empty → table), `Assignments/Index.razor` (row badges, `FluentAnchor` per row); rules: `blazor-components.md` (§CTS loads-vs-mutations, §Loading states, §Error boundaries, §CSS isolation incl. no `<style>`/no inline `style`, §FluentUI component-params-not-CSS, §FluentAnchor in grids), `section-card.md` (page-alert error pattern; NOTE: that rule targets the Students `SectionCard` — the **local** Assignments precedent is `SignOffSection`/`ResourcesSection`, which this round follows), `testing.md`, `dotnet-best-practices.md`; skills: `fluentui-icons`, `fluentui-component-props` (`dialog-ui`/`input-width-scale` not needed — no dialog, no form fields in a read-only list).

## Plan

### Goal

Surface failed notification deliveries on the author/admin assignment surface so a teacher can see **which guardian contact did not receive an assignment notification, on which channel, for which kind, after how many attempts, and why** — and whether a retry is still scheduled or the failure is terminal. The round is **UI-only over ar-16's read side**: render the delivered `GET /assignments/{id}/notification-failures` endpoint; no mutation, no new data model.

### Placement decision (parent, with rationale)

**ACCEPTED — new `NotificationFailuresSection.razor` (+ `.razor.css`) rendered from `Detail.razor` inside a conditional FluentTab "Notifications"**, following the established Assignments section pattern (`SignOffSection`: gate flag → `_busy` → `_error` → empty → table; self-loads its own data; CTS + `_disposed` posture). The tab is rendered only when the assignment has ever been publishable — `_item.Status` is `Published`, `Scheduled`, or `Closed` — because `NotificationLog` rows exist only after a publish. Label carries the count when the section has loaded failures: `Failures (N)`.

Alternatives considered and rejected (one line each):

- **Own page** (`/assignments/{id}/notification-failures`) — rejected: fragments navigation for a single read-only list; the detail page is already the teacher's per-assignment surface and the tab costs one click.
- **Section below the tabs** (like the inline Approval panel in Overview) — rejected: buries an operational alert under four tabs of Overview content; a dedicated tab keeps the failures list inspectable without polluting the always-visible Overview.
- **Badge/count on the detail header** — rejected: needs the failure count at header-render time (an always-on extra fetch for every detail view, including drafts that can have no rows), and a bare count without the tab hides the "why".

**Visibility trigger (decided):** tab rendered for `Status ∈ {Published, Scheduled, Closed}`; the section self-loads and renders a `FluentMessageBar Intent="MessageIntent.Info"` empty state when the API returns zero rows. No **`Index.razor` "N failures" indicator** — rejected: Index rows carry no failure data and a per-row failure fetch (or a new aggregate-count endpoint) is a backend/scope expansion this UI-only round will not take.

### In scope

1. `NotificationFailuresSection.razor` + `NotificationFailuresSection.razor.css` — the section component (CSS isolation only, no `<style>` blocks, no inline `style`).
2. `AssignmentsApiClient.GetNotificationFailuresAsync(Guid id, CancellationToken ct)` — GET, 200 → `NotificationFailureDto[]` (`[]` never null from the API), plus the missing `JsonStringEnumConverter<NotificationKindDto>` registration.
3. **Client-side contact-label enrichment** (decision (b), primary route): the section (or `Detail.razor` before rendering it) maps `ContactId` → a human display label using the already-injected `StudentsApiClient` contact list the Publish dialog already uses (`ListSubscribedContactsAsync(ContactOwnerType.Guardian, null, SubscriptionScope.AllAssignments)`); unmatched ids fall back to a short contact id. No backend change on this route.
4. Wiring into `Detail.razor` (conditional tab + section), loading/empty/error states, tenant-safe reads (the endpoint is already tenant-scoped; the client adds nothing to leak).
5. bUnit tests for the section + client-path coverage of the new ApiClient method (see binding list).
6. `documents/configuration.md` rows — **only if** something configurable is added; expected **not** to be needed (no new flag/parameter is planned).

### Out of scope (do NOT touch)

- **Any mutation**: retry / requeue / resend, acknowledge / dismiss, mark-as-read — requeue belongs to **E3** (`Assignments.Worker`).
- The `GetNotificationFailures` query/endpoint/contract **unless** the fallback additive field from decision (b) is triggered (see below) — no schema change either way; **no migration** is expected in this round.
- The **Families ward surface** (guardians never see failure lists).
- SMS/WhatsApp provider work, bounce/webhook ingestion, delivery receipts.
- `Index.razor` failure indicators; any other bounded context; any feature flag.

### Decisions (parent)

- **(a) Read-only vs actions: READ-ONLY.** No retry/requeue/resend/acknowledge affordances anywhere in the section (a `FluentAnchor` to nothing, a disabled button, or a "coming soon" affordance is worse than absence). Requeue is E3's deliverable; the section is built so a future action column can be appended without restructuring.
- **(b) Does `NotificationFailureDto` carry enough?** **Almost.** It carries `RecipientId`/`ContactId` (GUIDs only), `Channel`, `Kind`, `Attempt`, `FailureReason`, `NextRetryAt` — verified on disk. A teacher cannot act on a bare GUID. **Primary route — no contract change:** enrich client-side from the `StudentsApiClient` contact list (the same list the Publish dialog offers, which is the universe the publish recipients were selected from), rendering `DisplayName` (+ email if the DTO exposes one) with a short-id fallback. **Fallback additive change (only if verification fails):** add one optional record parameter `string? RecipientLabel = null` to `NotificationFailureDto` (additive, non-breaking, no migration — it is a Contracts record, not an entity column), populated best-effort by `GetNotificationFailuresHandler` via the ar-16 Api-side contact-resolver pattern, null when unresolvable (UI then falls back to the short id). **TO VERIFY (worker, first step):** confirm `ListSubscribedContactsAsync(ContactOwnerType.Guardian, null, SubscriptionScope.AllAssignments)` returns every contact id that can appear as a publish `ContactId` (i.e. the publish dialog's selectable universe equals the recipients' `ContactId` universe). If yes → primary route only, no contract edit; if no → apply the documented fallback additive change and record the deviation in the worker report.
- **(c) `NextRetryAt == null` (terminal) vs a future retry time:** terminal rows render a `FluentBadge Appearance="Appearance.Neutral"` **"Failed — no more retries"** (attempt count shown in the Attempts column); rows with a future `NextRetryAt` render `Appearance="Appearance.Lightweight"` **"Retry scheduled \<local time\>"** (via `ToLocalTime().ToString("g")`, matching the Detail date convention). Never a render-unsafe appearance (FluentBadge validates Accent/Neutral/Lightweight only — the ar-16/ar-13 documented deviations).
- **(d) Grouping:** **flat table, no grouping.** Failed-row counts are small (per recipient per kind, retry-capped at 5); the handler already orders `RecipientId → Attempt`. Columns: **Recipient** (label or short id) · Channel (description) · Kind (description) · Attempt · Reason (`FailureReason ?? "—"`; truncated visually via CSS `text-overflow`, full text in the cell `title`) · Retry (per (c)). Grouping by kind/channel adds nothing at these row counts.
- **(e) Empty state:** `FluentMessageBar Intent="MessageIntent.Info"` — "No failed notifications." (Info, not Success — the null-sender/unconfigured-SMTP path legitimately produces zero rows, per ar-16 decision (d); calling that "success" would be misleading). Error state: a `FluentMessageBar Intent="MessageIntent.Error"` with `ex.Message`, logged at Warning — the `SignOffSection` posture; a load failure must never silently render as "no failures".

### Expected files

- **New:** `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/NotificationFailuresSection.razor`
- **New:** `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/NotificationFailuresSection.razor.css`
- **Edit:** `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` — `GetNotificationFailuresAsync` + `JsonStringEnumConverter<NotificationKindDto>`
- **Edit:** `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Detail.razor` — conditional tab + `<NotificationFailuresSection AssignmentId="@Id" CanHaveFailures="…" />` (gate param; no Detail-side failure fetch)
- **Conditional edit (only on the fallback route):** `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` (+ `string? RecipientLabel = null`) and `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Queries/GetNotificationFailures/GetNotificationFailures.cs` (populate best-effort)
- **Tests (new):** `tests/SchoolCollab.Assignments.Tests.Unit/NotificationFailuresSectionBunitTests.cs` (+ rows in the existing `AssignmentDetailBunitTests.cs` for the tab-gating if cleaner)
- **Tests (edit, only on the fallback route):** `tests/SchoolCollab.Assignments.Tests.Unit/GetNotificationFailuresQueryHandlerTests.cs` (label populated / null-fallback)

### Ordered steps (single worker pass)

1. Read the rules before coding: `dotnet-best-practices.md` (+ skill — ArchitectureTests enforce the "Never" list), `blazor-components.md` (CTS loads-vs-mutations, loading states, CSS-isolation sections, FluentUI-params-not-CSS), `section-card.md`, `testing.md`; skills `fluentui-icons`, `fluentui-component-props`.
2. **Verification first (decision (b)):** confirm the `StudentsApiClient` contact list covers the recipients' `ContactId` universe (read `StudentsApiClient.ListSubscribedContactsAsync` + its source endpoint + the Publish dialog usage). Record the outcome; pick primary vs fallback route.
3. `AssignmentsApiClient.GetNotificationFailuresAsync` (+ the `NotificationKindDto` enum converter) — follow the `ListSignOffStatusesAsync` shape (GET, no special statuses needed: the API always returns 200 with an array).
4. `NotificationFailuresSection.razor` + `.razor.css` — self-loading section (gate param, `_busy`/`_error`/rows, CTS per the loads-vs-mutations rule, `_disposed` guards, `@key` on rows, contact-label enrichment, terminal-vs-retry badge, FluentUI component parameters over CSS).
5. Wire into `Detail.razor` — conditional tab for `Status ∈ {Published, Scheduled, Closed}`, section self-loads (no extra Detail fetch, mirroring `SignOffSection`).
6. bUnit tests (binding list below) + run `dotnet build SchoolCollab.sln` and the affected suites.
7. Write `documents/rounds/.ar-17-worker-report.md` (changed files, build/test verdicts, deviations) **before** finishing — mandatory.

### Binding test list (round acceptance)

bUnit (`NotificationFailuresSectionBunitTests.cs`, MockHttpMessageHandler pattern — no Moq/NSubstitute for HTTP):

1. **Rows render:** a 200 with 2 failed DTOs renders two rows with channel/kind descriptions, attempt, reason, and the retrying badge ("Retry scheduled …") for a future `NextRetryAt`.
2. **Empty state:** a 200 with `[]` renders the Info "No failed notifications." message bar (and never a table).
3. **Terminal vs retrying:** `NextRetryAt == null` → "Failed — no more retries" (Neutral); future `NextRetryAt` → "Retry scheduled …" (Lightweight); both asserted in one fixture.
4. **Error state:** the handler throws (scripted 500 / `HttpRequestException`) → the Error message bar renders with the exception message; no table, no silent empty.
5. **Visibility trigger:** in `AssignmentDetailBunitTests` (or the same file against a Detail fixture) — a Draft assignment does **not** render the failures tab; a Published one does.
6. **Client-path coverage (ApiClient):** the mock handler asserts the request hit `GET /assignments/{id}/notification-failures` and the payload deserializes into `NotificationFailureDto[]` including `NotificationKindDto` as a string on the wire (the missing enum converter is what this pins).
   > **Parent correction (post-review, R3):** superseded by §Review F3 — the live wire is **numeric** (the Assignments API registers neither delivery enum), so this test pins read-side *tolerance*, not a missing converter. The baseline text above is retained as the approved plan.
7. **No leakage:** rendered markup contains no absolute file-system paths (`C:\`, `/var/`, `/home/`) and no raw `StoragePath`-like strings; the short-id fallback never renders a full URL/path-shaped string.

### Acceptance criteria (parent)

- `dotnet build SchoolCollab.sln` — 0 errors; affected suites (`SchoolCollab.Assignments.Tests.Unit`, `SchoolCollab.Assignments.Api.Tests.Unit`) + `SchoolCollab.ArchitectureTests.Unit` green; full matrix run by the parent at freeze.
- Every binding test above exists and passes.
- The failures endpoint is consumed read-only; no mutation call exists anywhere in the diff.
- Contract touched **only** on the justified fallback route (additive optional record param, no migration); the primary route leaves Contracts untouched.
- CSS isolation respected (no `<style>` blocks, no inline `style`, no global stylesheet additions); FluentUI component parameters used instead of CSS.
- Worker report written before the pass is called done.

### Reviewer acceptance criteria (Tier 3 static)

- Decisions (a)–(e) implemented as stated; the (b) route choice (primary vs fallback) matches the verification evidence and is recorded.
- Read-only: no POST/PUT/DELETE to any failures route; no action buttons in the section.
- The section follows the `SignOffSection` posture: CTS recreated/disposed correctly (loads only — a single `_loadCts` is acceptable here), `_disposed` guards after every await, `catch (OperationCanceledException) { }` first, error never swallowed into an empty state.
- CSS isolation: styles in `.razor.css` only; no `<style>` in markup; no inline `style` attributes; FluentBadge appearances limited to Accent/Neutral/Lightweight (render-validated triad).
- No `InvalidOperationException` from new code; XML docs on the new ApiClient method and any new public member; primary-constructor style preserved in test helpers; no CPM `Version` attributes; net10.0; no MediatR; no new feature flag; no cross-context project reference (contact labels ride the existing `StudentsApiClient` HTTP client, not a project reference).
- Tests genuine (row/empty/terminal/error/gate/leakage all exercised against scripted HTTP, not vacuous mocks).

### UI-tester scope handover

The UI tester must not derive or expand its own scope. Exactly:

- **Changed UI files:** `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/NotificationFailuresSection.razor` (new) + `NotificationFailuresSection.razor.css` (new); `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Detail.razor` (edited — conditional tab only).
- **Page that renders them:** `/assignments/{id}` (`Detail.razor`), tab `Failures`/`Notifications`, visible for assignments in Published/Scheduled/Closed status.
- **ApiClient method called:** `AssignmentsApiClient.GetNotificationFailuresAsync(Guid id, CancellationToken)` → `GET /assignments/{id}/notification-failures`; contact-label enrichment (if implemented on the primary route) additionally reads via the existing `StudentsApiClient` contact list the Publish dialog already uses.
- **Navigation entry point:** `Index.razor` (`/assignments`) → row `FluentAnchor` (title) → detail page → the failures tab.
- **In scope for the tester:** row rendering (channel/kind/attempt/reason/retry states), terminal-vs-retrying presentation, empty state, error state (endpoint unreachable), tab visibility per status, CSS-isolation styling, no path/GUID leakage.
- **Out of scope for the tester:** any retry/requeue action (none exists), the Families ward surface, backend endpoint behaviour beyond reachability, Index.razor changes (none in this round).

### Constraints (non-negotiable)

- Read `.github/copilot/rules/dotnet-best-practices.md` (+ skill), `blazor-components.md`, `section-card.md`, `testing.md` **before** coding — ArchitectureTests enforce the "Never" list in CI.
- CPM: no `Version` on any `PackageReference`; net10.0; no MediatR (CQRS via `IQueryHandler` + Scrutor; this round adds no handler on the primary route).
- XML docs on new public members; primary constructors; typed exceptions only (no `InvalidOperationException` from new code; the ApiClient's existing unreachable-guard pattern stays as-is).
- Tenancy: no new query; the delivered endpoint is tenant-scoped and must be consumed as-is (no client-side tenant assumptions, no id/path leakage into logs).
- CSS isolation with no `<style>` blocks and no inline `style`; FluentUI component parameters (appearance, title, class) rather than CSS overrides; FluentBadge appearances limited to Accent/Neutral/Lightweight.
- No new feature flag without justification (none planned; the endpoint already ships un-gated like its sibling read routes).
- `dotnet build SchoolCollab.sln` must be 0 errors; run the affected test projects **plus** `SchoolCollab.ArchitectureTests.Unit`.
- **Output discipline:** one `dotnet test tests/<X> 2>&1 | grep -E "^\s*failed |total:|failed:" | head -40` per project per read; no ad-hoc pipelines, no `zz*.log` debug files; ≤2 attempts per result read; 5-minute cap per item.
- 30-minute cut-line: stop **with** the worker report written and the tree buildable.

## Worker Report

Worker pass: `ollama/deepseek-v4-flash:0731-cloud`, run `68d78431`. Full report: `documents/rounds/.ar-17-worker-report.md`.

**Changed files.** New — `NotificationFailuresSection.razor`, `NotificationFailuresSection.razor.css`, `tests/SchoolCollab.Assignments.Tests.Unit/NotificationFailuresSectionBunitTests.cs`. Modified — `AssignmentsApiClient.cs` (`GetNotificationFailuresAsync` + the missing `JsonStringEnumConverter<NotificationKindDto>`), `Detail.razor` (conditional `Notifications` tab), `AssignmentDetailBunitTests.cs` (section backends registered in the shared fixture + the binding item-5 visibility test). Nothing outside the plan's scope fences was touched.

**Decision (b) — PRIMARY route, verified with a concrete citation.** The worker traced `Detail.razor:476`, which loads the Publish dialog's contact universe via `ListSubscribedContactsAsync(ContactOwnerType.Guardian, null, SubscriptionScope.AllAssignments)` and passes `SubscribedContactDto.Id` values to `PublishAsync`; those are what ar-16 persists as `NotificationLog.ContactId`. So the client-side `ContactId → Value` mapping covers the universe, and the additive `RecipientLabel` fallback route was correctly **not** taken — Contracts untouched, no migration. This is the outcome the parent's pre-dispatch fact-check predicted.

**Decisions (a) read-only, (c) terminal-vs-retry badges, (d) flat table + CSS-truncated reason with full text in `title`, (e) Info empty vs Error bar** implemented verbatim.

**Build:** `dotnet build SchoolCollab.sln` — 0 errors (6 pre-existing warnings). **Tests:** Assignments 630/0; ArchitectureTests 21/0; all seven binding items present and passing; HTTP scripted via `RichardSzalay.MockHttp`.

**Deviation (accepted, cosmetic):** the tab label is the static `Notifications`, not the plan's `Failures (N)` count — a count needs a `Detail`-side fetch, which the plan forbids. The parent accepts this: the count was a nicety, the no-extra-fetch rule is the stronger constraint.

## Parent pre-review corrections (applied before the freeze)

Three defects found by the parent reading the artifact; all fixed pre-review so the reviewer verifies the corrected freeze, and all recorded here for auditability.

| # | Severity | Defect | Fix |
|---|---|---|---|
| 1 | **P1** | `<tr @key="row.ContactId">` — one contact can hold several failed rows per assignment (one per `NotificationKind`), so the key was non-unique. | `@key="RowKey(row)"` = `{RecipientId:N}:{ContactId:N}:{Kind}:{Attempt}`. |
| 2 | **P2** | The new client method was inserted *between* an existing `<summary>` and its method, so `GetSignOffContextAsync` lost its XML doc (and the new method carried two stacked summaries). | Moved the WS-C2 summary back onto `GetSignOffContextAsync`; the new method keeps its own single summary. |
| 3 | **P2** | `Task.WhenAll(failures, contacts)` coupled the primary read to the label lookup, so a **Students API outage hid the failures list** behind an error bar. | `TryLoadContactLabelsAsync` isolates the label read (rethrows on cancellation, otherwise logs Warning and degrades to short ids); `WhenAll` retained so the parallel-loading rule still holds. |

**P1 evidence — the duplicate key is a real, reachable exception.** The worker's suite passed 630/0 *with* the defect, because duplicate keys are not rejected on first materialisation (`Rows_Render_WithFutureRetryBadge` has two same-`ContactId` rows and passed). The parent added `SameContact_MultipleKinds_RendersDistinctRows_AndSurvivesReRender`, which re-renders the already-materialised table, then proved the test is **discriminating** by temporarily restoring the old key:

```
failed SameContact_MultipleKinds_RendersDistinctRows_AndSurvivesReRender
System.InvalidOperationException: More than one sibling of element 'tr' has the same key value,
'22222222-2222-2222-2222-222222222222'. Key values must be unique.
total: 631  failed: 1
```

Key restored, re-verified green. Any parent-driven re-render of the materialised table (tab switch, `Detail` state change) would have hit this — hence P1, and note it is a framework `InvalidOperationException` surfacing from new code, which the best-practices rule forbids by construction.

## Freeze record

- **Tree frozen** at 6 files: 3 modified (+68/−1 pre-fix), 3 new. Patch: `documents/rounds/diffs-ar-17-notification-failures-ui.patch` — **6 files, +565 / −1**, generated with `git add -N` for the new files (untracked scratch such as `brainstorms/` is excluded).
- **Authoritative matrix (parent-run, post-fix): 2,345 / 0.** Core 84 · **Assignments 631** · Assignments.Api 78 · Families 42 · Students 422 · Students.Api 1 · Settings 519 · Settings.Api 1 · Admin 546 · Architecture **21**. Only Assignments moved versus ar-16's 2,337 (= ar-16 623 + 7 worker bUnit + 1 parent regression test); every other project is byte-identical in count, so there is no collateral.
- `dotnet build SchoolCollab.sln` — **0 errors**.
- In-session build-after-every-change rule honoured; no build locks encountered.

## Review

**Reviewer:** `ollama-cloud/deepseek-v4.1-flash`, run `ecd6e7a2` (read-only, fresh context). Full verdict: `documents/rounds/.ar-17-review.md`.

**Verdict: ACCEPT — no P1.** 3 × P2 plus a batch of P3/NIT hygiene items (the reviewer's own tally mixed distinct findings with nit-level notes; the post-fix re-verify counted 3 P3 against the delta). All three parent pre-freeze fixes were verified correct and complete — and the composite key was confirmed *necessary* rather than over-fix, because `StudentContactResolver` does not dedupe contacts, so two wards sharing a guardian genuinely yield two recipients with the same `ContactId` (which is why `RecipientId` belonged in the key). Decisions (a)–(e), CSS isolation, rule compliance and scope fences were verified; all seven binding tests were judged non-vacuous (item 5 asserts the Draft negative *and* the Published positive; item 7's `NotContain(full GUID)` genuinely catches a naive `@row.ContactId` render).

**The three P2s — each verified by the parent against the code, none taken on faith:**

| # | Finding | Parent verification | Resolution |
|---|---|---|---|
| F1 | `RowKey` is a *derived* identity with no DB uniqueness guarantee, so duplicates stay reachable | **CONFIRMED.** `PublishAssignmentCommandHandler` calls `assignment.Publish(…)` — which **early-returns** when already `Published` (`Assignment.cs`) — then **still re-broadcasts** over the reused recipients, so a repeat publish creates duplicate `(RecipientId, Kind, Attempt)` rows. (The missing `(TenantId, AssignmentId, RecipientId, Kind)` unique index is the ar-16 residual carried to E3.) | Loop keyed by **index** (`@for` + `@key="i"`); `RowKey` deleted. The regression test now includes the exact F1 byte-identical row pair |
| F2 | Unfiltered `catch (OperationCanceledException)` swallows a transport timeout → **false empty state** | **CONFIRMED + PROVEN.** With the unfiltered catch, a scripted `TaskCanceledException` rendered `<div class="fluent-messagebar-message">No failed notifications.</div>` — the UI told the teacher "no failures" when the load had failed | Filtered to `when (ct.IsCancellationRequested)`; new test `TransportCancellation_ShowsErrorBar_NotEmptyState` pins it |
| F3 | The "missing enum converter" premise is **false** — the live wire is numeric | **CONFIRMED.** `Assignments.Api/Program.cs` registers **10** sibling enum converters and **neither** `NotificationKindDto` nor `ContactChannelDto`, so those two are numeric on the wire and would deserialize without the client converter | Kept as read-side **tolerance** with an honest code comment; the round narrative and the test comment were corrected; the **API-side registration gap is recorded as a residual** (the real inconsistency) |

**F6 (found as P3, accepted and fixed):** `AssignmentStatusDto.Archived = 4` is documented "read-only retention" — an archived assignment *was* published and keeps its failure history, so the gate omitting `Archived` hid it. Gate extended; the visibility test now asserts the Archived positive as well.

**Not actioned (recorded, not defects).** F4 — the `CanHaveFailures` gate is dead today (`Detail.razor` hard-codes `true`) and its spinner hazard is unreachable. F5 — the `Failures (N)` label was **not** strictly forced (an `EventCallback<int>` could carry the count with no Detail-side fetch), so the worker's justification was too strong; accepted as a UX follow-up rather than expanded into this round. F7 — two extra HTTP calls per qualifying Detail view. F9 — multi-ward guardian rows are indistinguishable (the DTO carries no ward). F8/F10/F11 — accepted as-is or nit-level.

## Post-review freeze record (supersedes the pre-review freeze above)

- **Tree re-frozen after the review fixes.** Patch `documents/rounds/diffs-ar-17-notification-failures-ui.patch` regenerated — **6 files, +613 / −1** (was +565/−1 before the fixes).
- **Authoritative matrix re-run after the fixes: 2,346 / 0.** Core 84 · **Assignments 632** · Assignments.Api 78 · Families 42 · Students 422 · Students.Api 1 · Settings 519 · Settings.Api 1 · Admin 546 · Architecture 21. No collateral.
- `dotnet build SchoolCollab.sln` — **0 errors**.
- **Both new tests were proven discriminating** by temporarily restoring the pre-fix code before restoring the fix: F1 produced the framework `InvalidOperationException: More than one sibling of element 'tr' has the same key value`; F2 produced the false "No failed notifications." markup on a transport timeout. Each revert was auto-restored and the tree re-verified green.

## Re-verification (post-fix)

**Re-verifier:** `ollama/glm-5.3:cloud` (the documented higher-model re-verify id — distinct from both the implementer and the first reviewer), run `3ddabec1`, read-only, verifying the **delta** only. Full verdict: `documents/rounds/.ar-17-reverify.md`.

**Verdict: ACCEPT for the post-fix freeze.** Per-fix: F1 **CORRECT** · F2 **CORRECT** · F3 **CORRECT** · F6 **CORRECT**. **P1 0 · P2 0** — no first-review P2 survived the fixes and nothing the first reviewer verified was weakened. Evidence highlights: `RowKey` is gone repo-wide (grep 0) and the regression test's rows 3/4 are byte-identical via the new `recipientId` helper parameter, so it discriminates against *both* prior key schemes; the dispose-cancel path remains the only reachable `ct.IsCancellationRequested == true` case (the token is cancelled solely in `Dispose`) and `_disposed` blocks rendering there, so the F2 filter cannot silently render the empty state; the F3 comment was confirmed against disk (exactly 10 sibling converters, neither delivery enum); the F6 gate was confirmed live for `Archived` (the query filters only `AssignmentId` + `Failed`, no status filter).

**3 × P3 hygiene items raised — all fixed by the parent before acceptance:**

- **R1 — the parent's own regression.** The parent's pre-review fix re-added the SignOff test's doc-comment opener as `// ──` instead of `/// <summary> ──`, leaving that block's `<summary>` unterminated and its opener missing — the same class of doc mangling as the client-method defect fixed pre-review. (Provenance, from the diff: the worker had copy-pasted the SignOff summary onto the new visibility test; the parent's replacement corrected the visibility test's summary but carried the mangled `// ──` prefix across to the SignOff block.) Corrected.
- **R2.** The section's `CanHaveFailures` XML doc still described the pre-F6 gate (`Published / Scheduled / Closed`). Corrected to include `Archived`.
- **R3.** The "missing enum converter" phrasing survived in the Plan's binding-test item 6 and in the worker's own report. The Plan is retained verbatim as the approved baseline and **annotated as superseded**; the worker report is left as the worker's historical record and is superseded by §Review F3; the review-section tally was reworded to stop asserting the first reviewer's conflated count.

## Acceptance

**ACCEPTED by the parent (round owner), on two independent verdicts plus the post-fix matrix.** The orchestrator's approved `## Plan` is the acceptance baseline. The static reviewer (`ollama-cloud/deepseek-v4.1-flash`, run `ecd6e7a2`) and the higher-model re-verifier (`ollama/glm-5.3:cloud`, run `3ddabec1`) both returned **ACCEPT**; every P1/P2 either raised was *fixed and re-verified* rather than waived, and the round-doc narrative was corrected where it had overstated a fix (F3, R3).

| Acceptance criterion (from the plan) | Status |
|---|---|
| `dotnet build SchoolCollab.sln` — 0 errors | **PASS** |
| Affected suites + Architecture green; full matrix at freeze | **PASS** — **2,346 / 0**: Assignments **632**/0, Architecture 21/0; the eight other projects are unchanged vs ar-16 → no collateral |
| Every binding test (items 1–7) exists and passes | **PASS** — all seven; none vacuous (two were *proven discriminating* by temporarily restoring the pre-fix code) |
| Failures endpoint consumed read-only; no mutation call anywhere in the diff | **PASS** — confirmed by both verifiers; grep 0 |
| Contract touched only on the justified fallback route | **PASS** — primary route taken: **Contracts untouched, no migration** |
| CSS isolation respected; FluentUI params over CSS; badge appearances inside the render-valid triad | **PASS** — confirmed by both verifiers |
| Worker report written before the pass was called done | **PASS** — `documents/rounds/.ar-17-worker-report.md` |

**Deviations accepted.** The static `Notifications` tab label stands (F5) — the re-verify showed a `Failures (N)` count was *achievable* without a Detail-side fetch, so the worker's justification was too strong; it is carried as a UX follow-up rather than expanded into a verified freeze. The remaining P3/NIT items (F4 dead gate, F7 per-Detail extra fetches, F9 multi-ward indistinguishability, F11 LF/CRLF, F8/F10) are recorded in §Review.

**Residual risk carried forward.** (1) The live HTTP path and the real (numeric) wire shape remain untested end-to-end. (2) The **API-side** missing `NotificationKindDto`/`ContactChannelDto` converter registration — the real inconsistency; the client converter is read-side tolerance only. (3) The F1 duplicate-row data class until the `(TenantId, AssignmentId, RecipientId, Kind)` unique index lands in **E3**. (4) `Failures (N)` discoverability not delivered. (5) Multi-ward guardian rows are indistinguishable (the DTO carries no ward). (6) Inherited `Detail` staleness on same-page A→B navigation (pre-existing, out of scope). (7) ~~**The unpublish→Draft gate hole**~~ — **CLOSED in this round under owner-approved option A** (see the final section): the gate is now `PublishedAt is not null` instead of a status list, with `PublishedAt` threaded through the Core DTO, the repository projection and the two query handlers.

**R1/R2/R3 are comment-only changes** — no behaviour change, so they were verified by build + the affected suites (0 errors, 632/0, 21/0) rather than a further reviewer pass; that proportionality call is recorded here deliberately.

## UI Tester

**Two attempts — the first failed and is recorded honestly.**

**Attempt 1** (`ollama/minimax-m3:cloud`, run `7b69b84d`) **timed out at 30 minutes with no output**: the child ran an unbounded `find ~/...` search, which held `bash` open until the deadline — a violation of the repo's never-`find ~` rule. Its partial reasoning produced no P1/P2. No stray processes were left behind and no report was written. The parent steered it off the live-run attempt (queued too late to be acted on) and re-dispatched with shell commands **explicitly forbidden**, six named files to read, six concrete checks to answer, and a ~10-minute budget.

**Attempt 2** (`ollama/minimax-m3:cloud`, run `ef9e418f`): **ACCEPT** — **0 P1, 1 P2, 2 P3**. Full report: `documents/rounds/.ar-17-uitest.md`.

- **P1 — none.** No crash, no false "no failures", no full-GUID leak.
- **P2** — `ContactLabel` had no `IsNullOrWhiteSpace` guard, so a present-but-blank `SubscribedContactDto.Value` (a non-nullable `string` with no content guarantee) rendered a **visibly empty Recipient cell** on the alerting surface.
- **P3(a)** — the short-id fallback was visually indistinguishable from a real label and could be misread as a person's name.
- **P3(b)** — only `.failures-reason` was width-bounded; the Recipient column was unbounded, so a long email could dominate the table on a narrow viewport.
- **PASS** — check 4 (`EnumHelper` never throws and never renders a number), check 5 (gate matches the *declared* status set), check 6 (render order `_busy → _error → empty → table`, with the F2 transport-cancel fix pinned on disk).
- **Could not verify** — no live instance, no browser/viewport check, per-member `[Description]` presence, and at that point `Scheduled`/`Closed` had no explicit per-status assertion.

**Parent fixes for the UI-tester findings:**

| Item | Fix |
|---|---|
| P2 blank cell | `ContactLabel` falls back to the short id when the looked-up value is null/whitespace; new test `BlankContactValue_FallsBackToMarkedShortId_NotAnEmptyCell` asserts the cell is never empty and equals the marked id (the assertion targets the fixed output directly, so it is discriminating by construction — no revert experiment needed) |
| P3(a) | the fallback is now marked `#a1b2c3d4` so it reads as an identifier rather than a name (still never the full GUID) |
| P3(b) | new `.failures-recipient` bounds the column (`max-width: 18rem` + ellipsis) with the full label in the cell `title`, mirroring the Reason cell |
| coverage gap | `NotificationFailuresTab_Visibility_ByStatus` now asserts `Scheduled` and `Closed` explicitly, alongside Draft-negative / Published / Archived |

### Additional P2 found by the parent (the UI tester did NOT find it) — carried as residual (7)

The UI tester verified the gate matched the **declared** status set; it did not test whether the declared set matches the **data**. It does not. `Assignment.Unpublish()` sets `Status = AssignmentStatus.Draft`, and **nothing ever deletes `NotificationLog` rows** (the only writes are `Add` in the broadcaster; the unpublish handler deletes *recipients* and resets gates). So: publish → failures logged → **unpublish → the assignment is Draft → the tab is hidden while the failure history still exists.** Bounded, not data loss — the assignment is back in Draft (not delivering) and re-publishing re-broadcasts and re-surfaces failures. **Not fixed in this round:** the clean fix requires a contract addition, because `AssignmentSummaryDto` carries **no `PublishedAt`**, leaving no client-side "has ever been published" signal; the fix is to add an additive `PublishedAt` to that DTO and gate on `PublishedAt is not null`. That is backend/contract work with its own verification cost and the plan fenced backend changes.

### Final freeze (post-UI-tester)

- Patch regenerated after the solution-doc records: **8 files, +697 / −6** — the six code/test files (at +664/−1) plus the two `documents/solution/` records; verified **drift-free** against the tree (`diff -q` clean).
- **Authoritative matrix re-run: 2,347 / 0** — Assignments **633**/0, Architecture 21/0, the eight other projects unchanged. `dotnet build SchoolCollab.sln` — **0 errors**. Patch verified **drift-free** against the tree (`diff -q` clean).
- The UI-tester fixes were verified by build + the affected suites rather than a further reviewer pass; the **pre-PR review required by the repo policy will independently cover the complete final patch**, so no verification gap is left open.

## Post-acceptance change (owner-approved option A) — residual (7) fixed

The owner reviewed the unpublish→Draft analysis and chose **option A: fix it in-round** rather than carry it. This is the only change made *after* acceptance, and it is recorded separately for that reason.

**Change (contract → gate).** The gate was a status heuristic (`Published|Scheduled|Closed|Archived`), which hid the history of an assignment returned to `Draft` by `Unpublish()` while its `NotificationLog` rows survive. It is now the semantically correct predicate **`_item.PublishedAt is not null`** ("has ever been published"). Because `AssignmentSummaryDto` did not expose `PublishedAt`, the field had to be threaded through all three layers — and each downstream layer would have silently dropped it:

| Layer | File | Change |
|---|---|---|
| Domain | `Assignment.cs` | **no change** — `PublishedAt` already existed (`:79`, set on publish `:313`), and `Unpublish()` does *not* clear it (it only nulls `AvailableFromUtc`). That is exactly why it is the right signal. |
| Core DTO | `DTOs/AssignmentSummary.cs` | + optional `DateTimeOffset? PublishedAt = null` |
| Repository projections | `Data/Repositories/AssignmentRepository.cs` **and** `WardAssignmentProjectionRepository.cs` | both project `a.PublishedAt`. The ward path was a **second** construction site missed by the first pass — it compiles (the param is optional) but reported never-published; it feeds `WardAssignmentListItemDto` (id/title/due/state/locked) and never the contract DTO, so there was **no user impact**, and it is fixed as a latent-trap guard |
| List query | `ListAssignmentsQueryHandler.cs` | passes `PublishedAt: s.PublishedAt` |
| By-id query | `GetAssignmentByIdQueryHandler.cs` | passes `PublishedAt: assignment.PublishedAt` |
| Contract | `ContractTypes.cs` | + optional `DateTimeOffset? PublishedAt = null` (append-only, so the six existing `AssignmentIndexBunitTests` constructions compile unchanged) |
| UI gate | `Detail.razor` | `@if (_item.PublishedAt is not null)` replaces the status list — which also subsumes the earlier F6 `Archived` fix |

**Verification.**

- `dotnet build SchoolCollab.sln` — **0 errors**.
- **Authoritative matrix: 2,347 / 0**, identical to the pre-change run — Assignments 633/0, Architecture 21/0, the eight other projects unchanged, so the contract change caused **no collateral**.
- The new assertion was proven **discriminating** by temporarily restoring the old status gate: exactly one test failed, with the message *"an assignment returned to Draft by Unpublish keeps its failure history — the old status test hid it."* Gate restored and re-verified green.
- Patch regenerated: **14 files, +757 / −16** (was 8 files, +697/−6) — the six original code/test files, **six** additional production files, the second test file, and the two solution-doc records. Drift-checked against the tree. (Superseded by the pre-flight figures below: **15 files, +870/−16**.)
- **Deployment note (bounded, not a defect):** both query handlers cache through `HybridCache` (`Expiration = 5 min`, `LocalCacheExpiration = 1 min`), so a cache entry written *before* this change lacks `PublishedAt` and would deserialize as `null`, hiding the tab for at most ~5 minutes after deploy. No action needed; recorded so it is not misdiagnosed later.

**Residual (7) is closed in this round.** It remains listed above only for provenance.

## Pre-PR review (repo pre-flight)

**Reviewer:** `ollama-cloud/deepseek-v4.1-flash`, fresh context, run `710b9243`. Full verdict: `documents/rounds/.ar-17-preflight-review.md`.

**Verdict: CLEAR FOR PR — no P1.** Every option-A delta item was judged CORRECT/complete: completeness across the layers (including the ward site, which the round doc had omitted before the fix); semantic correctness (`Assignment.Publish` is the **only** write to `PublishedAt` in the tree and `Close()/Archive()/Reject()` never touch it); the predicate cannot produce a false negative, because the broadcaster runs on the same tracked scoped DbContext immediately after `Publish()`, so the rows and the timestamp commit in one `SaveChanges`; and consumer impact is limited to `Detail.razor`. Nothing at P1 on the whole-patch sweep either (no new `InvalidOperationException`, no CPM `Version`, no MediatR, XML docs present, no cross-tenant read, no PII beyond the decision-(b)-approved contact values).

**1 × P2 — FIXED.** The three-layer threading *and* the `Unpublish`-preserves-`PublishedAt` premise had **zero** test coverage — precisely the silent-drop class this document warns about, since a forgotten optional projection compiles cleanly and reports never-published. New `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentSummaryPublishedAtTests.cs` pins the projection at **both** construction sites (`AssignmentRepository.ListAsync` and `WardAssignmentProjectionRepository.ListWardAssignmentsAsync`) plus the premise that `Publish()` stamps the value. Both tests were proven **discriminating** by nulling each projection in turn — each produced exactly one failure, its own test.

**3 × P3 — FIXED.** (i) `Detail.razor`'s comment claimed "Scheduled/Closed/Archived are always published first", which is **false** — `Close()` has no published precondition, so a never-published Draft can be Closed; the comment now names the data test explicitly. (ii) The `CanHaveFailures` XML doc still described the superseded status gate (the R2 text). (iii) "first published" in both DTO docs was wrong — `Publish()` re-stamps the value on a later publish, so it now reads "most recently published".

**1 × P3 — ACCEPTED as a residual, not fixed:** the visibility test's `Scheduled`/`Closed`/`Archived` positives pin helper-fabricated shapes (`MakeDto` infers `PublishedAt` from the status), so they cannot fail for a data reason; the meaningful assertion is the unpublish case. Recorded rather than contorted into a fake fixture.

**1 × P3 — RECORDED:** the pre-deploy cache window (see the option-A section).

### Final figures

| | |
|---|---|
| Build | `dotnet build SchoolCollab.sln` — **0 errors** |
| **Authoritative matrix** | **2,349 / 0** — Assignments **635**/0 (+2 layer tests), Architecture 21/0, the eight others unchanged |
| Patch | **15 files, +870 / −16** — 13 code/test + 2 solution-doc records, drift-free against the tree |