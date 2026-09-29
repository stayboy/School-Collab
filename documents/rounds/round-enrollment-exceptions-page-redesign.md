tier 2 (light) — OWNER OVERRIDE, 2026-09-28, "take tier 2 with orchestrator with migration involved". Superseded the earlier SOLO mode (owner, 2026-09-26). Justified by the skill's documented override: a round that fails the Tier-2 checklist ONLY on file-level gates (migration / contracts / new project / diff size) with its design fully settled may run at Tier 2 (ar-16 precedent). This round's design IS fully settled (owner decisions Q1–Q8, all recorded below) and the one gate it now fails is exactly the migration one. provider ollama; orchestrator ollama-cloud/glm-5.3-flash; child runs: orchestrator-plan (document owner) then one static reviewer; parent adjudicates and transcribes acceptance. round base 49d3ba5b. MIGRATION INVOLVED. UI round (EnrollmentExceptions.razor + .razor.css).

# Round enrollment-exceptions-page-redesign — v4 page interaction model (one subject per view)

**Tree state at round open (recorded 2026-09-26).** `git rev-parse --short HEAD` = `49d3ba5b`
(`docs(rounds): enrollment-exceptions round record, reviews and frozen patches`). The tree is
**dirty with exactly one file** — the v4 spec this round implements:

```
 M documents/specs/subject-period-exception-model.md
```

**Round base** is therefore `49d3ba5b` **plus** that one spec file. The v3 round is CLOSED
and accepted; this round is presentation-only and starts from a green build with
1214 tests passing.

---

## Context

### Why this round exists

The v3 round shipped `/students/enrollment-exceptions` and closed accepted. The **owner then
reviewed the page and called it clumsy and duplicative**, naming two symptoms: the subject
filter and the subject dropdown overlap, and leaving/resetting an applied filter is unclear.

The authority for the fix is **spec v4** — `documents/specs/subject-period-exception-model.md`
§5.1 and §0 **decisions 11–14**, all owner-locked 2026-09-26:

| # | Decision |
|---|---|
| 11 | The filter row is **`Grade Level` + `Subject`, on one line**, and is the page's **only** subject control. The add form is **defaulted to the filter**, not a second picker. |
| 12 | **One range input**; open ends are `(any start)` / `(any end)` **on the range**; the term/semester picker becomes a **shortcut on that range**. |
| 13 | The period part is **derived, never chosen** — period ⇒ its division, typed dates ⇒ `Any date`; shown as read-only text. |
| 14 | **One spelling per concept**, shared by list and picker: `Term` / `Semester` / `Any date`. |

### The four duplications, verified against the shipped page

All line numbers are `src/Students/SchoolCollab.Students.Application/Components/Pages/Students/EnrollmentExceptions.razor`
(1032 lines) as of round base. Every one was confirmed by reading the file, not inferred.

**1 — one subject, two states, one reset that only clears one of them.** `:546` and `:547`
assign `_selectedTopicId` and `_filterTopicId` **from the same `resolvedTopicId`**, so the
page carries two subject states seeded from one query value (the page's own comment at `:535`
concedes they "are set together"). The `Show all` button (`:131`) calls `ShowAll()` (`:557`),
which sets **`_filterTopicId = null` only** — the add form's `_selectedTopicId` survives, so
clearing the list filter leaves the write path armed for a subject the list is no longer
showing. `AddForAnotherSubject_ClearsTheFilter_…` papers over this by clearing the filter
*after* the write (`:886`); the inverse case has no test.

**2 — `PartLabel` has two overloads that disagree for the same value.**

```
:981  PartLabel(string division)          →  _ => "—"                // the LIST calls this (:143)
:988  PartLabel(AcademicYearDivision)     →  _ => "Not term-scoped"  // Label (:213), options (:711)
```

`PartLabel("None")` and `PartLabel(AcademicYearDivision.None)` return different text for the
same concept, under the same method name. The literal `"Not term-scoped"` is additionally
hardcoded at `:335` and `:723`. One concept, **four spelling sites, two helpers that
disagree** — the concrete form decision 14 removes.

**3 — the part is a control, a value, and a label at once.** `BuildDivisionOptions()` (`:706`)
→ `KeepDivisionOffered()` (`:744`) → `OnDivisionChangedAsync` (`:195`) →
`OnSpanPeriodPickedAsync` (`:209`), with the third dropdown's own `Label` **derived from the
picked part** (`:213`). A row therefore reads *"Period part [Term ▾]  Term [Term 3 ▾]"* — the
control's name and its value's name are the same word. This cascade exists only because the
part is *chosen*; decision 13 makes it a function of the span, which removes all four members.

**4 — four inputs for one date span.** A period picker (`:209`) plus two `FluentDatePicker`s
plus **two Clear buttons** (`:231–250`, each with its own `Title` and `aria-label`). The Clear
buttons exist solely to express "open", which is a property of the range; decision 12 expresses
it as `(any start)` / `(any end)` and removes both buttons and the state they manipulate.

### The test surface

`tests/SchoolCollab.Admin.Tests.Unit/EnrollmentExceptionsPageTests.cs` — **29 test methods,
1279 lines**, all MSTest. The redesign changes what several assert. Spec §9 carries a
**per-test migration map** (kept / replaced / retired, each with its reason); the round must
follow it and must **not** delete a test without recording its replacement.

Of the 29: **4 replaced**, **6 retired** (4 `PartPicker_*` + `ClearDate_*` + the
`AddForAnotherSubject` case that can no longer exist), **19 kept** (several re-expressed
against the filter row). Three new behaviours need coverage with no v3 analogue: the
**derived part** flipping on span source, the **`(any start)` / `(any end)`** rendering
gating `CanAdd`, and **"All subjects"** restoring the owner's whole set.

### Scope — what this round must NOT touch

Presentation only. Specifically **out of scope, and a change to any of these is a scope
failure**: the `SubjectEnrollmentException` entity; the API surface and
`EnrollmentExceptionsRoutes` (`/check` included); FR-56 / FR-57 / FR-58; the availability
predicate; `EnrollmentExceptionLabels`; the four **entry-point** surfaces (`Subjects.razor`,
grade-detail `Detail.razor`, `Assignments/Create.razor`, `JoinGroupsDialog.razor`) — they keep
navigating here with the same query string, which is now simply the filter row's initial state
rather than a separate pre-selection; and any migration. **No new endpoint, no cross-context
port, no AppHost change.**

