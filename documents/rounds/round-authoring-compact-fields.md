# Round — authoring-compact-fields

Provider: pi (models: parent plan, deepseek-v4.1-flash worker, kimi-k2.7-code reviewer — REVIEWER GATE SKIPPED, see Review)
Tier: 2 light (closed with a REDUCED gate set — see Review)
Base: HEAD 870b4b0 (tree DIRTY — the whole assignment-authoring feature is uncommitted on `feature/assignment-authoring-targets-and-rules`); this round's diff is isolated by the full-tree snapshot `School-Collab-round-base-authoring-compact-fields.tar` (2672 members), NOT by `git diff` against HEAD.
Status: CLOSED — pre-commit close: the round is committed nowhere yet (no commit, no PR, no push).

> **Round-start defect (recorded, parent's own).** The parent's round-start bash chained
> `tar && git diff > base-names && rev-parse > base-sha && cat > round-doc`. The `tar` leg exited
> non-zero (one index entry is deleted-but-unstaged, so it cannot be stat'd), which stopped the `&&`
> chain — so all four files failed to write while the snapshot itself was created. This round doc,
> `.base-names.txt` and `.base-sha.txt` were written afterwards by the parent; the worker recovered
> the plan text verbatim from the session transcript. Lesson: never chain a writer onto a tar/`&&`
> leg whose exit status can fail for a benign reason.

## Plan

### Goal
1. The **Rules** block becomes a `SectionCard` stacked in the right-hand column directly beneath the Targets & audience card.
2. Short fields are **paired inline** to shorten the form.

### Scope IN
- `AssignmentAuthoring.razor` — Rules out of the spine into the right column as `<SectionCard Title="Rules" ShowAddButton="false">` with the five readouts as `ItemTemplate` items (the `#authoring-rules` anchor and the five readout ids preserved); three field pairs, each ONE multi-input `FormRow` (`AlignTop` + `LabelPosition="Below"`): **(Assignment type · Grading format)**, **(Max score · Max attempts)**, **(Status · Available from)**.
- `AssignmentAuthoring.razor.css` — the right-column card-stack block only; no inline styles.
- `ScoringFieldsSection.razor` — OD1 only.
- Tests: `AssignmentAuthoringBunitTests.cs`, `ScoringFieldsSectionBunitTests.cs`, and any guard the approved direction invalidates.

### Scope OUT
No bound value, command, contract, schema or policy-readout value changes. No new component or CSS primitive. No other page/dialog/project (this is why `SectionCard` itself is out of scope — see the backlog line).

### Open decisions (settled)
OD1 — pairing Max score · Max attempts requires the attempt-cap control to leave `ScoringFieldsSection`. Adopted: move it into the Basics pair, leaving the section holding the pass score with its disabled-with-reason behaviour unchanged. OD2 — the right column is 320–460px and each readout is label + value + badge + optional link. Adopted: keep all five as card items and let the shared row chrome wrap; no compact row component, no truncation.

### Acceptance criteria
AC1 Rules renders through `SectionCard` (no `<h3>Rules</h3>`, no `authoring-compartment` section) inside the right-hand column, following the Targets card. AC2 the Rules card renders no Add button and all five readout ids still render. AC3 each of the three pairs is exactly ONE `FormRow` holding BOTH controls, top-aligned. AC4 regression guard: bindings and `authoring-basics-*` ids unchanged. AC5 the jump-nav still enumerates five entries and the Rules link resolves to the card. AC6 no inline `style=`/`<style>` in the touched files and the architecture guards pass.

## Worker Report (deepseek-v4.1-flash)

Implemented per plan. Changed: `AssignmentAuthoring.razor` (Rules card + three pairs + `PolicyReadout`/`PolicyReadouts` + `NoAddActionAsync`; stale Add-comment fixed), `AssignmentAuthoring.razor.css` (`.authoring-rules-card`), `ScoringFieldsSection.razor` (attempt-cap row out; `IsScoringInapplicable` exposed; hint reworded), `AssignmentAuthoringBunitTests.cs` (guards re-pointed; 5 new AC tests), `ScoringFieldsSectionBunitTests.cs` (1-field assertions + gate test), `AssignmentDetailBunitTests.cs` (one text-proxy assertion re-pointed).

Worker-reported numbers (NOT evidence): build 0 errors, no new warning; Assignments 915/0; Architecture 102/0.

Deviations, all adjudicated ACCEPTED by the parent:
1. Round doc absent at round start (parent defect, above).
2. `OnAddClick` `EditorRequired` + RZ2012 ignoring `ShowAddButton` — resolved per the parent's supervisor ruling: **documented inert `NoAddActionAsync`**; `SectionCard` untouched.
3. AC1 placement read as "the next card in the column", i.e. Rules follows the Targets card AND that card's own supporting readouts (UX-21 disabled replacement, TGT-16 preview). Accepted: inserting Rules between a card and its own preview would separate the preview from the card it describes.
4. `Count="@PolicyReadouts.Length"` — the shared header always renders its badge, so omitting it would show "0". Accepted.
5. Two bUnit guards re-pointed (`CompartmentTitles` → jump-nav anchors, now also asserting every link resolves; the Targets-title guard → per-card equality including the new stacking order). Acceptable as *selector corrections*, not weakenings — the assertions pin equal-or-stronger contracts.
6. OD1 ripples: hint text + the section's own suite (2 fields → 1); the disabled attempt cap keeps its gate via the now-public predicate; the shared reason paragraph is unchanged.
7. Paired selects `full-width` → `Class="w-6"` (the `input-width-scale` step); number fields keep `w-3`. No new CSS for the pairs.
8. Rules card `EmptyMessage` dropped (unreachable: five entries always).

### Parent verification (authoritative, replaces every child number)

`dotnet build SchoolCollab.slnx` - **succeeded, 0 errors**, and **0** `RZ2012` warnings anywhere in the solution
(the `NoAddActionAsync` route silenced the one the component contract would otherwise emit).
`dotnet test tests/SchoolCollab.Assignments.Tests.Unit` - **915 total, 0 failed**. `dotnet test
tests/SchoolCollab.ArchitectureTests.Unit` - **102 total, 0 failed**.

### Residual risks

- **No visual pass.** CSS-only outcomes are unverified in a browser: the Rules card rhythm inside the
  320-460px column, the "5" header badge, and the single label-below caption over a two-control pair.
- **One caption, two controls (residual P2, accepted).** A pair renders ONE `LabelPosition="Below"` caption
  naming both fields, so the second control has no individual `<label for>`. This is the approved mechanism,
  not an oversight; a follow-up should give each paired control its own accessible name if a UI tester confirms
  it reads poorly.
- The shared `.authoring-policy-row` wraps rather than truncating the readouts in the narrow column (OD2 as settled).

### Backlog (recorded, not fixed)

`SectionCard` declares `OnAddClick` as `[EditorRequired]` while `ShowAddButton` (default `true`) plus the
component own runtime null-guard both imply the callback is optional. The `RZ2012` analyzer cannot see the
render condition, so any readouts-only card is forced to pass a documented no-op — this page now carries one.
Correcting the attribute is a one-line change to `Students.Application` and belongs in a round whose scope
includes that project.

## Review

**REDUCED GATE SET — the independent static diff reviewer was deliberately SKIPPED.** Rationale, agreed with the owner before the worker returned: this is a cosmetic, UI-only change (card restyle + three field pairs) on a 6-file delta, and Tier 2's independent diff reviewer has the least marginal value on pure layout. The parent performed the scope-check against AC1–AC6 directly and recorded the deviation below. **This is a deviation from Tier 2's letter** and is recorded as such; the UI-tester pass (step 6) was likewise not run, so the CSS-only outcomes in "Residual risks" are unverified visually. Cosmetic-only rounds should default to Tier 1 (per the skill's tier table and its "never pay for model round-trips a task does not need" rule).

Parent scope-check (each AC, from the diff and the markup itself):
- AC1 ✔ Rules is a `SectionCard` inside the right column, last in the column, after the Targets card and its readouts; no `<h3>` and no `authoring-compartment` wrapper for it.
- AC2 ✔ `ShowAddButton="false"` + `NoAddActionAsync` (documented inert); all five readout ids preserved in the `ItemTemplate`.
- AC3 ✔ three pairs, one `FormRow` each, `AlignTop` + `LabelPosition="Below"`.
- AC4 ✔ no binding, id or command touched; the attempt cap's *location* is the discriminating half.
- AC5 ✔ five jump-nav entries; the Rules link resolves to the card container.
- AC6 ✔ no `style=`/`<style>` in the touched files; CSS confined to the page's isolated stylesheet.

## Acceptance

Verdict: **CLOSED** with a reduced gate set (no independent reviewer, no UI tester). The owner accepted the reduced close in advance on the grounds that the change is cosmetic. AC1–AC6 satisfied by the parent's scope-check plus the parent's own build/test pass.

### Post-close rework (solo, 2026-10-07) - user-reported defect

The pair rows had been built with `LabelPosition="Below"`, the wrong mechanism: `Below` deliberately
DROPS the 180px label gutter (`FormRow.razor.css` sets the label `width:auto`) because it exists for a
NESTED cell - its own comment names the EnrollmentExceptions add form as its only use. At top level it
produced both reported symptoms: (1) the row's single label rendered under the input cell, i.e. under the
FIRST of the two controls; (2) each pair's controls began ~192px left of every single-field row's
controls, so the field line was out of sync.

Fix (parent, solo): dropped `LabelPosition="Below"` from the three pairs, keeping `AlignTop` - the
documented multi-input HORIZONTAL configuration, which retains the gutter; and gave the second control of
each control-pair its own accessible name (`aria-label` on the grading select and the max-attempts field),
closing the residual P2 this round had recorded. The AC3 test was re-pointed from the Below mechanism to
`form-row--horizontal` + `form-row--align-top` and now carries a `NotContain("form-row--label-below")`
guard so the regression cannot return.
