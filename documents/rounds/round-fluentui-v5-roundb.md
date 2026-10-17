Provider: pi — Light round (Tier 2): worker `ollama-cloud/deepseek-v4.1-flash`, static diff reviewer `ollama-cloud/kimi-k2.7-code`
Mode: attended (owner pinned Round B; delivery = stack layer on the Round A PR)
Tier: 2 (light) — scripted sweeps + one static diff-only review; no plan-review agent, no UI-tester leg of its own (the deferred UI-tester pass rides on this round's close, Q51)
Base: layer on the Round A branch `fluentui-v5` (Round A PR open); layer branch `s2-fluentui-v5-roundb`
Status: planned

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