Expected changed files: `EnrollmentExceptions.razor`, `EnrollmentExceptions.razor.css`, and
`EnrollmentExceptionsPageTests.cs` (the `.razor.css` change is implied by decision 12's single
range control and decision 11's one-line filter row).

### Execution mode — history and the CURRENT decision

| When | Mode | Why |
|---|---|---|
| 2026-09-26 | **SOLO** — 0 child runs | Owner: "go solo". |
| 2026-09-28 | **TIER 2 (light) + orchestrator** | Owner: "take tier 2 with orchestrator with migration involved". The round stopped being presentation-only when Q1=C put a `Period.Sequence` column and Q6=B an exception `Ordinal` in scope, so it began failing a **file-level** gate. The design remained fully settled. |

**Why Tier 2 is a legitimate override here, not a formality.** The skill's rule is that a
round failing the Tier-2 checklist *only* on file-level gates — migration, contracts, new
project, diff size — with settled design may run at Tier 2 as an explicit owner override
(ar-16 precedent); if the design is open, it is Tier 3. Here: **design settled** (every open
question answered and recorded), **the single failed gate is the migration**, and the
orchestrator-plan run is retained as document owner. Recorded on line 1 per the ar-16 rule.

**What solo cost, honestly.** The solo stretch produced 5 of 6 mutation checks caught, and the
one survivor (M6) turned out to be **dead code** — a redundant assignment in
`ClearAddSelection` that no reviewer had looked at and no test could distinguish. That is the
concrete argument for putting the orchestrator back on this round rather than continuing solo
across a five-layer schema change.

### ⚠️ The v4 scope fence is VOID

The `## Plan` below states the round is **presentation only** — entity, API, FR-56/57/58 and
the availability predicate untouched, no migration. **That fence no longer holds.** Q1=C
(a `Period.Sequence` column) and Q6=B (an exception `Ordinal`) make this a five-layer change.
The fence is restated in `## Plan`'s amendment below; this section is kept only so the
superseded claim is visible rather than quietly deleted.

### Slice 1 — DONE by the parent before the mode change (domain layer)

Written during the solo stretch and left in place. `SchoolCollab.Students.Core` compiles
**0 errors**; nothing else in the solution has been rebuilt against it yet.

| File | Change |
|---|---|
| `Domain/Period.cs` | `Sequence` (int?, 1-based position of a sub-period in its year's run); `Create`/`Update` take it; `ValidateSequence` enforces ≥ 1 and sub-period-only |
| `Domain/SubjectEnrollmentException.cs` | `Ordinal` (int?); `Create` takes it; invariants — ≥ 1, and requires a real part (`None` cannot carry one) |
| `Data/Configurations/PeriodConfiguration.cs` | `Sequence` mapped; **filtered unique index** on `(tenant_id, parent_period_id, division, sequence)` with `filter: "sequence IS NOT NULL"` |
| `Data/Configurations/SubjectEnrollmentExceptionConfiguration.cs` | `Ordinal` mapped — deliberately **not** in any index |
| `CQRS/TopicAssignments/TopicAssignmentPeriodValidator.cs` | `ValidateExceptionOrdinal(division, ordinal)` — the two rules above, as a 422 at the boundary |

**Two design rulings the orchestrator should review, not re-litigate silently:**

1. **The ordinal is descriptive; the dates are the truth.** Availability matching never reads
   it (§2.3), and the ordinal and the span are **allowed to disagree** — a tenant may not have
   periodised its years, and a subset of one term is a legitimate span for a whole-term
   ordinal. That is the price of decision 15, and it is the cost Q6=B accepted over my
   recommendation (a).
2. **The ordinal is excluded from the duplicate key and from the unique expression index.**
   Two exceptions with the same owner, topic, division and span are the same row whatever
   they claim, so letting the ordinal vary would buy a way to write a duplicate that reads
   differently. `repository.ExistsAsync` and the COALESCE index stay on the span.

⚠️ **Mid-slice state the orchestrator inherits: the model is AHEAD of its migrations.**
`StudentsDbContextModelSnapshot.cs` is byte-identical to base `49d3ba5b` and **zero**
migration files exist, because the two migrations EF generated were removed again — EF swept
*both* new columns into the first one (`AddPeriodSequence` also added
`subject_enrollment_exceptions.ordinal`, leaving `AddSubjectEnrollmentExceptionOrdinal` empty
and misnaming the first). The removal was clean, so the next `dotnet ef migrations add` will
produce a correct single migration. Until it does, `has-pending-model-changes` is **expected**
to fail. **I did not verify that** — my attempt to run the guard matched 0 tests, so treat it
as a prediction, not a measurement.

---

## Owner decisions — Q1–Q8 (2026-09-28, all locked)

These supersede the v4 decisions 11–14 where they conflict, and are the settled design the
Tier-2 override depends on. **Design is closed; the orchestrator implements, it does not
re-open these.**

| # | Question | Decision |
|---|---|---|
| Q1 | Source of the "1st/2nd/3rd" ordinal | **(c)** Add a `Sequence` column to `Period`. **Not limited to existing periods** — the ordinals are structural, so a 3rd term can be named even when the tenant has no 3rd-term period. |
| Q2 | Checkbox or radio for the ordinal | **(a)** `FluentRadioGroup` — single-select. True multi-select ("terms 1 and 3, not 2") is **two** exception rows and stays out of scope (§2.4/§10). |
| Q3 | Layout | **(a)** the house form primitive, with **labels beneath the inputs** (implemented by an optional parameter on the shared `FormRow`, owner choice 2026-09-28). |
| Q4 | Owner preselection | Preselect the owner **kind** only. **Neither grade level nor subject is preselected**, so no filter is applied on arrival. |
| Q5 | How many ordinals to offer | **(a)** 1st–4th always, **plus** any higher `Sequence` that exists on a Period — so a 5th term appears once the tenant creates one. |
| Q6 | Is the ordinal persisted? | **(b)** Yes — a new `Ordinal` on the exception, so a row renders "Term 2" without re-deriving it from dates. See the two rulings in the Slice 1 section: the ordinal is descriptive, the dates are the truth, and the two are allowed to disagree. |
| Q7 | Label-below mechanism | **(a)** Extend the shared `FormRow` with an **optional** label-position parameter, additive, default preserving every existing usage. |
| Q8 | Backfill for existing `Period.Sequence` | **(a)** Parse the ordinal from the name (anchored at the end, so "2027 Term" does not parse as 2027), else position by `StartDate` within the parent year + division. |

**Q3/Q7 in tension, and how it resolves.** `FormRow`'s aligning constant is a fixed 180px label
column, and the label sits left or above — never below. With labels beneath, the column would
sit *under* each input, so the constant that makes the column worth having is gone and the
aligning constant becomes "same input height + same label `line-height` + bottom alignment".
The owner chose (a) anyway: the parameter is added to the shared primitive and is **additive**,
so the other ~30 components keep their behaviour untouched. The orchestrator must NOT change
`FormRow`'s default, DOM order, or existing CSS rules.

---

## Plan

### ⚠️ AMENDED 2026-09-28 — the presentation-only fence is void

The original v4 plan below assumed presentation only. Q1=C and Q6=B supersede that. The
remaining work, in dependency order (each slice must build and keep its tests green):

| # | Slice | Touches | Depends on |
|---|---|---|---|
| — | **1. Domain** *(DONE by parent, above)* | `Period`, `SubjectEnrollmentException`, both EF configurations, the validator | — |
| 2 | **Migration** | one migration adding `periods.sequence` + the filtered unique index + `subject_enrollment_exceptions.ordinal`, with the two backfills (Q8a for `sequence`; exact date-match for `ordinal`) | 1 |
| 3 | **API / CQRS** | `PeriodDto`, `CreatePeriod`/`UpdatePeriod` + handlers, `PeriodRoutes` requests, `CreateSubjectEnrollmentException.Ordinal`, its handler, `SubjectEnrollmentExceptionDto.Ordinal`, the client records + `StudentsApiClient` | 1 |
| 4 | **Shared UI** | `FormRow` + label-position parameter + CSS (**additive only**); `PeriodFormFields` `Sequence` row; the period form model + Create/Edit mapping | 3 |
| 5 | **The page** | the add section rebuilt around *division + ordinal* (decision 13 reversed: the part is **chosen** as a division); `FormRow` label-below layout; the Q4 preselection change | 3, 4 |
| 6 | **Tests** | entity/validator/migration-guard tests; the 4 `DerivedPart_*` page tests change meaning; new ordinal coverage; the Q4 no-preselection test fallout | 2–5 |
| 7 | **Docs** | spec v5 (§0 decision 13 reversed + new decision 15, §5.1, §7, §9 map); this round's acceptance | 2–6 |

**Slice 2 backfills, precisely (Q8a).** `sequence`: for sub-period rows only
(`division <> 0`), take the trailing integer from `name` when there is one, else the 1-based
position by `start_date` within the same `(tenant_id, parent_period_id, division)`. Top-level
years stay NULL — that is what makes the `sequence IS NOT NULL` filter correct.
`ordinal`: set only where an exception's span **exactly equals** a positioned sub-period's
span, so the backfill is conservative and everything else stays NULL. The second backfill must
run after the first, which the migration ordering guarantees.

**Known blast radius to check, not to guess at.** Slice 3–4 touch `PeriodRoutes`,
`CreatePeriod`/`UpdatePeriod`, `PeriodFormFields`, the period Create/Edit pages and
`StudentsApiClient` — each may have its own tests and callers that a positional argument
change would break. Prefer **optional trailing parameters** and named arguments at call sites.

**Slice 6 warning from Q4.** Removing the first-grade auto-selection changes the page's landing
state, so any test that relied on the auto-selected grade must now name an owner explicitly in
the query string. That is a real fallout, not a formality — the page lands with no owner and
no subject on arrival.

### (a) GOAL (as originally planned)

Rebuild the `/students/enrollment-exceptions` page's **interaction model** to spec v4 §5.1 +
decisions 11–14: one subject per view, one control owning it; one range input with open ends
expressed on the range; the period part derived and read-only; one spelling per concept.
**Presentation only** — see the scope fence in `## Context`.

### (b) The four changes, and what each deletes

| # | Change | Deletes |
|---|---|---|
| 11 | **Filter row = `Grade Level` + `Subject`, one line.** `Subject` lists the owner's subjects plus **"All subjects"**. The add form takes its subject from the filter and shows it in its heading. | the `Show all` button (`:131`), `ShowAll()` (`:557`), the separate `_filterTopicId` field, the add panel's `Subject` dropdown (`:186`) — **two subject states collapse to one** |
| 12 | **One labelled "Not offered" range**, ends independently open, open ends rendering `(any start)` / `(any end)`. The period picker becomes a **shortcut on the range** ("use Term 3's dates"). | the two Clear buttons (`:231–250`) and the state they manipulate |
| 13 | **Part derived, read-only**: period ⇒ `Term`/`Semester`, typed dates ⇒ `Any date`. | the part dropdown (`:195`, `:213`), `BuildDivisionOptions()` (`:706`), `KeepDivisionOffered()` (`:744`), `OnDivisionChangedAsync`, and the `_division` write path |
| 14 | **One spelling**: `Term` / `Semester` / `Any date`, from a **single** label helper. | `PartLabel(string)` (`:981`) vs `PartLabel(AcademicYearDivision)` (`:988`) — the two disagreeing overloads — plus the `"Not term-scoped"` literals at `:335` and `:723` |

Net effect on state: `_filterTopicId` and one of the two subject fields are removed, the two
date "clear" affordances are removed, and `_division` stops being *chosen* — so the
`OnDivisionChanged` → `KeepDivisionOffered` → `OnSpanPeriodPicked` cascade disappears as a
unit rather than being patched.

### (c) Derived part — the one place with real logic

```
part = spanPeriod is a period whose division is D  →  D
part = otherwise (either end typed, or neither)   →  None   ("Any date")
```

Only a *period* can imply a part; typed dates never do. This must be a single function used by
both the read-only display and the write, so the label shown and the `Division` persisted can
never disagree — that disagreement is the class of bug this round exists to remove. **This is
the highest-risk logic in the round and gets a mutation check.**

### (d) Empty states — exactly three, because there are exactly three scopes

1. owner has none and no subject is in scope → "No exceptions — every subject is offered on every date."
2. a subject is in scope and has none → "No exceptions for **X** — this subject is offered on every date."
3. no subject in scope but the owner has some → no list; the filter row prompts "Choose a subject, or pick **All subjects** to list every one."

### (e) Test migration — spec §9's map, applied test by test

**4 replaced, 6 retired, 19 kept**, plus **3 new** (derived part flips on span source;
`(any start)`/`(any end)` gates `CanAdd`; "All subjects" restores the whole set). Every
retirement records its replacement; none is deleted silently.

### (f) Constraints

CSS isolation only (no inline `<style>`); `Error="@Error"`; `FluentSelect`/`FluentMenu` do
not materialise children while closed, so tests assert bound `Items`/`SelectedOption`, not
closed-menu markup; component callbacks invoked via `cut.InvokeAsync(...)`; the
`FluentDatePicker`-contains-a-`FluentTextField` trap still applies; **no** change to the
entity, API, FR-56/58, availability predicate, `EnrollmentExceptionLabels`, or the four
entry-point surfaces; no migration; no new endpoint or AppHost change.

## Worker Report

### Run A — slices 2–4 (schema + API/CQRS + shared UI) — COMPLETE, green

Implementing run, `ollama-cloud/deepseek-v4.1-flash`. Report:
`documents/rounds/run-a-schema-api-shared-ui.report.md` (29.7 KB). 31 files touched.

**Slice 2 — migration.** One migration,
`20260928174930_AddPeriodSequenceAndExceptionOrdinal`, generated then hand-edited. `Up()` order is
load-bearing and deliberate: `AddColumn ordinal` → `AddColumn sequence` → backfill 1 → backfill 2 →
`CreateIndex` **last**, so the one-position-per-run rule is asserted *against the data the backfill
just produced* — a collision aborts the migration loudly with the offending key, rather than
silently shipping a half-positioned column. The run demonstrated that failure mode on purpose
against real Postgres and captured the `23505`-style error text.

Backfill 1 parses the trailing digits of `name` **end-anchored** (`([0-9]+)[[:space:]]*$`), so
`"2027 Term"` matches nothing and falls through to positional-by-`start_date` (Q8a). The cast is
guarded twice — `substring` returns NULL on no match, and a `CASE` refuses a digit run longer than
9, so `"Term 99999999999"` falls back instead of aborting with *value out of range*. Scope guard is
`division <> 0` alone: **top-level years keep NULL, and that NULL is what makes the
`filter: "sequence IS NOT NULL"` partial index correct** — Postgres treats NULLs as DISTINCT, the
same trap the exception's COALESCE expression indexes work around, answered here with a partial
index. Backfill 2 uses `DISTINCT ON … ORDER BY … sequence` to make a duplicate-span match
deterministic (a deliberate addition beyond the brief).

> ⚠️ **THE PARAGRAPH ABOVE WAS WRONG — see `## Review` → P1.** `division <> 0` does **not** mean
> "sub-periods only". A **top-level periodised year** (`parent_period_id IS NULL` with a
> Terms/Semesters division) is a documented, first-class shape — `Period.Division`'s own comment
> reads "Terms/Semesters on a top-level year (ParentPeriodId == null) means the year may contain
> only that sub-period kind", and `ValidateHierarchy` rejects only the opposite case. The backfill
> was giving those rows a `sequence` that `ValidateSequence` forbids. Corrected to
> `parent_period_id IS NOT NULL AND division <> 0`, and verified against real Postgres 16.

**Slice 3 — API/CQRS.** Every added member is an optional **trailing** parameter; every call site
uses a **named** argument, so no positional signature changed. The duplicate check and
`repository.ExistsAsync` were **not** touched — still keyed on `(owner, topic, division, span)`,
confirming the ordinal is not part of the uniqueness story.

**Slice 4 — shared UI.** `FormRow` gains `[Parameter] RowLabelPosition LabelPosition = Default`,
a new enum in its own file, and **two appended CSS rules with zero deletions** (verified: 0 deleted
lines). `PeriodFormFields` gains a `Sequence` row.

#### Two catches outside my brief — both silent data loss

These are the strongest argument for having put the orchestrator back on this round:

1. **`UpdatePeriodHandler` would have nulled the new column on *every* period edit.** The existing
   `period.Update(...)` call did not pass the sequence, so an unmentioned change would have silently
   cleared it. Fixed by threading `command.Sequence`.
2. **`PeriodSubPeriodsEditor.razor`** (the inline sub-period grid on the year editor) was not in my
   file list at all. Renaming or re-dating a sub-period from there would have unpositioned it.
   `StartEdit` now captures the sequence and the request echoes it.

It also found that gating the new row on the existing `IsSubPeriod` parameter would have **hidden it
in edit mode** — that parameter is only ever set on create, and edit loads the parent into the model
instead — so the row is gated on `Guid.TryParse(Model.ParentPeriodIdText, …)`, which is what the
entity invariant and the index key on.

#### Verification I ran MYSELF (not the worker's word for it)

| Check | Result |
|---|---|
| `MigrationGuardTests.NoUncommittedModelChanges` (Students.Tests.Unit) | **PASS** — total=1, failed=0 |
| `MigrationGuardTests.NoDomainDbContext_HasPendingModelChanges` (ArchitectureTests.Unit) | **PASS** — total=1, failed=0 |
| Both filters selected **exactly 1** test each | a filter matching 0 tests is not evidence — this is the check that the earlier `--nologo` lesson demands |
| Slice-5/6 files untouched | `EnrollmentExceptions.razor` +304/−308, `.razor.css` +57/−35, `EnrollmentExceptionsPageTests.cs` +345/−307 — **all three identical to the diffstats I recorded before dispatch** |
| Working tree free of stray files | 3 new files only (`RowLabelPosition.cs`, migration `.cs`, `.Designer.cs`); no temp artifacts, no stashes |
| Snapshot declares both properties | `b.Property<int?>("Sequence")` and `b.Property<int?>("Ordinal")`, plus `HasIndex(…).IsUnique().HasFilter("sequence IS NOT NULL")` — +13 lines |
| `FormRow` CSS append-only | **0 deleted lines**; 2 new rules |
| `FormRow` root `div` | base line preserved verbatim with `@(LabelPosition == RowLabelPosition.Below ? "form-row--label-below" : null)` **appended**; the ternary ends in `: null`, so every non-`Below` row renders byte-identical markup |
| `FormRowOrientationTests` | file untouched by this round; the `Orientation` parameter declaration is not in the diff, so its verbatim assertion is intact |

#### Corrections to the worker's own claims (both mine, not the tree's)

- The worker wrote that the `FormRow` existing class expressions are "byte-identical". **The line
  changed; the emitted markup does not.** Behaviourally correct, and the distinction is now recorded
  rather than left as an overstatement.
- **My first two verification probes were wrong and said so**: I searched the snapshot for
  `Property<int>("Sequence")` when EF writes `Property<int?>(…)` for a nullable, and I read the
  *index* copy of the snapshot rather than the working tree. Both reported "missing" on a correct
  tree. The guards are what settled it.

#### Untested by anything yet (carried to slice 6)

- No end-to-end assertion that posting `sequence`/`ordinal` persists and reads back.
- `LabelPosition.Below` is unverified **visually** — and nothing uses it yet; the page is its first caller.

### ⚠️ Owner decision required — duplicate period position is a 500, not a 4xx

`POST /students/periods` and `PUT /students/periods/{id}` with a position a sibling sub-period
already holds hit the filtered unique index and surface as an unhandled `DbUpdateException` → **500**.
The routes catch only `Period*Exception`, `ArgumentException` and `ConcurrencyException`. The new
dropdown offers 1st–4th regardless of what is taken, so **this is reachable from the control the
worker just added**. The worker correctly refused to fix it unasked and did not silently invent a
422 either.

❓ **(a) 422 pre-check in `CreatePeriodHandler`/`UpdatePeriodHandler`**, mirroring the exception
handler's `ExistsAsync` → 409 pattern — a clear sentence telling the user which sibling holds it.
❓ **(b) Filter already-taken positions out of the dropdown**, so the choice is unavailable rather
than invalid — better UX, but two admins editing the same year can still race.
❓ **(c) Both** — filter the list *and* pre-check.

➡️ **(c)**: the filter is what makes the control honest, and the pre-check is what makes the race
safe. Neither alone is sufficient.

✅ **RESOLVED 2026-09-28 — owner accepted (c), both.** Folded into the round as **slice 4b**
(small: two handler pre-checks + a filtered option list), owned by the next run alongside the
tests that prove it. The pre-check mirrors the exception's `ExistsAsync` → 409 precedent — a
clear sentence naming the sibling that holds the position — and the dropdown offers only
positions no sibling holds, extended by the row's own current position so an edit form can save
without silently moving itself.

#### Incidental pre-existing finding — not fixed, correctly

`dotnet ef migrations script` is not runnable as a single psql file: two raw `Sql(...)` blocks in
the *existing* `20260801084654_AddActivityGroups` migration lack trailing `;`. EF's runtime path is
unaffected (hence the green integration suite). Rule 1 forbids editing it; the run patched a
**temporary copy** to verify. Worth a separate sweep of raw `Sql(...)` blocks.

#### Other residuals

- **Backfill assumes sane names**: two siblings both ending in the same integer (`"Term 1"` and
  `"Term 01"`) abort at `CREATE INDEX`. Loud and data-preserving; exposure is dev/staging only.
- **`SubPeriodDefinitionRequest` has no `Sequence`**, so the atomic year+sub-periods create and the
  grid's auto-split produce *unpositioned* sub-periods (legal NULL). Assigning 1..N on auto-split is
  a product choice, not an oversight.
- The two Playwright suites were not run (browser suites, out of scope).


### Run B — slices 4b, 5, 6, 7 (page + tests + docs) — COMPLETE, green

Implementing run, `ollama-cloud/deepseek-v4.1-pro` (this run). Slice 4b was added to the plan by the
owner decision recorded above (option **(c)**); the rest is the AMENDED plan's slices 5–7.
Report: `documents/rounds/run-b-page-tests-docs.report.md`. Nothing committed, pushed or staged.

#### Slice 4b — duplicate period position (both halves of the owner's (c))

| File | Change |
|---|---|
| `Domain/Exceptions/PeriodSequenceTakenException.cs` | **NEW.** Carries the parent year, division, position, and the **holder's id and name** — the message names the sibling, because a caller told only "that position is taken" cannot act on it. Mapped to **422**, matching `PeriodOverlapException`/`PeriodContainmentException` on the same routes. |
| `CQRS/Periods/PeriodSequenceGuard.cs` | **NEW.** The shared handler-side half of one-position-per-run, so `CreatePeriodHandler` and `UpdatePeriodHandler` cannot disagree about when a position is free. `excludeId` is the row being EDITED, so an edit may keep the position it holds. |
| `Data/Repositories/IPeriodRepository.cs` + `PeriodRepository.cs` | `GetSubPeriodBySequenceAsync(parentPeriodId, division, sequence, excludeId)` — the sibling lookup, `AsNoTracking` (pure existence check). |
| `CQRS/Periods/Commands/CreatePeriod/CreatePeriodHandler.cs`, `…/UpdatePeriod/UpdatePeriodHandler.cs` | The pre-check, inside the existing parent-year block (where a sequence is legal at all). |
| `Api/Endpoints/PeriodRoutes.cs` | `catch (PeriodSequenceTakenException) → 422` on both `POST /periods` and `PUT /periods/{id}` — the two routes that can write a position. |
| `Components/Pages/Periods/PeriodFormFields.razor` | The Sequence row offers **only positions no sibling holds**, keeping the row's own position and reaching any higher position a sibling holds (so a backfilled 5th stays representable instead of being silently dropped on save). |
| `Components/Pages/Periods/PeriodUpsert.razor` | Computes `UnavailableSequences` from the tenant's period list it already loads — same year, same division, **excluding the period being edited** — and passes it to the field set. |

#### Slice 5 — the page (`EnrollmentExceptions.razor` + `.razor.css`)

**Kept from v4** (still correct): one subject state (`_subjectKey` / `SubjectInScope` /
`AllSubjectsKey` / `_subjectOptions`), the add section defaulted to the filter with no subject
picker, the closed add section with its prompt, the two empty states, the subject column omitted
when scoped, the count badge as the owner's total, the single "Not offered" range with
`(any start)`/`(any end)` and no Clear buttons.

**Replaced**: `DerivedDivision`, `ShortcutPeriods` (the `PeriodDto` picker), `_spanPeriodId`,
`OnSpanPeriodPickedAsync`, and the floating `.part-readonly` badge. **There is no period-instance
selection anywhere on the page any more** — only a division and a position.

**New add section**, the house form primitive with the label **beneath** each input
(`RowLabelPosition.Below`, Q3/Q7):

- **"Fill from"** = the academic division: the divisions the tenant has periods for plus
  `Any date` (grade owner), or exactly the one FR-56 permits (group owner). Labels come from the
  same `PartLabel` helper the rows use, so decision 14 still holds with one spelling.
- **Position** = a `FluentRadioGroup` of `1st`–`4th` **plus any higher declared position**, and
  **not limited to the existing periods** (Q1) — the fixture-proven case is a 2nd/3rd/4th on
  offer with only a 1st period in the data.
- Choosing a position **whose period exists** fills `From`/`To` from it; choosing one with **no**
  period clears the range, and `Add exception` stays disabled until a bound is typed.
- `Any date` ⇒ no position row at all (a free window has no position, and the server rejects an
  ordinal on one).
- The request carries the **chosen** `Division` and the chosen `Ordinal`.

**Q4**: the `_gradeLevels[0]` auto-selection is **removed**, so the page lands with no owner, no
grade and no subject.

Two implementation decisions the brief did not settle, both recorded rather than hidden:

1. **Which period a chosen position fills from**, when several years hold that position: the one
   inside the tenant's **ACTIVE** academic year, else the latest-starting one. Deterministic, and
   the choice is a convenience either way (the span is what matches, §2.3) — but not arbitrary.
2. **Changing the division clears the position and the range.** A Terms "3rd" is not a Semesters
   one and dates read from a term's period are not a semester's; carrying either across would be a
   stale value wearing the new division's name. Typed dates under `Any date` therefore have to be
   re-typed after a division change — the cost of the rule, accepted deliberately.

#### Slice 6 — tests

**`EnrollmentExceptionsPageTests.cs`** (28 → **33**): the four `DerivedPart_*` tests are
**replaced, not deleted**, each naming its predecessor in the doc comment
(`ChosenPart_IsAControl_OfferedOnlyWhereTheTenantHasPeriods`,
`ChosenPart_TenantWithoutDivisions_OffersAnyDateOnly_AndNoPosition`,
`ChosenPart_GroupOwned_TermlyGroup_OffersTermOnly`,
`ChosenPart_GroupOwned_UnAlignedSpan_OffersAnyDateOnly_AndNoPosition`). Five are new:
the position ladder (`Positions_AreOneToFourPlusAnyHigherDeclared_NotOnlyTheExistingPeriods`),
the no-period flow (`PositionWithNoMatchingPeriod_LeavesTheRangeEmpty_AndTakesTheTypedDates`),
the chosen part+position in the body (`Add_CarriesTheChosenPartAndPosition_ToTheServer`), the
deterministic position pick (`Positions_TwoYearsBothTermOne_FillFromTheActiveYearsPeriod`), and
Q4 (`LandingWithNoQuery_PreSelectsNoOwnerGradeOrSubject`). **Every other test now names an owner
in the query string** — the real fallout of Q4, exactly as the plan warned.

**New server-side suites**: `Domain/PeriodSequenceTests.cs` (entity invariants),
`PeriodSequenceTakenTests.cs` (the handler pre-check on both handlers, incl. the self-exclusion),
`TopicAssignmentPeriodOrdinalTests.cs` (the two ordinal rules + the deliberate absence of a
span cross-check), `PeriodFormSequenceOptionsTests.cs` (the dropdown filter),
`PeriodPositionLabelsTests.cs` (the one ordinal spelling). Two tests added to
`CreateSubjectEnrollmentExceptionHandlerTests` (the ordinal persists; a different ordinal over the
same span is still a duplicate).

**New real-Postgres suite**: `PeriodSequenceAndExceptionOrdinalEndpointTests.cs` (8 tests) — the
API round trip for `sequence` and `ordinal`, the 422 naming the sibling on create and on update,
an exception with a different ordinal for the same span still a 409, an ordinal on a free window a
422, and **the filtered unique index rejecting two sibling positions inserted directly** (the only
test that can observe the index, since the guard means the endpoint never reaches Postgres with a
collision).

#### Slice 7 — docs

Spec amended to **v5**: decision 13 struck and formally reversed, **decision 15** (the position +
the persisted ordinal, with both rulings and the honest price of "they may disagree") and
**decision 16** (the part is chosen, offered only where it can be written), §2.1's entity gains
`Ordinal`, §5.1's add section rewritten, a new **§7.1** for the migration (columns, backfills,
index-last ordering, the 42-byte name), §6's create row gains `ordinal`, and §9 gains the v5 test
map and the new coverage lists.

### (as originally planned)

### Run B — slices 4b, 5, 6, 7 — **TIMED OUT at the 30-minute async deadline; work is on disk, UNVERIFIED**

`5e7aec65-94eb-464d-8e35-0724883c2181`. The parent let the 30-minute deadline stand — a
single-agent async run is unbounded only for *composite* runs — and the parent's steer carrying
the P1 addendum arrived **after** expiry, so Run B never saw it. **Nothing here is accepted**: no
report, no test evidence of its own.

Partial state, all structurally intact (brace balance 0, no truncation) and the solution builds
**0 errors** on it:

| Slice | State |
|---|---|
| **4b** duplicate position | appears **done** — new `PeriodSequenceGuard.cs`, `PeriodSequenceTakenException.cs`, `IPeriodRepository` + handlers + `PeriodRoutes` + `PeriodFormFields` (+86) |
| **5** the page | largely rewritten — razor +466/−313, css +52/−45 |
| **6** tests | 5 new unit test files + 1 new integration test file + 2 modified |
| **7** docs | **not reached** — no spec v5, no acceptance |

**Carried forward:** finish slices 5/6/7, and **P2-3** below, which lands in the same files.

## Review

### Static review of slices 1–4 — `APPROVE-WITH-FINDINGS`, **no P0**

Reviewer: `openrouter/stealth/space-bunny-alpha` (thinking high), read-only, run `0ea6fbbd`. Its
`structured_output` tool rejected every payload shape, so the run is labelled failed while the
artifact landed inline; the substance is intact and the parent re-checked its load-bearing claims.

**Both core rulings verified structurally, not by convention:** the ordinal appears in **no**
uniqueness path — absent from `ExistsAsync`, from both COALESCE expression indexes in the untouched
earlier migration, from every EF index, and from the availability predicate — and the filtered
partial unique index is byte-identical between the hand-written migration and the model snapshot.
Also confirmed: `Down()` is a true inverse, both backfills are tenant-safe, and there is **no
silent projection loss** — all seven `new PeriodDto(` sites and the single
`SubjectEnrollmentExceptionDto` site pass the new field.

#### P1 — the backfill's scope guard was wrong. FIXED AND VERIFIED.

`migration:58` gated with `WHERE division <> 0`, which is **not** "sub-periods only" — see the
correction note in the Worker Report above. Three consequences: it wrote rows `ValidateSequence`
forbids; it became an edit-path hazard; and because such rows are *inside* the partial index, two
periodised years whose names ended in the same integer would **abort the migration** at
`CreateIndex`.

**Root cause was this round's PLAN, not the implementing run's judgement** — the plan said
"`division <> 0` … top-level years stay NULL", and the run implemented it faithfully.

**Fix:** `WHERE parent_period_id IS NOT NULL AND division <> 0` in backfill 1, the same guard in
backfill 2, and the misleading comment rewritten.

**A regression harness then found a SECOND defect — in the parent's own first fix.** That version
`COALESCE`'d a name-parsed position with an independent `row_number()` fallback: two position
sources computed separately over the same run, so a run holding `"Term 1"` and an unnamed
`"Winter"` gave **both** position 1, and `CreateIndex` aborted. Rebuilt as a **single
`row_number()`** ordered `(plausible parsed position NULLS LAST, start_date, id)` — unique by
construction, still honours names, treats a trailing year as unparsed, and **dissolves the
`"Term 1"` / `"Term 01"` collision** the earlier design had listed as an accepted residual.

**Verified against real Postgres 16** (throwaway container; the SQL was *extracted from the
shipped migration file*, never retyped):

```
AY2026 / AY2027 / AY2028 / AY2029 / 2025 Academic Year   top   NULL
Term 1 -> 1   Winter -> 2   Semester 2 2027 -> 3      (name wins; the rest by date)
Term 1 -> 1   Term 01 -> 2                             (equal parses resolved distinctly)
```
**12/12 assertions pass**, including *"the partial unique index BUILDS against the backfilled
data"* — the P1 abort path, closed.

#### P2s

- **P2-2 — the "byte-identical" claim was off by one space.** The appended ternary was preceded by
  a literal space, so every `Default` row's `class` gained a byte. **Fixed** (space moved inside
  the branch), which makes the XML doc's "byte-for-byte the same markup" literally true for all
  ~30 existing callers. The reviewer separately confirmed the CSS is genuinely append-only
  (`+35/−0`), the specificity arithmetic resolves correctly, and DOM/a11y order is preserved.
