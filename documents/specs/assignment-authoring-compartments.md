# Assignment Authoring — Compartmentalized Single-Page Experience

> **Status:** adopted (grill session 2026-10-01; all frontier decisions ratified by the
> owner). This is the durable, source-of-truth contract for **how an assignment is
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
>
> **Implementation status:** **not implemented.** This document is the input to the
> R1–R3 rounds in §14; no code, migration or test has been written from it yet.
>
> **Parallel surface (2026-10-01):** the Prefab **teacher portal**
> (`documents/specs/teachers-ward-portal-prefab-plan.md` §1 T1–T7) is a
> **read/review second surface**; this Blazor compartment design remains the
> **authoring destination**. Both tracks run (owner decision Q5 = A); the portal
> is the eventual migration target for the working assignment feature set.

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

**TGT-12** — **Per-recipient grade** drives notification caps/channels at publish
(the notification policy already resolves per grade). A multi-grade assignment
therefore has one policy for authoring and per-recipient policy for delivery.

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

## 17. References

- `documents/specs/assignment-request-feature-spec.md` — AR feature set, personas, lifecycle.
- `documents/specs/assignment-policy.md` — policy field set, resolution, enforcement, grade-detail UI.
- `documents/specs/assignment-creation-with-ai.md` — AI generation endpoint, prompt seam, wizard wiring (to be re-hosted by this design).
- `documents/specs/notification-delivery-plan.md` — notification policy.
- `documents/solution/assignment-request-go-forward-breakdown.md` — WS-A…WS-G workstreams and shipped-state inventory. Its **WS-A2 decision (f)** (archive command sweep-only) is **superseded by §11 LIF-3**.
- `.github/copilot/rules/blazor-components.md`, `.github/skills/dialog-ui/SKILL.md`, `.github/skills/input-width-scale/SKILL.md` — UI conventions this page must follow.
