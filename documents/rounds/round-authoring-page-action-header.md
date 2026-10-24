Provider: pi, Tier 0 solo (owner, 2026-10-24 — "keep this work simple"; no review seat dispatched).
Mode: attended

# Round — page action header owns the pair (#404)

## Settled by the owner (round 11, 2026-10-24)

| # | Question | Settled as |
|---|---|---|
| Q51 | Option shape | **No switch.** "No idea what this is for" — the placement is not configurable; the pair belongs in the page header |
| Q53 | What moves | `Cancel` + the primary only. The **kebab moves beside the title**, after the status badge; the action pair sits flush on the content's right edge |
| Q55 | Sticky | The **`StickyHeader` feature is removed from `PanelSection`** entirely — the page action header is the only pinned row. Two stacked pinned rows was the overlap the owner reported |
| Q57 | Duplicate title | The page header carries the title; the app bar keeps its own (they are the same page name, resolved independently) |
| — | The rule under the title | **Removed**, and its reserved space collapsed |

Deferred/not done, on the owner's instruction: no `AuthoringActionPlacement` enum, no `ShowActionBar` toggle, no runtime flag.

## Implementation

| File | Change |
|---|---|
| `AuthoringShell.razor` | always renders the page action header (no `ShowActionBar`); new `TitleMenu` slot rendered after the title group; the action cluster is flush right |
| `AuthoringShell.razor.css` | no border, no radius, no bottom rule; padding `0 0 6px 0`; `.authoring-actionbar-controls { margin-left: auto }` |
| `PanelSection.razor(.css)` | `StickyHeader` removed (parameter, class, CSS rule); new **`ShowHeader`** (default `true`) renders no header DOM at all when false, and a headerless panel is forced expanded (a hidden chevron could never reopen it) |
| `AssignmentAuthoring.razor` | shell call owns `Cancel` + primary (`LeadingActions`/`PrimaryId`/`PrimaryLabel`); `PageMenu` becomes the shell's `TitleMenu`; block header keeps only `SummaryBack`; `HeadingText` title-cased (**New Assignment / Edit Assignment / Assignment Overview**); `ShowHeader` off on Create |
| `TemplateEditor.razor` | shell owns `Cancel` + save/adopt; block header keeps only `Reset`; **Title restored on Create** ("New Notification Template" — the bar is the page header again, so it must name the page); `ShowHeader` off on Create |
| docs | `panel-layout-pattern.md` §4 + §6 amended (placement, kebab, stickiness); `adr-authoring-surface-pattern.md` decision-1 note amended rather than left contradicting the code |

## Verification

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `SchoolCollab.Admin.Tests.Unit` — `PanelSectionTests` + `PanelLayoutSurfaceTests` + `TemplateEditorSurfaceTests` | **30 / 0** |
| `SchoolCollab.Assignments.Tests.Unit` — `PanelLayout_*` | **4 / 0** |
| Full suites | **not run** — owner waived for this change (2026-10-24); the full run is left to PR CI |

Test changes carried with the code: `StickyHeader_PinsTheHeaderGroupAndKeepsItsRule` → `HeaderGroup_KeepsTheRule_AndThePanelIsNeverSticky`;
`PanelLayout_FirstBlockOwnsThePageAction_AndTheShellRendersNoActionBar` → `PageActionHeader_OwnsThePair_AndNoBlockHeaderIsSticky`;
`TheMessagePanel_CarriesThePageActionInItsStickyHeader…` → `ThePageActionHeaderCarriesThePair_AndTheMessagePanelIsNotSticky`;
`EveryPanelOnTheEditor_CarriesItsHeading` → `OnCreate_TheMessageBlock_RendersHeaderless` + `OnEdit_TheMessageBlock_KeepsItsHeading`.