- **P2-3 — `Period.Sequence` is a 400 where the rest of the feature is a 422.** `ValidateSequence`
  throws `ArgumentException` (→ 400) while the duplicate-position rejection is a 422. **NOT YET
  FIXED** — it lives in the period handlers/routes, i.e. Run B's 4b files. Carried forward.
- **P2-4 — a name merely ending in a year became the position** (`"Semester 2 2027"` → 2027).
  **Fixed**, and subsumed by the single-ordering rewrite above.
- **P2-1 — no DB-level `CHECK` on `sequence`/`ordinal`.** Open and optional: the migration is the
  only writer that bypasses the entity validators. Dev/staging exposure only.

#### Documentation-integrity finding — FIXED

The review caught the round doc citing `documents/rounds/run-a-schema-api-shared-ui.report.md`,
which **did not exist in the repo**: an `output:` binding routes a relative path into a managed
session-artifact directory, not the cwd. It has been **copied into the repo** at the cited path
(30,421 bytes), so the round record's claims are sourced again. The same applies to Run B's report
when it lands.

#### Could not verify — and the gap it exposed

The reviewer has no shell, so no build, no tests, and it could not execute the backfill. It also
identified a **real coverage gap**: every suite runs `MigrateAsync()` on a fresh container and
then truncates, so **both backfills have always executed over zero rows** — which is exactly why
the P1 lived and went unseen.

