# Assignment Authoring — Compartmentalized Single-Page Experience

> **Status:** adopted (grill session 2026-10-01; all frontier decisions ratified by the
> owner) — **CLOSED 2026-10-06**: implemented in the main line and swept against the
> code, with §17 kept as the recorded residual set (carried follow-ups, not open work
> against this spec). This is the durable, source-of-truth contract for **how an assignment is
> authored, edited and viewed** and for the **targeting model** that decides who
> receives it. It **supersedes the create wizard** (`Assignments/Create.razor`'s
> three-step `FluentWizard`) as the intended authoring flow.
>
> **Companion specs (not replaced by this one):**
> - `documents/specs/assignment-request-feature-spec.md` — the AR feature set,
>   personas, lifecycle and notification rules. This spec governs the *authoring
>   surface* over that feature set.
> - `documents/specs/assignment-policy.md` — the per-grade/tenant policy contract
>   (source of the read-only policy values shown in compartments 3 and 4).
> - `documents/specs/assignment-creation-with-ai.md` — AI question generation,
>   incorporated here as an **action**, not re-specified.
> - `documents/specs/notification-delivery-plan.md` — notification policy contract.
> - `documents/specs/assignment-authoring-content-questions-modern-ui.md` — **presentation-only
>   refinement** of compartment 5 (Content & Resources) and compartment 6 (Questions & AI): a
>   Claude-project-style two-column composition, the Context-style resource card, a composer hero,
>   and a single-line question list whose kebab opens the editor in a dialog. It changes no
>   behaviour, gating, save semantics or contract defined below (this spec stays CLOSED and
>   authoritative for those).
> - `documents/specs/assignment-create-edit-redesign.md` — **presentation-only addendum
>   (2026-10-08):** reordered Create field order, the bottom "Instructions" compartment,
>   strand/lesson picker buttons, breadcrumb navigation, and the **summary-first draft-edit
>   surface** (summary card with pencil + always-on Questions & AI + Targets accordion).
>   Where it reorders or hides markup it wins; behaviour, gating, save semantics and the
>   §11 lifecycle matrix stay authoritative here.
>
> **Implementation status:** **implemented in the main line.** R1 (#288), R2 (#291) and
> R3 (#293) are merged on `main` — the compartmentalized authoring page, the targeting
> model and the attachment-grounded AI surface specified below exist in code, with tests.
> What those rounds deferred or found open (and the defects they closed by hand) is
> recorded durably in §17.
>
> **Closed 2026-10-06 (spec staleness sweep).** Everything this spec specifies is shipped;
> the four §17 entries are the only carried residuals and they have no owner — this spec
> is no longer a work queue. Re-open it only if one of them is scheduled, or if the
> authoring surface is redesigned again.
>
> **Parallel surface (2026-10-01):** the Prefab **teacher portal**
> (`documents/specs/teachers-ward-portal-prefab-plan.md` §1 T1–T7) is a
> **read/review second surface**; this Blazor compartment design remains the
> **authoring destination**. Both tracks run (owner decision Q5 = A); ~~the portal
> is the eventual migration target for the working assignment feature set.~~
> *(**Superseded 2026-10-05:** the portal's scope closed at read/review/grade — this Blazor
> compartment design **is** the authoring surface, not a way-station toward a portal-side
> authoring experience. `documents/solution/portal-scope-decision.md`.)*

---

## 1. Summary

Assignment authoring becomes **one compartmentalized page** used in three modes —
**Create**, **Edit** and read-only **View** — instead of a create wizard plus a
divergent edit form. The page splits an assignment into **six compartments**
rendered as stacked, always-visible section cards with a sticky compartment
jump-nav and a sticky action bar.

Two things are added by this spec beyond presentation:

1. an `Instructions` field (student-facing, distinct from `Description`), and
2. a **multi-constraint targeting model** (`AssignmentTarget`) supporting grade
   levels, streams, individual students, activity groups, and "everyone".

Recipients are **derived**: the students matching the constraints are the
recipients of a published assignment. A live preview shows the match count while
authoring.

---

## 2. Decision log (ratified 2026-10-01)

| # | Decision |
|---|---|
| D1 | **No wizard.** Create, Edit and View are one compartmentalized single-page experience. |
| D2 | **Six compartments** (§3); Assignment Type and Grading Format live in **Basics**, not in Submission. |
| D3 | **Stacked section cards** + sticky compartment jump-nav + sticky action bar (not tabs, not accordion). |
| D4 | Policy-derived fields render **read-only resolved values** with "Inherited from Grade/Tenant policy" badges. No per-assignment policy overrides in this design. |
| D5 | **One shared component, three modes**; Edit reaches full parity with Create while `Draft`/`Scheduled`; `Published|Closed|Archived` open read-only. `Detail`'s operational tabs remain. |
| D6 | **Sticky action bar** for the primary status-driven action + **kebab overflow** for secondary actions; publish-time choices captured in a confirm dialog. |
| D7 | The **AI generation action is a header/kebab shortcut** that anchors to the Questions & AI compartment's generator (single source of form state) — not a duplicate dialog. |
| D8 | **Full scope** accepted: presentation + `Instructions` + multi-constraint targeting + attachment-grounded AI generation, delivered as the R1–R3 stack (§14). |
| D9 | **`AssignmentTarget`** child rows with union semantics; `AllStudents` short-circuits and is mutually exclusive with other kinds. |
| D10 | **Stream targets are grade-agnostic** (stored as `StreamCodedValueId`; resolved against active enrollments). |
| D11 | **Primary grade** on the assignment drives authoring defaults + approval gate; **per-recipient grade** drives notification caps/channels at publish. |
| D12 | **Conditional fields render disabled with an inline reason**, never hidden (§12). |
| D13 | **Lifecycle action matrix** per §11; published structural edits stay locked; unpublish → edit is the path. |
| D14 | **Round slicing + execution mode** per §14 (R1 Light Tier 2; R2/R3 Full four-agent Tier 3). |

---

## 3. The compartment model

| # | Compartment | Contents | Visibility |
|---|---|---|---|
| 1 | **Basics** | Title · Description · Instructions · Assignment Type · Grading Format · Subject/Topic · **Primary grade** | Always visible; the always-on spine |
| 2 | **Audience & Targets** | Target constraint set (grade levels · streams · individual students · activity groups · Everyone) + live recipient preview | Always visible |
| 3 | **Delivery & Publishing** | Delivery mode (draft/scheduled/published) · Available-from · Due date · Archive grace days · resolved notification policy (read-only) · approval requirement (read-only) | Always visible |
| 4 | **Submission & Sign-off** | Max score · pass score · max attempts · feedback mode (auto/instant/teacher) · guardian review (`MandatoryReview`) · guardian signature (`RequiresSignature`) · resolved signature requirement (read-only) | Always visible; consequence fields disabled when inapplicable (§12) |
| 5 | **Content & Resources** | Content modules (video/guide) · attachments · URL resources | Always visible |
| 6 | **Questions & AI** | Question count · type mix · difficulty mix · AI generation · manual question editor · staged drafts | Always visible; rendered disabled-with-reason for offline types (§12) |

**UX-1** — The six compartments are the canonical assignment information
architecture; the same ordering and grouping is used in Create, Edit and View.

**UX-2** — Compartment 1's Type/Grading choice gates the *consequences* shown in
compartment 4 and the availability of compartment 6; it never reorders or removes
a compartment (§12).

---

## 4. Layout and interaction contract

**UX-3** — A single page renders **all six compartments stacked** in one scroll.
No step gating, no "Next", no tab-hiding of authoring config.

**UX-4** — A **sticky compartment jump-nav** (six anchor links) is present at all
times, and the **sticky action bar** sits at the top of the page.

**UX-5** — The **primary action** in the bar is status-driven (§11). Secondary
actions (including **Generate questions**) live in the bar's **kebab overflow**.

**UX-6** — Publish-time choices (publish now vs. schedule, approval submission when
required) are captured in a **confirm dialog**, replacing the decision the wizard's
Review step used to hold.

**UX-7** — Leaving the page with unsaved changes raises a confirmation
(explicit save; no autosave).

**UX-8** — The page follows repo UI conventions: `SectionCard`-style compartment
cards, `FormRow` field layout, the shared dialog shell for dialogs, the input-width
ladder for field sizing, isolated `*.razor.css` (no inline styles).

---

## 5. Modes and parity

**UX-9** — One shared component serves three modes:
- **Create** — empty form; Save creates a Draft.
- **Edit** — loaded form; full parity with Create while status is `Draft` or
  `Scheduled` (including the question editor, content modules, attachments and URL
  resources — closing today's `Edit.razor` gap).
- **View** — all compartments read-only; for `Published`, `Closed` and `Archived`.

**UX-10** — `Detail.razor`'s operational tabs (**Submissions**, **Guardian
Sign-off**, **Notifications**) are retained and are not part of the authoring
compartments; they present per-recipient runtime data, not authoring config. The
`Detail` Overview tab is superseded by the shared **View** mode (retire or alias
it during implementation).

**UX-11** — Structural edits are refused server-side outside `Draft|Scheduled`
(existing `Assignment.Update` guard is unchanged). View mode is the fallback for
later statuses.

**UX-21** — **Edit parity requires the load half, not just the render half.**
The update contract's child collections (`Questions`, `Attachments`, `Resources`,
`ContentModules`) are **full-replacement when non-null** and preserve when
`null`. Edit must therefore load the persisted children before enabling those
editors (`GET /assignments/{id}/authoring`), and **fail closed**: if that read
fails, the child editors render **disabled-with-reason** rather than live-empty,
so an author can never add one child and silently replace the whole set.
*(Recorded after the R1 P1: the first implementation rendered the editors against
an empty form model, which made a single added question wipe every persisted
question, attachment and resource.)*

---

## 6. Always-visible vs. policy-resolved fields

**UX-12** — "Always visible" applies to the base spine: Title, Description,
Instructions, Assignment Type, and every field defining **delivery, publish,
submission and signature** behaviour. These are rendered on the page in every mode
(editable or read-only), never collapsed away.

**UX-13** — Fields whose value comes from the tenant/grade **policy** (notification
blocked/preferred channels and caps, `RequiresApprovalBeforePublish`, the signature
requirement default) render as **read-only resolved values** with an
"Inherited from Grade/Tenant policy" badge and a link to the owning policy card.
They are never editable on the assignment.

**UX-14** — The author-overridable per-assignment fields remain editable in their
compartments: `RequiresSignature` (signature *requirement*, as opposed to the
policy *default*), `MandatoryReview`, `MaxScore`, `PassScore`, `MaxAttempts`,
`DueDate`, `AvailableFromUtc`, `ArchiveGraceDays`.

---

## 7. Targeting model

### 7.1 Storage

**TGT-1** — Targeting is stored as **`AssignmentTarget` child rows** on the
assignment aggregate (tenant-scoped, audited), replacing the single-choice
`TargetAudienceType` + `GradeLevelId` + `AssignmentActivityGroup` as the authored
source of truth:

| Field | Notes |
|---|---|
| `Id` | PK |
| `TenantId` | direct tenancy (operational data) |
| `AssignmentId` | FK → assignment, cascade |
| `Kind` | `AllStudents` \| `GradeLevel` \| `Stream` \| `Student` \| `ActivityGroup` |
| `RefId` | nullable; the referenced id (null for `AllStudents`) |
| `DisplayOrder` | 0..n-1 |
| audit | `CreatedAt`/`UpdatedAt` |

**TGT-2** — `AllStudents` has no `RefId` and is **mutually exclusive** with every
other kind: when selected, the constraint pickers are disabled.

### 7.2 Semantics and resolution

**TGT-3** — **Union semantics**: a student is a recipient if it matches *any*
target. The resolved set is deduped by `StudentId`.

**TGT-4** — `GradeLevel` resolves to students with an active enrollment in that
grade for the current period.

**TGT-5** — `Stream` stores `RefId = StreamCodedValueId` and resolves against
active `StudentEnrollment.StreamCodedValueId`, **grade-agnostic** (the
grade↔stream bridge is M:N). The picker groups streams under their offering grade
for legibility, but grade is not part of the stored constraint.

**TGT-6** — `Student` stores `RefId = StudentId`; the student must be active and
not soft-deleted. UI is a server-search autocomplete over `GET /students?search=`.

**TGT-7** — `ActivityGroup` stores `RefId = ActivityGroupId`; resolves to active
members, excluding archived groups (preserves existing FR-20/EC-4 semantics).

**TGT-8** — Resolution is performed by a new **`IAssignmentTargetResolver`** port in
`Assignments.Core`, implemented in `Assignments.Api` against **one new Students
endpoint** (`GET /students/by-target?gradeLevelIds=&streamCodedValueIds=&studentIds=&activityGroupIds=`)
returning the union of matching active student ids, tenant-scoped.

**TGT-9** — At publish, the resolved student-id set is passed into the existing
`ResolveSubscribersRequest.StudentIds`; contact/subscription/notification-policy
filtering is unchanged.

**TGT-10** — Target resolution is **fail-closed at publish**: a resolver outage or
an empty resolved set must block publish (never publish to nobody or everybody).
This contrasts deliberately with the fail-open assignment/notification policy
resolver.

### 7.3 Primary grade and policy scope

**TGT-11** — The assignment keeps a single **primary grade** (reusing
`GradeLevelId`, relabelled "Primary grade" in Basics). It drives:
- authoring defaults (the `RequiresSignature` snapshot), and
- the approval gate (`RequiresApprovalBeforePublish`).

**TGT-12** — **Delivery policy resolves once per sendout, from the primary
grade.** `PublishAssignmentCommandHandler` resolves a **single** effective
notification policy from `Assignment.GradeLevelId` and applies it to every
recipient in the sendout; there is **no per-recipient grade resolution**
(`NotificationRecipientFilter` takes one policy). A multi-grade assignment
therefore has one policy for authoring and **one** policy for delivery — the
primary grade's.

> **Recorded gap (corrected 2026-10-02 during the R2 plan-review).** This
decision previously claimed that *per-recipient grade* drives notification
caps/channels at publish. **That is false against the shipped seam** — no such
resolution exists. The R2 plan-review caught the claim (P1-3) and it is
corrected here rather than silently carried. **True per-recipient delivery
policy is not implemented** and is not part of R2: the filter consumes a single
policy, so per-recipient resolution would require splitting the sendout by
grade (or resolving policy per recipient inside the filter). Tracked as a
follow-up.

### 7.4 Validation and migration

**TGT-13** — At least one target is required. Resolved set must be non-empty at
publish. Existing group/topic-assignment validations are preserved.

**TGT-14** — Migration backfills: each existing `GradeLevelId` → a `GradeLevel`
target; each `AssignmentActivityGroup` row → an `ActivityGroup` target;
`AllStudents` assignments → an `AllStudents` target.

**TGT-15** — `TargetAudienceType` and `GradeLevelId` remain as **derived/compat**
values for the list DTO and ward projection for one release, then are dropped in a
follow-up round.

### 7.5 Recipient preview

**TGT-16** — Audience & Targets shows a live, read-only preview: **students
matched** and **contacts reachable** (after policy filtering). The preview is
resolved server-side from the current constraint set, debounced, and is advisory
only — publish re-resolves fresh.

---

## 8. AI question generation action

**AI-1** — The page exposes a **"Generate questions"** action in the action bar's
kebab overflow, satisfying the requirement to trigger generation as an explicit
action rather than only a compartment field.

**AI-2** — The action **anchors to the Questions & AI compartment's generator**
(focus/scroll), so count, type mix, difficulty and resource selection keep a single
source of form state that survives save-without-generate. It does **not** open a
second, independent dialog.

**AI-3** — Generation is grounded on the assignment's resources. Today only
**URL** text is extracted (`HtmlAgilityPack`, ≤3 URLs). R3 adds **attachment
grounding**: server-side text extraction from staged uploads, threaded into
`QuestionGenerationRequest.ResourceTexts` with caps and per-resource fail-open
behaviour.

**AI-4** — Generation remains gated by assignment type/grading format
(`QuestionGenerationGate`); when unavailable, the action and compartment render
disabled-with-reason (§12).

---

## 9. Instructions

**INS-1** — New `Instructions` property on `Assignment` (`TEXT`, nullable),
distinct from `Description`.

- `Description` — internal/author summary.
- `Instructions` — **student-facing** task text.

**INS-2** — Instructions render in Basics and are surfaced read-only on the ward
player (threaded through the ward DTOs).

**INS-3** — Instructions are **not** fed to AI question generation (resources are).

---

## 10. Delivery, submission and signature visibility

**UX-15** — Compartment 3 (Delivery & Publishing) surfaces: current status, the
schedule/available-from value, due date, archive grace, the resolved approval
requirement and the resolved notification policy — all visible in every mode.

**UX-16** — Compartment 4 (Submission & Sign-off) surfaces scoring/attempts,
feedback mode, guardian review and guardian signature, plus the resolved signature
requirement. Inapplicable fields are disabled-with-reason, not hidden (§12).

---

## 11. Lifecycle action matrix

| Status | Primary action | Kebab / secondary | Field editability |
|---|---|---|---|
| Draft | Publish | Save draft · Generate questions · Submit for approval (when required) · Schedule | Editable |
| Scheduled | Unpublish | Save · Reschedule | Editable |
| Published | Unpublish | Close | Read-only (View) |
| Closed | Archive | — | Read-only (View) |
| Archived | — | — | Read-only (View) + Notifications tab |

**LIF-1** — Approver actions (Approve/Reject) remain on the Index/Detail surfaces,
not on the authoring page.

**LIF-2** — Published structural edits stay locked; no due-date-only exception in
this design (unpublish → edit).

**LIF-3** — The Closed row's **Archive** action is served by
`POST /assignments/{id}/archive` (mirroring the `/close` route: 404 when absent,
400 on an invalid transition). This **supersedes WS-A2 decision (f)** in
`documents/solution/assignment-request-go-forward-breakdown.md`, which kept
`ArchiveAssignmentCommand` sweep-only with no public route; the authoring surface
needs the explicit retention action. The route inherits whatever authorization
the assignment endpoint group carries (including the teacher-portal policies in
`teachers-ward-portal-prefab-plan.md` §1 T4).

---

## 12. Conditional-field behaviour

**UX-17** — Inapplicable controls render **disabled with an inline reason**
("Not applicable for Teacher-graded"), never hidden, so the page does not reflow
when the type/grading changes.

**UX-18** — Gating reuses the existing rules: `ScoringFieldsSection` (pass score and
attempts for `AutoGraded`/`InstantGraded`) and `QuestionGenerationGate` (generator
availability by type/format).

**UX-19** — The **Questions & AI** compartment renders as a disabled block with an
explanation for offline (`Manual`) types rather than disappearing. **Content &
Resources** is always active.

---

## 13. Non-functional requirements

- **NFR-1 Accessibility** — the page meets WCAG 2.1 AA: jump-nav is keyboard
  reachable, disabled controls expose their reason to screen readers, dialogs trap
  focus (shared dialog shell).
- **NFR-2 Layout stability** — changing Type/Grading never adds/removes
  compartments; only enables/disables controls.
- **NFR-3 Fail posture** — target resolution is fail-closed at publish (TGT-10);
  policy resolution stays fail-open (unchanged).
- **NFR-4 Consistency** — one shared component across Create/Edit/View; no divergent
  field sets.
- **NFR-5 Testing** — every contract below has a discriminating test; the repo's
  `dotnet build`/`dotnet test` gates and `SchoolCollab.ArchitectureTests.Unit`
  apply.

---

## 14. Round slicing and execution mode

| Round | Content | Execution mode |
|---|---|---|
| **R1** | Compartmentalized shell (three modes) · `Instructions` · Edit parity (question/resource editors) · action bar + kebab · jump-nav | **Light round** (Tier 2). *Option to run Solo if a faster first deliverable is wanted.* |
| **R2** | Targeting: `AssignmentTarget` entity + migration · primary grade · `IAssignmentTargetResolver` port + Students `by-target` endpoint · recipient preview · audience compartment UI | **Full four-agent** (Tier 3) |
| **R3** | Attachment text extraction · AI-from-attachments · AI action wiring | **Full four-agent** (Tier 3) |

**UX-20** — Each round is independently acceptable (demoable, test-green,
mergeable per the main-branch policy). R2 carries a schema migration; R3 carries a
new extraction dependency and a change to the AI generation contract.

---

## 15. Out of scope

| Item | Note |
|---|---|
| Per-assignment override of policy fields | D4 — policy stays read-only-resolved |
| Autosave | UX-7 — explicit save |
| Due-date-only edit of a published assignment | LIF-2 |
| Dropping `TargetAudienceType`/`GradeLevelId` | TGT-15 — follow-up round |
| Subject/curriculum streams as a targeting axis | "Streams" here = `GRSTREAMS` grade streams; confirm if a different meaning is intended |
| Drawn signatures, rubric scoring, comment threads | Remain Phase-5 AR items |

---

## 16. Verification surface

| Contract | Discriminating test |
|---|---|
| UX-3/UX-4/UX-9 — six compartments render in all three modes; Edit parity | bUnit: shared component in Create/Edit/View |
| UX-12/UX-14 — always-visible spine + editable per-assignment fields | bUnit: field presence per mode; policy badges |
| UX-13 — policy fields read-only with inherited badge | bUnit: no editable control for policy fields |
| UX-17/UX-18/UX-19 — disabled-with-reason gating | bUnit: type/grading change toggles enablement, not presence |
| UX-5/UX-6 — action bar primary action per status; publish dialog | bUnit: action matrix (§11) |
| AI-1/AI-2 — Generate action anchors to compartment | bUnit: kebab action focuses/scrolls to Questions & AI |
| AI-3 — attachment grounding | Unit: extraction + `ResourceTexts` assembly, caps, fail-open |
| INS-1/INS-2 — Instructions round-trip to ward DTO | Unit + route test |
| TGT-1/TGT-14 — target rows + backfill migration | Migration/model test; handler tests |
| TGT-3..TGT-9 — union, stream, student, group resolution | Unit: resolver with fake lookups; route test on `by-target` |
| TGT-10 — fail-closed publish on resolver failure | Handler test: outage ⇒ publish refused |
| TGT-11/TGT-12 — primary grade vs per-recipient grade | Handler tests: policy scope |
| TGT-13 — validation (≥1 target, non-empty, groups) | Handler tests |
| TGT-16 — preview counts | Route/handler test + bUnit |
| LIF-1/LIF-2 — action matrix + locked structural edit | bUnit + handler test |

---

## 17. Deferred / known gaps

These residuals are **recorded, not silently dropped**: each was found by a review or UI-tester pass,
judged non-blocking for the round that found it, and has no owner yet. **The spec itself was closed
on 2026-10-06** — the entries below are carried follow-ups, not open work against this design; the
spec is re-opened only if one of them is scheduled. `documents/rounds/` is
declared ephemeral (`documents/rounds/README.md`), so this table is their durable home — extend it
when a round defers something, and strike an entry (naming the round that closed it) when it is
done rather than letting this list drift away from the code.

| Gap | What is missing | What it takes to close |
|---|---|---|
| **Archived-group relink is refused with 422 — the route half** (R1; the UX half was closed by round `authoring-residuals-mopup`) | A persisted link to a group that has since been **archived** cannot be re-written through the replace-set route, so a save that re-asserts such a set would fail — `LoadLinkedGroupIdsAsync` therefore still drops ids the picker cannot represent. The link stays readable but is not re-writable, and the page now names what it dropped (`#authoring-groups-dropped-links`) instead of discarding it silently, which tells the author what is true of the link without making it writable. | A relink route (or a handler rule) that accepts an **unchanged** archived id. |
| **A read-only View has no reason to convey** (R2, verified benign) | The grades / streams / students pickers carry a hard-coded `aria-describedby="authoring-audience-constraint-reason"`. The only state where that paragraph is absent is a plain read-only View, where no reason exists and the controls are disabled by `IsReadOnly` — so the idref does not actually dangle on a control that had something to say. | None while that reasoning holds; re-check if a reason is ever introduced on a read-only surface. |
| **Structured output for question generation** (R3 / D5) | `AssignmentQuestionGenerationService` builds a prompt and then parses free text back into JSON (`AssignmentQuestionResponseParser` + `AiTextCleaner`) instead of using `Microsoft.Extensions.AI` 10.6.0's response-format / structured-output support. Real, but orthogonal to R3 — that round already carried a schema migration, a new extraction dependency and a cross-host contract change. | Move the question-generation call to a structured response format and delete the tolerant parser, once a provider in the supported set is known to honour it. |
| **Question rows have no stable identity across a save** (R3 / P2-5) | The create/update paths implement the child contract as full replacement — snapshot the ids, `RemoveQuestion` each, `AddQuestion` the inbound set — so every question row is re-minted with a fresh `Id` on every save. R3's `GenerationId` survives that because it rides the wire DTOs, but any future feature that needs to *refer* to a question across saves (a publish-time selection, a per-question sort, an answer keyed to a question id) has no stable id to bind to. | Either give the save path a key-preserving merge (match inbound rows to persisted rows by id and update in place), or snapshot the selection at publish time instead of referencing live rows. |

Round `authoring-ai-attachments-r3` closed **a pre-existing P0: owned children could not be added to a persisted
assignment on PostgreSQL**. Found by R3's Postgres round-trip tests, not by a review: every owned
collection under `Assignment` (`AssignmentAttachment`, `AssignmentQuestion`, `QuestionOption`,
`AssignmentReview`) declared `HasKey(x => x.Id)` over a `Guid` but never `ValueGeneratedNever()`, so the
key kept the `ValueGeneratedOnAdd` convention and EF Core read an explicitly-set key on a newly-attached
owned instance as an **already existing** row — tracking it as `Modified` and emitting
`UPDATE <child table> SET … WHERE id = <the new id>` with **no INSERT**. On PostgreSQL that affected zero
rows, so `SaveChanges` threw `DbUpdateConcurrencyException`: adding or replacing any question,
attachment, option or review on a persisted assignment failed outright, while the InMemory provider
silently "succeeded" (it performs no row-count check) and the unit suites therefore never saw it. Closed
by adding `ValueGeneratedNever()` to all four owned keys in `AssignmentConfiguration` — **no migration is
required** (`dotnet ef migrations has-pending-model-changes` reports none), because the key's store
generation is not part of the physical schema. Guarded by
`tests/SchoolCollab.Assignments.Tests.Integration/OwnedChildInsertPostgresTests`, which asserts the new
child row is actually present when read back from a fresh context and which threw
`DbUpdateConcurrencyException` at the pre-fix snapshot.

Round `authoring-residuals-closeout` closed **P2-c — transaction rollback is unproven** (the mid-transaction
failure is now fault-injected in `tests/SchoolCollab.Assignments.Tests.Integration` against real Postgres:
the second write fails with the first already inside the open transaction, and the committed state read
back from a fresh context is asserted equal to the pre-handler state — the rollback test fails when
`BeginTransactionAsync`/`CommitAsync` are removed, so the rollback and not the fixture is what passes) and
**UX-7 / F17 defence-in-depth** (the reload path carries its own gate
instead of relying solely on the upstream location-changing handler; the reload itself still proceeds when
the form is dirty, because refusing it would leave A's state on screen under B's URL — the stale-write path
F17 removed — but the discard is no longer silent: an unconsented `Id`/`Mode` swap renders
`#authoring-unsaved-edits-discarded`, while a discard the author confirmed in the in-app confirmation is
not announced back at them).

Round `authoring-residuals-mopup` closed **`ReloadAsync` re-reads the summary only** (the reload now runs
the same persisted-set load as the initial Edit load, and holds it back while the form has unsaved work),
**F4 — a restored student target renders a fallback chip** (the restored ids are re-resolved, so the chip
renders the name the picker would have shown), **The students picker cannot name its reason** (the reason
is declared on the row's own labelled group, which `FluentAutocomplete` cannot rewrite away), the **UX
half of the archived-group relink** (the dropped link is named on the page), and the ux7 round's
**`IsDirty` cost per render** (the snapshot is a 128-bit fingerprint of the save payload rather than the
serialized payload, so the per-render comparison no longer materializes the request).

The R2 UI P2 backlog (**F7** kebab disabled-without-reason, **F12** empty-state preview wording,
**F13** first-render debounce delay, **F15** per-row reason placement, **F17** missing
`OnParametersSet`) and the spec'd-but-unimplemented **UX-7** (§4) were closed by round
`authoring-ux7-residuals`; the table above is what remains open.

Round `snapshot-config-guard` closed the **snapshot-vs-config equality gap** the R3-1 re-verify found:
`tests/SchoolCollab.ArchitectureTests.Unit/OwnedKeyValueGenerationArchitectureTests` is now the gate for
it, and it reads the EF-generated model artifacts under `src/` — every `Migrations/*ModelSnapshot.cs`,
plus the newest `.Designer.cs` of each migrations directory (the two live mirrors; an older designer is an
immutable record of the model as it stood at that migration, which the differ never consults) — failing if
an **owned** key (a `Property<Guid>("Id")`
declared inside an `OwnsMany(...)` block, so a match cannot leak in from a neighbouring entity) still
carries `.ValueGeneratedOnAdd()`. That annotation is the mirror-side half of the owned-key P0 above: it
makes EF read an explicitly-set key on a newly-attached child as an **already existing** row and emit
`UPDATE <child table> SET … WHERE id = <the new id>` with no INSERT. The gate has to be a
source-inspection test, because the differ cannot see this annotation at all:
`dotnet ef migrations has-pending-model-changes` reports "No changes have been made to the model since the
last migration" on the stale artifact and on the corrected one alike, which is why R3-1's drift was
invisible to that gate *and* to the runtime `HasPendingModelChanges()` guard in `MigrationGuardTests`.

---

## 18. References

- `documents/specs/assignment-request-feature-spec.md` — AR feature set, personas, lifecycle.
- `documents/specs/assignment-policy.md` — policy field set, resolution, enforcement, grade-detail UI.
- `documents/specs/assignment-creation-with-ai.md` — AI generation endpoint, prompt seam, wizard wiring (to be re-hosted by this design).
- `documents/specs/notification-delivery-plan.md` — notification policy.
- `documents/solution/assignment-request-go-forward-breakdown.md` — WS-A…WS-G workstreams and shipped-state inventory. Its **WS-A2 decision (f)** (archive command sweep-only) is **superseded by §11 LIF-3**.
- `.github/copilot/rules/blazor-components.md`, `.github/skills/dialog-ui/SKILL.md`, `.github/skills/input-width-scale/SKILL.md` — UI conventions this page must follow.

---

> **Superseded (2026-10-10):** **INS-2** — where the instructions render — is replaced by the unified instructional-materials area: see `documents/specs/instructional-materials.md` §0. **INS-1 and INS-3 remain in force** (the student-facing text field; instruction items stay out of the AI reference set).

