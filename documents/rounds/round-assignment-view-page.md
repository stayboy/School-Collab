# Round: assignment-view-page — the view page + the audience read

**Status:** in progress — 2c mock and its six pins approved, worker dispatched 2026-10-30.

Mode: **attended** (Tier 2 + compensating probe; owner, 2026-10-30). Branch: `feat/assignment-view-page`, worktree
`../School-Collab-assignmentview`. Base head: `4338b9ce`. Spec:
`documents/specs/assignment-detail-view-modernization.md` (merged, 3× `0813`-reviewed). Issue: #453, round (a) of two.

**Profile: `ollama-cloud`** — one provider for the whole round. Worker `ollama-cloud/deepseek-v4.1-flash:1017`; plan
gate, diff reviewer and acceptance seat `ollama-cloud/deepseek-v4-pro:0813`. The parent (plan author, acceptance
transcriber) sits on its session model `cline-pass/deepseek-v4.1-flash-1017` — never the verifier seat, and the gate
is never the plan's author.

---

## Resumed (owner, 2026-10-30) — the 2c gate is closed

The round was parked after the plan gate and resumed the same day in a tree of its own,
`../School-Collab-assignmentview` (the branch had been sitting in the primary checkout, which had to detach to free
it). Nothing under `src/` was touched by the park; the branch carried this doc and nothing else.