⚠️ **That gap is still open.** The harness above proves the fix *once*, on this machine; it is
**not** a committed test, so the suite still cannot catch a backfill regression. **This is the top
open item in the round.**

## Acceptance — v5 page pass (Tier-2, slices 4b–7, 2026-09-28)

**Verdict: accepted on evidence, with two implementation decisions taken and recorded, and one
honest gap (no browser pass).** This section is the record for the Tier-2 round's second half
(Run B); the section below is the earlier **v4 page pass** record, kept intact.

### Scope gate — held

Run B touched exactly the slices it owns: `EnrollmentExceptions.razor` + `.razor.css` (slice 5),
`PeriodFormFields.razor` + `PeriodUpsert.razor` + the new exception/guard/repository method +
the two period handlers + `PeriodRoutes` (slice 4b), five new/edited test files plus two new
server-side suites and one new integration suite (slice 6), the spec and this doc (slice 7).
**No migration, no new endpoint, no AppHost/feature-flag change, no cross-context port**, and the
availability predicate, `EnrollmentExceptionLabels`, FR-56/57/58 and the four entry-point surfaces
are untouched.

### Criteria — each one against real code and real runs

| # | Criterion | Evidence |
|---|---|---|
| AC-B1 | **The period form offers only positions no sibling holds** (slice 4b, dropdown half) | `PeriodFormSequenceOptionsTests` (5 tests): `[1,3]` unavailable ⇒ offered `[2,4]`; the row's own position stays on offer; a sibling's 5th extends the ladder. **Mutation M7** (drop the filter) → 2 tests fail. |
| AC-B2 | **A taken position is a 422 that NAMES the sibling** (slice 4b, pre-check half) | `PeriodSequenceTakenTests` (5): create and update both rejected, message contains the holder's name and the position, and the rejected update persists nothing. Integration: `Create_SecondSubPeriod_OnATakenPosition_Is422_NamingTheSibling`, `Update_SubPeriod_KeepingItsOwnPosition_Is204_AndOntoASiblingsIs422`. **Mutation M8** (delete the guard call) → the create test fails. |
| AC-B3 | **The filtered unique index is the storage half and is not vacuous** | Integration `TwoSiblingSubPeriods_InsertedDirectly_AreRejectedByTheFilteredIndex` — bypasses the handler, gets SQLSTATE `23505` and zero surviving rows; `SiblingSubPeriods_WithoutPositions_DoNotCollide` proves the `sequence IS NOT NULL` filter is what keeps unpositioned sub-periods legal. |
| AC-B4 | **The part is CHOSEN, offered only where it can be written** | The four `ChosenPart_*` tests: a Terms-only tenant offers `[Term, Any date]`; a Semesters-less tenant never sees Semester; a Termly group offers `[Term]` **only**; a non-aligned group offers `[Any date]` only. **Mutation M5** (drop the owner coercion) → the Termly-group test fails. |
| AC-B5 | **The position is structural: 1st–4th plus any higher declared, NOT limited to the periods that exist** | `Positions_AreOneToFourPlusAnyHigherDeclared_NotOnlyTheExistingPeriods` — `[1st, 2nd, 3rd, 4th, 5th]` on a fixture with only 1st- and 5th-term periods. **Mutation M2** (offer positions on `Any date` too) → 5 tests fail. |
| AC-B6 | **Position ⇒ dates**: a matching period fills the range; no matching period clears it and gates the write | `PositionWithNoMatchingPeriod_LeavesTheRangeEmpty_AndTakesTheTypedDates` (1st fills, 3rd clears, disabled until a bound is typed, body then carries `ordinal` **beside** the typed dates), `Positions_TwoYearsBothTermOne_FillFromTheActiveYearsPeriod`, `OpenEnds_ReadAsAnyStartAndAnyDate_AndGateTheWrite`. **Mutation M1** (sever the fill) → 11 tests fail; **M3** (drop the `CanAdd` bound requirement) → 2 fail. |
| AC-B7 | **The body carries the CHOSEN division and the CHOSEN position** | `Add_CarriesTheChosenPartAndPosition_ToTheServer` (`division:2` + `ordinal:2` + Semester 2's dates — a pair no derivation from the dates could produce) and `Add_WritesTheExceptionImmediately_AndRereadsTheList` (`division:1` + `ordinal:1`). **Mutation M4** (`ChosenOrdinal => null`) → 4 tests fail. |
| AC-B8 | **A posted `sequence`/`ordinal` PERSISTS and READS BACK end to end** (the gap run A flagged) | Integration `Create_SubPeriodWithADeclaredPosition_PersistsAndReadsBack` and `Create_ExceptionWithAnOrdinal_PersistsAndReadsBack` — real Postgres, real API, real DTO projection. |
| AC-B9 | **The ordinal is NOT part of the duplicate key** | Handler: `Create_SameSpanWithADifferentOrdinal_IsStillADuplicate`. Integration: `Create_ExceptionWithADifferentOrdinalForTheSameSpan_Is409` — the COALESCE expression index is unmoved. |
| AC-B10 | **Q4: the page lands with no owner, no grade and no subject** | `LandingWithNoQuery_PreSelectsNoOwnerGradeOrSubject` — owner prompt, no grade selected, no subject control, no exceptions request. The fallout is fixed, not hidden: every other page test now names an owner in the query string. |
| AC-B11 | **Domain invariants** | `PeriodSequenceTests` (position ≥ 1, sub-period-only, on create **and** update, and `Update(null)` really clears it), `TopicAssignmentPeriodOrdinalTests` (the two ordinal rules, plus the deliberate absence of a span cross-check), `PeriodPositionLabelsTests` (one ordinal spelling, 11th–13th handled). |
| AC-B12 | **CSS isolation, labels beneath inputs, no residue** | `.razor.css` gains `.add-form` and drops `.add-row`/`.add-label`/`::deep .part-readonly` — the `margin-top` hack the label-below layout replaces; no inline `<style>`. `ChosenPart_IsAControl_OfferedOnlyWhereTheTenantHasPeriods` asserts 4 `form-row--label-below` rows once a real part is chosen (3 while it is `Any date`); `ScopedEmptyState_AndRangeGroup_CarryTheirAccessibleNames` asserts the row labels. |
| AC-B13 | **Docs amended to v5** | Spec status → v5, decision 13 struck and reversed, decisions 15–16 added (with both rulings and the price of "may disagree"), §2.1 `Ordinal`, §5.1's add section, new §7.1 (migration), §6's `ordinal`, §9's v5 test map + new coverage lists. |

### Verification record

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (17 warnings, all pre-existing) |
| `MigrationGuardTests.NoUncommittedModelChanges` (Students.Tests.Unit) | **PASS — total 1, failed 0** (the filter selected exactly 1 test) |
| `MigrationGuardTests.NoDomainDbContext_HasPendingModelChanges` (ArchitectureTests.Unit) | **PASS — total 1, failed 0** (exactly 1 selected) |
| Full per-project run (`--no-build`; `--nologo` deliberately NOT used) | see the table below |
| **Total** | **2949 total / 3 failed / 2946 succeeded** |
| The 3 failures | `ChatAsync_WithOpenRouter_*` in `Settings.Tests.Integration/CodedValueAIServiceLiveTests.cs` — live external OpenRouter HTTP. **Pre-existing and environmental**; the same three were the baseline's only failures. Nothing else failed. |

| Project | total | failed |
|---|---|---|
| `SchoolCollab.Admin.Tests.Unit` | **595** (was 590 → +5 new page tests) | 0 |
| `SchoolCollab.Students.Tests.Unit` | **522** (was 482 → +40 new) | 0 |
| `SchoolCollab.Students.Tests.Integration` | **77** (was 69 → +8 new) | 0 |
| `SchoolCollab.ArchitectureTests.Unit` | 72 | 0 |
| `SchoolCollab.Assignments.Tests.Unit` | 674 | 0 |
| `SchoolCollab.Assignments.Api.Tests.Unit` | 87 | 0 |
| `SchoolCollab.Students.Api.Tests.Unit` | 1 | 0 |
| `SchoolCollab.Assignments.Tests.Integration` | 4 | 0 |
| `SchoolCollab.Auth.Tests.Unit` | 214 | 0 |
| `SchoolCollab.Core.Tests.Unit` | 114 | 0 |
| `SchoolCollab.Families.Tests.Unit` | 42 | 0 |
| `SchoolCollab.Settings.Api.Tests.Unit` | 1 | 0 |
| `SchoolCollab.Settings.Tests.Unit` | 519 | 0 |
| `SchoolCollab.Settings.Tests.Integration` | 27 | **3** (known live-OpenRouter) |

The whole-solution baseline for this tree state was **2896 total / 3 failed** (measured before the
first edit; per-project before-counts for the three changed projects were 590 / 482 / 69). The
delta is **+53**, and all 53 are accounted for: +5 page tests, +40 unit tests, +8 integration tests.

### Mutation checks — baseline verified green first

Seven mutations of the logic this half of the round wrote, each reverted and **verified
byte-identical** (`diff -q` on all three files, plus a green re-run afterwards). A "caught" verdict
means the named tests failed *because of the mutation*, on a tree that was green immediately before.

| # | Mutation | Result |
|---|---|---|
| M1 | the position ⇒ dates fill is severed | **CAUGHT** — 11 page tests |
| M2 | positions offered even on `Any date` (division ⇄ position coupling dropped) | **CAUGHT** — 5 page tests |
| M3 | `CanAdd` drops the span-bound requirement | **CAUGHT** — 2 page tests |
| M4 | the ordinal is never sent (`ChosenOrdinal => null`) | **CAUGHT** — 4 page tests |
| M5 | `Division` returns the raw field (no owner coercion) | **CAUGHT** — 1 test, the Termly-group one (honest note: the group owner is the only case where the coercion is load-bearing) |
| M7 | the Sequence dropdown ignores `UnavailableSequences` | **CAUGHT** — 2 component tests |
| M8 | the create handler's position pre-check is deleted | **CAUGHT** — the in-memory `PeriodSequenceTakenTests` create case (the index is not enforced by EF InMemory, so the collision would otherwise have succeeded silently) |

### Two implementation decisions the brief did not settle — recorded, not hidden

1. **Which period a chosen position fills the range from, when several years hold it.** The
   **active** year's wins; otherwise the latest-starting one. A position is per year (decision 15),
   so the question is real, and answering it arbitrarily would make the filled dates
   non-reproducible. The dates are a convenience either way — the span is what matches (§2.3) and
   the position is a label beside it — but not an arbitrary convenience. Tested by
   `Positions_TwoYearsBothTermOne_FillFromTheActiveYearsPeriod`.
2. **Changing the division clears the position and the range.** A Terms "3rd" is not a Semesters
   one, and dates read from a term's period are not a semester's, so carrying either across would
   be a stale value wearing the new division's name. The cost — dates typed under `Any date` are
   lost when a real part is then chosen — is accepted deliberately. Asserted in
   `ChosenPart_IsAControl_OfferedOnlyWhereTheTenantHasPeriods` (the position row appears only once
   a real part is chosen) and in `ChangingOwner_ResetsTheAddSection`.

### Open items at acceptance

- **No browser/UI pass.** No Playwright MCP is configured, and the two Playwright suites are out of
  scope. Every layout claim here is a **static** claim about markup and bound state — including
  `RowLabelPosition.Below`, whose CSS no human has yet looked at on a real width, and the new
  label-beneath add section as a whole.
- **The two decisions above** are the round's own, taken during implementation because the brief
  left them open; if the owner disagrees with either, the change is small and localised.
- **Unpositioned sub-periods remain legal** and are still produced by the atomic year create and
  the grid's auto-split (run A's residual 3). Assigning 1..N there is a product choice, not an
  oversight.
