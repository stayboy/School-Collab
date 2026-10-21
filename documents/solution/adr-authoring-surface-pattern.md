# ADR: Authoring-surface pattern — one shell for landing and authoring pages

- **Status:** Accepted (owner, 2026-10-21 — grill round Q63–Q71)
- **Date:** 2026-10-21
- **Supersedes:** none — **extends** `assignment-authoring-compartments.md` UX-4/D6 (sticky action bar)
  and D8 (breadcrumb) from "the assignment surface" to "every authoring surface"
- **Related:** `.github/copilot/rules/blazor-components.md` § "Landing-page performance pattern";
  `documents/specs/notification-template-authoring-redesign.md` §3.2–§3.5, §7;
  `documents/rounds/round-notification-template-authoring-redesign.md`;
  `documents/rounds/round-authoring-pattern-conformance.md`

## Context

The notification-template authoring redesign took the assignment landing and the assignment
authoring surface as its **starting pattern**. A conformance review of the delivered work found four
different kinds of drift, and the distinction matters more than the list:

| Finding | Kind | Evidence |
|---|---|---|
| The landing grid budgets no track for the Actions column | **defect + doc gap** | `Index.razor` sets six tracks for six data columns; `LandingPage.EffectiveColumns` appends a seventh when `RowActions` is supplied. `LandingGridSettings` documents "the data grid columns" and never mentions the appended column. |
| The editor loads without a `CancellationToken` | **rule breach (scope ambiguous)** | `TemplateEditor.razor` calls `GetByIdAsync(id)` with no token; `blazor-components.md` rule 3 says *"every page that loads data in `OnInitializedAsync` must own a `CancellationTokenSource`"* — under a landing-page heading. |
| No sticky action bar on the template editor | **inferred only** | UX-4/D6 mandate it for the assignment surface; the redesign spec §3.3 says only *"layout mirrors `.authoring-layout--cq`"* and reproduces the **grid alone**. |
| No breadcrumb on create/detail | **inferred only** | D8 calls itself *"the first `FluentBreadcrumb` usage in the repo"*; `fluentui-v5-residual-backlog.md` §5 lists the hand-authored trail. No repo-wide rule exists. |

One of these reached a user: `Index.razor` passed `AriaLabel` to `DropdownComponent`, which never
declared it. Blazor validates parameters at render time, so the page threw on first render — and the
toolbar that contained it renders only behind a `SectionOutlet`, which the bUnit renderer does not
provide, so every gate stayed green. That is the cost of a pattern living in one page's code instead
of in a shared component.

Two surfaces are about to diverge permanently unless the pattern is made explicit: the template
editor is the second authoring page in the repo, and every future Settings page will copy whichever
of the two it finds first.


## Decision

1. **The assignment authoring surface is the canonical authoring pattern, for every context.** A
   shared `AuthoringShell` in `SchoolCollab.Admin.Shared` owns the breadcrumb trail and the sticky
   action bar (title · status/owner · primary action · secondary kebab). `AssignmentAuthoring` and
   `TemplateEditor` both consume it. `Admin.Shared` is the only legal home: Settings may not reference
   `Assignments.Application`, and a second copy is the drift this ADR exists to prevent.
2. **`LandingPage` owns its bars; the page owns its grid tracks.** A page's `GridTemplateColumns`
   budgets one track per rendered column, **including** the Actions column the wrapper appends.
   `LandingGridSettings` states the contract; the template landing appends `auto`.
3. **A context bar is a `LandingPage` fragment.** A filtered landing's "why am I seeing fewer rows"
   bar rides a new `ContextBar` `RenderFragment`, rendered above the title **inside** the wrapper —
   which also makes it testable, unlike the `page-toolbar` `SectionContent`.
4. **Compartment chrome carries at most one title, and a title is not mandatory.** Compartment 1 of
   the assignment surface renders as a **headerless panel**: `PanelSection`'s box shape (1px border,
   radius 8, padding 12) drawn with the theme's divider token — **not** `PanelSection`'s literal
   `#e1e1e1`, which its own CSS comment calls a theme fight — and `aria-label="Basics"` as its
   accessible name. The "X-1: exactly one title" claim in the code was never a spec rule; the rule is
   *at most* one.
5. **An unsaved-changes guard is not a default authoring requirement.** The assignment surface
   implements UX-7 on both paths because a 40-field form loses real work; the template editor has four
   fields and one Save and omits it (redesign spec §7). Absence is a recorded decision with a pointer
   to the assignment shape, not an oversight.
6. **The read-only idiom is per surface.** One bar when a single cause explains every locked control
   (template: type + contact mode, both fixed per template); per-control `.authoring-reason` when the
   controls have different causes (assignment UX-17).

## Alternatives rejected

- **Copy the action bar and breadcrumb into `TemplateEditor`.** Two implementations of one chrome with
  no test that they agree — the drift that produced the toolbar break.
- **Let `LandingPage` append the Actions track itself.** It cannot know whether that column wants
  `auto` or a fixed width, and it would silently re-flow every existing landing with no track-level
  test.
- **Use `PanelSection`'s literal border colour for the headerless Basics panel.** Its own comment
  records why a literal colour is wrong: it ignores the light/dark tokens every other container
  resolves through.
- **Implement the unsaved-changes guard now.** It would pull the `beforeunload` JS module in for one
  page whose draft is four fields.

## Consequences

- `blazor-components.md` gains the actions-track contract, and rule 3 (CTS/`IDisposable`) is scoped to
  **every** data-loading page rather than landings only.
- The redesign spec's §3.2/§3.3/§3.5 gain the shell, the `ContextBar` and the read-only idiom; §7
  records why the unsaved guard is absent.
- `assignment-authoring-compartments.md` records the headerless first compartment.
- `AssignmentAuthoringBunitTests` is the regression net for the assignment side of the shell refactor;
  the template side gains shell/breadcrumb assertions; `ContextBar` acquires the page test the toolbar
  never had.
- The live pass stays with the owner (the AppHost cannot be exercised by the agent here).
