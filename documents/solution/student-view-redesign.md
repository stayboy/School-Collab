# Student view page — redesign

**Status:** Proposed (awaiting the owner's grill round, §7)
**Branch:** `feat/student-view-redesign` (worktree `../School-Collab-studentview`, off `origin/main`)
**Surface:** `/students/{id}` — `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor` (1038 lines) + `Detail.razor.css` (144 lines)
**Mock:** `.pi/mock/student-view-v1.html` (+ `student-view-v1.png`, `shot-student-v1.js` — gitignored, local review artifacts)
**Supersedes:** `documents/solution/student-view-page-modernization.md` — that doc is historical: it names
`SchoolCollab.Students.Admin`, a project that no longer exists. Nothing in it is actionable.

---

## 1. Findings

The four complaints in the request are real and each has a distinct cause. Two are **defects**, not taste.

### 1.1 The status badge — two independent defects

**Defect A — the text is near-black on brand blue, measured 2.88:1.**

`FluentBadge Appearance="Filled"` with no `Color` renders FluentUI's own host rule:

```css
fluent-badge { background-color: var(--colorBrandBackground);
               color: var(--colorNeutralForegroundOnBrand); }   /* #0f6cbd on #ffffff */
```

The **text does not live on that host**. `FluentBadge` projects its content as light-DOM children, and
`SchoolCollab.Admin.Shared/wwwroot/app.css:88` repaints every element of those tag names:

```css
p, span, label, small, strong, em, code, pre { color: var(--neutral-foreground-1, inherit); }
```

So the inner `span` computes `#242424` while its host paints `#0f6cbd`:
**`#242424` on `#0f6cbd` = 2.88:1**, under WCAG AA's 4.5:1.

This is the **same defect class the repo already fixed once**: `app.css:507-519` and
`MainLayout.razor.css:43-48` carry the identical measurement and the identical cause for the app bar
("a child that sets no colour of its own silently picks up the page's `--colorNeutralForeground1` — the
light-mode #242424-on-blue the UX review caught, 2026-10-24"). The badge simply never got the same fix.

**Defect B — one word, three meanings; and the colour key does not exist.**

The page renders the word **"Active"** for three unrelated concepts — the student record, an enrollment,
an activity group — in the identical blue pill, so a reader cannot tell which "Active" they are looking at.
`StatusBadge` also paints `Deleted` in **the same blue fill** as `Active`, so the
archived state is visually indistinguishable from the healthy one.

Separately: **`BadgeColor.Success` is dead in FluentUI 5.0.1.** The shipped stylesheet declares
`fluent-badge[color=sucess]` — misspelled. `BadgeColor.Success` (`Detail.razor:215`, 20 usages repo-wide)
and `BadgeColor.Brand` therefore match **no rule** and fall back to the default brand fill, i.e. to exactly
the same pixels as passing no colour at all. The enrollment `Status` cells and the student header
(`Detail.razor:136-144`, `:434-447`) pass no colour at all, so they land on the same fill by a second route.

> Consequence: every status on the page is blue. The one thing a status pill must do — separate states —
> it cannot do at all.

### 1.2 `Detail.razor.css` consumes design tokens that do not exist

`panel-layout-pattern.md` §3 already records the rule and the failure mode: FluentUI 5.0.1 ships no token
CSS and this app uses no design-system provider, so a bare `var(--missing)` does not degrade — **the whole
declaration is dropped at computed-value time**.

| Line | Declaration | Reality today |
|---|---|---|
| `.page-subtitle` | `color: var(--neutral-foreground-hint)` | token **is** defined (`app.css:45`) → works |
| `.muted` | `color: var(--neutral-foreground-hint)` | works |
| `.student-contact-history__entry` | `background: var(--neutral-layer-1, #fafafa)` | v4 name **undefined** → falls back to the **literal**, so a history card is near-white in **dark mode** |
| `.student-contact-history__entry` | `border: 1px solid var(--neutral-stroke-divider-rest, #e0e0e0)` | defined since 2026-10-24 → works, but a second literal disagrees with `#e1e1e1` |
| `.enrollment-actions-col fluent-button[appearance="lightweight"]` | `font-size` on the web component | dead: v5 renders `appearance="transparent"`, never `lightweight` |

The dark-mode literal is the visible one: the audit timeline is a white card on a dark page.

### 1.3 The layout is a stack of tables, so nothing has a shape

Read top to bottom, today's page is: title row → one 7-field `FluentCard` in a 4-column grid → `<h3>` +
`FluentDataGrid` → `<h3>` + `FluentDataGrid` → `<h3>` + `FluentDataGrid` → `<h3>` + `<ul>` → "Back to
Students". Concretely:

- **Three different data types wear the same chrome.** An enrollment *journey* (a sequence over time), a
  guardian *set* (people, each with up to 3 contact columns) and an activity-group *set* (chips) are all
  rendered as column-headed grids. A grid earns its chrome when rows need to be *compared column-wise*;
  none of these do.
- **The guardian grid is the worst fit.** The linked-mode `GuardianGrid` carries
  `Guardian | C1 | C2 | C3 | Primary | Actions` — a contact-column profile inherited from the *picker*.
  On a full-width page that is fine; on a phone-width content area it is an awkward horizontal scroll to
  read one person's phone number, and the relationship name is buried under the name rather than being a
  scannable marker.
- **The most important fact on the page is not stated.** "Is this student currently enrolled, in what, and
  since when" is the question the page exists to answer, and it is answerable only by scanning a grid row
  for a `Status` cell that says "Active" (see §1.1).
- **No identity.** No monogram/avatar, no grouping — the name is a bare `<h1>` and the only visual weight is
  whatever the browser gives a heading.
- **`.enrollments-grid` and the guardians grid carry a hardcoded `min-height: 220px`** so the two sections
  "present a balanced footprint" — a layout war between two blocks instead of a layout.

### 1.4 Nine visible action controls, no hierarchy

| Control | Level | Today's treatment |
|---|---|---|
| Edit | page | Outline button, top right |
| Enroll | section | **Primary** |
| Transfer / Withdraw | row | Transparent labelled buttons (2 per row, only on the active row) |
| Join Group | section | **Primary** |
| Leave | row | Transparent button per row |
| Manage | section | **Primary** |
| Back to Students | page | Outline button, **at the very bottom** after all content |

Three "primary" buttons on one page means none of them is primary. Two of the six sections have no action
at all, and the page's only navigation control is stranded below the fold.

### 1.5 The page never shows the student's subjects — and the repo already has them

The single largest gap in the current page, and it is **not** a new feature: everything needed exists.

| Piece | State |
|---|---|
| `GET /students/{id}/subjects` | **exists** — `StudentTopicAssignmentRoutes.cs:27` |
| `StudentsApiClient.ListStudentSubjectsAsync` | **exists** — `StudentsApiClient.cs:1964` |
| the §6 union query (`ListStudentSubjects`) | **exists** — grade derived server-side from the active enrolment, no caller-supplied grade |
| `StudentSubjectEnrollmentDto` (name, code, `RequiresEnrollment`, `IsEnrolled`) | **exists** |
| `RailBand` + `RailChip` in both states | **exists** — the edit page renders them today |
| A **Subjects** block on the **view** page | **missing** — `Detail.razor` never calls `ListStudentSubjectsAsync` |

So the view page shows placeholders, guardians, groups and audit history, but **not what the student
studies**. The edit page already carries it (`Edit.razor:173-235`, four bands in the settled order
**Placement → Activity groups → Needs explicit enrolment → Assumed**, owner Q42/Q45), and
`subject-enrollment-requirement-flag.md` FR-ER-16/17 names the *student page's* subject section as a
consumer of that same union — the DTO's own doc comment says so ("the union member the student-page
'Subjects' section … read").

**The two enrolment kinds are the whole point, and they are not interchangeable:**

- **Assumed** (`RequiresEnrollment = false`) — the subjects a student holds **by virtue of the grade**.
  Membership *is* the enrolment (FR-ER-1): there is no per-student row, so nothing to join and nothing to
  un-join. The chip is a **fact**.
- **Needs explicit enrolment** (`RequiresEnrollment = true`) — a per-student `StudentTopicAssignment` row is
  required (FR-ER-3). An enrolled one is a fact; a missing one must be drawn **struck through and labelled**
  ("not chosen"), never omitted, so the gap is visible rather than absent.

**Activity-group category — verified, with a caveat.** The mock's category label is real: `ActivityGroup.Category`
exists on the entity (`ActivityGroup.cs:39-40`, *"FR-1: optional free-text Category (<= 100)"*) and on
`ActivityGroupDto`. But it is **optional** and **free text**, not a controlled vocabulary, so the render rules are:
no category ⇒ render **nothing** (the current page prints an em dash, which reads as a missing value); a long
value ⇒ truncate inside the chip rather than let it push the ✕ out of reach. The mock's fourth group carries no
category to make that case visible.

`StudentSubjectEnrollmentBandTests.cs` already pins exactly that behaviour on the edit page (both states in
one band, the struck chip's name is a real `<button>`, one enrolment written against the **active year**).
The view-page work is the **call plus the markup** — no new component, no new query, no schema change.

### 1.6 The page shows contact **history** for contacts it never shows

`Detail.razor:905-908` loads the audit with

```csharp
await Api.ListContactAuditEntriesAsync(ownerType: ContactOwnerType.Student, ownerId: Id, ct: ct)
```

so the `Contact history` panel lists changes to **the student's own** contacts — guardian contact edits do
not appear there. Meanwhile the student's **current** contacts are nowhere on the page: the standalone
Contacts section was removed in 2026-07-27 and guardian contacts live in the guardians grid. The page
therefore documents the history of a list it does not display.

That is an inversion, not a preference. `StudentsApiClient.ListContactsAsync(ownerType, ownerId)` already
exists (`:2203`, `GET /contacts?ownerType&ownerId`) and the edit page already renders the current list via
`ContactsEditor OwnerType="ContactOwnerType.Student"` in its **main** column (owner Q81). Nothing new is
needed to fix it — one call and one read-only block.

It also makes the history legible: an entry reading *"maria@example.com → maria.delacruz@example.com"* only
means something to a reader who can see which contacts exist now.

**Labels must say whose contacts these are.** The two sets sit inches apart on the same page, so both blocks
name their owner: the Contacts block says "the student's own contacts, guardians' are below", and the rail
panel says it audits the student's own contacts only.

### 1.7 Assignment stats — the one block that is **not** reuse

Unlike subjects and contacts, a per-student assignment count set **does not exist** and cannot be derived
from what the page can already reach.

| Question | Answer |
|---|---|
| Is there a per-student assignment list? | **yes** — `GET /assignments/{studentId}/assignments` (`AssignmentRoutes.cs:1719`), handler `ListWardAssignmentsHandler` |
| Does its DTO carry what a stat needs? | **no** — `WardAssignmentListItemDto(Id, Title, DueDate, State, HasLockedModules)`; `State` is only `NotStarted / InProgress / Completed` |
| Is publish state on it? | no — `Assignment.Status` (`Draft / Published / Closed / Scheduled / Archived`, `AssignmentStatus.cs`) is read by the handler and then dropped |
| Is marking state on it? | no — `AssignmentSubmission.ReviewState` (`Pending / Reviewed / Graded`) is read by the handler and then dropped |
| Is there a stats/summary endpoint anywhere? | **no** — no `AssignmentStats` / `AssignmentCounts` / summary query exists in the module |

**The good news, and it is unusual:** the ward handler already loads *both* missing values for every row —

```csharp
var assignment = await assignmentRepository.GetAsync(summary.Id, ct);      // assignment.Status  ← already here
...
var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(...); // submission.ReviewState ← already here
```

so it reads them and throws them away. Two additive fields on the DTO would make the four counts derivable
with **no extra database work**. But that handler is also an **N+1** (one `GetAsync` + one submission + one
progress read *per row*), and it feeds the Families ward portal and the mobile app — widening its contract so
an admin page can count rows would ship noise to two other consumers and inherit the N+1.

So the shape is a **dedicated count query** — four grouped counts, one round trip — not a widened list.

**Where the hop goes.** `Students.Application` (the RCL that owns this page) does not reference the
Assignments context, and it *cannot* reference `Assignments.Application`: that project already references
`Students.Application`, so the reverse would be a cycle. Two legal paths:

| Path | Cost | Precedent |
|---|---|---|
| **(a) Students.Api enriches** — port in `Students.Core` + HTTP client in `Students.Api` calling a new `GET /assignments/{studentId}/assignment-stats`, then `GET /students/{id}/assignment-stats` for the UI | one extra hop; a second place to touch when the stat shape changes | **exactly this pattern already exists**: `IActivityGroupAssignmentQuery` (`Students.Core/Services`) + `ActivityGroupAssignmentQueryHttpClient` (`Students.Api/Services`), registered at `Students.Api/Program.cs:87` on `https+http://assignments-api`, and `studentsApi = studentsApi.WithReference(assignmentsApi)` (`AppHost/Program.cs:386`) is already wired — so **no AppHost change and `CrossModuleWiringTests` unaffected** |
| **(b) Students UI calls assignments-api directly** — add `Assignments.Contracts` to `Students.Application` + a second `AddCrossModuleHttpClient` on the existing `https+http://assignments-api` address (the admin host already references it) | fewer hops; **new** cross-context UI coupling (Students UI ⇒ Assignments contracts) with no precedent in Students | `SchoolCollab.Families` and `Mobile.Logic` reference `Assignments.Contracts` — but for the *ward* surface, which is Assignments' own domain |

Recommended: **(a)**. It is the repo's own cross-context read pattern, the AppHost wiring is already in
place, it keeps the count SQL in the context that owns the data, and AGENTS.md permits read/enrichment hops
provided they **degrade gracefully** — a failed stats hop must render "unavailable", never fail the page.

**Why this matters for the round's shape.** Every other block is a call plus markup. This one is: a query +
handler + endpoint in Assignments, a port + client + endpoint in Students, a UI client method, a DTO, and
tests in two suites. **It is larger than the rest of the round combined**, which is why §7 Q4 asks whether
it ships in the same round or as its own stacked layer.

**Semantics, stated so the numbers can be audited (mock §B sample: 24 / 21 / 18 / 15):**

| Tile | Definition | Source |
|---|---|---|
**Axis 1 — the work's progress** (four tiles, one bar, totals *Assigned*):

| Tile | Definition | Source |
|---|---|---|
| **Assigned** | the assignment's audience includes this student | ward projection candidate set |
| **Sent out** | …and it is `Published` (or a `Scheduled` assignment whose publish time has passed) | `Assignment.Status` |
| **Submitted** | …and the student has a submission with `FinalizedAt` set | `AssignmentSubmission.FinalizedAt` |
| **Marked** | …and that submission is `ReviewState.Graded` | `AssignmentSubmission.ReviewState` |

A submitted-but-unmarked assignment must sit in **awaiting marking**, never in *Marked* — the states are
`Reviewed`/`Pending` vs `Graded`, and collapsing them would overstate the teacher's progress.

**Axis 2 — the guardian-review gate** (two tiles, its own bar, totals *required*):

| Tile | Definition | Source |
|---|---|---|
| **Requires guardian review** (denominator) | sent out **and** `Assignment.MandatoryReview` | `Assignment.MandatoryReview` (per-assignment, or forced by the resolved effective policy) |
| **Reviewed** | …and a `GuardianSubmissionGate` row exists with `ReviewedAt` set | `GuardianSubmissionGate.ReviewedAt` |
| **Awaiting review** | …and it does not | the same row, absent or unreviewed |

**The two axes are not one chain, and must not share a bar.** Guardian review *gates* submission
(`GuardianSubmissionGate`: a Primary guardian review enables student self-submit, or the guardian submits on
the ward's behalf), so it is an **overlay on the sent set, not the next step after marking**. It also only
exists on assignments that require it — which is why it carries its own denominator ("required on 12 of the
21 sent", never "of 24": an unsent draft cannot be gated). An assignment held by the gate counts in
**awaiting review**, never in *awaiting submission* — otherwise the one number that explains why the work is
not moving is invisible. A five-segment single bar was drawn first and **rejected**: it forced two
near-identical ambers into one scale.

**Presentation (owner steer, this round):** the review axis is **one horizontal bar** — `GUARDIAN REVIEW …
83%`, a 12px track, and a single footnote line — not a caption plus two tiles plus a second bar. The first cut
spent four rows on two numbers that fit one; the percentage is the obvious read and the counts sit in the
footnote. The four progress tiles keep their tile shape (accepted).

**Guardian review ≠ guardian sign-off.** `SignOffState` (`None / AwaitingSignature / Signed`) is the
*signature* stage **after** submission, and it is a third number — deliberately **not** counted in this panel
(§7 Q127 asks whether it should be).

**The count query therefore joins three things** the ward list does not: `Assignment.Status`,
`Assignment.MandatoryReview`, `AssignmentSubmission.ReviewState` (+ `FinalizedAt`), and
`GuardianSubmissionGate.ReviewedAt` — as grouped counts, not per-row N+1.

---

## 2. Proposal

### 2.1 Regions — reuse `.region-grid`, add no new layout CSS

```
[ AuthoringShell: crumbs `Students › {name}` ]
[ identity header  (student avatar, name, status pills, meta line)   ⋮ page kebab ]
┌ main  minmax(0,1fr) ─────────────────────────┐┌ rail  minmax(320px,460px) ──────┐
│ Details            (borderless, no kebab)    ││ Assignments      n   (no kebab) │
│ Enrollment      n  [⋮]                       ││   2x2 tiles + funnel bar        │
│   current-placement strip (inline zone)      ││ Activity groups  n   [⋮]        │
│   Placement history (timeline)               ││   chips (✕ leaves)              │
│ Contacts        2  (no kebab)                ││ Subjects         n   (no kebab) │
│   the student's own contact chips            ││   From Year 8 (8A)              │
│ Guardians       n  [⋮]                       ││   Needs explicit enrolment band │
│   guardian cards (monogram, labels, contacts)││   Assumed band                  │
│                                              ││ Contact history  0  [▼]         │
│                                              ││   empty-state sentence          │
└──────────────────────────────────────────────┘└─────────────────────────────────┘
                                                 (rail column is sticky)
```

Both regions draw nothing; the **main** blocks carry `ShowBorder="false"` and the **rail** blocks keep the
box — exactly `panel-layout-pattern.md` §3.

**Why guardians sit in the main column** (the one place this mock could be read as deviating from §5,
which sends collections to the rail): a guardian row carries a name, a relationship pill, an emergency
marker and up to three contact chips. At 320–460px that row either wraps into six lines or scrolls
sideways. The repo has already answered the same ownership question the same way — round Q81 put the
student **Contacts** block in the edit page's main column, non-collapsible. Guardian contact lookup is also
the most frequent task on this page. **This is open decision §7 Q2, not a decision taken here.**

### 2.2 Blocks and their actions

| Block | Region | Border | Collapsible | Actions |
|---|---|---|---|---|
| Details | main | no | no | — |
| Enrollment | main | no | no | panel kebab `Enroll in grade level…`; per-node kebab `Transfer…`, `Withdraw…` |
| Contacts | main | no | no | **none** — read-only here; the editor is `ContactsEditor` on `Edit` (Q81 put it in that page's main column) |
| Guardians | main | no | no | panel kebab `Manage guardians…`, `Add guardian…`; per-card kebab `Edit link…`, `View all contacts…`, `Remove link…` (destructive) |
| Subjects | rail | yes | no | **none** — the only subject action is fixing a missing enrolment, and that lives on `Edit` (FR-ER-24 names the edit page as the enrolment surface) |
| Assignments | rail | yes | no | **none** — a derived count set; drilling in belongs to the Assignments surface |
| Activity groups | rail | yes | no | panel kebab `Join activity group…`; per chip `✕` leave (single action ⇒ stays visible) |
| Contact history | rail | yes | **yes** (starts expanded) | — |

**Assignments leads the rail.** It is the only *derived* panel and the only one whose numbers change
week to week (subjects and groups change when the grade or the club does), so it takes the sticky column's
top slot — visible without scrolling the placement history. The rest of the rail keeps the settled order.

**Rail order beyond that is the settled one, not taste.** `Activity groups → Subjects → Contact history` mirrors the
edit page's settled band order (§1.5: *the enrolment kinds read after what the enrolment IS*), so the two
surfaces teach each other. Both subject bands are `RailBand` + `RailChips` + `RailChip` — **no new variant,
no new component** (`component-first-policy` Q22a).

**Subjects carries no panel kebab.** The only subject action is fixing a missing enrolment, and per FR-ER-24
the edit page is the enrolment surface; a one-item menu would also violate the repo's own
`RowActionsMenu` rule (one action ⇒ a visible control, not a kebab). The struck chip therefore *names* the
fix and does not perform it — see §7 Q8 for the alternative.

**Contact history is student-scoped, and says so.** It audits `ContactOwnerType.Student` only (§1.6); a
reader who assumes guardian edits land here would look in the wrong place, so the panel carries a one-line
hint. The heading itself stays `Contact history` — `ContactAuditSmokeTests` matches it by role + name.

Contact history starts **expanded**, not collapsed: `ContactAuditSmokeTests.cs:66,111` asserts the
`Contact history` heading **and** the recorded reason text are visible after navigating to the page. A
collapsed default hides the body and breaks that Playwright leg.

### 2.3 Status vocabulary — **a shared CSS class, not a component**

> **Corrected against `component-first-policy.md`** (owner challenge, this round). The first draft called
> for a `StatusPill` component. **It does not pass the policy**, and the cheaper shape is also the correct one.

One shape, four **state** variants, dot + text label on a tinted surface, never a brand fill, never colour
alone. Measured in both themes (mock §C):

| Variant | Light `fg on bg` | Ratio | Dark `fg on bg` | Ratio | States it claims |
|---|---|---|---|---|---|
| success | `#14532d` on `#e7f4ec` | 8.05:1 | `#a6e3bb` on `#1e3a2b` | 8.45:1 | an open placement with no exit date |
| warning | `#7a5314` on `#fdf3e2` | 6.21:1 | `#f0d9a8` on `#3a2f1c` | 9.48:1 | no open enrollment — the state that must never look like success |
| danger | `#7d1c1c` on `#fdecec` | 8.95:1 | `#f7b7b7` on `#3d1f1f` | 8.78:1 | soft-deleted record |
| neutral | `#424242` on `#f0f0f0` | 8.82:1 | `#d6d6d6` on `#2d2d2d` | 9.48:1 | a closed placement, labelled with its exit date |

Lives in `Admin.Shared/wwwroot/app.css` as `.status-pill` + `.status-pill--{variant}` + `.status-pill__dot`,
beside `.region-grid` and the `w-1`…`w-9` ladder — **shared styles are cheap to share, components are not.**

**Why a class and not a component (`component-first-policy.md` §2, §4, §6):**

1. **One call site, not two.** The *uses* are four (header + three timeline nodes) but they are four uses on
   **one surface**. §2's "extract at the second occurrence" counts call sites, and §6 says so in terms:
   *"the trigger is ≥2 call sites, not ≥2 similar looks."* One page is one call site.
2. **The design-named exemption does not apply yet.** §4 lets a design-named component be born with one
   implemented call site **only if the design commits the second**, landing this round or next. The
   repo-wide badge migration is **deferred** (Q118), so the second surface is not committed — the exemption
   would have to be granted explicitly, and there is no reason to spend it here.
3. **It must not become a wrapper around `Chip`.** `Chip` is a shared primitive whose contract is *an item
   in a set* (dismiss, toggle, selection). A status pill's contract is *a state that is or is not in force*
   (dot, semantic tint, no dismiss). If this is extracted later, its doc comment must say that, or the next
   agent will "unify" two things that only look alike.

**Extraction is deferred to the migration round, where it earns its keep** — by then it has ≥2 committed
surfaces and a stable contract.

**Labels are not states.** `Year 8 (8A)`, `Mother`, `Emergency contact`, `Games/STEM` and
`Term 1 · 2026/2027` are **context labels**, and the first draft wrongly gave them a `StatusPill` variant
(pushing `info` into the enum). They need no new shape at all: **`FluentBadge Appearance="Tint"` /
`BadgeColor.Informative` already survives the `app.css:88` repaint at 13.0–13.5:1**, because the repainted
`#242424` sits on a pale fill — the defect only bites a *brand* fill (2.88:1). Measured:

| Badge | Fill | Repainted text | Ratio |
|---|---|---|---|
| `BadgeColor.Informative` | `#ebebeb` | `#242424` | **13.02:1** |
| `Appearance="Tint"` | `#e7f0fb` | `#242424` | **13.49:1** |
| `BadgeColor.Subtle` | `#ffffff` | `#242424` | **15.52:1** |
| `Appearance="Filled"` (no `Color`) | `#0f6cbd` | `#242424` | **2.88:1** ← the defect |

So the defect surface is exactly **the 81 of 143 `<FluentBadge>` elements that pass no `Color`** (counted
this round) — not "all badges", and not the labels.

Three rules the class block encodes:

1. **The foreground is set at class specificity**, so `app.css:88`'s element-level `span { color: … }` rule
   cannot reach it. The 2.88:1 defect cannot recur wherever the pill is placed.
2. **The label states the fact, not the colour key** — "Exited 31 Aug 2026", not "Completed"; "Enrolled" /
   "Not enrolled" / "Archived", not three "Active"s.
3. **The dot is `aria-hidden`**; the label alone carries the meaning.

### 2.4 Kebab map — and where a kebab is *wrong*

`RowActionsMenu` already encodes the repo's rule: **one action ⇒ a labelled button, two or more ⇒ `⋮`.**
Applying that honestly:

| Menu | Items | Why grouped here |
|---|---|---|
| Page header (`AuthoringShell.TitleMenu`) | 3 — `Edit student…`, `Copy student number`, `Back to students` | page-scope actions only; a page menu never duplicates a panel menu (`panel-layout-pattern.md` §4) |
| Enrollment panel | 1 — `Enroll in grade level…` | kept as a kebab (not a labelled button) purely to match `/students/{id}/edit`, which settled exactly this in round **Q83** |
| Enrollment node | 2 — `Transfer…`, `Withdraw…` | both mutate *that* placement |
| Guardians panel | 2 — `Manage guardians…`, `Add guardian…` | same target aggregate |
| Guardian card | 3 — `Edit link…`, `View all contacts…`, `Remove link…` | all scoped to *that* link; `Remove` uses `RowAction.Destructive` so the confirm comes free |
| Activity groups panel | 1 — `Join activity group…` | the only set-level action |

**Deliberately not kebabbed:** `✕` leave on a group chip (one action, already scoped by its own chip — a
kebab would cost a click and hide the only affordance), and `Back to students` as a *visible* control (the
crumb `Students` navigates there; the menu item is the keyboard-reachable duplicate).

Net: **9 scattered controls → 5 kebabs + 3 inline affordances**, and every one of the 9 is still reachable.

### 2.5 Component map — reuse first

| Piece | Status | Notes |
|---|---|---|
| `AuthoringShell` (+ `TitleGroup`, `TitleMenu`) | **reuse** | currently used by students create/edit; the view page gets the same chrome |
| `PanelSection` | **reuse** | title / `Count` / `HeaderActions` kebab / `Collapsible` / `ShowBorder` |
| `RowActionsMenu` + `RowAction` | **reuse** | page, panel and row kebabs — one component, no second kebab |
| `FieldDisplay` | **reuse** | Details fact grid |
| `RailChips` / `RailChip` | **reuse** | activity-group chips (removable = `OnRemove`) |
| `.region-grid*` (`app.css:193-240`) | **reuse** | no per-page breakpoint |
| `.status-pill` CSS class | **new (CSS, not a component)** | 4 uses on this one surface. Extraction deferred to the migration round — §2.3 |
| `RailBand` / `RailChips` / `RailChip` | **reuse** | the two subject bands, exactly as `Edit.razor` draws them |
| `ListStudentSubjectsAsync` | **reuse** | the §6 union is already served and already wrapped — §1.5 |
| `ListContactsAsync` | **reuse** | `GET /contacts?ownerType&ownerId` — the page simply never called it — §1.6 |
| `Monogram` | **new, optional** | initials + deterministic hue; 2 call sites here, both on this page (§7 Q4) |
| `Timeline` | **new, optional** | 2 call sites here (enrollment journey, contact history) (§7 Q3) |
| `--status-*` + `--avatar-*` tokens | **new** | `app.css` `:root` + dark; 15 status values + monogram pair |

Everything new goes through `component-first-policy.md` §4: the mock draws its states, the contract is
recorded in the round doc, and the two-call-site rule is satisfied per component or an exemption is asked.

---

## 3. Alternatives considered and rejected

| Alternative | Why rejected |
|---|---|
| **Keep `FluentBadge`, just set `Color`** | `BadgeColor.Success` is a no-op in 5.0.1 (`[color=sucess]` typo, §1.1). Only `Danger` / `Warning` / `Informative` / `Subtle` / `Important` / `Severe` actually paint. It would also leave the foreground at the mercy of `app.css:88`. |
| **Fix only `app.css:88`** (e.g. drop `span` from the selector) | The rule exists to theme raw HTML the app renders itself; removing `span` breaks muted/secondary text app-wide and does nothing for `BadgeColor.Success`. It is also unverifiable in isolation — the blast radius is every `<span>` in the repo. Kept as open decision §7 Q1. |
| **Keep three `FluentDataGrid`s, restyle them** | The grids are the wrong instrument, not badly styled (§1.3). Styling them leaves the guardian contact-column squeeze and the "scan a row to learn the state" problem intact. |
| **Tabs** (Overview / Enrollments / Guardians / History) | Out of the question — `StudentDetailSectionsTests.Detail_Does_Not_Use_FluentTabs` pins the single-page constraint and the owner's original instruction stands. |
| **Put every collection in the rail** (strict §5) | Guardian cards do not fit 320–460px (§2.1). Open decision §7 Q2. |
| **Build an `AuthoringLayout` wrapper** | `.region-grid*` already ships in `app.css:193-240` and is consumed by `TemplateEditor` + `AssignmentAuthoring`. Round 12 (Q85) already corrected exactly this mistake: reuse, build nothing. |
| **Drive Badge-to-Pill migration repo-wide in this round** | ~110 call sites and no user-visible defect on most of them. Separate round; §7 Q5. |

---

## 4. Test and documentation impact

**`tests/SchoolCollab.Admin.Tests.Unit/StudentDetailSectionsTests.cs` (546 lines, 22 tests) — the largest
single cost of this redesign.** These are *source-string* assertions (`ReadDetailSource()` + regex), and
roughly half pin structure the redesign removes or moves:

| Test | Fails because |
|---|---|
| `Detail_Has_All_Four_Section_Headings` | wants `<h3>Enrollments</h3>` and `class="profile-grid"`; blocks become `PanelSection` (`<h4 class="panel-section__title">`) |
| `Enroll_Action_Lives_In_Section_Header` | asserts the Enroll label sits before the first `<FluentDataGrid>` — there is none |
| `Transfer_And_Withdraw_Actions_Are_Per_Row` | asserts both labels inside the grid and after `<FluentDataGrid>`; they become `Transfer…`/`Withdraw…` inside the timeline node kebab |
| `Detail_Title_Row_Uses_Enriched_Name_Format` | the enriched `Name (Gender, Age)` line is being split into the name + meta line |
| `Profile_Demographics_Are_Stacked_Top_Down` | `profile-grid` is replaced by an explicit fact grid |
| `Detail_ActivityGroups_Section_After_Enrollments` | order changes: groups move to the rail |
| `Detail_Preserves_Legacy_Layout_CSS_Classes` | asserts `.title-row` / `.page-title` survive in `Detail.razor.css` — they are deleted |
| `Detail_CSS_Defines_Detail_Card_Popup_Clipping_Fix` | the FluentCard-with-dropdowns disappears; the workaround moves or goes || `Detail_ContactHistory_Section_Is_After_Guardians` | history moves to the rail |

Plan: keep the source-level approach (the file's own doc comment explains why bUnit is impractical here —
`StudentsApiClient` is concrete with no interface, and `TenantGate` is heavy), re-point each assertion at
the new structure, and **add** render-level tests where a new component is pure:
no component tests (there is no `StatusPill` component); `TimelineTests` if the timeline is built.
The existing behavioural tests (`Contact history loads`, `Loading/Error/Empty states`, `Guardian
OnViewAllContacts wiring`) must stay green unchanged.

**Subjects parity (new risk, new coverage).** `StudentSubjectEnrollmentBandTests.cs` pins the union semantics
on the **edit** page only. The view page must re-assert the same three invariants against its own markup and
its own seam ids (`#student-view-subjects`), or the two surfaces silently diverge: both chip states in one
band, an Assumed chip is never struck, and a missing required chip is visible and labelled. The union call
is the same client method, so no API test is needed.

**Playwright:** `ContactAuditSmokeTests.cs:66,111-112` needs the `Contact history` heading **visible** and
the reason text rendered — satisfied by an expanded-by-default panel (§2.2). `GuardianAdminTests.cs`
exercises guardian editing from the detail page; its locators are re-checked in the round.

**Docs:** mark `documents/solution/student-view-page-modernization.md` as superseded (one line, pointing
here). Add nothing to `AGENTS.md` — the panel pattern is the single source for layout rules and this page
complies with it.

---

## 5. Implementation steps

Ordered so each step builds and can be accepted on its own.

1. **Tokens.** Add the 15 `--status-*` values and the `--avatar-bg`/`--avatar-fg` pair to
   `Admin.Shared/wwwroot/app.css` (`:root` + dark). *Gate: `dotnet build`.*
2. **`.status-pill` + variants** in `Admin.Shared/wwwroot/app.css` (beside `.region-grid`). Owns its
   foreground at class specificity; dot `aria-hidden`. **No component** — §2.3. *Gate: build; the page's own
   bUnit tests assert the labels.*
3. **Page skeleton.** `Detail.razor`: `AuthoringShell` (crumbs, identity `TitleGroup`, page `TitleMenu`) +
   `.region-grid-host/.region-grid` + the five `PanelSection`s. **Move, don't rewrite, the loader** —
   `OnInitializedAsync`, the six `Load*` paths, `CancellationTokenSource`, `_disposed` guards, dialog opens
   and callbacks all stay byte-identical.
4. **Enrollment block.** Current-placement strip + timeline (or grid — §7 Q3). `ActiveEnrollment` /
   `IsActiveEnrollment` unchanged; per-node kebab replaces the Actions column.
5. **Subjects rail panel.** Call `ListStudentSubjectsAsync` alongside the existing loader (the page already
   loads guardians + contact history in parallel — add the third call to that same `Task.WhenAll` shape, not
   a fourth sequential await). Render `Assumed` and `Needs explicit enrolment` as `RailBand`s with
   `RailChip`s; a missing required subject is struck + labelled, a subject list that has not loaded renders
   **nothing** (never "no subjects"), and an empty grade renders one sentence naming the cause.
6. **Contacts block.** Add `ListContactsAsync(ContactOwnerType.Student, Id)` to the existing parallel load and
   render the current contacts read-only in main, **above** Guardians (contacts and guardians answer one
   question: how do I reach this student's family). One line names the owner, because the two sets sit
   inches apart.
7. **Guardians block.** Guardian cards (monogram, relationship + emergency **labels**, contact chips,
   per-card kebab). `StudentGuardiansList` stays as the editor path used by `Edit.razor`; if the view page
   stops using it, say so in the round doc rather than deleting it.
8. **Activity groups + Contact history.** Rail panels; `RailChip` with `OnRemove`; history re-rendered with
   `--status-*` rather than the `#fafafa` literal.
9. **Assignments stats (§1.7) — its own layer.** In Assignments: `StudentAssignmentStats` query returning the
   six counts (four progress + reviewed/awaiting-review, with the review denominator) grouped in one round trip
   over `Assignment.Status`, `MandatoryReview`, `AssignmentSubmission.ReviewState`/`FinalizedAt` and
   `GuardianSubmissionGate.ReviewedAt`, `GET /assignments/{studentId}/assignment-stats`, tests. In Students: port
   `IStudentAssignmentStatsQuery` in `Students.Core`, HTTP client in `Students.Api` beside
   `ActivityGroupAssignmentQueryHttpClient`, `GET /students/{id}/assignment-stats`, graceful degradation (a
   failed hop returns null → the panel renders "unavailable", never fails the page). In the page: the rail
   panel. *Can ship as its own `gh stack` layer (§7 Q122).*
10. **`Detail.razor.css` rewrite.** Delete `.title-row`/`.page-title`/`.profile-grid`/`.section-header`/
   `.enrollments-grid` min-height hacks; keep only what the new markup needs; fix the dead v4 tokens (§1.2).
11. **Tests.** Re-point `StudentDetailSectionsTests`; add `TimelineTests` if the timeline is built. No
   `StatusPillTests` — there is no component to test (§2.3).
12. **Verify.** §6.

Not in scope: any change to `Edit.razor` / `Create.razor`, any API/contract change, any migration
(this page is read-only + existing dialogs), repo-wide badge migration.

---

## 6. Verification

| Check | Command | Must be |
|---|---|---|
| Build | `dotnet build SchoolCollab.slnx` | 0 errors |
| Gate (baseline) | `scripts/round/gates.sh --no-maui --no-integration` | 0 failures; skipped legs named in the PR/round doc |
| Gate (escalation) | `scripts/round/gates.sh` | required — the diff touches `SchoolCollab.Admin.Shared` (shared chrome) |
| Unit | re-pointed `StudentDetailSectionsTests` (+ `TimelineTests` if built) | green |
| Live UI | Playwright `ContactAuditSmokeTests` + `GuardianAdminTests`; screenshot in light, dark and a narrow host | visible in all three; no 2.88:1 pair; no white card in dark; a light monogram disc in dark is a **fail** |
| Contrast | re-measure the pill pairs in the browser (computed colour, not the mock's numbers) | every pair ≥ 4.5:1 |
| Contacts | the student's own contacts render in main; a guardian contact edit creates **no** history entry | the audit really is student-scoped (§1.6) |
| Assignment stats | hand-check all six counts against a seeded student; a **submitted but unmarked** assignment lands in *awaiting marking*, not *Marked*; an assignment **held by the review gate** lands in *awaiting review*, never *awaiting submission*; the progress bar totals *Assigned*, the review bar totals *required* | the tile semantics in §1.7, not just that numbers render |
| Stats degrade | stop assignments-api and load the page | the panel says unavailable; the rest of the page still renders |
| Subjects | the rail panel renders both bands for a seeded student; switch the student's grade and the list changes | the union really is grade-derived, not cached |
| Defect closed | grep the running page for `background: #0f6cbd` on a badge | absent — status colour is token-driven |

---

## 7. Decisions (settled)

The grill round opened fourteen questions. All are settled; each keeps the recommendation it was asked
with, and the answer is recorded rather than assumed — a recommendation nobody ratified is not a decision.

| # | Question | Ruling |
|---|---|---|
| Q114 | Global or page-local badge fix? | **The `.status-pill` class only.** `app.css:88`'s `span` rule stays; narrowing it repo-wide is a separate change with its own blast radius. The pill wins its colour back at class specificity (§2.3). |
| Q115 | Guardians: main or rail? | **Main.** Contact columns do not fit 320–460px, and the Q81 precedent already put the student's contacts in the edit page's main column. |
| Q116 | Enrollment: timeline or grid? | **Timeline.** Two committed call sites on this surface (placement history, contact history) make the component legitimate. |
| Q117 | `Monogram`: component or inline? | **Inline.** Both call sites are on this page — one *surface*, not two. |
| Q118 | Badge adoption scope now? | **This page only.** The 81 colourless `FluentBadge` elements are a separate migration; the component lands with it. |
| Q119 | Contact history: rail + collapsible? | **Rail, collapsible, starts EXPANDED** — `ContactAuditSmokeTests` asserts the heading and a reason string are visible. |
| Q120 | `Back to students`? | **Crumb + page kebab.** The footer button is gone. |
| Q121 | Where does a missing required subject get fixed? | **`Edit`.** FR-ER-24 makes the edit page the enrolment surface; the view page's chip names it and carries no `OnActivate`. |
| Q122 | Assignments stats: same round or its own layer? | **Its own `gh stack` layer** — it is the only piece needing a new query + endpoint in Assignments *and* a port + client + endpoint in Students, and tests in two suites. |
| Q123 | Where does the stats read live? | **Students.Api enriches**, per `AGENTS.md` § "Synchronous reads": port in `Students.Core`, client in `Students.Api` beside `ActivityGroupAssignmentQueryHttpClient`, `studentsApi.WithReference(assignmentsApi)` already wired at `AppHost/Program.cs:386`. |
| Q124 | Stats: rail or main? | **Rail, first panel.** |
| Q125 | Four tiles + funnel bar, or four tiles only? | **Tiles + bar.** The bar shows the funnel without the reader doing arithmetic, and every segment carries a legend label. |
| Q126 | What does "sent out" mean? | **`AssignmentStatus.Published`** (plus a `Scheduled` assignment already past its publish time). Not the notification's `DeliveryStatus.Sent` — that is a stricter and different claim. |
| Q127 | Count guardian sign-off too? | **No.** `SignOffState` is a stage after submission; a third axis and an eighth number do not fit a 460px column. It belongs on the assignment list. |

---

## 8. Implementation record — layer 1 (the page)

**Scope shipped:** Q122 made the assignments-stats panel a *separate* stacked layer, so this layer is the
page itself: steps 1–8, 10–12. Step 9 (stats) is the next `gh stack` layer and is **not** in this diff.

### 8.1 What shipped

| Step | File | Notes |
|---|---|---|
| 1–2 | `src/SchoolCollab.Admin.Shared/wwwroot/app.css` | 16 `--status-*` tokens (4 variants × bg/fg/dot/border) in `:root` and in `[data-theme="dark"]`; 4 `--monogram-*` lightness steps per theme; the `.status-pill` + 4 variant rules. Two dark-mode gaps were closed while adding them: the dark block never declared `--accent-fill-rest` (light `#0f6cbd` on a dark surface is ~2.3:1), and it carried none of the new state tokens. |
| 3 | `.../Pages/Students/Detail.razor` | `AuthoringShell` with a 2-crumb trail, an identity `TitleContent` (monogram + name), a `Badge` slot (status pill + grade + period) and a page `TitleMenu`. `.region-grid-host/.region-grid`, 4 main blocks, 3 rail blocks. No `PrimaryLabel` — the page commits nothing. |
| 4 | same | Current-placement strip (label/value pairs) + placement-history timeline with per-node kebabs. `ActiveEnrollment` / `IsActiveEnrollment` untouched. |
| 5 | same | Subjects rail panel: `ListStudentSubjectsAsync`, split on `RequiresEnrollment` into the same `RailBandVariant.Required` / `.Assumed` bands the edit page uses. A missing required subject is a struck chip with a reason and **no `OnActivate`** (Q121). |
| 6 | same | Contacts block (`ListContactsAsync(ContactOwnerType.Student, Id)`), read-only chips, one note naming the owner, above Guardians. |
| 7 | same | Guardian cards; `StudentGuardiansList` is **no longer referenced by the view page** (left in place — the edit path still uses it). |
| 8 | same + `Detail.razor.css` | Activity-groups chips (optional `Category` renders only when set, never an em dash) and the contact-history audit re-rendered with `--status-*`; the `#fafafa` literal fallback is gone. |
| 10 | `Detail.razor.css` | Rewritten: only what the new markup consumes. `.detail-card`'s popup-clipping workaround is deleted **with its reason** — the view page hosts no popup-bearing control any more (pinned by a test). |
| 11 | `StudentDetailSectionsTests.cs`, `StatusBadgeColourArchitectureTests.cs`, new `StudentViewRedesignTests.cs`, new `StudentViewRendersTests.cs` | See §8.3. |
| — | `AuthoringShell.razor.css` | `flex-wrap: wrap` on `.authoring-title-group`. The action bar was built to hold ONE owner chip; this page puts a pill and two context badges beside the name, and without wrapping they push the actions off the right edge at a narrow width. |

### 8.2 Deviations from §5, and why

1. **"15 tokens" is 16.** §5 said bg/fg/dot/border per variant = 15; it is 4 variants × 4 = 16. The border
   token is what lets one pill rule serve both themes, which is why it was never the optional one.
2. **`--avatar-bg`/`--avatar-fg` became `--monogram-*` lightness steps.** A fixed avatar background pair
   cannot follow a derived hue; the component sets `--monogram-hue` inline and the theme owns saturation and
   the three lightness steps. (The mock first shipped a hue-only disc and rendered pale lavender on a dark
   page — that is why the split exists.)
3. **The monogram class is `.student-monogram`, not `.avatar`** — page-scoped CSS. A shared `.avatar` would
   be the second call site the component-first policy gates.
4. **No `TimelineTests`.** The timeline is markup over a `foreach`; the render tests (§8.3) assert both nodes
   and the current-node marker from a real payload, which is the same guarantee without a second component
   to unit-test.
5. **The contact-history change-kind badge also became a pill.** It was a `FluentBadge Appearance="Filled"`
   whose helper returned `Filled` for both branches — code that looked like a decision and made nothing. Its
   `Deleted`/`Updated` distinction now comes from the pill variant, which paints.

### 8.3 Tests

| File | Change |
|---|---|
| `StudentDetailSectionsTests.cs` | 10 of 22 tests read the retired markup. Re-pointed, not deleted: `<h3>` heading checks → `PanelSection Id=` + region order; "Enroll in the section header" → the panel's `HeaderActions`, plus an assertion that it is *not* in the page kebab; "Transfer/Withdraw per row" → per *placement*, built by `NodeActions` and gated on `IsActiveEnrollment`; the guardians-grid tests → the card markup + `HasMoreContacts`; the popup-clipping test → "the view page hosts no popup-bearing control"; the legacy-CSS whitelist → "every class the markup consumes exists in the scoped CSS, and the pill/tokens are *shared*, never copied". Two tests were passing on a **code comment** ("Join Group"); both now assert the action list. |
| `StudentViewRedesignTests.cs` | **New** — 12 checks: the shared shell, navigation placement, block ids and borders, the pill in shared CSS with all four variants, the pill-beats-the-global-`span`-rule coupling, every pill labelled with an `aria-hidden` dot, no `BadgeColor.Success`, the palette declared in **both** themes, an undeclared-token guard over every `var(--…)` the page CSS consumes, and a **computed WCAG contrast** assertion per variant per theme (measured 6.21–9.48:1 — computed, not recorded). |
| `StudentViewRendersTests.cs` | **New** — 7 bUnit renders against a scripted `HttpClient`: the skeleton draws 4 main + 3 rail blocks and the identity header; the six reads actually happen; the pill states the enrollment in words; the placement strip and both timeline nodes render from real DTOs; the subject bands separate required-missing (struck, with a reason) from assumed; the student's own contact appears while a guardian's number never does; the optional `Category` renders when set; and an empty tenant renders the gate's fallback having fetched **nothing**. |
| `StatusBadgeColourArchitectureTests.cs` | The student view was pinned to `BadgeColor.Success`/`Subtle`. Moved to a second table, `MovedToStatusPill`, pinning the pill variants instead — the same obligation (a status chip must distinguish its states) through a mechanism that paints. |

### 8.4 One finding worth carrying forward

`BadgeColor.Success` **does not paint** in FluentUI 5.0.1: the shipped rule is misspelled
`fluent-badge[color=sucess]`, verified against the package's own stylesheet. Every "Success : Subtle" pin in
`StatusBadgeColourArchitectureTests` therefore proves a binding is *present*, not that it works — those seven
sites render two identical `Filled` chips today. The test's doc comment now records this. A repo-wide
migration to `.status-pill` is the fix; it is out of scope here (Q118).

### 8.5 Verification (layer 1)

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (76 pre-existing warnings, none from this change) |
| `scripts/round/gates.sh` (full — escalation required by the `Admin.Shared` diff) | **every leg that ran is green**; MAUI head 0 errors |
| Skipped / known-red legs | the 2 OpenRouter **live** tests fail on this machine by design (`ChatProviderLiveTests`, HTTP 400) and CI self-skips them for lack of a key. Nothing else skipped. |
| `SchoolCollab.Admin.Tests.Unit` | 864 passed / 0 failed |
| Gate re-runs | two earlier runs of the same gate each failed **one** `Assignments.Tests.Unit` test (**different tests**), while that project passed 1390/1390 twice standalone and each victim passed 3/3 in isolation. Nothing in this diff touches Assignments. Recorded as a **flake**, and the third gate run was clean on every leg (round doc) |
| `Students.Tests.Unit` | 695 / 0 |
| `ArchitectureTests.Unit` | 158 / 0 |
| bUnit renders of the page | 7 / 7 — the page draws, with real payloads |
| Live browser probe (Playwright, real AppHost + seeded DB) | **run — see §8.6.** Light, dark (through the app's own switcher) and narrow, three students |

### 8.6 Rendered proof — measured in the running app (2026-10-28)

The AppHost was started from this worktree (a second container set, since the main checkout's was already
running) and probed with Playwright/Chromium at 1440x950 @DPR2 against real seeded students. Not bUnit markup,
not the mock.

| Assertion | Measured |
|---|---|
| The page draws | `#student-view-details` present; **4** main blocks + **3** rail blocks; 1 fact grid; 1 timeline node; 1 page monogram; **2** status pills; `fluent-tabs` **0** |
| Status pill, light | `status-pill--success` "Enrolled", `rgb(20,83,45)` on `rgb(231,244,236)` = **8.05:1** (the test's computed value, confirmed in the browser) |
| Status pill, dark | `rgb(166,227,187)` on `rgb(30,58,43)` = **8.45:1** |
| The original defect is gone | `<fluent-badge>` elements painting the host `#0f6cbd`: **0** |
| Dot is decoration | `aria-hidden="true"` on every `.status-pill__dot` |
| Dark tokens resolve | `--status-success-bg` → `#1e3a2b`; `--accent-fill-rest` → `#479ef5` (the dark gap closed in §8.1); page background `#1f1f1f` |
| Monogram in dark | disc `rgb(94,51,38)` — a dark disc, not the pale lavender the mock first shipped |
| Context labels in dark | `Tint` badge host `rgb(8,35,56)` with a white label ≈ **15.5:1** — legible, so the two context badges stay `FluentBadge` |
| Layout conformance, both themes | main blocks `border-top: 1px solid rgba(0,0,0,0)` (borderless); rail `#e1e1e1` light / `#3d3d3d` dark |
| Narrow | host **548px** → one grid track `548px`, rail below main, action bar `scrollWidth == clientWidth` (the `flex-wrap` fix holds) |
| Console | **0** errors |

Per-student, which is where the empty states live:

| Student | Pill | Placement | Subjects block |
|---|---|---|---|
| STUA03 | Enrolled | strip + 1 node | `NEEDS EXPLICIT ENROLMENT` band, **2** chips, "1 of 2 enrolled" |
| STUA02 | **Not enrolled** | none | "Subjects follow the placement — this student has no active enrollment." |
| STUA01 (the #427 student) | Enrolled | strip + 1 node | **"No subjects could be resolved for this placement."** — same grade as STUA03, which shows two chips. The difference is the period, which is #427's shape, and the wording now states the outcome instead of claiming the grade is empty |

**Two defects the render found, both fixed here:**

1. **"1 days in this placement"** — a pluralisation bug in the placement strip and the timeline meta. No test
   saw it: the render tests asserted the *block*, never the count's wording. `DaysText(int)` now returns
   "1 day" / "N days", and `StudentViewRendersTests` dates the active placement *yesterday* so the singular
   branch is exercised.
2. **The Contacts panel repeated itself when empty** — the ownership note ("This student's own contacts —
   guardians' contacts live on their cards below") restated what the empty message already said. The note now
   renders only when there is a list to disambiguate.

**Two defects the probe invented, and why they were not "fixed".** Setting `data-theme="dark"` by hand — on
`<html>` and on `<body>` — is **not** what the app's theme switch does, and it produced two false positives: a
`Tint` badge that measured white-on-pale-blue and a `Subtle` button at `#424242`. Switching theme through the
app's own control puts the attribute on `body` only, and both then measure legible (15.5:1 and `#d6d6d6`). The
lesson is the one already in `.github/copilot/rules/agent-lessons.md` under *"prove a VALUE"*: a synthetic theme
is not the theme.

**Still not live-verified:** the guardian **cards** and the per-card contact chips — none of the three seeded
students has a guardian, so that markup is covered by `StudentViewRendersTests` only.