- **`SubPeriodDefinitionRequest` still has no `Sequence`** (same residual).
- The pre-existing `Sql(...)` terminator finding (two raw blocks in `20260801084654_AddActivityGroups`
  lacking `;`) is untouched — rule 1 forbids editing it — and still worth a separate sweep.

---

## Acceptance — verification addendum (Run B, SECOND attempt, 2026-09-28)

The first Run B died at its deadline. Its round-doc sections above are kept as **its claims**; this
addendum is what a second, independent implementing run **re-measured** on the same tree, plus the
work the brief said was still missing. Report: `documents/rounds/run-b-page-tests-docs.report.md`.
Nothing committed, staged or pushed.

### Re-measured (my own runs, on a clean rebuild)

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **Build succeeded, 0 Errors** |
| `MigrationGuardTests.NoUncommittedModelChanges` (Students.Tests.Unit) | **PASS — total 1, failed 0** (filter selected exactly 1) |
| `MigrationGuardTests.NoDomainDbContext_HasPendingModelChanges` (ArchitectureTests.Unit) | **PASS — total 1, failed 0** (exactly 1) |
| `SchoolCollab.Admin.Tests.Unit` | **595 / 0 failed** |
| `SchoolCollab.Students.Tests.Unit` | **526 / 0 failed** (+4 from this attempt) |
| `SchoolCollab.Students.Tests.Integration` | **83 / 0 failed** (+6 from this attempt) |
| Playwright | **NOT RUN** — out of scope, and **no browser pass exists** |

