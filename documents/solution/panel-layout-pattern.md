# Panel layout pattern — create / edit surfaces

**Status:** adopted (2026-10-23)

**This file is the single source of truth for the pattern.** No spec, rule file,
skill or `AGENTS.md` restates it — they carry a pointer and nothing else. If a doc
and this file disagree, this file wins and the other doc is amended in the same
change.

---

## 1. What it is

Every create / edit surface in the admin app wears **one skeleton**: a wide **main
region** holding the entity's own fields, and a **rail region** holding one panel per
**relationship**. The pattern was settled by bringing the notification-template editor
and the assignment authoring surface onto one chrome (owner, 2026-10-23) after they had
drifted into two incompatible box languages.

```
┌──────────────────────────────────────────────────────────────┐
│  <Page title / breadcrumb>          (AuthoringShell owns this)│
├───────────────────────────────────────────┬──────────────────┤
│  MAIN REGION — entity's own fields        │  RAIL REGION     │
│  ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐  │ ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐ │
│  ┆ Details / Message   [Cancel] [Save] ┆  │ ┆ Relationship┆ │
│  ├╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┤  │ ├╌╌╌╌╌╌╌╌╌╌╌╌╌╌┤ │
│  ┆ <fields>                            ┆  │ ┆ <items>     ┆ │
│  └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘  │ └╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘ │
│  ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐  │ ┌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┐ │
│  ┆ Own content (e.g. Questions & AI)   ┆  │ ┆ Relationship┆ │
│  └╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘  │ └╌╌╌╌╌╌╌╌╌╌╌╌╌╌┘ │
└───────────────────────────────────────────┴──────────────────┘
     main track: minmax(0, 1fr)              rail track: minmax(320px, 460px)
```

---

## 2. The skeleton — regions draw nothing

```
.region-grid {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(320px, 460px);
    gap: 16px;
    align-items: start;
}
```

- Both columns are **layout regions**: no border, no background, no padding, no
  radius. They exist only to place blocks.
- Blocks are laid out with `display: flex; flex-direction: column; gap: 16px`.
- The rail is a stack of panels in reading order; it is **not** wrapped in a
  container that draws a box.
- One track definition for both columns, everywhere. (Today's two grids already agree on
  `minmax(0,1fr) minmax(320px,460px)` / `16px` / `start`; they are duplicated per
  component and collapse onto this rule during the round that adopts it.)
- **Collapse is driven by available width, never by a viewport magic number** (owner,
  2026-10-23). The region grid drops to a single column when *its own container* can no
  longer hold both tracks at their minimums — a **container query**, not
  `@media (max-width: 1400px)`. A viewport breakpoint lies the moment the same surface
  renders inside a narrower host region (a drawer, a dialog, a page with a wider nav).
- **When it collapses the rail goes LAST** — main region first, because the fields are
  what the author came for, and the relationships are enrichment.
- One collapse rule, defined once; a page may not carry its own breakpoint.

**Why regions draw nothing (lesson L2, 2026-10-23).** The assignment authoring surface
nested its rail panels inside a `.authoring-compartment` that carried its own border and
surface, so the rail was a box containing boxes — two border rules one inside the other,
and a second surface tone inside a compartment that already drew one. Its own stylesheet
already carried a "would double-frame them" note and a `border: none` workaround. A region
that draws nothing removes the class of defect: there is exactly one frame per block, so
no workaround is needed and no future block can re-introduce the second frame.

---

## 3. The block — exactly one box

`PanelSection` (`src/SchoolCollab.Admin.Shared/Components/PanelSection.razor`) is the
**only** framed thing on these surfaces.

| Property | Value |
|---|---|
| border | `1px solid var(--neutral-stroke-divider-rest, #e1e1e1)` — **rail blocks only** |
| background | `var(--neutral-layer-1, #ffffff)` |
| padding | `16px` |
| radius | `8px` |
| gap between children | `12px` |

- **The frame is regional (owner, 2026-10-24).** A panel is a box by default
  (`ShowBorder` defaults `true`) and the **main region turns it off** via
  `ShowBorder="false"` — its block is the page's own content, not a card, and its only edge is the
  header rule that separates the title block from the body. The **rail keeps the box**: a narrow
  column of unboxed blocks reads as one undifferentiated list with nothing to say where one ends.
  Hidden state is `border-color: transparent`, **never** `border: none`, so both variants keep an
  identical box model and swapping one cannot shift the layout. Call sites: borderless =
  `authoring-basics` (Details), `authoring-questions`, `template-editor-message`; bordered = every
  rail block (Targets & Audience, Content & Resources, Rules, Constraints, Preview).

