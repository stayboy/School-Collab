# Round: assignment-view-page — the view page + the audience read

Mode: **attended** (Tier 2 + compensating probe; owner, 2026-10-30). Branch: `feat/assignment-view-page`.
Base head: `4338b9ce`. Spec: `documents/specs/assignment-detail-view-modernization.md` (merged, 3× `0813`-reviewed).
Issue: #453, round (a) of two.

---

## Parked — resume here (owner, 2026-10-30)

Work stopped after the plan gate, before the 2c mock. Nothing under `src/` has been touched; the branch carries this
round doc and nothing else.

**State**

| Item | Value |
|---|---|
| Branch | `feat/assignment-view-page`, pushed, **no PR opened** (the round never reached a worker) |
| Base | `4338b9ce` (= `main` after #457) |
| Round doc | this file, committed on the branch |
| Plan gate | **complete** — `0813` verdict *revise*, five findings, all applied (see § "Plan gate") |
| Next step | **2c — the text mock**, three sections, for owner approval |

**To resume:** read this file top to bottom, then render the 2c mock. Do not dispatch a worker before the owner
approves it. The plan is settled; only the look is outstanding.

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

## Plan

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
*AC4:* one `<h1>`, zero tabs, both regions present. The container-query collapse is **not** asserted here — a
collapse rule is CSS and bUnit sees structure only; the live-render probe checks it at 900 px (gate finding 5).

**P6 — Embeddable authoring (§4.1, Q129).** One additive parameter suppresses the authoring shell and yields its
body; Edit and Create unchanged. *AC5:* the existing authoring bUnit suite passes untouched.

**P7 — Tables and paging (Q156, Q162).** Questions, the audience/Delivery table page at 15; the pager is **absent**
when the set fits; Delivery and Questions expose a 15/25/50 control.
*AC6:* an 8-row Questions table renders no pager; a 28-row audience table renders `1–15 of 28`.

**P8 — Status strip (§4.8, Q155).** One panel, three segments (Progress · Guardian sign-off · Notifications)
separated by coloured rules drawn from the `--status-*` tokens.
*(Gate finding 3: the Progress segment's "Open progress" affordance points at `/assignments/{id}/progress`, which
does not exist until round b. In round (a) it renders **disabled with a reason** — "Progress page arrives with the
submission queue" — never a link that 404s. Priced here so the worker does not invent a route.)*

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

## Step 2c — the text mock

Posted **once** after the plan gate, in the skill's fixed format, covering **three sections**: published view, draft
view, and the audience table at row level. No worker is dispatched until the owner approves it.

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

|---|---|
| Page | `Assignments.Application/Components/Pages/Assignments/Detail.razor` **(rewrite)** + `Detail.razor.css` |
| Page-local components | `AudienceTable.razor`, `StatusStrip.razor` (Assignments.Application, beside the page) |
| Authoring | `AssignmentAuthoring.razor` — embeddable mode (suppress its shell) |
| Contract | `Assignments.Contracts/ContractTypes.cs` — audience DTOs + cap constant |
| Query | `Assignments.Core/CQRS/Assignments/Queries/GetAssignmentAudience/**` + `…/GetAssignmentAudiencePreview/**` |
| Endpoint | `Assignments.Api/Endpoints/AssignmentRoutes.cs` — `GET /{id}/audience` **and** `GET /{id}/audience/preview` |
| Client | `Assignments.Application/Services/AssignmentsApiClient.cs` |
| Tests | `Assignments.Tests.Unit` (handler + bUnit page), `ArchitectureTests.Unit` (typed results — existing) |