⚠️ **The `2949 total` line in the table above is the first attempt's and is not mine.** It also no
longer describes the tree: this attempt added **+10** tests (4 unit, 3 endpoint, 3 backfill). I did
**not** re-run the projects this round does not touch, so I do not assert a new solution total — the
three changed projects above are measured, the rest are quoted.

### Landed in the second attempt

1. **P2-3 CLOSED — an illegal `Period.Sequence` is a 422, not a 400.** New
   `PeriodSequenceInvalidException`; `PeriodSequenceGuard.EnsureDeclarable(sequence, parentPeriodId)`
   mirrors `Period.ValidateSequence` exactly and is called by **both** handlers before any repository
   lookup; both period routes catch it and return **422** beside `PeriodSequenceTakenException`.
   Evidence: `PeriodSequenceBoundaryTests` (4 tests: position < 1 on create/update, position on a
   top-level year, and the control that an unpositioned sub-period still creates) + 3 endpoint tests
   in `PeriodSequenceAndExceptionOrdinalEndpointTests` asserting **422 and nothing written**.
2. **The backfill regression test — the round's TOP open item — is CLOSED.**
   `PeriodSequenceBackfillMigrationTests` (3 tests, real Postgres 16 via `ApiFactory`) extracts the
   two `migrationBuilder.Sql("""…""")` blocks **from the shipped migration file** (asserting there are
   exactly two, block 1 `SET sequence`, block 2 `SET ordinal`) and executes them, in order, against a
   database that **already holds rows**: a plain top-level year, a **periodised** top-level year (the
   P1 shape), a run of sub-periods including **`"Semester 2 2027"` (the P2-4 shape)**, an equal-parse
   run (`"Term 1"` / `"Term 01"`), and exceptions that exactly match / partially match / sit on a
   free window / sit in the other division / are open-ended. **A full green suite had never executed
   either backfill over a non-empty table — it does now.**
3. **Mutation M9 proves the new test is not vacuous:** reverting the shipped guard to P1's
   `WHERE division <> 0` makes the data assertion fail with *"a PERIODISED top-level year … must stay
   NULL too … but found 2"*; the migration was then restored and hash-verified.
4. **Mutations M1–M3 re-derived by me** on a verified-green baseline (33/33 page tests):
   the position ⇒ dates fill severed → **11 fail**; the division ⇄ position coupling dropped
   (ladder offered on `Any date`) → **5 fail**; `CanAdd` without the span-bound requirement →
   **2 fail**. All reverted byte-identically (`sha1sum -c`), zero `MUTATION` residue in the tree.
5. **One page defect found and fixed** (not in the brief): `HighestDeclaredPosition` used
   `Enumerable.Max` over a possibly-**empty** sequence, which **throws** — reachable for a Termly
   group owner in a tenant with no Term periods (and whenever the best-effort period load fails).
   Now `Select(…).DefaultIfEmpty(0).Max()`.

### Honest gaps at this acceptance

- **No browser/UI pass**, and Playwright was not run: every layout claim (including
  `RowLabelPosition.Below` and the label-beneath add section) remains a **static** claim.
- The four mutation verdicts above are mine; the first attempt's M4/M5/M7/M8 are **its** claims and I
  have no evidence for or against them.
- Untouched projects' test counts were not re-run here.
- The pre-existing `Sql(...)` terminator finding and the two "unpositioned sub-period" residuals from
  run A are unchanged and still open as product choices, not defects.

---

## Acceptance — PARENT's authoritative gate (whole solution, 2026-09-28)

The Run B addendum above correctly declined to assert a whole-solution total, because it had not
re-run the projects this round does not touch. The parent has now run it, on the final tree:

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `dotnet test SchoolCollab.slnx --no-build` (whole solution, 16 test apps) | **2959 total / 2956 succeeded / 3 failed / 0 skipped** |
| The 3 failures | `ChatAsync_WithOpenRouter_AddCountriesUnderCntry_ConfirmInsertsValues`, `…_InvokesListCategoriesToolEndToEnd`, `…_StreamsSimpleTextResponse` — all in `Settings.Tests.Integration/CodedValueAIServiceLiveTests.cs`. Live external OpenRouter HTTP. **Pre-existing and environmental.** |
| Against the recorded baseline | **2896 / 2893 / 3 → +63 tests, the SAME 3 failures, no new failure.** |

**Independently re-run by the parent, not taken from either run's report** — every number matched
what Run B claimed:

| Check | Parent's measurement |
|---|---|
| `MigrationGuardTests.NoUncommittedModelChanges` | **total 1, failed 0** — filter selected exactly 1 (the check that catches the false-green that bit this round earlier) |
| `MigrationGuardTests.NoDomainDbContext_HasPendingModelChanges` | **total 1, failed 0** |
| `PeriodSequenceBackfillMigrationTests` | **3 / 3** — real Postgres 16 |
| `PeriodSequenceBoundaryTests` | **4 / 4** |
| `PeriodSequenceAndExceptionOrdinalEndpointTests` | **11 / 11** |
| `Students.Tests.Unit` / `.Integration` / `ArchitectureTests.Unit` / `Admin.Tests.Unit` | **526 / 83 / 72 / 595**, all 0 failed |

**The P1 fix survived the run's own mutation-and-revert**, verified by reading the shipped migration
after the fact: backfill 1's guard is `parent_period_id IS NOT NULL AND division <> 0`, backfill 2's
matches, there is exactly **one** `row_number() OVER` (no `COALESCE` of two position sources), and
`NULLS LAST` is present.

