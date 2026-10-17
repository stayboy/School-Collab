Provider: pi — Light round (Tier 2): worker `ollama-cloud/deepseek-v4.1-flash`, static diff reviewer `ollama-cloud/kimi-k2.7-code`
Mode: attended (owner pinned Round B; delivery = stack layer on the Round A PR)
Tier: 2 (light) — scripted sweeps + one static diff-only review; no plan-review agent, no UI-tester leg of its own (the deferred UI-tester pass rides on this round's close, Q51)
Base: layer on the Round A branch `fluentui-v5` (Round A PR open); layer branch `s2-fluentui-v5-roundb`
Status: CLOSED — Round B complete and committed on the stack layer. Gates: non-incremental build 0 errors, `RZ10012` = 0, MAUI head 0/0, `dotnet test` 4093/4098 (2 opt-in live-AI failures), Admin 697/0; review had no blockers. Q53 (`FluentNav.Title`) ships open.

## Objective

Finish the FluentUI 4.14.2 → 5.0.1 upgrade for the surfaces Round A deliberately deferred: the five
**warning-only** rename families (each renders today as an inert HTML element with an `RZ10012`
warning), the **dialog merge** onto a single `DialogShell`, and the v5 **DOM/`::deep` selector audit**.
The end state: no v5-absent component remains in markup, one dialog shape across the repo, and the
repo-owned rules/skills describing that shape.

## Scope

**IN**
1. **Five rename families** — **measured at kickoff** (counts are *files carrying the tag in markup*; earlier estimates were larger and included comment/doc text, and several families were already converted by the Round A legs):

   | Family | Files | v5 target |
   |---|---|---|
   | `FluentProgressRing` | **48** | `FluentSpinner` (keeps `Stroke`) |
   | `FluentTextField` | **10** | `FluentTextInput` |
   | `FluentNavMenu` / `FluentNavLink` / `FluentNavGroup` | **1** (`Admin/Components/Layout/NavMenu.razor`) | `FluentNav` / `FluentNavItem` / `FluentNavCategory` |
   | `FluentBreadcrumb` / `FluentBreadcrumbItem` | **1** each | **no v5 replacement** — authoring, not a rename |
   | `FluentNumberField`, `FluentSearch` | **0** | already done |

   The machine signal for this worklist is **`RZ10012: Found markup element with unexpected name '<X>'`**, measured on a **non-incremental** build (`dotnet build SchoolCollab.slnx --no-incremental`) — at kickoff there are **94** such lines. An incremental build reports **0** of them because nothing recompiles, so the gate below must never be measured incrementally.
2. **The dialog merge** (Q38–Q42, mechanical): a `<DialogShell>` owning the **body wrapper +
   `dialog-container` + validator + `DialogShellFooter`**, taking the form as its child. 42 dialogs,
   six shapes → ~3 codemod buckets. `ReadOnlyDialogShell` stays separate (Q40). It must **not** own the
   `EditForm` (see OUT, item 1). Fused into the **same pass per file** as item 1 (Q41), so each dialog
   is touched once.
3. **v5 DOM/`::deep` selector audit**: v5 renamed tags (`fluent-select` → `fluent-dropdown`, tab markup,
   the `<fluent-field>` wrapper) and moved parts into shadow DOM, so selectors in the 40+ `.razor.css`
   files may now match nothing.
4. **Rule/skill updates in the same PR**: `dropdown-ui`, `fluentui-component-props`, `dialog-ui`,
   `blazor-components` — renames the tree has not performed yet are written as if already true today.

**OUT (explicitly, each with its trigger)**
1. **Form-ownership consolidation** (Q43–Q45): `DialogShell` declaring the one `EditForm`, the five
   `*FormFields` becoming form-free, and their ~10 page consumers declaring their own. Staged as its own
   leg behind per-surface validation tests, because a mistake there fails **silently**.
2. **The ~66 pre-existing inert attributes** that were already dead in v4.14.2 (Q37 backlog).
3. **The pre-existing `AiChat.razor` inert `Disabled`** (Q34).

## Gates (acceptance)

| Gate | Target |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| MAUI head — `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false` | 0 errors |
| `dotnet test` | 0 failures except the opt-in live-AI tests |
| `RZ10012` lines, measured on a **non-incremental** build (`--no-incremental`) | **0** (was 94) — this is the round's own proof that the rename families are done |
| Independent static diff review | no blocker |
| Deferred UI-tester pass (Q51) | Round A + B visible deltas bug-hunted, findings triaged |

## Worklist

_(filled in as legs complete — leg, model, outcome, numbers)_

## Findings

_(none yet)_

## Delivery

- **Layer branch:** `s2-fluentui-v5-roundb`, created off Round A's tip (`d2a4d8d1`) — Round B's work needs Round A's port, so it cannot branch from `main`.
- **Stack, not a plain PR.** `ci.yml` triggers only on a `pull_request` whose **base is `main`**, so an PR based on `fluentui-v5` would report "no checks reported" forever. Registering the layer with `gh stack` is what makes CI evaluate it against the stack root (`main`). Round A's PR alone stays a plain PR (it is based on `main`).
- **Blemish, disclosed (Round A's commit).** `documents/rounds/round-fluentui-v5-roundb.md` (this file) was written while Round A's change set was being staged, so it landed inside Round A's commit `d2a4d8d1`, and the frozen `diffs-fluentui-v5-rounda.patch` lags the commit by exactly that one file. Amending a pushed commit needs a force-push, which the merge policy forbids, so it is left to self-heal: Round B commits this same file, and the patch artifact stays the record of Round A's *code* delta.

## Findings

### F1 — `Immediate` / `ImmediateDelay` on `FluentTextInput` are VALID (leg B1's risk 1 was a false alarm)

B1 reported that `Immediate="true" ImmediateDelay="300"` on `LandingPage.razor` / `EntityGrid.razor` are "not v5
parameters of `FluentTextInput` (verified absent from the 5.0.1 XML)" and therefore inert. **The premise is wrong, and
the test that produced it is the trap the rule warns about.** The v5.0.1 source shows
`public partial class FluentTextInput : FluentInputImmediateBase<string?>, …` and `FluentInputImmediateBase<TValue>`
declares `[Parameter] public bool Immediate { get; set; }` and `[Parameter] public int ImmediateDelay { get; set; } = 200;`
— i.e. both are inherited parameters, and XML doc comments list only a type's **own** members, which is exactly why they
do not appear under `FluentTextInput` in the XML. Its base's summary describes the mode as "immediate validation mode
(using the `OnInput` event)", so per-keystroke behaviour is intact. **No change needed; no decision required.**
This is a live confirmation of the method recorded in `.github/copilot/rules/blazor-components.md` Step 1 (resolve the
inheritance chain — generic bases and the external `InputBase<TValue>` members are legitimate): reading the XML without
the chain produces a false "inert attribute" report, in either direction.

### F2 — `<FluentNav Title="…">` is a rename-induced regression (real, 1 line)

`src/SchoolCollab.Admin/Components/Layout/NavMenu.razor:9-10` renders
`<FluentNav Width="250" Title="@(_currentModule == "settings" ? "Settings" : … : "Navigation")">`. In **4.14.2 that Title
lived on `FluentNavMenu`** (`P:…FluentNavMenu.Title` — and `FluentNav` itself did not exist in v4), so the v4 value was a
real parameter. v5's `FluentNav` exposes no `Title`, so after leg B1's rename the computed label silently degrades to a
plain HTML `title` attribute via `AdditionalAttributes`. This is therefore **in scope for Round B** (the rename created
it), not backlog. v5's own label mechanisms: `FluentNavSectionHeader.Title` (a visible section header) or the native
`aria-label` (an accessible name for the navigation landmark, no visual change). Raised as Q53.

### F3 — the RZ10012 worklist is converging as predicted

Parent-measured after B1: **0 errors**, `RZ10012` = 8 lines = 4 unique sites, all in the three dialog files leg B2 owns
(`ActivityGroupEditDialog.razor:37`, `SegmentEditDialog.razor:82,83`, `StudentTransferDialog.razor:39`); the nav and
breadcrumb families are at **0** files. `FluentProgressRing` remains in 9 dialog files and `FluentTextField` in 4 — and
B1's correction stands (parent-confirmed: 22 `CS0618` obsolescence warnings): `FluentProgressRing` is an obsolete compat
shim that still renders `fluent-spinner`, so that rename is deprecation hygiene rather than a rendering fix.

### F4 — the approved dialog shape (Q38–Q42 + Q45) is self-contradictory — leg B2 stopped and escalated

Leg B2 refused to reshape the 42 dialogs and asked for the boundary, with two decisive facts:

1. **Validator and footer can only live inside the `EditForm`.** ASP.NET's `DataAnnotationsValidator.OnInitialized`
   throws `InvalidOperationException` without an ambient `EditContext`, and `DialogShellFooter`'s button is
   `Type="Submit"` with no `@onclick`. So a shell that owns "body wrapper + validator + footer" **must** own the form —
   which Q45 (and the brief, twice) forbids. "Mechanical merge now, form ownership later" is therefore impossible: the
   merge *is* the form-ownership change.
2. **`dialog-container` cannot move for free either.** Four dialogs carry **component-scoped** overrides on it that would
   silently stop applying once the element lives in `DialogShell` (the CSS-scope-attribute hazard `dialog-ui` §3 warns
   about): `CodedValueDialog.razor.css:5`, `AttributeDefinitionDialog.razor.css:2`,
   `NotificationPolicyFieldEditDialog.razor.css:4`, `TopicEditDialog.razor.css:1`; and `QuestionPromptDialog.razor:10`
   passes a second class (`dialog-container cq-prompt-dialog`) the shell would have to accept as a parameter.

B2 also noted the shapes reduce to a single mechanical change **only if** the shell is a pure wrapper — the (a)–(e)
census changes nothing per-file once it is. Held Part 1 pending the owner's decision (Q54); Parts 2 and 3 continue.

### Grill round Q54 — SETTLED (d): the dialog-shape work moves wholesale into the form-ownership leg

Owner confirmed option (d). **Consequences, recorded so the next leg inherits them intact:**

- **Round B is now mechanical only**: the rename families inside the dialogs (the 4 `RZ10012` sites,
  `FluentTextField` ×4, `FluentProgressRing` ×9) plus the breadcrumb assertions. No `DialogShell` is created in this
  round, and none of the 42 dialogs is reshaped — so they are touched **once**, not twice.
- **Q38–Q42 are superseded**, not cancelled: the dialog merge cannot be separated from form ownership (F4), so it
  merges into the Q43 leg, which becomes **"the dialog shape leg"**. Its scope, in one pass over the 42 dialogs:
  1. `DialogShell` owns `EditForm` **and** `dialog-container` **and** `DataAnnotationsValidator` **and**
     `DialogShellFooter`, taking the fields as its content (`Model` / `OnValidSubmit` as parameters).
  2. The five `*FormFields` components become form-free (fields against the ambient `EditContext`), and their ~10 page
     consumers declare their own `EditForm`.
  3. The four component-scoped `dialog-container` overrides (`CodedValueDialog.razor.css:5`,
     `AttributeDefinitionDialog.razor.css:2`, `NotificationPolicyFieldEditDialog.razor.css:4`,
     `TopicEditDialog.razor.css:1`) move to shared rules, and `QuestionPromptDialog`'s second class
     (`dialog-container cq-prompt-dialog`) becomes a shell parameter.
  4. `ReadOnlyDialogShell` stays separate; the architecture test moves from `<DialogShellBody>` to `<DialogShell`;
     `dialog-ui` is rewritten for the new shape.
  5. **Per-surface validation tests** (invalid submit blocked, valid submit reaches the API) — mandatory, because a
     mistake here fails silently and no green suite reports it (Q45).
  6. It is a **behavioural** leg, not a Tier-2 mechanical one: it should be planned as its own round with a plan review.

### Independent static review (`45729f31`) — no blockers, two majors, both actioned

The reviewer first **stopped and asked** rather than reviewing nothing (it has no git access and Round B's patch artifact
did not yet exist — the parent's omission; `diffs-fluentui-v5-roundb.patch` was written in response, 68 files, 0 new-file
entries, i.e. a complete capture). It then hunted the specific defect class that blocked Round A and found a fresh
instance:

1. **`NavMenu.razor:9-10` — `FluentNav.Title` is inert (= Q53).** Independently confirmed against the 5.0.1 XML: `FluentNav`
   declares `Width` and no `Title`; `FluentNavSectionHeader.Title` is v5's mechanism. Owner decision, still open.
2. **`LandingPageTests.SearchBox_Change_Fires_SearchTextChanged` was a vacuous placeholder** (leg B3, fixed below).

Everything else verified: the rename families are mechanically correct and complete, and `Icon`→`IconRest`, `Width`,
`Href`, `Match`, `Disabled`, `Tooltip`, `Expanded` all map to real v5 parameters. No blocker.

### Leg B3 (`e559989f`) — the vacuous test is now real, with non-vacuity proved

**Drive mechanism, established empirically** (a temporary failing assertion was used to dump the real render tree):
v5's `FluentTextInput` renders `<fluent-field class="…"><fluent-text-input slot="input" blazor:onchange=… blazor:ontextimmediate=… /></fluent-field>`.
There is **no `oninput`** — with `Immediate="true"` the keystroke path goes through the custom **`ontextimmediate`** — and
the inner editable `<input>` is **shadow DOM, unreachable from bUnit**. The test now drives `.ChangeAsync("W1")`
(`onchange`), which is canonical bUnit, not coupled to the `Immediate` flag, and is the spelling the repo already uses
elsewhere; awaiting `cut.InvokeAsync(...)` awaits the handler chain, so the assertion is synchronous and non-flaky.

**Non-vacuity proved by breaking it first** (build → filtered run → restore, twice): with the event dispatch commented out,
`captured.Should().Be("W1")` **FAILED** with `found <null>` — both against the intermediate and the final drive line. The
old assertion (`captured.Should().BeEmpty("no change event is raised")`) was true whether or not the binding existed.

Parent-verified after B3: `dotnet build SchoolCollab.slnx` **0 errors**; Admin **697 total / 0 failed / 697 passed**; and no
tautological "no change event" assertion remains in that file.

## Acceptance

**Round B delivered the five v5-absent rename families, an authored breadcrumb replacement, the selector/test re-points, and one de-vacuumed test — every gate measured by the parent.**

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx --no-incremental` | **0 errors** |
| `RZ10012: Found markup element with unexpected name '<X>'` | **0** (was 94 lines at kickoff) — measured non-incrementally, which is the only valid way to measure it |
| v5-absent tags anywhere in `src/` | **0 files** for all eight families (`FluentTextField`, `FluentNumberField`, `FluentNavMenu`, `FluentNavLink`, `FluentNavGroup`, `FluentProgressRing`, `FluentBreadcrumb`, `FluentSearch`) |
| MAUI head — `dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false` | **0 errors / 0 warnings** (required because `Admin.Shared` changed — per the rule added in Round A) |
| `dotnet test` | **4093 passed / 4098 total**; the only 2 failures are the opt-in live-AI tests |
| Admin suite (after B3) | **697 / 0 / 697** |
| Independent static review | **no blockers**; 2 majors — the vacuous test (fixed in B3) and `FluentNav.Title` (open, Q53) |
| Frozen artifact | `documents/rounds/diffs-fluentui-v5-roundb.patch` — 68 files, no new-file entries |

**Delivered:** `FluentProgressRing`→`FluentSpinner` (42 files / 47 tags in B1 plus 9 dialog files / 10 sites in B2),
`FluentTextField`→`FluentTextInput` (6 + 4 files), the nav family → `FluentNav`/`FluentNavItem`/`FluentNavCategory`
(1 file, 170 changed lines, the round's largest hand-edit), the breadcrumb authored in place as real `FluentLink` crumbs
(v5 ships no replacement), `::deep fluent-text-field` → `::deep fluent-field` and the nav CSS re-pointed to the real v5
DOM classes, and the breadcrumb test restored to assert the trail's structure.

**Measured correction carried forward:** `FluentProgressRing` is **not** inert in 5.0.1 — it is an obsolete compat shim
that still renders `fluent-spinner` (22 `CS0618` obsolescence warnings), so that rename is deprecation hygiene, not a
rendering fix. The true `RZ10012` families were `FluentTextField`, the nav trio and the breadcrumb pair.

**NOT satisfied by this round, each with its trigger:** the dialog shape (Q54 deferred it to the dialog-shape leg),
form-ownership consolidation (Q43–Q45), the ~66 pre-existing inert attributes (Q37 backlog), the two pre-existing defects
(`AiChat` `Disabled`), and the UI-tester pass (Q57, awaiting the owner). **`FluentNav.Title` (Q53) ships open in this
layer** — a one-line `aria-label` fix, explicitly not applied because the owner's instruction covered commit/push/stack only.