| Item | Value |
|---|---|
| Branch / tree | `feat/assignment-view-page` @ `8546ddef`, pushed, **no PR** |
| Base | `4338b9ce` (= `main` after #457) |
| Round doc | this file, committed on the branch |
| Plan gate | **complete** — `0813` verdict *revise*, five findings, all applied (see § "Plan gate") |
| 2c mock | **approved** by the owner, 2026-10-30, with six pinned decisions (§ "Step 2c") |
| Next step | worker run (Tier 2) → freeze + build/gates → static diff review → acceptance |

**Still owed outside this round** (own issues, do not absorb):

- #458 — sweep the dozen raw `FluentDataGrid`s that do not page.
- #459 — the two red Admin tests (`StudentDetailSectionsTests` source-text crash; `ActivityGroupsPageTests` flake).
- A one-line precision fix to `agent-lessons.md`'s UTF-8 entry: as written it blames `Get-Content | Set-Content`,
  but a *symmetric* ANSI round-trip is byte-preserving. The corruption needs `-Encoding utf8` on the write side. The
  entry overstates the trap and should say so.

**Open question carried forward:** the round prices **two** reads (published audience + draft preview). If the 2c
mock or the worker's first pass reveals a third contract-shaped need, that is the signal to escalate to Tier 3
mid-round rather than absorb it.

## Why this round, and why this tier

The owner kept Tier 2 deliberately and bought a **probe** to cover what Tier 3 would have added. The tier table
puts Tier 2 at *"Behavioural, no UI, single-context plan"*; this round has a page rewrite and a new endpoint, so it
does not qualify on paper. That is recorded, not hidden — `AGENTS.md` says go up a tier when ambiguous, and this is
an explicit downward call with a compensating control.

| Tier-3 seat substituted | Replacement |
|---|---|
| Independent plan review | a `deepseek-v4-pro:0813` pass over this plan before the worker runs |
| Static diff review | **kept** — Tier 2 already has it |
| UI tester | a **live-render probe** against seeded dev data, checked against the approved mock |

## Scope

**In:** the `/assignments/{id}` surface rebuilt on the skeleton (one header, no tabs, main + rail), the **audience
read**, the **Delivery** and **Status** panels, the **audience table** with its privacy rule and pager, and an
embeddable mode for the authoring surface so the view page owns the chrome.

**Out:** the progress page and the submission page (round b), the `SubmissionForReviewDto` widening (round b),
inline structural editing, notification retry, any schema change.

## Expected files — the UI trigger and the escalation set

| Area | File |
|---|---|
| Page | `Assignments.Application/Components/Pages/Assignments/Detail.razor` **(rewrite)** + `Detail.razor.css` |
| Page-local components | `AudienceTable.razor`, `StatusStrip.razor` (Assignments.Application, beside the page) |
| Authoring | `AssignmentAuthoring.razor` — embeddable mode (suppress its shell) |
| Contract | `Assignments.Contracts/ContractTypes.cs` — audience DTOs + cap constant |
| Query | `Assignments.Core/CQRS/Assignments/Queries/GetAssignmentAudience/**` + `…/GetAssignmentAudiencePreview/**` |
| Endpoint | `Assignments.Api/Endpoints/AssignmentRoutes.cs` — `GET /{id}/audience` **and** `GET /{id}/audience/preview` |
| Client | `Assignments.Application/Services/AssignmentsApiClient.cs` |
| Tests | `tests/SchoolCollab.Assignments.Tests.Unit` (handler + bUnit page), `tests/SchoolCollab.ArchitectureTests.Unit` (typed results — existing) |

**The UI trigger fires** — the page, both page-local components, the authoring embeddable mode and the client are
`.razor`/`.css`/client files. That is what made the 2c mock mandatory, and it is the file set the test suite must
pin.

**R7/D2 adds shared chrome.** `SchoolCollab.Admin.Shared/Components/Authoring/AuthoringShell.razor` joins the set,
which **escalates the gate to `scripts/round/gates.sh` with no flags** plus the MAUI head build
(`-f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false`) — `AGENTS.md`'s named tripwire, not a formality.

## Plan

**Sizing / dispatch.** Eight plan items plus the held blocks, in one bounded context, and the slices share files
(`ContractTypes.cs`, `AssignmentsApiClient.cs`, `Detail.razor`), so a fan-out would put two writers on one file.
**`Dispatch: two sequential passes`** — 2a (R7's `Embedded` mode + the Details embed + the Questions table) and 2b
(rail Content & Resources / Rules / Approval + the lifecycle matrix and header actions + restored tests). The split
is deliberate, not a recovery: the second half is bigger than a fresh context should carry in one pass, and this
repo has timed out a single pass of this size before.

**Open decisions.** None outstanding: the six the 2c mock surfaced plus R7 (§ "Pinned decisions") are folded into
P5/P5b/P6/P7/P8 below.

**P1 — Contract (§4.2, Q145, Q146, Q154).** `AssignmentAudienceDto(Rows, Truncated, Degraded)`;
`AudienceStudentDto(StudentId, StudentName, Reached, Contacts[])`;
`AudienceContactDto(OwnerType, Channel, GuardianRole?, SubscriptionActive, NotifyOnBroadcast, DeliveryState, SentAt,
OpenedAt, FailureReason, RetryScheduled, TemplateId)`. **No address field of any kind.** Cap declared once as a
shared constant (`AudienceCap = 200`). *(Gate finding 2: `OwnerType`, `NotifyOnBroadcast` and `TemplateId` were
missing from the first draft — §4.2 names all three, and `OwnerType` is precisely what distinguishes a student-owned
contact from a guardian-owned one, the defect that renders a student contact as a hex fragment today.)*
*AC1:* the serialized DTO contains no `@` and no phone-shaped string.

**P2 — Query handler (Q145).** Cohort from the existing target resolver; contacts from persisted recipients;
delivery from the notification log (**authoritative**), with `AssignmentRecipient.OpenedAt` supplying *opened* only;
names from the batched subscriber read (no per-student hop). Order by student name. Cap at `AudienceCap`, set
`Truncated`. If the log or directory read fails, return `Degraded = true` with rows still carrying the cohort and `DeliveryState = Unknown` — never a silent empty list.
*AC2:* a student with no subscriber appears with `Reached = false` and zero contacts.
*AC3:* a missing or stale log row yields `DeliveryState = Unknown`, not a fabricated `Sent`.
*(Gate finding 4: the first draft collapsed two different outages into one. Split: a **directory** (name) failure
leaves the rows intact with `StudentName = ""` and delivery state unaffected; a **log** failure leaves the roster and
contacts intact with `DeliveryState = Unknown`. Neither yields an empty panel, and neither is reported as the other.)*

**Seam note — verified, not assumed (parent, before the gate returned).** There is **no `NotificationLog`
repository**, so the first draft of this step named a port that does not exist. The established read path in this
context is **`AssignmentsDbContext` injected directly into the query handler** — `GetNotificationFailuresHandler`
does exactly that (`db.NotificationLogs.AsNoTracking().Where(AssignmentId == …)`), relying on the global tenant
query filter. The handler follows that precedent; **no new repository file is added**, and the expected-files table
is unchanged.

**P2b — The DRAFT rows read (gate finding 1 — this was the plan's blocker).** The published read is
log-backed and only exists after publish, so it cannot serve a draft; P5 draws Targets & Audience as a table on a
draft (Q152), which means a **second read**:
`GetAssignmentAudiencePreviewQuery` → cohort resolved from the assignment's **persisted targets** via
`IAssignmentTargetResolver` → contacts via the batched subscriber read → policy filter applied (the same
`NotificationRecipientFilter` publish uses) → rows shaped exactly like the published read, with
`DeliveryState = NotPublishingYet` and `SentAt`/`OpenedAt` null. **Nothing persisted.** Called **once per page
load**, never on the authoring surface's 500 ms debounce.
Route: `GET /assignments/{id}/audience/preview`; client `GetAssignmentAudiencePreviewAsync`. Cap 200, same constant.
*AC7:* a draft with targets renders rows with no delivery state; a student with no subscriber renders
`Reached = false`; the endpoint writes nothing (assert row count unchanged after the call).
*(The round therefore prices **two** reads, not one. That is the strongest single argument that this work is
contract-shaped; the owner chose Tier 2 knowing the tier table disagrees — see the tier section.)*
`/recipients` (404 for out-of-scope or unknown id), no new policy.

**P4 — Client.** `GetAssignmentAudienceAsync` returning the DTO, matching the endpoint path exactly.

**P5 — The page (§2, §4.8, Q151–Q155).** `AuthoringShell` at the top; `region-grid`; main = Details · Questions ·
Delivery · Status; rail = Content & Resources · Rules (a draft swaps Delivery for **Targets & Audience** and puts
**Approval** in the rail). **No `FluentTab` in the rendered page** — that is the regression guard.
*AC4:* **one action-bar heading — shell-owned**, zero tabs, both regions present. (*R2 amended this: `AuthoringShell`
renders `<h2 class="authoring-heading">`, and the student view — this page's precedent — carries no `<h1>` at all, so
the original "one `<h1>`" could only pass by changing shared chrome. Rejected: a `HeadingLevel` parameter on the
shell for one heading level, at full-gate cost.*) The container-query collapse is **not** asserted here — a collapse
rule is CSS and bUnit sees structure only; the live-render probe checks it at 900 px (gate finding 5).

**P5b — the header action row (R1).** The Published primary is **`Open progress`, rendered disabled with the reason**
until round b ships the route — the same ruling P8 applies to the Status segment's link, so the page never carries an
`href` that 404s. The chips ride `AuthoringShell.LeadingActions`, because the shipped control order is
`LeadingActions → primary → SecondaryActions`; the mock draws that order, it does not invent one.
*AC4b:* a published page renders the primary disabled with a non-empty reason; no rendered link resolves to
`/assignments/{id}/progress`.

**P6 — Embeddable authoring (§4.1, Q129) — REVISED by R7/D2.** The shipped truth: `Mode=View` always takes the
`ShowSummary` branch (`AssignmentAuthoring.razor:1397-1399`), so its body is a summary card — `<h3>` + badge + facts
`<dl>` + notes + a read-only questions *hint* + a collapsed targets accordion — and the rail exists only in the
`!ShowSummary` Edit branch. Embedding that body as-is cannot produce the approved mock (second title, hint instead
of a table, no rail), so the plan's original "suppress its shell and yield its body" is amended as follows.

*What `Embedded` means, exactly:* one additive parameter on **both** `AuthoringShell` (Admin.Shared) and
`AssignmentAuthoring`, default **false**. When true the body is **facts + notes and nothing else** — no breadcrumb
nav, no action bar, no `authoring-summary__header` (`<h3>`/badge/back/pencil), **no questions hint**, **no targets
accordion** (the page owns Questions and Targets & Audience, so either one would be a duplicate block). Default
false keeps all 21 shell call sites and the 11 existing View-mode tests byte-identical.
*AC5:* the existing authoring bUnit suite passes untouched; a test asserts the embedded body renders the facts and
notes and none of `#authoring-summary`'s header, the questions hint, or `#authoring-targets`.
*AC5b:* `Detail` renders Details as `PanelSection(ShowBorder=false, Title="Details", header action `Edit ▸`)`
wrapping `<AssignmentAuthoring Mode="View" Embedded="true" />` — one facts read-out, one definition (NFR-4).

**P7 — Tables and paging (Q156, Q162).** Questions, the audience/Delivery table page at 15; the pager is **absent**
when the set fits; Delivery and Questions expose a 15/25/50 control.
*AC6:* an 8-row Questions table renders no pager; a 28-row audience table renders `1–15 of 28`.
*(R5: rows-per-page sits in the panel header; the pager and the cap notice — `Showing 1–15 of 200 (capped)` — share
the footer. R6: no delivery filter on this panel in round (a); name order only, and filtering is the progress page's
job in round b (US34).)*

**P8 — Status strip (§4.8, Q155).** One panel, three segments (Progress · Guardian sign-off · Notifications)
separated by coloured rules drawn from the `--status-*` tokens.
*(Gate finding 3: the Progress segment's "Open progress" affordance points at `/assignments/{id}/progress`, which
does not exist until round b. In round (a) it renders **disabled with a reason** — "Progress page arrives with the
submission queue" — never a link that 404s. Priced here so the worker does not invent a route.)*
*(R3: the Status panel renders on a **draft** too — one shape in both states — its segments reading
`Not published yet` / `Not requested` / `Nothing sent` with one reason line beneath, rather than being hidden until
publish.)*

## Pass log

**Pass 1 (landed, parent-verified).** Contract + both reads + client + the rebuilt page shell + `AudienceTable` + `StatusStrip`.
Parent ran the gates itself rather than trusting the worker's numbers:

| Leg | Result |
|---|---|
| `dotnet build SchoolCollab.slnx -v q` | **0 errors**, 39 warnings (none from this round's files) |
| `SchoolCollab.Assignments.Tests.Unit` | **1398 / 0 failed** |
| `SchoolCollab.ArchitectureTests.Unit` (`FriendlyErrorAllowList` 298 → 288) | **160 / 0 failed** |
| Scoped: `GetAssignmentAudience*` + `AssignmentDetailBunit` | **30 / 0 failed** (10 handler + 20 bUnit) |

**Deviations accepted from pass 1** — each verified in-tree, not taken on the worker's word:

- `Assignments.Api/Program.cs` gained a `JsonStringEnumConverter<AudienceDeliveryStateDto>` (4 lines) so the delivery
  enum serialises by name. Not in the expected-files table; accepted — a wire enum rendered numerically would put
  `DeliveryState = 3` in front of a reader. **Add `Program.cs` to the expected set.**
- `FriendlyErrorAllowList` 298 → 288, with the ten `Detail.razor` entries deleted: the rewrite converts the page's
  three raw `ex.Message` sites to `FriendlyError`/`FriendlyErrorBar`. The ratchet moves **down**, which is the only
  direction that guard permits.
- The draft preview reports a policy-filtered contact as `BlockedByPolicy` instead of omitting it, so the mock's
  *Blocked by policy* verdict exists at all; `NotPublishingYet` stays the state of a *reachable* draft contact (P2b).
  Correct: omitting the row would render a guardian as absent rather than refused.
- A cohort-resolver failure on the published read degrades to the persisted recipient cohort with `Degraded = true`
  (rows intact) — the same "never an empty panel" rule P2 applies to the two named outages.
- Status renders `Cohort · Submitted · Graded · Reached` and `Signed · Outstanding`; the mock's `Started` and
  `Reassigned` have no supporting read (`SignOffStatusDto` cannot distinguish a reassignment) and are **not
  invented** — same ruling as the dropped `pts` column.

**Test-count audit (parent, because a rewrite that deletes tests can hide a deletion).** 33 methods removed, 20 added
— net −13. The removed set is the chrome the old page wore: approval decision buttons, the schedule dialog, the
submissions tab (version history + review form), the notification-failures tab, the sign-off card, certificates, and
three duplicate-copy tests. Of those, spec §4.3 deliberately moves **version history and the review form** to the
submission route (round b) and §4.9 puts **notification retry** out of scope, so those deletions are correct. Three
are **not** correct and are restored in pass 2b (below): the approval trio, the lifecycle verbs, and the
`Duplicate` copy tests — the rebuilt page still ships `OnDuplicateAsync` (`Detail.razor:411`) with **no test at all**.

### Plan correction (mine, not the worker's) — P5b was under-specified

The plan pinned only the **published** primary (R1: `Open progress`, disabled, with the reason). Reading the shipped
truth settles the rest: the old page had no lifecycle buttons of its own — it inherited them from the **embedded
`AssignmentAuthoring` action bar** (`AssignmentAuthoring.razor:50-56`, "the lifecycle actions … now live in the
authoring action bar's §11 matrix"; the removed `Detail_Draft_ShowsScheduleAction` opens *that* kebab). Pass 1
dropped the embed's shell — correctly, it was the double chrome — **and with it every lifecycle affordance**. Spec
§2 is settled and explicit, so this is a gap in the plan, not a question:

> §11 lifecycle primary — **Draft → Publish**, **Scheduled → Unpublish**, **Closed → Archive**. For a **Published**
> assignment the primary is ***Open progress***, with Unpublish and Close in the kebab.

**P5b (extended).** The header primary is **status-driven**, not a constant:

| Status | Primary | Disabled? |
|---|---|---|
| Draft | `Publish` | disabled with the reason while approval is required and outstanding/not requested |
| Scheduled | `Unpublish` | live |
| Closed | `Archive` | live |
| Published | `Open progress` (R1) | **always disabled** with the round-b reason |
| Archived | — | no primary; Duplicate only |

The kebab (`PageActions`) carries `Duplicate` always, plus `Schedule…` / `Reschedule…` on Draft/Scheduled (US7:
"schedule, reschedule, close or archive **here**, so that lifecycle actions are not split across surfaces") and
`Close` on Published. Every verb already exists on the client — `PublishAsync:295`, `UnpublishAsync:332`,
`CloseAsync:338`, `ArchiveAsync:348`, `ScheduleAsync:359` — so this is **wiring, not new API**, and needs no contract
change.
*AC4c:* the primary's label is asserted for all five statuses; a published page's kebab contains `Close` and
`Duplicate`; a draft's kebab contains `Schedule…`; no status renders `Open progress` except Published; `Duplicate`
keeps its three behavioural tests (confirm → posts → navigates, decline → no post, failure → error line).

**Pass 2a (landed, parent-verified).** `Embedded` mode on `AuthoringShell` + `AssignmentAuthoring`, the Details embed, and the page-local `QuestionsTable`. Eight files; the round doc's expected set grows by `SchoolCollab.Admin.Shared/Components/Authoring/AuthoringShell.razor`,
`QuestionsTable.razor(.css)`, `tests/SchoolCollab.Admin.Tests.Unit/AuthoringShellTests.cs`.

| Leg | Result |
|---|---|
| `dotnet build SchoolCollab.slnx -v q` | **0 errors** (39 warnings, none from this round) |
| `SchoolCollab.Assignments.Tests.Unit` | **1406 / 0** (1398 → +8; the 11 existing `View` tests untouched) |
| `SchoolCollab.ArchitectureTests.Unit` | **160 / 0** (incl. `NoSeveredBlockGuards_InRazor`) |
| `SchoolCollab.Admin.Tests.Unit` | **874 / 1** — the red is `StudentDetailSectionsTests.Transfer_And_Withdraw_Actions_Are_Per_Placement`, a source-scan `Substring(…)` with a negative length on a file this round never touches |

**Deviations accepted from pass 2a:**

- The shell pass-through is the guard expression `Embedded="@(Embedded == true && Mode == AssignmentAuthoringMode.View)"`, **not** bare `Embedded="Embedded"` — with the bare form an Edit render lost its trail, and the worker's own `Edit_Embedded_…` test caught it before I did. Every rendering gate uses the literal pinned expression. This is the guard the brief asked for, reached by a better route.
- The Questions and Targets wrappers (`.region-grid-host`/`.region-stack`) still emit empty and zero-height when suppressed, rather than taking a fourth condition. Accepted: no ids, no height, no content — a further condition would be a harder diff for no reader-visible gain.
- The targets `@if` had to be written on one line: `NoSeveredBlockGuards_InRazor` fails a condition whose continuation wraps. The guard is right; the formatting is its cost.
- The child read sits in `LoadOperationalReadsAsync` with the same token and per-read `try/catch` as its siblings, and refreshes after Unpublish.

**#459 accounting.** The out-of-scope note above says *two* red Admin tests; the real count is **one**, and it is the source-scan crash in `StudentDetailSectionsTests`. The second red recorded in #459 has since cleared; the note is corrected here so the next reader does not chase a ghost.

**R1 vs. spec §2 — a real conflict, resolved in R1's favour, on the record.** The spec puts *Unpublish* **and** *Close* in the published page's kebab; **R1** (owner, this round) pins *Unpublish* as the live **secondary** action. The owner's later pin wins over the spec's placement, so the pair is split: Unpublish stays secondary, `Close` is the kebab item. Deliberate, not an accident — and if round b revisits the header, the split is the first thing to unify.

**P5b (extended) — the header primary is status-driven**, spec §2/§11: *Draft → Publish* (disabled with the reason while approval is required and not approved), *Scheduled → Unpublish*, *Closed → Archive*, *Published → Open progress* disabled (R1), *Archived → no primary*. Kebab: `Duplicate` always; `Schedule…`/`Reschedule…` on Draft/Scheduled (US7: not split across surfaces); `Close` on Published. Every verb already exists on the client (`PublishAsync:295` … `ScheduleAsync:359`) — **wiring, not new API**.
*AC4c:* the primary label is asserted for all five statuses; a draft's kebab holds `Schedule…`; a published kebab holds `Close`; no non-Published status renders `Open progress`; every disabled primary carries a non-empty reason.

**Pass 2b (landed, parent-verified).** The rail (Content & Resources · Rules · Approval), the status-driven header
matrix, and the restored tests. Three files: `Detail.razor`, `Detail.razor.css`, `AssignmentDetailBunitTests.cs`.
Assignments **1427 / 0** (1406 → +21).

**Deviations accepted from pass 2b:**

- The mock's `Rules` count is dropped — Rules is a **field set, not a collection**, and §6 counts collections only.
- `ResourceDto` carries no extraction outcome, so a *resource* renders as a link **without** a grounding line; only
  attachments (which carry `ExtractionStatus`) report grounding. The mock drew `grounds ✓ · URL text` for a link;
  that line had no field behind it, so it goes the same way as the `pts` column.
- `ApprovalStatus.Denied` is terminal and renders a read-only line with no buttons — the brief enumerated three
  states and missed the fourth; offering `Submit` there would be server-rejected, which is worse than a dead button.
- One shared `_actionError` bar (`#assignment-action-error`) replaced the per-verb `_duplicateError`/`_unpublishError`
  fields — six private error fields would be six spellings of one state.
- `RefreshPageAfterMenuAction()` forces an explicit render in the three kebab handlers: a `RowActionsMenu` callback's
  automatic render stops at that child and never reaches the page, so a kebab verb's error line and refreshed header
  were being set and never shown. The duplicate-failure test caught it; `Index.razor` does the same for its row
  actions. Accepted — it is a real framework seam, not a workaround.
- `_questions` became `_children`: one child read now feeds Questions **and** Content & Resources (no second call).
- Kebab labels use the pinned `Schedule…`/`Reschedule…`; the authoring surface's own kebab still reads
  `Schedule`/`Reschedule`. Not reconciled — this round pins this page's wording only.

**Parent acceptance fix (mine, test-only).** AC4b's guard scanned `FindAll("a")` for a `/progress` href, but
`FluentLink` renders `<fluent-link href>` — so the assertion could not see the very link it forbade. Tightened to
`FindAll("[href]")`. Detail bUnit 47 / 0 after the change. The worker flagged the weakness rather than leaving it
buried, which is the right way to surface a guard that does not guard.

### The gate (parent, no flags — `Admin.Shared` is in the diff)

`scripts/round/gates.sh` — **escalated, no `--no-maui`, no `--no-integration`**:

| Leg | Result |
|---|---|
| unit (all contexts, 4701 tests) | **1 red** — `StudentDetailSectionsTests` (#459, pre-existing, out of scope) |
| integration (151 tests, 3 skipped) | **2 red** — the OpenRouter live pair, which CI self-skips for lack of a key and which cannot reach green on a developer machine |
| architecture scanner | **160 / 0** |
| portals (Settings + Students Playwright) | green |
| MAUI head, `-m:1 -p:BuildInParallel=false` | **0 errors, 0 warnings** |

**Every red is pre-existing and documented; none is reachable from this diff.** The two OpenRouter tests are the
reason `--no-integration` is a baseline flag locally — recorded here rather than silently passed over, per the
gate rule that a leg that did not go green is named.

## Plan gate (2b) — `deepseek-v4-pro:0813`, independent of the plan's author

**Verdict: revise — blocked on one P1.** Five findings, all applied above before dispatch.

| # | Finding | Sev | Disposition |
|---|---|---|---|
| 1 | The draft audience table had **no priced read** — only the published, log-backed read was named, so the worker would invent one or ship a data-less panel (Q152) | **P1** | **P2b added** — `GetAssignmentAudiencePreview` + `/{id}/audience/preview` + client method + 3 ACs; expected-files table updated. The round prices **two** reads. |
| 2 | `AudienceContactDto` dropped `OwnerType`, `NotifyOnBroadcast`, `TemplateId` | P2 | Applied — all three restored in P1; `OwnerType` is what fixes the student-contact hex-fragment defect |
| 3 | Progress segment links to `/progress`, a route that arrives in round b | P3 | Applied — P8 renders it **disabled with a reason**, never a 404 link |
| 4 | Directory outage and log outage collapsed into one degradation | P3 | Applied — split in P2: names degrade to empty; delivery degrades to `Unknown`; neither empties the panel |
| 5 | AC4's collapse clause is CSS, not bUnit-assertable | P3 | Applied — moved to the live-render probe; AC4 keeps the structural half |

Note on the gate itself: the first invocation returned a **tool-call block instead of an answer** — the model decided
it needed to open a file even with `--no-tools`. Re-ran with tools explicitly forbidden; the second run answered. Both
runs are in `.pi/`, the second is the record.

## Step 2c — the text mock (approved)

Rendered **once** after the plan gate, three sections (published view, draft view, the audience table at row level),
in the skill's fixed dotted-box format and generated at exact widths — a mock whose columns misalign is unreadable
and gets re-worked, not approved.

**Verdict:** approved by the owner, 2026-10-30 — *"all as recommended"*, which approves the look **and** the six
pinned decisions below. It is a **design artifact**, never evidence: it settles layout, grouping and chrome, proves
no behaviour, and is not cited as verification anywhere. The live-render probe (this round's substitute for the
Tier-3 UI tester) checks the real page against it at 900 px.

**What the mock settled that the plan had not yet said.** Main blocks are **borderless** with a header rule only
(`ShowBorder="false"`, `panel-layout-pattern.md` §3 regional frame) while rail blocks are framed and dotted (§6);
the header is the **shipped** `AuthoringShell` shape — crumbs nav, `[title][badge][kebab]`, then
`LeadingActions → primary → SecondaryActions`, then the fact row — not a prettier arrangement. Two plan statements
were corrected by it: AC4's `<h1>` (R2) and the absent Published primary (R1).

### The mock, as approved

```text
SECTION A · PUBLISHED — /assignments/{id}
 Assignments › Photosynthesis lab report
 Photosynthesis lab report  [ Published ]  ⋮         [ Approval ✓ approved ]  [ Sign-off 21/28 ]   Open progress ▸   Unpublish
 Biology · Homework · 20 points · Due Thu 12 Nov 2026
──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

 Details                                                                   [Edit ▸]   ╭┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╮
────────────────────────────────────────────────────────────────────────────────────  ┆ ▾ Content & Resources   4         ⋮  ┆
 Status      Published                                  Due          Thu 12 Nov 2026  ├┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┤
 Subject     Biology                                                 Points       20  ┆ [doc] Lab safety sheet.docx          ┆
 Grading     Rubric · 4 bands                     Published    Mon 03 Nov 2026 09:12  ┆       grounds ✓ · extracted text     ┆
 Description Students write up the photosynthesis practical they ran in               ┆ [vid] Practical demo.mp4             ┆
             class: 400–600 words plus the labelled results table.                    ┆       no extractable text            ┆
                                                                                      ┆ [lnk] Royal Society — photosynthesis ┆
 Questions  8                                                    [Edit questions ▸]   ┆       grounds ✓ · URL text           ┆
────────────────────────────────────────────────────────────────────────────────────  ┆ [img] Bench setup.png                ┆
 1  Describe the effect of light intensity on rate   Short answer   4 pts             ┆       image only · not grounded      ┆
 2  Which gas is released during photosynthesis?     Multiple       4 opt             ┆                                      ┆
 3  Label the chloroplast diagram                    Diagram        6 pts             ┆ 5 slots · 20,000 chars each          ┆
 4  Explain why the leaf was destarched first        Long answer    6 pts             ┆ instructions excluded (INS-3)        ┆
 5  State two controlled variables                  Short answer   2 pts              ╰┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╯
 6  Calculate the mean rate from the table           Calculation    4 pts
 7  Suggest one source of error                     Short answer   2 pts              ╭┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╮
 8  Predict the result at 60 cm                      Long answer    4 pts             ┆ ▾ Rules   2                       ⋮  ┆
                                                                                      ├┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┤
 Delivery  28                                                [ 15 ▾ rows per page ]   ┆ Due dates not accepted after         ┆
────────────────────────────────────────────────────────────────────────────────────  ┆ Thu 12 Nov 2026 16:00                ┆
 Student        Guardians        Chan.  Delivery          Detail                      ┆                                      ┆
────────────────────────────────────────────────────────────────────────────────────  ┆ Guardian review mandatory            ┆
 Adeyemi, Tolu  none             —      Not reached       no subscribed contact       ┆ before finalization                  ┆
 Okafor, Ngozi  2 · primary, CC  Email  Opened            sent 09:14 · opened 18:22   ╰┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╯
                                 SMS    Failed            mailbox full · retry 09:40
 Okonkwo, Chidi 1 · primary      Email  Sent              sent 09:14
 Bello, Ifeoma  1 · primary      Email  Skipped           guardian opted out
 Hassan, Yusuf  2 · primary, CC  Email  Unknown           delivery log unavailable
                                 SMS    Unknown           delivery log unavailable
 Mensah, Ama    1 · primary      Email  Opened            sent 09:15 · opened 07:02

 Showing 1–15 of 28 · 24 reached                                            ‹ 1 2 ›

 Status
────────────────────────────────────────────────────────────────────────────────────
 PROGRESS                      ┆ GUARDIAN SIGN-OFF       ┆ NOTIFICATIONS
 Cohort 28 · Started 26        ┆ Signed 21               ┆ Queued 0 · Sent 19
 Submitted 19 · Graded 12      ┆ Outstanding 6           ┆ Failed 3 · Skipped 6
 Reached 24 · Signed 21        ┆ Reassigned 1            ┆ Retry scheduled 3
 Open progress ▸  (disabled — arrives with the submission queue)

SECTION B · DRAFT — /assignments/{id}  (same page, same panel, other state)
 Assignments › Yeast respiration write-up
 Yeast respiration write-up  [ Draft ]  ⋮          [ Approval outstanding ]  [ Sign-off — ]   Publish ▸ (disabled)   Duplicate
 Biology · Coursework · 30 points · Due Mon 24 Nov 2026
──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

 Details                                                                   [Edit ▸]   ╭┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╮
────────────────────────────────────────────────────────────────────────────────────  ┆ ▾ Content & Resources   2         ⋮  ┆
 Status      Draft                                      Due          Mon 24 Nov 2026  ├┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┤
 Subject     Biology                                                 Points       30  ┆ [doc] Yeast practical brief.docx     ┆
 Grading     Points                               Created      Fri 31 Oct 2026 14:02  ┆       grounds ✓ · extracted text     ┆
 Description Carbon dioxide production in yeast at four sugar concentrations.         ┆ [lnk] BBC Bitesize — respiration     ┆
                                                                                      ┆       grounds ✓ · URL text           ┆
 Questions  6                                                    [Edit questions ▸]   ┆                                      ┆
────────────────────────────────────────────────────────────────────────────────────  ┆ 5 slots · 20,000 chars each          ┆
 1  State the aim of the practical                  Short answer   2 pts              ┆ instructions excluded (INS-3)        ┆
 2  Name the gas collected                          Short answer   2 pts              ╰┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╯
 3  Record your results in the table                Table          8 pts
 4  Plot volume against concentration               Graph          8 pts              ╭┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╮
 5  Describe the trend                              Long answer    6 pts              ┆ ▾ Rules   1                       ⋮  ┆
 6  Evaluate one source of error                    Long answer    4 pts              ├┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┤
                                                                                      ┆ Guardian review mandatory            ┆
 Targets & Audience  28                                      [ 15 ▾ rows per page ]   ┆ before finalization                  ┆
────────────────────────────────────────────────────────────────────────────────────  ╰┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╯
 Student        Guardians        Chan.  At publish        Detail
────────────────────────────────────────────────────────────────────────────────────  ╭┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╮
 Adeyemi, Tolu  none             —      No contact        no subscribed contact       ┆ ▾ Approval  1                     ⋮  ┆
 Okafor, Ngozi  2 · primary, CC  Email  Will notify       resolved at publish         ├┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┤
                                 SMS    Will notify       resolved at publish         ┆ Submitted Fri 31 Oct 2026 14:05      ┆
 Okonkwo, Chidi 1 · primary      Email  Will notify       resolved at publish         ┆ by J. Mensah                         ┆
 Bello, Ifeoma  1 · primary      Email  Blocked by policy guardian opted out          ┆                                      ┆
 Hassan, Yusuf  2 · primary, CC  Email  Will notify       resolved at publish         ┆ [ Approve ]  [ Reject… ]  [ Deny… ]  ┆
                                                                                      ┆ Reject and Deny ask for a reason.    ┆
 Showing 1–15 of 28 · 24 will notify                                        ‹ 1 2 ›   ╰┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄╯

 Status
────────────────────────────────────────────────────────────────────────────────────
 PROGRESS                      ┆ GUARDIAN SIGN-OFF       ┆ NOTIFICATIONS
 Not published yet             ┆ Not requested           ┆ Nothing sent
 —                             ┆ Outstanding —           ┆ Queued 0
 —                             ┆ Reassigned —            ┆ Failed 0 · Skipped 0
 Delivery, sign-off and progress all start at publish.

SECTION C · THE AUDIENCE TABLE AT ROW LEVEL
 PUBLISHED — the panel is titled "Delivery"
 Student        Guardians        Chan.  Delivery          Detail
──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
 Adeyemi, Tolu  none             —      Not reached       no subscribed contact
 Okafor, Ngozi  2 · primary, CC  Email  Opened            sent 09:14 · opened 18:22
                                 SMS    Failed            mailbox full · retry 09:40
 Okonkwo, Chidi 1 · primary      Email  Sent              sent 09:14
 Bello, Ifeoma  1 · primary      Email  Skipped           guardian opted out
 Hassan, Yusuf  2 · primary, CC  Email  Unknown           delivery log unavailable
                                 SMS    Unknown           delivery log unavailable

 DRAFT — the same panel, titled "Targets & Audience"
 Student        Guardians        Chan.  At publish        Detail
──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
 Adeyemi, Tolu  none             —      No contact        no subscribed contact
 Okafor, Ngozi  2 · primary, CC  Email  Will notify       resolved at publish
                                 SMS    Will notify       resolved at publish
 Okonkwo, Chidi 1 · primary      Email  Will notify       resolved at publish
 Bello, Ifeoma  1 · primary      Email  Blocked by policy guardian opted out

 CAPPED — 320-student cohort: the read stops at 200 (Q146), the table pages inside it
──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
 Showing 1–15 of 200 (capped)                                  ‹ 1 2 … 14 ›
 The cap is stated where the pager is, so a page can never be read as the whole cohort.
```

### Consent checklist — approved together with the layout

| Panel | Region | Collapsible | Action lives in |
|---|---|---|---|
| Details | main | **no** | panel header — `Edit ▸` (deep-links to `/edit`) |
| Questions | main | **no** | panel header — `Edit questions ▸` (deep-links) |
| Delivery / Targets & Audience | main | **no** | panel header — rows-per-page only; pager in the footer; **no row actions** this round |
| Status | main | **no** | segment footer — `Open progress ▸`, disabled this round (P8, R1) |
| Content & Resources | rail | **yes** | header kebab; grounding readout per row |
| Rules | rail | **yes** | header kebab |
| Approval (draft only) | rail | **yes** | **body buttons** — `Approve` / `Reject…` / `Deny…` (R4) |

### Pinned decisions (attended — owner, 2026-10-30: "all as recommended")

| # | Fork | Pinned answer | Rejected |
|---|---|---|---|
| R1 | Published primary while the progress page is round b | `Open progress` stays the primary and renders **disabled with the route's reason**; `Unpublish` stays live beside it | promoting `Unpublish` to primary for one round (two matrix rows for one state); shipping a stub route |
| R2 | AC4's `<h1>` | amend AC4 to **one shell-owned action-bar heading** (`<h2>`); the shell itself is not touched | a `HeadingLevel` parameter on `AuthoringShell` (shared chrome, full gate, for one heading level) |
| R3 | Status panel on a draft | **renders**, segments reading `Not published yet` / `Not requested` / `Nothing sent`, one reason line | hiding it until publish (≈90 px saved, but it hides the panel a reader needs *before* publishing) |
| R4 | Approval decisions | **body buttons** — `Approve` / `Reject…` / `Deny…` (US6: the decision lives with the content) | header kebab — consistent with row actions, but hides the panel's whole purpose |
| R5 | Pager chrome | rows-per-page **in the panel header**; pager + cap notice **in the footer** | control beside the pager (crowds the footer and competes with the cap notice) |
| R6 | Delivery triage | **no filter** in round (a) — name order, unreached rows visually distinct; filtering is the progress page's job (US34) | a `Failed · 3` filter chip row (new chrome, wrong round) |
| **R7** | How the page gets its blocks, given the embedded View body is a summary card | **D2** — the page owns the skeleton, Delivery, Status, Questions table and rail; **Details** embeds `AssignmentAuthoring Mode=View` via a new additive `Embedded` mode (shell + summary header + questions hint + targets accordion suppressed). Cost accepted: `Admin.Shared` joins the round, so the gate runs with **no flags** plus the MAUI head build | **A** — page owns all six blocks (forks the facts layout *and* orphans `Mode=View`: its 11 tests would keep passing against an unreachable surface); **B** — embed the View body as-is (fails its own "matches the mock" acceptance); **C** — widen the component's View body into a full read-only skeleton (structural change to 4,021 lines, inside the round's own named risk #1) |
| **R8** | PR shape for a round whose successor depends on it | **`gh stack`** — owner, 2026-10-30: *related tasks ride one train.* Round (a) is **layer 1** rooted on `main` (`gh stack init feat/assignment-view-page`); round (b) is `gh stack add`-ed on top when it starts, so review reads as an ordered sequence and layer 1's CI covers both | a **normal PR**, on the argument that the Q131 dependency is satisfied by round (a) being *merged* (round b then branches from a main that already carries the audience read) — correct on the mechanics, rejected on the owner's practice: two rounds of one arc should not arrive as two unrelated PRs |
| **R9** | What happens when the live pass disagrees with the approved mock | **Rework through the worker** — the probe's findings go back to a worker as its next brief (never a parent patch), then the patch is re-frozen and the probe re-run. Scope line: *a reader would notice* (missing block, wrong order, duplicated heading, a dead or lying control, the 900 px collapse not collapsing) ⇒ rework; spacing/tone/pixel drift ⇒ PR notes | fixing in the parent seat (unattributable edits inside a frozen diff); "note everything" (ships a page nobody looked at — the 2026-10-14 media-dialog failure the live-pass rule exists for) |

**Mock correction recorded with R7 (my error, not the owner's).** The approved Questions table drew a `pts` column;
no per-question points field exists anywhere in `Assignments.Contracts` (`AssignmentQuestionReadDto` =
`Id, QuestionText, QuestionType, DisplayOrder, ModelAnswer?, Options[], GenerationId?, ResponseKinds?,
Instructions?`). The column is **dropped**; the feasible set is `# · question · type · options`. Two Status-strip
counts the mock drew have no supporting read either — `Started` and `Reassigned` (a `SignOffStatusDto` cannot
distinguish a reassignment) — so the strip renders Cohort · Submitted · Graded · Reached and Signed · Outstanding,
and does not invent them.

No candidate lessons were raised at this gate: the mock needed no owner correction. The six items were decisions
the mock **surfaced**, not fixes it required.

### Grill round (owner, 2026-10-30) — PR shape, live pass, rework policy

Five questions, one round, all settled. R8 and R9 above carry the two that changed the plan; the rest are the
execution contract for closing the round.

| # | Question | Settled |
|---|---|---|
| Q1 | PR shape | **`gh stack`** (R8) — round (a) = layer 1 via `gh stack init feat/assignment-view-page`; round (b) = `gh stack add` when it begins. The park commit rides inside layer 1. |
| Q2 | Probe environment | **Dev posture**: `FEATURE:DisableOIDCAuth=true` from the AppHost's `appsettings.Development.json`, the already-running `postgres`, fixtures seeded by a new runbook modelled on `documents/runbooks/student-view-probe-seed.md` (fixed ids, tenant as a variable) — one **published** assignment with a 28-row audience, one **draft** with targets. The OIDC path is deliberately unexercised: this round changes no authorization decision. |
| Q3 | Sequence | **Commit → probe that SHA → `gh stack submit`.** A probe defect becomes a second commit on layer 1, so the reviewed tip is always the probed tip. |
| Q4 | Probe executor and evidence | `deepseek-v4-pro:0813`, **fresh run**, driving Playwright: screenshots at **900 px and 1400 px** for **both** states, a DOM checklist taken from the approved mock (exactly one heading; chips → primary → secondaries; the disabled primary's reason; three status segments; three rail panels; `1–15 of 28` with the cap sentence; **no** `fluent-tab`), and the browser console log. It may not substitute a bUnit render for the browser. |
| Q5 | Defect policy | **Rework via the worker** (R9): "a reader would notice" ⇒ back to a worker, re-freeze, re-probe; cosmetics ⇒ PR notes. |

### Grill round 2 (owner, 2026-10-30: "all as recommended") — the ship mechanics

A second round, because pinning the stack unblocked the questions that hang off it. All six settled.

| # | Question | Settled |
|---|---|---|
| Q1 | Merge mechanics for a one-car stack | Layer 1 merges with the **canonical `gh pr merge --squash --delete-branch`**; `gh stack merge --yes --squash` takes over once round (b) makes it a genuine train. The one `gh stack sync` before `gh stack add` is the parent's cost. |
| Q2 | Round (b) timing | **Waits for layer 1 to merge**, then `gh stack add`. Stacking on a tip no browser has validated would let a probe-driven rework (R9) rewrite the base under round (b). |
| Q3 | PR presentation | **Ready for review, hand-written body** — summary, AC table, gate table naming the non-green legs, and both probe screenshots. Not `--auto`: a probe has already validated the diff by then, so a draft would hide it. |
| Q4 | Probe fixtures | **Committed** `documents/runbooks/assignment-view-probe-seed.md` + its SQL, following `student-view-probe-seed.md` (fixed ids, tenant as a variable, never in `MigrationService`). Round (b)'s tester reuses them. |
| Q5 | Arbitration: browser vs mock vs spec | **Spec wins over the mock.** A spec↔mock divergence is recorded as a **mock correction**, not a defect; rework is only for where the page fails both. Precedent: the `pts` column and resource grounding were already dropped this way. |
| Q6 | The vacuous-assertion class (two instances this round) | **Filed as** [#460](https://github.com/stayboy/School-Collab/issues/460); only assertions in files this layer touches are fixed here. The issue names both worked examples — the `FindAll("a")` guard that could not see `<fluent-link>`, and the degradation test whose fake *threw* where production degrades quietly. |

### Grill round 3 (owner, 2026-10-30: "all as recommended") — the name source, and who drives the probe

**Q1 — student names for contact-less students.** The audience read takes `StudentName` from the
`contacts/subscribed` response, and a student with no subscribed contact produces no row there — so the
spec's "explicit *not reached* rows" would render with a **blank name**, hiding the identity of exactly the
students a teacher must chase. `students/by-target` returns `Guid[]` only; the only other Students reads are
`/{id}` (an N+1), `/by-number`, `/by-grade/{gradeId}` and the paged list. **Settled: extend `students/by-target`
to return `{ id, name }`** and have the two audience handlers name rows from the cohort they already resolved —
this deletes the "names ride the subscriber read" smell at the root. Because that is a **cross-context contract
change** in a round already carrying shared chrome, it ships as **layer 2 of the stack**, not folded into the
committed layer 1 (which stays reviewable as it stands). Rejected: deferring to round b (ships a panel that
cannot say *who* was not reached) and any client-side join (there is no second field to join on).

**Pass 2d (defect, found by fixture design, not by the probe).** My pass-2c rule marked the whole panel degraded
whenever *any* cohort student lacked a name — which, given names ride the subscriber read, fires on a perfectly
normal tenant where somebody simply has no guardian contact. **Rework via the worker (R9):** degrade only on a
**total** name loss over a non-empty cohort; partial loss stays silent and is recorded as a known limit.

**Probe executor changed (owner, cost).** `clef:27b` **does not exist** in this Pi registry (verified: no `clef`
model under any provider; `typesafe/jev-router` is a router, not a visual judge; the 27B vision models are
`qwen/qwen3.8-27b`, `prism-ml/ternary-bonsai-2-27b`, `gemma-3-27b-it`, none on ollama-cloud). Settled split:
**`ollama-cloud/gemma4:31b` drives the browser and collects evidence** (screenshots at 900/1400 px for both
states, the DOM checklist, the console log), and **the parent renders the fidelity verdict** against the
approved mock. The driver never grades its own evidence; pixel-matching a screenshot against box-art has no
ground truth, so the checklist — not an image diff — is the real test.

## Resume point (updated 2026-10-30, late) — live probe DEFERRED by owner

**Owner call: park the live pass until all UI work is completed** — overriding the "probe before submit" order
(grill round 2 Q3). Nothing is submitted, nothing is pushed. The verdict above stays `ACCEPTED PENDING LIVE PASS`;
the probe is a park, not a pass. `gh stack submit --open` happens only after the browser finally sees the page.

The AppHost was killed and its ports checked closed; `postgres-84ff496f` / `rabbitmq-84ff496f` were left running
on purpose (persistent data-volume lifetime) — the seeded fixtures and the two settings rows survive the pause.

**Learned while trying to probe (would have been re-paid at resume):**

1. THIS session's postgres is `postgres-84ff496f` on `127.0.0.1:11637` (the user-secrets-pinned port). The older
   `postgres-057ea9d7` also held `11637` for the first hours, so seeding went to **the wrong instance** — that is
   the whole "Assignment not found" mystery. At resume, seed **only** whatever container currently owns `11637`.
2. Aspire re-creates the postgres/redis/keycloak/mailpit/pgadmin containers with **random name suffixes at every
   AppHost start** (cache went `uhhuwftu` → previous `ncabnuqg`). Any restart recipe must look them up by port or
    prefix, never by the exact name. Postgres/rabbitmq are the exception: `WithDataVolume` + persistent lifetime.
3. Tenant ids **rotate with the database**, not the code: this fresh instance's Hydeson is
   `6363fc4d-2c3d-4d50-94eb-047b0608babf` (the earlier `514faa38…` died with its instance).
4. **A fresh students-db has no students, so the seed's gender lookup has no source** — pass
   `-v gender_id=<guid>` explicitly (this instance: `GENDER_FEMALE` = `6eb9109a-96d6-47e4-adc8-6a05ea4e540b`),
   exactly as the SQL's own comment says.
5. The dev tenant claim is `TenantProvider` → `tenant_id` claim, where the dev `TestAuthHandler` reads Redis key
   **`dev:tenant-selection`** (value = the GUID as plain UTF-8 text; `dev:tenant-default` holds the portal's
   applied default) through `IDevTenantSelection` (`DevTenantSelection.cs:81`). Clicking the switcher never
   took; writing the key directly is the reliable path. The session's Redis rejected `redis-cli` with
   *Connection reset by peer* on PLAINTEXT but the auth'd attempt was never completed — try `--tls -p <6380-mapped>
   -a <cache-password>` first next time.
6. **The fixture author bug is already fixed** (teacher `…00a1` → dev teacher `…0003`): the list page and the
   by-id read both apply `TeacherScope.AllowsAnyTargetGrade` (`ListAssignmentsQueryHandler.cs:165`), which filters
   out rows the scoped dev teacher didn't create — the earlier "No assignments yet." was this + the wrong
   instance, jointly.
7. Memory pressure note: the machine sat at **0.2 GB free** when the probe hit `ERR_INSUFFICIENT_RESOURCES`. At
   resume, check memory before launching a browser; headless chromium + eight services need several GB.

**The probe harness is ready and proven working** at `C:/tmp/avprobe` (`probe.js`; four screenshots + `probe-report.json`): it launches system Edge (`channel: 'msedge'` — the cached playwright chromium build is missing),
waits for `#assignment-facts` beyond the no-prerender shell, and captures both states at 900/1400 px plus the DOM
report. It currently screenshots the *empty* states — at resume: tenant right → DB right (`11637` owner) →
`node probe.js` → judge.

### Resume order (unchanged from the pause, corrected)

1. Start AppHost; wait for admin on `https://localhost:7300`.
2. Confirm which container owns `11637`; seed that one (students `-v gender_id=…`, assignments) under the tenant
   the pill will show — write Redis `dev:tenant-selection` directly with that tenant's current GUID.
3. Re-apply the two settings rows if the instance changed (`blocked_channels=[1]`, approval-required = true).
4. `node probe.js` → judge against the approved mock; spec beats mock; mock divergences recorded as corrections.
5. Any reader-visible defect ⇒ **rework via a worker** (R9), re-freeze the patch, re-probe.
6. Then `gh stack submit --open`, hand-written body.

`clef:27b` remains unusable for subagents until pi restarts; the probe's evidence role is filled by `probe.js`,
and the fidelity verdict stays with the parent seat (per grill round 3).

## Static review — `deepseek-v4-pro:0813`, independent of the implementer

Reviewed the frozen 22-file patch against the plan, the approved mock and the spec. **Verdict: `CLOSED WITH NOTES`.**
No blocking defect: tenant filtering (global query filter, foreign id → 404), cap/`Truncated` agreement, name-then-contact
ordering, `Degraded`/`Unknown` instead of a fabricated `Sent`, preview persists nothing, `Reached = false` for a
contact-less student, one batched subscriber read and no N+1; the privacy field set (no address, phone, guardian name
or extracted text in the DTO **or** the client mapping); the endpoints' inherited group policy and `TypedResults`;
`Embedded` default-off with the guard on every gate and one call site; the header matrix; the panel structure.

**The three findings were all degradation surfaces** — and two of them broke the plan's own "never a silent empty
list" promise (P2, gate finding #4), so they were **fixed in pass 2c rather than carried**:

1. `GetAssignmentAudience.cs:184-192` — the "directory failure → `Degraded = true`" branch was **unreachable** in
   production: `StudentsContactResolver` swallows `HttpRequestException` per owner, so a name outage returned few
   names with `Degraded = false` and the panel silently rendered `—`. The green test missed it because it used a
   *throwing* fake the live resolver never behaves like.
2. `GetAssignmentAudiencePreview.cs:76/:106` + `AudienceTable.razor:53-55` — an empty **degraded** payload rendered
   "No students resolve…", a false statement about why the panel was empty.
3. `AudienceTable.razor:47` — no "could not be loaded" state, so a hard failure spun forever, unlike its Questions /
   Materials / Rules siblings; and `GetAssignmentAudiencePreview.cs:123-124` ran the policy resolvers outside any
   `try/catch`, turning a policy outage into a 500.

**Deviations the reviewer accepted:** `AudienceLimits.AudienceCap` as a static wrapper rather than a bare constant
(one source of truth either way — naming, not drift); and `Duplicate` as kebab-only rather than the mock's visible
secondary beside the draft primary, because the six pins never covered its placement and the later P5b matrix + AC4c
pin kebab-only, which supersedes the drawing.

## Named risks

1. **The authoring component is the round's real risk.** Suppressing its shell touches a component with a large
   bUnit suite and several call sites; a regression there is worse than a wrong panel on the view page.
2. **Privacy is a contract property, not a view property.** No address exists in the DTO, so no later template can
   render one.
3. **NFR-4.** The page must not re-implement read-outs the authoring body already renders; the embeddable mode is
   how that is avoided, and duplicating markup would defeat it.
4. **Cap × paging.** 200 rows at 15 per page: the panel states truncation and the pager must not contradict it.

## Out of scope (round b carries it)

Progress page, submission page, `SubmissionForReviewDto` widening, notification retry/requeue, status pills on the
edit surface, the twelve unpaged raw grids (#458), and the two red Admin tests (#459).

---

## Acceptance

**Mode:** attended. **Tier 2.** Acceptance written by the plan's author, which is also the parent seat; the static
diff review was independent (`deepseek-v4-pro:0813`) and is not this verdict.

**Verdict: `ACCEPTED PENDING LIVE PASS`** — *not* final. `testing.md` § "UI rounds need one live pass" makes a live
browser pass mandatory before a `.razor` round may be called accepted, and no such pass has run (see the last
paragraph of this section). The code, contract and gate verdicts below stand on their own; only the word
*accepted* is withheld until the probe reports.

| AC | Verdict | Evidence |
|---|---|---|
| AC1 — audience read composes targets ∪ recipients ∪ log, names batched, no per-student hop | pass | `GetAssignmentAudience.cs`; reviewer verified one batched subscriber read, no N+1 |
| AC2 — a student with no subscriber is `Reached = false` with zero contacts | pass | `AStudentWithNoSubscriber_IsUnreachedAndContactless` |
| AC3 — a stale log row yields `Unknown`, never a fabricated `Sent` | pass | handler tests; reviewer confirmed the degradation paths |
| AC4 — one shell-owned heading, zero `FluentTab`, both regions | pass | bUnit; `NoSeveredBlockGuards_InRazor` |
| AC4b — published primary disabled **with a non-empty reason**, no `/progress` link | pass | `#assignment-progress-reason`; href guard tightened to `[href]` this round |
| AC4c — status-driven primary for all five statuses; kebab matrix | pass | label matrix + kebab tests, added in pass 2b |
| AC5 — the authoring suite passes untouched; `Embedded` yields facts + notes only | pass | 11 pre-existing `View` tests green, unedited; `Edit_Embedded_…` pins the guard |
| AC6 — 8 rows ⇒ no pager; 28 rows ⇒ `1–15 of 28` | pass | Questions 8-row and audience 28-row tests |
| AC7 — draft preview renders rows with no delivery state and **writes nothing** | pass | `ADraft_WithTargets_…_AndWritesNothing` asserts recipient and log counts unchanged |
| P2 "never a silent empty list" | pass **after pass 2c** | the three review findings were all this promise; see § "Static review" |

**What shipped:** 22 files (src + tests) in `documents/rounds/diffs-assignment-view-page.patch` — two reads + contract
+ routes + client, the rebuilt `Detail.razor` on the panel skeleton, page-local `AudienceTable` / `QuestionsTable` /
`StatusStrip`, the `Embedded` mode on `AuthoringShell` + `AssignmentAuthoring`, and the status-driven header matrix.

**Gate (escalated, no flags):** unit 4,701 with **1 pre-existing** red (#459), integration 151 with **2 pre-existing**
reds (the OpenRouter live pair CI self-skips), architecture **160/0**, both portal suites green, MAUI head
**0 errors / 0 warnings**. Every non-green leg is named above; none is reachable from this diff.

**Residual risks carried, with the owner informed:**

1. A cohort student whose directory row yields no name now marks the panel degraded — the price of the only
   observable signal for a swallowed resolver failure. Rows stay intact; no current fixture trips it.
2. A *resource* (link) renders without a grounding line: `ResourceDto` carries no extraction outcome.
3. `Schedule…`/`Reschedule…` on this page use the pinned ellipsis; the authoring surface's kebab still reads
   `Schedule`/`Reschedule` — unreconciled by design this round.
4. `Unpublish` (secondary) and `Close` (kebab) are split on Published by R1's pin over spec §2's wording — the first
   thing to unify if round b revisits the header.

**Committed as `d46b7223`** on `feat/assignment-view-page` (on top of the park commit `8546ddef`), and the branch was adopted as **layer 1** of a `gh stack` rooted on `main` (`gh stack init`). Nothing is pushed and no PR exists: `gh stack submit` runs only after the live pass passes (Q3). The probe fixtures, their runbook and the probe's outcome land as further commits on this same layer.

**One check not run — and it is a gate, not a courtesy.** The 900 px live-render probe was **not executed**. The
repo rule above (`.github/copilot/rules/testing.md` § "UI rounds need one live pass") makes it mandatory for any
round whose changed-file list contains `.razor`/`.razor.css`, "automated Playwright asserts preferred, a hand-driven
pass with the console open the floor" — which is exactly why the verdict is *pending* rather than *accepted*. AC4's
container-query collapse is currently verified **by structure only**: bUnit sees the grid, it cannot see a CSS
collapse rule (gate finding 5 deferred precisely this). The page has never been rendered in a browser this round,
so **no claim of visual fidelity to the mock should be read into this acceptance**. The probe, its executor and its
required evidence are the open question in the round's grill round.

---

## Resume point (paused 2026-10-30, resume 18:00 GMT)

**Everything is committed and green; only the live pass is unfinished.** Tree clean, two stack layers, nothing pushed, no PR.

```
● feat/assignment-view-names  3452a5ad  layer 2 — names from the target read
○ feat/assignment-view-page   41e9cc7f  layer 1 — the page, the reads, fixtures, 2d
└ main
```

| Gate | Result |
|---|---|
| build `SchoolCollab.slnx -v q` | 0 errors |
| Assignments 1432 · Assignments.Api 207 · Students.Api 38 · Students 695 · Architecture 160 | **0 failures** |
| `gates.sh` no flags | 2 pre-existing #459, 2 OpenRouter live (CI skips), 1 flake (`CodedValueDropdown_WithSelectedId_MarksOptionSelected`, 3/3 green in isolation on this tree) |
| MAUI head | 0 errors / 0 warnings |

### The one blocker, precisely

The probe runs and the app renders — Blazor circuit connects, nav and pages paint — but **the app's tenant is `System`**, and every read therefore returns nothing:

- `◐ System` is the tenant pill; "Hydeson School" beside it is a *term/school* label, not the tenant.
- The switcher lists `(system / no tenant)`, `Dev School`, `Hydeson School`, `Little Legends`. Clicking `Hydeson School` did **not** take: after click + reload the pill still reads `System`, and both the list page ("No assignments yet.") and the detail page ("Assignment not found.") stay empty. `[role=option]` matches 0 — it is a `FluentSelect`, so it needs its own interaction (or the Redis dev-selection key written directly; the `cache-*` container rejected `redis-cli` with an *I/O error* on both ports, so auth/TLS needs checking first).
- Server-side the tenant comes from `TenantProvider` → the `tenant_id` claim, which the dev `TestAuthHandler` seeds from Redis `IDevTenantSelection` (`TenantProvider.cs:34-58`, `AdminDefaultTenantResolver.cs:16`). That is the seam to drive.

### Data already in place (verified against the DB)

- Both probe assignments seeded under **`514faa38-37c2-4969-8825-51cac6854723`** (Hydeson School): `a50b0000-…0001` published, `a50b0000-…0002` draft. 28 students, the five contact shapes, 8+6 questions, 2 attachments, 1 resource; published mix Sent 18 / Opened 4 / Failed 3 / Skipped 2 / Unknown 1, 23 reached.
- Settings-db rows for the same tenant: `blocked_channels=[1]` (id `7c0b0000-…0001`, inserted) and `requires_approval_before_publish=true` (updated the tenant's pre-existing row `77e42279-e2bc-471a-87ca-e5506549dcd6`, whose prior value was **NULL** — that is the restore). Dev School's rows were deleted again, so it is back to pre-probe state.
- **Seed fix still needed in the runbook**: its fixed GUIDs are unique per *database*, not per tenant — seeding a second tenant collides on `pk_tenant_notification_policies`. The working form is `INSERT … ON CONFLICT (id) DO UPDATE SET tenant_id = EXCLUDED.tenant_id`, and for `tenant_assignment_policies` an **UPDATE of the tenant's existing row** (there is a unique index per tenant).

### Restart recipe

```bash
# from the repo root — Admin lands on https://localhost:7300 (self-signed; ignore TLS errors)
nohup dotnet run --project src/AppHost/SchoolCollab.AppHost > /tmp/apphost.log 2>&1 &
# postgres is postgres-057ea9d7 on 127.0.0.1:11637; password in the AppHost user-secrets
# (Parameters:postgres-password). Seed tenant: 514faa38-37c2-4969-8825-51cac6854723
```

Probe harness is ready at `C:/tmp/avprobe` (`probe.js`, `probe-report.json`, four PNGs — currently screenshots of the empty "not found" states, worth keeping only as proof the harness works). Playwright 1.64.1 is installed there and launches the **system Edge** (`channel: 'msedge'`) because its pinned chromium build is not in the cache.

### Order of work when resumed

1. Get the app into the **Hydeson** tenant (switcher or Redis), confirm `/assignments` lists the two PROBE rows.
2. Re-run `node probe.js` → four screenshots + DOM report.
3. Judge the screenshots against the approved 2c mock (parent seat; the driver collects only). Record any spec↔mock divergence as a **mock correction**, per R-Q5.
4. Any "a reader would notice" defect ⇒ **rework via a worker** (R9), re-freeze the patch, re-probe.
5. Only then `gh stack submit --open` with a hand-written body (summary, AC table, gate table naming the non-green legs, both screenshots).

`clef:27b` is visible to the session (`ollama/clef:27b-cloud`, text-only) but the **subagent registry rejects it** under all three id forms — a restart of pi is needed before it can drive a probe.