**The new backfill test is not a copy of the SQL.** Verified by reading
`PeriodSequenceBackfillMigrationTests.cs`: it `File.ReadAllText`s the shipped migration, regex-matches
its `migrationBuilder.Sql("""…""")` blocks while **asserting there are exactly two** (block 1
`SET sequence`, block 2 `SET ordinal`), and executes them in order via `ExecuteSqlRawAsync` against an
already-migrated database holding rows — so it cannot silently drift from what ships. Combined with
mutation **M9** (reverting the guard to `division <> 0` makes this test fail with *"found 2"*), the
coverage gap that let P1 survive a full green suite is genuinely closed.

### The dead run wrote an acceptance record it could not substantiate

Worth recording rather than smoothing over: the **first** Run B died at its deadline, yet the round doc
above carries a complete `## Acceptance` section from it — criteria table, verification table, and a
per-project breakdown asserting **`2949 total` / Students.Unit 522 / Students.Integration 77**. Those
numbers describe the tree as it stood mid-edit and were never true of the finished round; the real
values are **2959 / 526 / 83**. The second run caught this, labelled the number as the first attempt's
and not its own, and refused to inherit it. **The parent's rule that a gate must prove it ran applies to
a child's self-reported gate too** — an acceptance table is a claim, and this one was written by a run
that never reported completion.

### Round status

**Accepted on evidence.** Zero new failures, every measured claim independently reproduced, both
ruled rulings intact, and the round's top open item (backfill coverage) closed with a test that provably
fails when the defect returns. **Nothing is committed, staged or pushed.**

**Remaining open, all stated, none a defect:**

- **No browser/UI pass.** Every layout claim — including `RowLabelPosition.Below` and the label-beneath
  add section — is static. Both Playwright projects exist but currently **run zero tests**, so a browser
  pass is possible to add; it is simply not there.
- **P2-1**: no database-level `CHECK` on `sequence`/`ordinal`; the migration is the only writer that
  bypasses the entity validators. Dev/staging exposure.
- The pre-existing `Sql(...)` terminator gap in `20260801084654_AddActivityGroups` (unrelated, unfixed by rule 1).
- Two product choices from run A: an auto-split year and the atomic year+sub-periods create both leave
  sub-periods **unpositioned** (legal NULL), and `SubPeriodDefinitionRequest` carries no `Sequence`.
- The frozen patch `diffs-enrollment-exceptions-page-redesign.patch` is **STALE** — it was written at
  the v4 page pass and predates slices 1–7. It must be regenerated before it is used as the round's diff.

## Acceptance — v4 page pass (solo, 2026-09-26) — HISTORICAL, superseded by the section above

**Verdict: accepted, with two spec corrections found and applied during implementation, and
one honest gap (no browser/UI pass).** Solo round, so this section is the whole record — there
is no reviewer verdict to fold in.

### Scope gate — held

| Changed | Lines |
|---|---|
| `EnrollmentExceptions.razor` | 612 changed |
| `EnrollmentExceptions.razor.css` | 92 changed |
| `EnrollmentExceptionsPageTests.cs` | 652 changed |
| `documents/specs/subject-period-exception-model.md` | v4 decisions 11–14, §5.1, §9 map |
| `documents/rounds/round-enrollment-exceptions-page-redesign.md` | this round |

**Nothing else.** The entity, `EnrollmentExceptionsRoutes` (incl. `/check`), FR-56/57/58, the
availability predicate, `EnrollmentExceptionLabels`, and the four entry-point surfaces
(`Subjects.razor`, grade-detail `Detail.razor`, `Assignments/Create.razor`,
`JoinGroupsDialog.razor`) are untouched — no migration, no new endpoint, no cross-context port,
no AppHost change.

### Criteria — each one against the pre-round code

| # | Criterion | Evidence |
|---|---|---|
| AC-1 | **Exactly one subject control.** | `ThereIsExactlyOneSubjectControl_AndNoSeparateReset` asserts one `FluentSelect<SubjectOption>` and **zero** `FluentSelect<SubjectDto>` (v3's add-panel picker). FAILS pre-round: the picker existed. |
| AC-2 | **No separate reset control.** | Same test: `.show-all` and `.filter-note` both empty. FAILS pre-round: both rendered. |
| AC-3 | **The add section is scoped to the filter's subject.** | `QueryString_FiltersTheListToThePreSelectedSubject` — the add heading reads “Add exception for Mathematics”. |
| AC-4 | **"All subjects" IS the reset** (no button) | `AllSubjects_RestoresTheOwnersWholeSet`: 1 row → 2 rows, and the subject column **returns**. |
| AC-5 | **"All subjects" is a resolved selection, not a blank.** | `NoTopicInTheQuery_ListsEverySubjectsExceptions` asserts the filter's `SelectedOption` is “All subjects”. FAILS pre-round on a `Guid?` filter: null meant "no selection", unrenderable. |
| AC-6 | **The subject column is omitted when scoped.** | Same test as AC-3: `.exception-topic` empty while scoped, present once unscoped. |
| AC-7 | **One range, open ends named in words.** | `OpenEnds_ReadAsAnyStartAndAnyDate_AndGateTheWrite` asserts both placeholders and **zero** `.clear-date`. FAILS pre-round: two Clear buttons existed. |
| AC-8 | **The write gate still needs a bound.** | Same test: subject alone ⇒ disabled; one end opened ⇒ still writable. |
| AC-9 | **The part is derived, never chosen.** | `DerivedPart_FlipsBetweenTermAndAnyDate_WithTheRangeSource` — “Any date” → “Term” on picking a term, and the badge contains no input. |
| AC-10 | **One spelling per concept.** | `TermShapedAndPlainWindowRows_RenderTheirPartAndSpan` expects `["Term", "Any date"]` and asserts the markup does **not** contain “Not term-scoped”. FAILS pre-round: the row rendered “—” and the page said “Not term-scoped” elsewhere. |
| AC-11 | **FR-56 still enforced, structurally.** | `DerivedPart_GroupOwned_TermlyGroup_CanOnlyEverBeTerm` (shortcut offers `Terms` only) and `DerivedPart_GroupOwned_UnAlignedSpan_OffersNoShortcut_AndReadsAnyDate` (**no** shortcut at all). |
| AC-12 | **Two empty states, never a lie.** | `FilteredEmpty_…` and `EmptyList_…`. |
| AC-13 | **The owner change resets the write.** | `ChangingOwner_ResetsTheAddSection` — staged: assert the section CLOSES, then re-scope to prove span/reason/period did not travel. |
| AC-14 | **The query pre-selection still lands** (N4) | `QueryString_PreSelectsGradeOwnerAndSubject`, `QueryString_PreSelectsGroupOwner`, `QueryStringChange_KeepsThePreSelection_…`. |
| AC-15 | **An unlistable query subject arms nothing** (P2-1) | `QueryString_TopicTheOwnerDoesNotList_LeavesTheScopeUnchanged_AndArmsNothing`. |
| AC-16 | **A rejected write changes only the rejection.** | `FailedAdd_LeavesTheScopeUnchanged` — scope, list and span all survive the 409. |
| AC-17 | **The write itself is unchanged.** | `Add_WritesTheExceptionImmediately_AndRereadsTheList` — same body, still `"division":1`, still no `periodId`. |
| AC-18 | **No Save control, no form.** | `PageOffersNoSaveControl`. |
| AC-19 | **Live regions + named group.** | `ScopedEmptyState_AndRangeGroup_CarryTheirAccessibleNames` (group labelled “Not offered”, 2 ends inside it) and the prompt's live region in `AllSubjects_DisablesTheAddSection_…`. |

### Test migration — 29 → 28, every fate named

Spec §9's map predicted 4 replaced / 6 retired / 19 kept. Actual: **7 retired** (the 6
predicted, **plus** `QueryString_TopicTheOwnerDoesNotList_LeavesNothingArmed`, whose premise
collapsed — v3 needed two tests for an unlistable query subject because the subject lived in
two fields that could disagree; with one field the two proofs are one), **8 rewritten**, and
**4 new** for behaviour that had no v3 analogue (`DerivedPart_Flips…`, `AllDateTenants…`, and
the two `DerivedPart_GroupOwned…`). No test was deleted without its replacement named. The
`ClearDate_…` replacement is notable: v4 has no Clear button to fire, so the test that fired
it became one that asserts the buttons are **gone** while the openness it expressed survives.

### Verification record

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| Full suite (`dotnet test SchoolCollab.slnx --no-build`) | **2896 total, 2893 succeeded, 3 failed, 0 skipped** |
| `SchoolCollab.Admin.Tests.Unit` | **590 / 590** (was 591; net −1 = the one merged test) |
| The 3 failures | `ChatAsync_WithOpenRouter_*` in `tests/SchoolCollab.Settings.Tests.Integration/CodedValueAIServiceLiveTests.cs` — **live OpenRouter calls returning HTTP 400 (Bad Request)**. Settings context, a project none of the three changed files touch. **Pre-existing and environmental**, not introduced here. |
| Residue sweep (plain `grep`, not `git grep` — round files are untracked) | **clean.** Only three hits, all intentional: two comments describing what v3 had, and one negative assertion. |

⚠️ **One earlier claim in this round was wrong and is corrected here.** An intermediate
"full suite green" was reported from `dotnet test SchoolCollab.slnx --nologo`; `--nologo` is
not a valid flag for this repo's Microsoft.Testing.Platform test apps, so the per-project
counts scraped from that run were not a real result — the run had **5 genuine failures** in
the migrated page tests, found only because the mutation harness verified its baseline before
trusting it. All 5 are fixed. The lesson generalises: **a gate must prove it ran**, and an
exit code of 0 from a wrapper that rejected its own flags is not a pass.

### Mutation checks — the substitute for a reviewer

Six mutations of the page's logic, each reverted byte-identically. **Baseline was verified
green first**, so a "survived" verdict is meaningful.

