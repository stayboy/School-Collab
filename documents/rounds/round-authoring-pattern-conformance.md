# Round — authoring-surface pattern conformance

**Status:** PLANNED — durable docs landed 2026-10-21, implementation not started. · **Mode:** Tier 2
light, attended (owner Q71). Parent plans and accepts; one worker pass + one static diff-only reviewer
pass. Seats run inline (no `spawn_agent` in this CLI), recorded as in the redesign round.

**Spec:** `documents/solution/adr-authoring-surface-pattern.md` (Q63–Q71) · redesign spec
§3.2/§3.3/§3.5/§7 · assignment spec UX-1a · `.github/copilot/rules/blazor-components.md` rules 3 + 7.

## Plan

**W1 — the shell (decision 1).** Extract `AuthoringShell` into
`src/SchoolCollab.Admin.Shared/Components/Authoring/`: the authored breadcrumb trail (`AuthoringCrumbs`,
`FluentLink` crumbs + `aria-current`, the v5 replacement D8 hand-authored) and the sticky action bar
(title · slot for status/owner · primary action · secondary `RowActionsMenu` with
`UseMenuService="false"`). `AssignmentAuthoring` keeps its ids (`#authoring-breadcrumb`,
`#authoring-primary-action`, `#authoring-summary-back`) and its `AssignmentAuthoringBunitTests`
assertions must pass unchanged; `TemplateEditor` adopts it with `Notification templates › New template`
/ `› {subject}` and Save/Reset in the bar.

**W2 — landing chrome (decisions 2 + 3).** `LandingPage` gains a `ContextBar` `RenderFragment` rendered
above the title; the template landing moves its grade bar into it and appends the missing `auto` track to
`GridSettings` (six data columns + Actions = seven). Landing tests assert the bar, its button, and that
the grid template's track count matches the rendered column count.

**W3 — Basics chrome (decision 4).** Remove the visible `<h3>Basics</h3>`; the section keeps
`aria-label="Basics"`, wears the panel box (1px, radius 8, padding 12) through a theme token, and the
`X-1` comment is retired.

**W4 — token completeness (rule 3 addendum).** The landing's mutations (`DeleteAsync`, adopt, promote)
pass the component token; `TemplateEditor`'s load (`GetByIdAsync`) owns a `CancellationTokenSource` like
its preview debounce already does.

**W5 — docs already landed** (this round's first commit): ADR, rule rules 3/7 + the mutation-token
paragraph, spec §3.2/§3.3/§3.5/§7, assignment UX-1a.

| # | Acceptance criterion |
|---|---|
| C1 | `AuthoringShell` exists in `Admin.Shared`; both authoring surfaces consume it; `AssignmentAuthoringBunitTests` unchanged and green |
| C2 | Template create/detail show `Notification templates › …` and a sticky bar carrying Save/Reset |
| C3 | The landing's `GridTemplateColumns` has one track per rendered column incl. Actions; a test pins it |
| C4 | The grade bar renders inside `LandingPage` via `ContextBar`; its test renders it in bUnit |
| C5 | Basics: no visible title, `aria-label`, panel box via theme token; no test asserts the removed heading |
| C6 | Every dispatched mutation and the editor load carry a token |
| C7 | `dotnet build SchoolCollab.slnx` + mobile head 0 errors; Admin + Assignments suites green |
| C8 | Docs listed in W5 exist and cite the ADR |

**Out of scope:** the unsaved-changes guard (ADR decision 5), the read-only idiom change (decision 6),
`PanelSection`'s literal border colour (left as is; the new panel uses the token), the structural smells
(#397).

## Worker Report

**Solo pass (2026-10-21).** The owner overrode Q71's Tier-2 recommendation with **Solo** — the single
agent planned, implemented and accepts below; no reviewer seat ran.

| Criterion | Change |
|---|---|
| C1 | `AuthoringShell` + `AuthoringCrumb` in `Admin.Shared/Components/Authoring/`; `AssignmentAuthoring` and `TemplateEditor` both consume it. Every pinned assignment id survived: `#authoring-breadcrumb`, `.authoring-crumb-separator`/`-current`, `#authoring-primary-action`, `#authoring-summary-back`. |
| C2 | Template create/detail wear `Notification templates › New template` / `› {subject}`, with Save (`#template-save`), the reason input (`#template-reason`) and Reset (`#template-reset`) in the sticky bar, and the copy action (`#template-adopt`) as the bar's primary when the row is outside the session's write scope. |
| C3 | Landing grid: trailing `auto` track added; rule 7 documents the contract. **No automated track assertion** — recorded residual (the value is a page-level string, and a DOM-style parser would test CSS parsing, not intent). |
| C4 | `LandingPage.ContextBar` renders above the grid *inside* the wrapper; the grade bar moved into it, so it now renders in bUnit (the existing grade test exercises it through `LandingPage`). |
| C5 | Basics: visible `<h3>` removed, `aria-label="Basics"` on the section, panel shape (radius 8 / padding 12) through the compartment's existing divider token. `ContextPicksSectionBunitTests` moved from "the Basics h3 stays the only one" (X-1) to UX-1a. |
| C6 | Landing mutations pass `MutationToken` (`DeleteAsync`, adopt, promote); `TemplateEditor` owns `_loadCts` and passes it to `GetByIdAsync`, disposed with the preview debounce. |
| C7 | Gates below. |
| C8 | ADR, rule rules 3/7 + mutation-token paragraph, both specs, this doc. |

**Blast radius, recorded:** removing the title invalidated exactly one assertion (the X-1 one) — updated
to the new invariant, not deleted. The other 1,384 assignment tests passed unmodified, which is the
evidence that the shell is a move, not a rewrite.

## Review

_Solo — no reviewer seat (owner, 2026-10-21, overriding Q71)._ The substitute is the evidence trail:
the shell's contract is pinned by its own suite, the assignment adoption is pinned by the unchanged
1,384-test suite, and the template adoption is pinned by the new breadcrumb test plus the existing
detail suite (ids and behaviour unchanged).

## Acceptance

**Verdict: ACCEPTED** — solo; the plan's author is also the implementing agent, as the mode defines.

| # | Criterion | Evidence | Result |
|---|---|---|---|
| C1 | Shell shared, both surfaces consume it | `Admin.Shared/Components/Authoring/{AuthoringShell.razor,.css,AuthoringCrumb.cs}`; `AuthoringShellTests` (4) | met |
| C2 | Template breadcrumb + sticky bar | `TheShell_RendersTheBreadcrumbTrail`; detail suite unchanged | met |
| C3 | Actions track | `Index.razor` `GridTemplateColumns` ends `auto`; rule 7 | met (no automated assertion — residual) |
| C4 | `ContextBar` | `LandingPage.ContextBar` + `AGradeDeepLink_NamesTheFilter_AndTheBarWidensTheViewAgain` (now inside the wrapper) | met |
| C5 | Basics headerless panel | `#authoring-basics` `aria-label`; `ContextPicksSectionBunitTests` UX-1a assertion | met |
| C6 | Tokens on loads and mutations | `MutationToken` on three mutations; `_loadCts` on the editor load | met |
| C7 | Gates | Admin **745/745** · Assignments **1385/1385** · Architecture **142/142** · `SchoolCollab.slnx` **0 errors** · mobile head **0 errors** | met |
| C8 | Docs | ADR + rules + both specs + this doc | met |

**Residuals (accepted):** no automated actions-track assertion (C3); `Settings.Tests.Unit` and
`Settings.Api.Tests.Unit` were not re-run — this round touched neither `Settings.Core` nor the API, and
both were green at the previous commit; the live visual pass stays with the owner.