- **Token colours only.** No literal hex. A literal ignores light/dark and produced the
  off-white-on-off-white defect that had to be removed from `PanelSection` on 2026-10-17;
  the border literal `#e1e1e1` survived that pass and is retired by this pattern.
- **Every token you use must exist, or carry a fallback** (2026-10-24). This app uses no
  FluentUI design-system provider and FluentUI 5.0.1 ships no token CSS, so `--neutral-*`
  is supplied **only** by `SchoolCollab.Admin.Shared/wwwroot/app.css` (`:root`, plus the
  dark overrides). A bare `var(--undefined)` does not degrade — the **whole declaration**
  becomes invalid at computed-value time, so `border: 1px solid var(--missing)` renders no
  border at all and `background: var(--missing)` paints transparent. That is exactly how the
  first version of this pattern shipped a box with **no border and no surface**: panels
  matched the page and every rail block lost its edge. Write `var(--token, <light literal>)`
  at the use site, and add both the token and its dark value in `app.css`. The repo's own
  `body` rule has always done this (`var(--neutral-layer-1, #ffffff)`) — follow it.
- **One box language.** `.authoring-compartment` is deleted; nothing else may define a
  second framed container for these surfaces.
- **Never nest a box inside a box.** A block that needs sub-structure uses plain
  sub-sections, an inline disclosure, or a table — not another panel.
- **An inline zone is not a panel.** A drop target, an empty-state strip or a chip row
  draws a dashed or no border and is never a `PanelSection`.

---

## 4. The header row — title left, actions right, one line

```
┌──────────────────────────────────────────────────────────────┐
│ <Title>                                 [Cancel] [Save]      │
├──────────────────────────────────────────────────────────────┤   ← full-width rule
│ <body>                                                        │
└──────────────────────────────────────────────────────────────┘
```

- `Title` is an **optional** parameter on `PanelSection` so the component can render an
  untitled box; **in practice every block on these surfaces is titled**. The earlier
  "Message" heading was removed as redundant and restored the same day by the owner: an
  untitled box makes the reader infer the block's subject.
- The thin rule sits **under the whole header row** — spanning the title *and* the
  actions — never as the title's own border.