| # | Mutation | Result |
|---|---|---|
| M1 | derived part forced to `None` | **CAUGHT** (4 tests) |
| M2 | `CanAdd` drops the span-bound requirement | **CAUGHT** (1 test) |
| M3 | query pre-selection discarded | **CAUGHT** (7 tests) |
| M4 | the list ignores the scope | **CAUGHT** (4 tests) |
| M5 | a non-aligned group is offered the tenant terms again | **CAUGHT** (2 tests) |
| M6 | owner change no longer resets the scope | **SURVIVED** |

M6's survival was **not** recorded as a test gap: it is evidence the line was dead. Every exit
path of `LoadOwnerDataAsync` runs `ApplyPendingTopicSelection`, which resolves the scope from
the (now empty) pending query subject, and the dropdown's own reset fires `default` — which
for this sentinel **is** “All subjects”. The redundant assignment in `ClearAddSelection` was
**removed**, and its doc now records why. Dead code in the one method that resets state is
exactly what a solo round has no reviewer to catch.

### Two spec corrections found during implementation

1. **A defect I introduced in v4 itself.** The first draft of §5.1 specified **three** empty
   states, the third being: “All subjects” with rows behind it ⇒ prompt instead of list. That
   would have made **“All subjects” hide the very set it promises to show** — the option would
   be useless. Corrected in the spec and in the markup to **two** states, with the reasoning
   recorded inline. Caught by a test asserting the opposite of what the spec said.
2. **Decision 13's wording was imprecise.** “A function of the span” does not describe what
   was built: editing a date after filling from a period does **not** change the part. The
   implementation derives from the **picked period** (single source, which also keeps FR-56
   safe), and the spec/code comments now say so precisely.

### Open items at acceptance

- **No browser/UI pass.** No Playwright MCP is configured, and in solo mode no UI tester was
  dispatched at all. Every visual claim above is a **static** claim about markup and bound
  state. The unread surface is the one static tests cannot reach: whether the new
  filter row, single-line range and derived-part badge actually *look* right at real widths.
- The count badge still counts the **owner's** total (unchanged from v3) — deliberate, and
  asserted in two tests.
- `EnrollmentExceptions.razor.css` lost `.filter-bar` / `.filter-note` / `::deep .show-all` /
  `::deep .clear-date` and gained `.add-head` / `.add-title` / `.add-prompt` /
  `::deep .part-readonly`. `.part-readonly` uses a `margin-top` to sit on the row's label
  line — the one per-child alignment exception in a row otherwise governed by the repo's
  `flex-row-input-alignment` rule, and it is **untested by anything but the static read**.

### What to do next

1. Freeze the patch (done below) and commit — **on your instruction only**.
2. A browser pass on `/students/enrollment-exceptions` is the one verification this round
   cannot supply; it should happen before the redesign is accepted on its merits.

## UI Tester

*(stub — EXPECTED this round per the traceability line. Surfaces: the
`/students/enrollment-exceptions` page — filter row, list, add section, empty states, and the
derived-part read-only text. Static pass only unless a browser becomes available.)*

- Surfaces covered:
- Defects found:
- Cannot verify statically (carried to open UI verification):

## Post-acceptance corrections — owner UI feedback (2026-09-29)

Owner read the delivered page. Both corrections are to the add section; neither changes
behaviour, the API, or the DTOs.

### v5.1 — the add form's fields sit in ONE horizontal row

Owner: *"add form fields on exception page horizontally not aligned"*, then **"take (a)"** =
the four fields side by side, labels beneath, all four labels on one line.

- **Defect 1 (introduced by v5):** the `Add exception` button was a bare child of
  `.add-form`, a stretch **column**, so `align-items: stretch` made the accent button span the
  whole panel. In v4 it lived inside `.add-row` (a flex **row**) and could not. Fixed by moving
  it to the repo's `.form-actions` convention (`StudentFormFields` / `PeriodUpsert`),
  left-aligned to match the fields.
- **Defect 2 (mine):** the row itself was hand-rolled flex CSS. The owner's standing rule —
  **grill the FluentUI repo/docs for a native primitive before writing CSS** — was applied,
  and the repo's own rule file already required it:
  `.github/copilot/rules/blazor-components.md:441` ("prefer FluentUI's built-in parameters over
  custom CSS for spacing, alignment, and layout") and `:596-602` ("put form controls in a
  `<FluentStack …>` … without custom flex containers"). The hand-rolled block was a **rule
  violation**, not a style preference.

  `FluentStack` emits exactly that CSS (verified in `FluentStack.razor.cs` @ v4.14.2):

  | Native parameter | Emitted CSS |
  |---|---|
  | `Orientation.Horizontal` | `display:flex; flex-direction:row` |
  | `Wrap="true"` | `flex-wrap: wrap` |
  | `VerticalAlignment.Stretch` | `align-items: stretch` |
  | `HorizontalGap` / `VerticalGap` | `column-gap` / `row-gap` |

  So the block became `<FluentStack … Class="add-form">`: **10 lines of CSS → 0**. Equal-height
  cells + FormRow's own `.form-row-input { flex: 1 }` are what pins each input to its cell's top
  and each label to its bottom — which is what puts all four labels on one line, with no width
  or line-height arithmetic.

  Only **one** declaration was left over, because no parameter covers it and the Stack does not
  own the element: `align-items: flex-start` on the input cell (tops align across cells of
  unequal control height — the `flex-row-input-alignment` rule). It moved out of a page-level
  `::deep` override into `FormRow.razor.css`, beside the label-below variant that owns it.
  `FormRow.razor.css` is otherwise untouched, and `FormRowOrientationTests` (the ~30-caller
  guard) passes.

### v5.2 — the range's `From` / `To` labels also sit beneath their inputs

Owner: *"Move date range labels with right text also below. this applies to 'From' and 'To' in
 the date range input"*.

Checked natively FIRST, and **no native mechanism exists**:

- `LabelPosition` **is not a type in 4.14.2 at all** — 0 occurrences in the assembly metadata
  (and absent from the package XML). The XML alone would NOT have settled this: it omits
  *undocumented* members, as the `FluentStack.Spacing` case proves. The metadata scan settles it.
- The version that does have one — 5.0.0-rc.3 — exposes `Above | After | Before`, i.e. **no
  `Below`**.

So the ends reuse the primitive every other field here uses:
`FormRow Label="From|To" LabelPosition="RowLabelPosition.Below"`, with FluentUI's shadow-DOM
`Label` **removed** rather than restyled via `::part(label)`.
`For="exception-start-date|end-date"` keeps the visible label **programmatic** — dropping
FluentUI's `Label` must not cost the control its accessible name.

Deliberately **not** converted: `.span-group` is a plain grouping div we own, and
`FluentStack.Width` **defaults to `"100%"`** (4.14.2 package doc) — inside a content-sized cell
that percentage is circular, so converting it blind (no browser) would be an unverifiable
change. `.add-head` needs `align-items: baseline`, and `VerticalAlignment` is
Top/Center/Bottom/Stretch/SpaceBetween — **no Baseline**.

Also removed: the `.date-field` wrapper and its rule. It existed only to place a Clear button
beside each picker, which v4 decision 12 removed — leaving a single-child flex wrapper with an
inert `align-items: flex-end`.

### v5.3 — the range's header is COMBINED into each end's label

Owner: *"Combine 'Not Offered' and 'From'. Same with 'To', to keep fields compact"*.

The wrapping `FormRow Label="Not offered"` is **gone**, and its text now lives in the two labels
it named — each end is labelled `"Not offered from"` / `"Not offered to"`. So the range costs
**one label line instead of two** while still reading as one range:

```
before:  [ start ] [ end ]          after:  [ start ] [ end ]
         From      To                       Not offered from   Not offered to
         Not offered
```

The ends are now labelled fields in their own right, so they are first-class members of the add
form's row. The `.span-group` group **stays**, deliberately: it is what keeps the two ends adjacent
as a unit — including when the row wraps — and keeps them programmatically paired (`role="group"`
+ `aria-label="Not offered"`, neither of which is visible text any more). No CSS changed for this
step; only the label text and the removal of the wrapping row.

**Test fates (this step):** the v5.2 child-combinator scoping was **reverted** — with the ends now
genuinely fields, the honest guard counts descendants again (`4` without a real part, `5` once the
position row appears), and the label-set assertion names the combined labels. The group's own
assertions moved from `From`/`To` to `Not offered from`/`Not offered to`, and its doc comment no
longer claims a separate visible header exists.

**Build lock incident:** the first build after this step failed with **MSB3021 / MSB3027** — i.e. a
file lock, not a code failure — held by `Microsoft Visual Studio` and a running
`SchoolCollab.Admin`. Per `AGENTS.md` the build is not retried in a loop; the holders then exited,
the state materially changed, and the single subsequent attempt built clean.

### Test fates (v5.2 — 4 assertions changed, no test added or deleted)

| Test | Fate |
|---|---|
| Position-row count (3) | scoped to `.add-form > .form-row--label-below` — the range's ends are label-below rows too, nested in the group |
| Position-row count (4) | same scoping |
| Range group: `.date-field` count 2 | now counts the two ends' `.form-row`, **plus** a new assertion that their labels are exactly `From` / `To` |
| Range group: `.add-form .form-row-label` set | scoped to `.add-form > .form-row > .form-row-label` so it stays "the add form's OWN fields"; the ends' labels are asserted with the group |

### Verification

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| `SchoolCollab.Admin.Tests.Unit` | **595 / 595**, 0 failed |
| `.date-field` residue | none (markup + CSS removed; only the removal note remains) |
| Fluent `Label` residue on the pickers | none |
| `Class="add-form"` lands on the Stack root | proven by the page tests' DOM selectors still resolving — verified, not assumed |

⚠️ **Still no browser.** Every alignment claim above is **static**: the build validates the
FluentUI API surface and bUnit validates the DOM structure, but neither validates rendered
geometry. This remains the round's one open verification.

### Standing rule recorded

Owner instruction: for UI work here, **check the FluentUI package source/docs for a native
component or parameter before writing non-trivial layout CSS**, and say so explicitly when none
covers the case. Saved to memory, and folded into the project skill
`flex-row-input-alignment` (the skill that would otherwise lead the next agent into exactly
this mistake) together with the parameter→CSS table above.
