Provider: pi, light round Tier 2, 2-model set (owner 2026-10-23) — plan gate + diff review + acceptance on `ollama-cloud/deepseek-v4-pro:0813`, worker on `ollama-cloud/deepseek-v4.1-flash:1017`. Parent (plan author) runs `clinepass/cline-pass/deepseek-v4.1-flash-1017`, so the gate is never the author.
Mode: attended

# Round — panel layout pattern (create / edit surfaces)

## Goal

One skeleton for every create / edit surface: a **main region** of the entity's own fields and own
content, and a **rail region** of panels, one per relationship. One box, token-coloured, regions
drawing nothing, title-left / actions-right on a sticky header row, a sticky rail, and collapsibility
as a per-panel opt-in. The contract is `documents/solution/panel-layout-pattern.md`, written and
approved in this session; this round makes the code match it.

## Plan review iteration 1 — how each P1 was resolved

| P1 | Finding | Resolution in this revision |
|---|---|---|
| 1 | Plan claimed `Title` "stays optional (already is)". **False** — `PanelSection.razor:41` is `[Parameter, EditorRequired]`, reverted when the Message heading was restored. | **W1 now makes `Title` genuinely optional**, matching contract §4 and the owner's Q7 answer ("titled, but optional"). No contract amendment; the code changes. |
| 2 | Action-bar removal requires `AuthoringShell` (the bar's owner) but it was out of scope; the ADR mandates the shell own the bar; **and neither surface has a `Cancel` button today**. | **`AuthoringShell.razor(.css)` and `adr-authoring-surface-pattern.md` added to scope** (W4). The shell keeps ownership of *what* the actions are and delegates *where* they render (W3). `Cancel` was dropped in iteration 1; the owner then directed it **into** the round ("put it in this round", 2026-10-23) — see *Scope change after gate PASS* below. |
| 3 | Two different "Content & Resources" blocks render on the edit surface (`AssignmentAuthoring.razor:416` PanelSection vs `:614`→`:639` `ResourcesSection`), and `ResourcesSection` carries its own `.cq-header` chrome; the `--summary` grid and the Create-mode skip were unmentioned. | **W2 now enumerates the merge**: one rail panel; `ResourcesSection` becomes body-only; `#authoring-content` region deleted; Create skip and summary branch kept and stated. `ResourcesSection.razor(.css)` added to scope. |

Also fixed from the P2s: CSS line cites corrected (`#authoring-basics` is `.razor.css:47`; the framed
wrapper is `.authoring-compartment--cq` at `:159`), `.panel-section__rule`'s literal border retired too,
and the container query names the ancestor that gets `container-type`.

## Scope change after gate PASS (owner, 2026-10-23)

`Cancel` is **in scope for this round** (owner instruction "put it in this round"; tracked as issue #403).
It was out of scope when the gate reviewed, so this delta ships without a third gate pass — the
skill's plan-review budget is one iteration and it is spent. The delta is covered by the **diff
reviewer and the acceptance seat**, and the original scope finding ("a button that does not exist
today") is explicitly **superseded by owner authority**, not silently dropped.

**The dirty-form question is answered by the codebase, not by preference:** the app already guards
unsaved navigation on the assignment surface — `AssignmentAuthoring.razor:838-850`
(`UnsavedChangesMessage`, `UnsavedChangesDiscardAction`, "The guard cancels on ANY non-discard
outcome") plus the `window.beforeunload` listener in `AssignmentAuthoring.razor.js:38-41`. `Cancel`
reuses that guard rather than inventing a second one. The template surfaces have **no** dirty guard
today, so their `Cancel` leaves immediately; that asymmetry is recorded, not hidden, and the
template-side guard is a follow-up.

## Plan

### W1 — `PanelSection`: the one box, optional title, optional collapse
`src/SchoolCollab.Admin.Shared/Components/PanelSection.razor` + `.razor.css`

- CSS: `border: 1px solid var(--neutral-stroke-divider-rest)`, `background: var(--neutral-layer-1)`,
  `padding: 16px`, `border-radius: 8px`. Today `.panel-section` has `padding: 12px`, a literal
  `#e1e1e1` border and no surface; **`.panel-section__rule` also carries a literal `#e1e1e1`** — both
  literals retired.
- **`Title` becomes optionally-typed and genuinely optional** (`string?`, no `EditorRequired`), per
  contract §4. It is `[Parameter, EditorRequired]` today (`PanelSection.razor:41`).
- NEW `[Parameter] public bool Collapsible { get; set; }` — **default false**. False ⇒ **no chevron
  element in the DOM at all** (not a disabled one, not CSS-hidden). True ⇒ chevron `<button>`
  **immediately before the title**, `aria-expanded` + `aria-controls` → body id, body collapses to
  the header row, starts expanded, state in the component instance.
- NEW sticky-header mode: header row (title + actions) sticks at the shared app-bar offset, keeps the
  surface token and its bottom rule.
- Header row not clickable. Order: `[chevron?] Title [count] … [kebab] [page action]`.

### W2 — Assignment authoring onto the skeleton
`AssignmentAuthoring.razor` + `.razor.css`, `ResourcesSection.razor` + `.razor.css`

- **Delete the framed containers**: `.authoring-compartment` (`.razor.css:18`), `#authoring-basics`'s
  override (`.razor.css:47`), `.authoring-compartment--right` (`:201`), and the framed
  `.authoring-compartment--cq` (`:159`). Regions keep layout only.
- **Merge the two "Content & Resources" blocks.** Today: a `PanelSection Title="Content & Resources"`
  inside the row-1 rail wrapper (`AssignmentAuthoring.razor:416`) *and* a second block at `:614`
  (`#authoring-content`, `.authoring-compartment--cq`) whose live branch renders
  `<ResourcesSection>` (`:638`) while read-only (`:615-620`) and disabled-reason (`:627`) branches
  render their own `.cq-header` heading. After: **one** rail panel — `ResourcesSection` renders its
  body only (its `.cq-header`/`.cq-count`/`.authoring-compartment-title` chrome at
  `ResourcesSection.razor.css:22-34` is removed and the panel supplies the title/count), the
  read-only and disabled-reason branches become that panel's body states, and `#authoring-content`
  as a separate region is deleted.
- **Rail (Q8/§5):** `Targets & Audience` (no chevron) · `Content & Resources` (chevron) ·
  `Rules` (chevron). **Main:** `Details` (sticky header, owns the page action) then
  `Questions & AI` (no chevron). Today's nesting to remove: `#authoring-targets` (`.razor:381`) is a
  framed compartment containing the `PanelSection`s at `:416`, `:485`, `:555`.
- **Collapse = container query**, not `@media (max-width: 1400px)` (`.razor.css:146`). The element
  carrying `container-type: inline-size` is the **page/region wrapper**, not the grid (a grid cannot
  query itself); the grid queries that ancestor. Rail last when collapsed. `.authoring-layout`
  (`.razor.css:128`) and `.authoring-layout--cq` (`:135`) collapse onto one shared definition.
- **Preserved branches, stated:** Create mode keeps skipping the whole row-2 wrapper
  (`.razor:608`); the summary view keeps `#authoring-targets` as `authoring-summary__targets`
  (`.razor:719`, `.razor.css:309-314`) — summary stays single-column.

### W3 — Notification-template editor, and where the page action lives
`TemplateEditor.razor` + `.razor.css`, `AuthoringShell.razor` + `.razor.css`

- **`AuthoringShell` keeps ownership of the action set and loses the bar's placement.** It already
  owns `PrimaryId`/`PrimaryLabel`/`PrimaryDisabled`/`OnPrimaryAction` and `SecondaryActions`
  (`AuthoringShell.razor:88-100`) and renders them in `<header class="authoring-actionbar">`
  (`:39-64`). It gains a way to **not render the bar** (the shell's sticky bar is what
  `adr-authoring-surface-pattern.md` decision 1 mandates, so that ADR is amended in this change —
  W4). The page renders the same primary/secondary controls into the first `PanelSection`'s
  `HeaderActions`, which is sticky — so nothing is lost by dropping the separate bar.
- **`Cancel` on all four surfaces** (owner, 2026-10-23; issue #403): a secondary-weight button in the
  first block's header row, left of the primary, navigating to the owning list page —
  `/notification-templates` for the template editor, `/assignments` for the assignment authoring
  surface. It does **not** replace `Reset` or `Back to summary`; those are different verbs (restore
  loaded values / change view of the same draft), so all three coexist. On the assignment surface it
  must pass through the **existing** unsaved-changes guard (`AssignmentAuthoring.razor:838-850`); the
  template surfaces have no guard today and are left unguarded, which is recorded in this round doc
  and tracked as a follow-up.
- **No other new buttons.** Everything else is relocated exactly as it is today: template create
  `Create template`; template edit `Save` + `Reset` (the shell's `SecondaryActions`); assignment
  `Save as draft` + `Back to summary` (`.razor:1397-1404`) + the secondary kebab (`.razor:1406-1413`).
- Message panel: sticky header carrying the page action; no chevron.
- Preview's `Refresh` **moves from the panel body footer** (`.template-editor-preview__actions`) into
  the panel **header** (consent table), which is the change the table asserts.
- Rail: `Constraints` (chevron) · `Preview` (chevron). Region wrappers
  (`.template-editor-message-panel`, `.template-editor-rail`) stay layout-only; `.template-editor-layout`
  loses its `@media` bound and its duplicated track definition.

### W4 — Docs
- The 5 contradicting specs amended, each keeping requirements and **pointing at** the pattern doc
  instead of restating chrome: `specs/assignment-authoring-compartments.md`,
  `specs/assignment-create-edit-redesign.md`,
  `specs/assignment-authoring-content-questions-modern-ui.md`,
  `specs/notification-template-authoring-redesign.md`,
  `specs/notification-template-naming-and-editor-surface.md`.
- **`documents/solution/adr-authoring-surface-pattern.md`** — decision 1 (the shell owns the sticky
  action bar) is superseded by contract §4; amended in the same change, as the contract's preamble
  requires.
- `documents/solution/panel-layout-pattern.md` changes only if the implementation disproves it, and
  then the disagreement is recorded.

### W5 — Tests
bUnit per surface, external behaviour only: box classes/parameters; **no chevron element when
`Collapsible` is false**; chevron precedes the title when true; rail membership exactly per the consent
table; the page action inside the first header row; **one** Content & Resources block on the edit
surface; `.authoring-compartment` absent.

### Expected files

```
src/SchoolCollab.Admin.Shared/Components/PanelSection.razor
src/SchoolCollab.Admin.Shared/Components/PanelSection.razor.css
src/SchoolCollab.Admin.Shared/Components/Authoring/AuthoringShell.razor
src/SchoolCollab.Admin.Shared/Components/Authoring/AuthoringShell.razor.css
src/SchoolCollab.Admin.Shared/wwwroot/app.css                     (region grid + sticky/app-bar tokens)
src/Assignments/.../Pages/Assignments/AssignmentAuthoring.razor
src/Assignments/.../Pages/Assignments/AssignmentAuthoring.razor.css
src/Assignments/.../Pages/Assignments/ResourcesSection.razor
src/Assignments/.../Pages/Assignments/ResourcesSection.razor.css
src/Settings/.../Pages/NotificationTemplates/TemplateEditor.razor
src/Settings/.../Pages/NotificationTemplates/TemplateEditor.razor.css
documents/solution/adr-authoring-surface-pattern.md
documents/specs/assignment-authoring-compartments.md
documents/specs/assignment-create-edit-redesign.md
documents/specs/assignment-authoring-content-questions-modern-ui.md
documents/specs/notification-template-authoring-redesign.md
documents/specs/notification-template-naming-and-editor-surface.md
tests/SchoolCollab.Admin.Tests.Unit/...
tests/SchoolCollab.Assignments.Tests.Unit/...
```

### Out of scope
The rich-text editor toolbar (issue #402 excludes it) · the app bar (issue #402) · Student Detail rail
(own issue) · persisting collapse state · adding any button that does not exist today.

## Mock (approved) and consent checklist (approved; `Cancel` added by owner 2026-10-23)

The console mock approved in this session: assignment (main `Details` + `Questions & AI`; rail
`Targets & Audience` / `Content & Resources` / `Rules`, `▾` on the last two) and template (`Message`;
rail `Constraints` / `Preview`), chevron **leading** the title, rail and first header row sticky.

| Panel | Region | Collapsible | Header controls (order per §6) |
|---|---|---|---|
| Message | main | no | leading: **Cancel** · kebab: Reset · **Create template** / **Save** |
| Details | main | no | leading: Back to summary · **Cancel** · kebab · **Save as draft** |
| Questions & AI | main | no | kebab |
| Targets & Audience | rail | no | kebab |
| Content & Resources | rail | yes | kebab |
| Rules | rail | yes | kebab |
| Constraints | rail | yes | kebab |
| Preview | rail | yes | Refresh |

## Acceptance criteria

1. `PanelSection` box = token border + `--neutral-layer-1` surface + 16px + radius 8; **no literal
   colour** anywhere in `PanelSection.razor.css` — including `.panel-section__rule`.
2. `PanelSection.Title` is genuinely optional (no `EditorRequired`, nullable), and every current
   call site still passes a title.
3. `Collapsible` exists, defaults **false**, and a false panel renders **no** chevron element in the
   DOM.
4. The chevron renders **before** the title and carries `aria-expanded` + `aria-controls`.
5. The header row is not clickable (no `@onclick` on the row).
6. The first block's header row is sticky and holds the **existing** page action; the shell renders
   no separate sticky action bar on these surfaces; **the only new control is `Cancel`** (criterion 16).
7. The rail column is sticky at the same offset; **one** app-bar-height custom property feeds both.
8. `.authoring-compartment` no longer exists in markup or CSS.
9. Regions draw no border/surface/padding on either surface; one shared region-grid definition (no
   duplicated `grid-template-columns`).
10. Collapse is a **container query** on a named `container-type: inline-size` ancestor; the
    `@media (max-width: 1400px)` rule is gone; rail last when collapsed.
11. Rail membership and collapsibility match the consent table exactly; `Targets & Audience` carries
    no chevron; main-column panels carry none.
12. **Exactly one** Content & Resources block renders on the edit surface, and `ResourcesSection`
    renders no heading chrome of its own.
13. Slotted Fluent controls sized `width: 100%` (no `inherit` reintroduced); ladder respected
    (Title/Subject W7, Code W4, pickers W5, fill W9).
14. The 5 specs **and the ADR** are amended, each pointing at the pattern doc; no spec restates chrome.
15. `dotnet build SchoolCollab.slnx` 0 errors; `Admin.Tests.Unit`, `Settings.Tests.Unit`,
    `Assignments.Tests.Unit`, `ArchitectureTests.Unit` all 0 failures.
16. **`Cancel` renders on all four surfaces** (template create, template edit, assignment create,
    assignment edit), sits in the first block's header row left of the primary, and navigates to the
    owning list page (`/notification-templates`, `/assignments`). `Reset` and `Back to summary` still
    render. On the assignment surface `Cancel` goes through the existing unsaved-changes guard.
17. No control other than `Cancel` is added that does not exist today.

## Open decisions

None. Q5–Q18 closed in this session. The `Cancel` button was approved by the owner (2026-10-23) and is
**in this round** — issue **#403** stays open as its record, and the dirty-form question it raised is
answered by the existing guard rather than left to preference. The only asymmetry introduced is
that the template surfaces have no dirty guard; that is stated in this doc as a known gap.

## Acceptance

**Verdict: PASS** (parent, the plan's author, 2026-10-24). The reviewer chain was
BLOCK (2 P1) → rework → delta review RESOLVED on all four → one residual P1 (X-4) → fixed → final
confirmation requested from the same seat. Criterion 15 is backed by the parent's **own** gate run,
not a delegate's claim: the reviewer marked it UNPROVEN from a static seat, so the parent ran it.

| # | Verdict | Evidence |
|---|---|---|
| 1 | MET | `PanelSection.razor.css` — token border, `--neutral-layer-1`, `16px`, radius `8px`; no literal hex in any rule |
| 2 | MET | `[Parameter] public string? Title` (no `EditorRequired`); 8/8 call sites still pass a title |
| 3 | MET | `Collapsible` defaults `false`; `@if (Collapsible)` wraps the whole chevron `<button>` |
| 4 | MET | chevron `<button>` is the header's first element child, immediately preceding `<h4>` |
| 5 | MET | no `@onclick` on `.panel-section__header`; the chevron is the only control |
| 6 | MET | `StickyHeader="true"` on Details/Message; `ShowActionBar="false"` both shells |
| 7 | MET | rail and header row both `top: var(--app-bar-sticky-offset, 0px)` |
| 8 | MET | no `.authoring-compartment` in any `.razor`/`.css` |
| 9 | MET | one `.region-grid` track; `__main`/`__rail` draw nothing |
| 10 | MET | `@container region-grid-host (max-width: 899px)`; `@media (max-width: 1400px)` deleted; rail last |
| 11 | MET | consent-table membership pinned by `PanelLayout_RailMatchesTheConsentTable_*` |
| 12 | MET | one `#authoring-materials-section`; `.cq-header` removed from `ResourcesSection` |
| 13 | MET | see residual R3 — evidenced, but its hunks are the earlier round's |
| 14 | MET | 6 specs + ADR point at the pattern doc; ADR keeps dated superseded note; every stale "unchanged" claim annotated after this review |
| 15 | MET | parent-run gates: build **0 errors**, Admin **811/0**, Settings **682/0**, Assignments **1391/0**, Architecture **144/0**; MAUI head **0 errors** |
| 16 | MET | `Cancel` on all 4 surfaces → `/assignments` · `/notification-templates`; assignment path passes the location-changing guard |
| 17 | MET | no control added beyond `Cancel` |

### Residuals (recorded, not fixed in this round)

| # | Residual | Disposition |
|---|---|---|
| R1 | Template surfaces have **no** unsaved-changes guard, unlike the assignment surface | Recorded asymmetry; follow-up issue, not this round |
| R2 | `--app-bar-height` and `--app-bar-sticky-offset` are two properties where the contract says one | Accepted — both sticky elements read the same offset; naming only |
| R3 | `#authoring-summary-edit` pencil moved `Primary`→`Outline` once Publish took the Primary slot | Unplanned but correct; cosmetic; recorded here rather than shipped silently |
| R4 | The frozen patch also carries the **earlier** round's uncommitted work (`app.css` width block, `#authoring-basics` padding, tag-menu/`Constraints` hunks) | Disclosed to both reviewers, who reviewed it as contamination, not as this round |
| R5 | Round-diff isolation base was not snapshotted at round start, so the patch is `git diff HEAD` over the round's paths | Process miss owned by the parent; the `isolate-round-diff-over-uncommitted-prior-round` skill exists precisely for this |

### Process notes

- The tag-menu / width / constraint-lookup work visible in `TemplateEditor.razor(.css)` is the
  **earlier** defect pass, still uncommitted; both reviewers were told to exclude it.
- Spec text is the parent's own work: the rework worker implemented the bulk of W4, and the parent
  fixed the three stale claims the sweep kept finding (`LA-1` prose, `S5` "rail is not sticky",
  `X-4` "jump-nav order unchanged").

(final confirmation from the review seat is recorded below when it lands)

## Escalation provenance

The worker pass (`ollama-cloud/deepseek-v4.1-flash:1017`) **timed out at 30 min** without a report. The
partial state was inspected before escalating, not assumed: 37 files changed, build green on its last
run, W1/W2/W3 landed, and two residuals remained — one failing test
(`CollapsiblePanel_PutsTheChevronBeforeTheTitle_AndStartsExpanded`) and W4 incomplete (2 of 6 docs).
Six `scratch-*.py` files it left in the repo root were deleted by the parent.

Per the skill's escalation ladder, the blocked pass was resumed on the **round's escalation seat**,
`ollama-cloud/deepseek-v4-pro:0813`, with a residual-only brief (fix the test, finish W4, run the five
gates). One escalation for this pass.

## Grill round — L8/L9 and split-before-escalate (owner, 2026-10-24)

Owner answer: **"L8/L9 all as recommended"** — every question below settled at its recommendation.

| # | Question | Settled as |
|---|---|---|
| Q19 | What discriminates *split* from *escalate* on a timed-out pass? | By failure shape — scope-shaped splits, capability-shaped escalates (later hardened: the size branch is mechanical) |
| Q20 | Split unit: the original workstreams, or the state? | **State-first** — freeze what landed, dispatch only the residual |
| Q21 | Does a split-after-timeout consume the ≤1 rework iteration? | **No** — a re-dispatch is not a review iteration (later capped so it cannot chain) |
| Q22 | Per-slice worktrees or sequential in one tree? | **Sequential, one tree** — a residual is dependent by construction |
| Q23 | **L8** — plan-time dispatch budget | **>3 workstreams** ⇒ the plan must declare `single pass` or `one worker per workstream` |
| Q24 | **L9** — `/to-tickets` offer | Also offered when **>4 workstreams and no published tickets**; owner-invoked only |

This table is the record the conformance audit (2026-10-24) found missing — the round doc had claimed an
owner-settled grill round while `## Open decisions` said "None", so the `(owner, 2026-10-24)` stamp on the
plan-time rules read as the parent attributing its own additions to the owner. The frontier had in fact
been put to the owner and answered; only the record was absent.

### Rule this round produced (owner 2026-10-24)

The round exposed a gap: fan-out did not qualify (no slice ≥10 files, and W2/W3/W5 all depend on W1's
new API), the ~3k-line sizing bound did not fire (2,449 insertions), so escalation was the only move
the skill allowed — although the failure was **scope-shaped** (steady progress, 37 files landed, green
build, a named residual), which is the cheap symptom, not the expensive one. The owner settled the fix
in a grill round; it is now in `.pi/skills/orchestrator-worker-reviewer/SKILL.md` as the
**scope-split rung** (classify by recorded state: ≥1 workstream complete + green build ⇒ split, not
escalate; state-first unit; sequential one tree; one split per workstream; tier-aware rework bound)
plus the plan-time **dispatch declaration (>3 workstreams)** and the **`/to-tickets` offer (>4
unticketed workstreams)**.

The proposed text was adjudicated by `deepseek-v4-pro:0813` **before** it was written, and that pass
rejected three defects in the parent's draft: it contradicted the existing "escalation re-dispatches
the same scope" rule, it graded by judgement where the time-box rule demands a recorded bound, and its
"≤1 rework iteration" clause was false on Tier 3 (≤2). All three are corrected in the written text,
and the fix for the judgement defect is the point of the rung: the classifier reads the round doc's
own workstream list and the build result, not the parent's opinion of the worker's demeanour.

---

## Post-acceptance defect pass (2026-10-24)

**This section was NOT part of the reviewed round.** The `## Acceptance` above was written against the
diff frozen at 09:53. Three owner-reported defects arrived afterwards and are recorded here so the
acceptance is not retro-fitted to work it never saw.

| # | Owner report | Root cause | Fix |
|---|---|---|---|
| D1 | Literal text `namespace SchoolCollab.Admin.Shared.Components.Authoring` rendered on the page | `AuthoringShell.razor:1` carried a **bare `namespace` line with no `@`** — a C# header pasted into a Razor file. Razor parses it as markup, so it is *content*, not a directive. The diff reviewer had flagged the line and stood down because no static evidence was available; the build and every bUnit suite stayed green because rendering markup is not an error. | Line deleted. The folder already derives that namespace (`AuthoringCrumb.cs`). |
| D2 | Panel borders missing everywhere; panels indistinguishable from the page | `border: 1px solid var(--neutral-stroke-divider-rest)` and `background: var(--neutral-layer-1)` used **bare tokens that nothing defines** — no design-system provider is used and FluentUI 5.0.1 ships no token CSS. An undefined `var()` does not degrade: the **whole declaration** is invalid at computed-value time, so the border resolved to `border-style: none` and the surface to `transparent`. `git show HEAD` confirms the border was a visible `#e1e1e1` literal before this round's "token colours only" rule replaced it. | Neutral token set defined in `Admin.Shared/wwwroot/app.css` (`:root` + `[data-theme="dark"], .dark`), and **fallbacks added at every `PanelSection` use site** — `var(--token, <light literal>)` — which is what the repo's own `body` rule has always done. Pattern doc §3 gained the rule "every token you use must exist, or carry a fallback". |
| D3 | Owner refinement: the **main** region may be borderless, the **rail** must not be | The pattern made every block a box; a rail column of unboxed blocks has no edge to read, but the main block is the page's own content rather than a card | `PanelSection.ShowBorder` (`[Parameter]`, default **true**) → `panel-section--borderless`, hidden as `border-color: transparent` (never `border: none`) so the box model is identical in both variants. `ShowBorder="false"` on `authoring-basics`, `authoring-questions`, `template-editor-message`; every rail block keeps the box. The header rule (`.panel-section__rule`) is a separate element, so the title/body divider survives — pinned by a test. Pattern doc §3 amended. |

**Verification status — deliberately incomplete and stated as such:**

| Gate | Result |
|---|---|
| `SchoolCollab.Assignments.Tests.Unit` (after `ShowBorder` + call sites) | **1391 / 0 failed** |
| `SchoolCollab.Admin.Tests.Unit` | **NOT RUN since the new tests were written.** The DLL on disk is from 09:50; `PanelSectionTests`' two new tests and the amended surface test are from 10:35 |
| Full `dotnet build SchoolCollab.slnx` | **BLOCKED** — `MSB3027`/`MSB3021`, output DLLs locked by `SchoolCollab.Admin` (PID 31604, a running instance) |

So the round's "Admin 811/0" **does not cover D3's tests**, and this section must not be read as claiming
green. Also note none of D1–D3 was diff-reviewed: they were made after the review chain closed, from
owner reports, and a delta review is owed before the work is committed.