- **Actions are right-aligned on the title's row**, vertically centred with it.
- **The page's primary action and `Cancel` live in the page action header** owned by
  `AuthoringShell`, flushed to the content's right edge (`Save as draft`, `Create template`,
  with `Cancel` immediately to their left). The page action header is the **only** pinned
  row. *Superseded 2026-10-24 (owner, issue #404): an earlier revision put the pair in the
  first block's sticky header row; the owner reported that it restricted and overlapped the
  block as it expanded, so the pair moved back up and `PanelSection`'s `StickyHeader` was
  removed rather than kept as a second pinned row. Two stacked pinned rows was the defect.*
- **That page's own kebab sits beside the title**, after the status badge — block menus
  belong to the block, page menus to the page.
- Row-level actions (add, edit, remove inside a rail panel) use `RowActionsMenu` as a
  kebab in the panel's header slot.

---

## 5. Rail membership — the ownership test

**A block belongs to the rail iff it presents another aggregate or collection. The
entity's own fields and its own child content stay in the main column.**

| Surface | Main region | Rail region |
|---|---|---|
| Notification template | **Message** (title, code, description, type, subject, body) | **Constraints** · **Preview** |
| Assignment | **Details** (status, title, subject, grading, score) · **Questions & AI** | **Targets & Audience** · **Content & Resources** · **Rules** |
| Student (planned) | Details | **Guardians** · **Contacts** |

The test is ownership, not position, and not "it lives in the margin":

- **Questions & AI → main column.** Questions are the assignment's own content — the
  author writes them here, they are not a link to another aggregate.
- **Content & Resources → rail.** Materials and references are a *collection of related
  records*; the panel presents other rows, not fields of this one.
- **Constraints → rail.** They point at grade levels, streams, subjects and activity
  groups — reference data owned by other contexts.
- **Preview → rail.** It is a derived read of the entity, not a field; the rail is
  "everything that is not the form", which is why a derived panel is admissible there
  while a field never is.

Two consequences worth stating because they were both live defects: (a) Content &
Resources and Questions & AI were laid out the other way round in an earlier mock — the
existing `grid-template-areas: "questions content"` already had it right, and the mock
was wrong; (b) swapping them is a **layout** consequence of the ownership test, never a
per-page preference, so no surface may make the opposite choice for taste.

---

## 6. Rail panel contract

Each rail panel presents **one** relationship and carries:

1. a **title** naming the relationship (noun, Title Case);
2. a **count** when it holds a collection (`Targets & Audience  2`);
3. a **kebab** (`RowActionsMenu`) for add / edit / remove — the item name is the primary
   affordance, the kebab holds the rest;
4. an **empty state** — one sentence saying what the absence means, not a blank body;
5. a **collapse toggle** (chevron) in the header, because the rail is an accordion
   (owner, 2026-10-23) — see below;
6. **no nested box**, ever.

### The rail is an accordion — but collapsibility is OPT-IN per panel

A panel may be collapsible; most are not. Collapsibility is a **per-panel opt-in**, never a
property of "being in the rail" (owner, 2026-10-23):

- **`Collapsible` is an optional parameter, defaulting to OFF.** A panel shows a chevron
  only when its caller asked for one. Opt-in is deliberate — an opt-out default means every
future caller has to remember to switch the affordance off, and forgetting it silently
adds a control the owner did not consent to.
- **A non-collapsible panel renders no affordance at all** — no chevron, no disabled
chevron, no hover hint. The user must never be shown something that looks collapsible and
is not. That is a hard requirement, not styling.
- **The main region is never collapsible.** `Details`, `Message` and every other
main-column block carry no collapse control: the fields are the page, and hiding them
behind a disclosure serves nobody.
- **`Targets & Audience` is not collapsible** even though it sits in the rail — an explicit
owner call (2026-10-23). The rail is where collapsibility is *available*, not where it is
*mandatory*.

Where a panel is collapsible: the chevron sits **immediately BEFORE the title**, on the left
(`▾ Details`), and the body collapses to the header row. Collapsible panels start
**expanded**.

**Header row, left to right — the one order:**

```
[▾ chevron, collapsible panels only] [Title] [count]          [kebab] [page action]
```

- The **chevron leads**, attached to the title it expands — it is part of the title's
  affordance, not a trailing control. A trailing chevron was proposed and rejected by the
  owner (2026-10-23); the console mock has always drawn it leading, and the mock is the
  agreed view.
- The **kebab and the page action stay right**, in that order: kebab (this panel's menu)
  then the page action (the whole form's commit).
- **The header row is NOT clickable.** A clickable row overlaps the kebab's hit area and
  hides a target; the chevron is a real `<button>` with `aria-expanded` + `aria-controls`
  and the title stays a plain heading.
- **Collapsing hides the body, never the panel** — title, count, chevron and kebab stay
  visible, so a relationship's existence is never a click away. That is the whole point of
  the rail.
- **Do not reach for `FluentAccordion`.** It brings its own item chrome, which fights the
  one-box rule (§3) and leaves no header slot for the count and the kebab. The toggle is a
  `PanelSection` parameter (`Collapsible`) + a chevron button in the existing header row.
- Collapse state is **per panel and not persisted**: a re-render keeps it, a remount
  returns to expanded, so a user never has to remember what they hid.

**The consent checklist.** Because collapsibility is a per-panel decision the specs cannot
derive, every mock names it panel by panel (collapsible / not), and the owner approves
that list together with the layout. Current list:

| Panel | Region | Collapsible |
|---|---|---|
| Details · Message | main | **no** |
| Questions & AI | main | **no** |
| Targets & Audience | rail | **no** |
| Content & Resources | rail | **yes** |
| Rules | rail | **yes** |
| Constraints · Preview | rail | **yes** |

### Sticky behaviour (owner, 2026-10-23; amended 2026-10-24)

Two things stick to the top of the scroll port, and only two:

- The **page action header** (title · kebab · `Cancel` · page action) — `AuthoringShell`.
  It is the only row that carries the page's commit, so it is the only row that pins.
- The **rail column** is `position: sticky; top: <below the app bar>; align-self: start`,
  so relationships stay reachable while the form scrolls.
- **No block header pins.** `PanelSection.StickyHeader` was **removed** on 2026-10-24
  (owner, #404): with the page action back in the page header, a pinned block header was a
  second pinned row that overlapped the block as it expanded. A block header is a label.
- Both offsets come from **one** custom property for the app-bar height. A page may not
  invent its own offset.

A rail panel that needs a sub-editor uses an **inline section toggle** inside its own
body. Opening a dialog from a dialog is forbidden for the same reason (a second modal on
top of the first loses the user's place) — see `.github/copilot/rules/section-card.md`.

Panels that fail the ownership test do not go in the rail to balance the columns. If the
rail is empty, the page is single-column.

---

## 7. Field and control widths

The W1–W9 ladder is authoritative for input widths (`FieldWidth` enum for
host-independent call sites; `Class="w-N"` only resolves inside the Admin host).

| Step | Width | Typical use |
|---|---|---|
| W1–W2 | 80 / 120px | **single-line text fields only — never a dropdown** |
| W3 | 160px | **the narrowest a dropdown may be**, small enums |
| W4 | 200px | code / reference fields |
| W5–W6 | 240 / 280px | medium single-line |
| W7 | 320px | identity fields (template title, subject) |
| W8 | 400px | deliberate bounded field |
| W9 | fill | the row's widest input, textareas |

**A dropdown is never sized below W3 — 160px is a floor, not a preference
(measured 2026-10-25).** Fluent UI v5 hard-codes `min-width: 160px` on the shadow
`.control` of `fluent-dropdown` / `fluent-select`, and the host keeps `overflow: visible`,
so a host narrower than 160px does not clip the control — the control paints **over** its
next sibling. At W2 (120px) a `ContactChannel` dropdown's field measured 120px while its
control measured 160px, leaving a **2px** overlap with the label; at W3 the field and the
control both measure 160px and the label clears by **8px**. CSS cannot enforce this (the
minimum lives inside the shadow root), so it is enforced three ways: this rule, the
`FieldWidth` doc comment, and `SchoolCollab.ArchitectureTests.Unit.DropdownWidthFloorArchitectureTests`,
which fails on a `DropdownForEnum` / `CodedValueDropdown` / `DropdownComponent` sized W1/W2
or a raw `FluentSelect`/`FluentDropdown`/`FluentAutocomplete` declared under 160px.
Do **not** "fix" it with `overflow: hidden` on the host: that hides the next defect instead
of failing loudly, and risks clipping the focus ring.

**Slotted Fluent controls must be sized with `width: 100%`, never `width: inherit`
(lesson L3, measured 2026-10-23).** `FluentTextInput` and friends are slotted into their
`fluent-field` host. `width: inherit` takes the host's *computed* width — which for a
`w-9` field is the percentage itself — and that percentage then resolves against the
slotted grid area, so the control fell back to its intrinsic **185px inside a 655px
field**. A `w-9` title and a `w-4` code field therefore rendered identically and read as
the same size. `width: 100%` makes the control fill the host (title 655px, code 200px).
`fluent-text-area` never showed the defect because it is block-level inside the field.

Fixed ladder steps also carry `flex: 0 0 auto` so a narrow cell can never squeeze a fixed
step down into looking like a smaller one; the fill step carries `flex: 1 1 auto` so its
`100%` basis is honoured.

---

## 8. Why this pattern

| Lesson | Evidence | Rule it produces |
|---|---|---|
| L1 | mock review 2026-10-23 — Content & Resources and Questions & AI were placed by "which column looks balanced" | §5: rail membership is decided by ownership of the presented rows |
| L2 | `AssignmentAuthoring.razor.css` 2026-10-23 — rail panels nested inside a bordered compartment; the file already carried a "would double-frame them" note | §2: regions draw nothing |
| L3 | live measurement 2026-10-23 — `width: inherit` left a slotted control at 185px inside a 655px field | §7: `width: 100%` |
| L4 | mock approved as "the look" then re-litigated against the code | a mock is a design artifact, never verification evidence (owned by step 2c of `orchestrator-worker-reviewer`) |
| L5 | two grids in the repo carried different viewport breakpoints (1400px vs 1100px) for the same decision | §2: collapse on the container's own width, one rule, defined once |
| L6 | Q11(a) traded legacy stickiness for one row; owner reversed it because the actions then scrolled | §6: the first block's header row is sticky, which removes that cost |
| L7 | a 2-panel rail needed nothing, a 5-panel rail would | §6: rail panels are accordions, expanded by default |
| — | `PanelSection` CSS history 2026-10-17 — a literal off-white surface fought the theme | §3: token colours only |

---

## 9. Where these facts live

**Here.** Every other file points at this one:

| File | Carries |
|---|---|
| `AGENTS.md` | one row in the specialty-instructions table |
| `documents/specs/assignment-authoring-compartments.md` | requirements only; layout points here |
| `documents/specs/assignment-create-edit-redesign.md` | requirements only |
| `documents/specs/assignment-authoring-content-questions-modern-ui.md` | requirements only |
| `documents/specs/notification-template-authoring-redesign.md` | requirements only |
| `documents/specs/notification-template-naming-and-editor-surface.md` | requirements only |
| `documents/solution/adr-authoring-surface-pattern.md` | the decision record and its history — never restated rules |
| `.pi/skills/orchestrator-worker-reviewer/SKILL.md` | step 2c requires a mock that **renders** this file |

Adding the same rule to two of those is the scattering this file exists to end.
