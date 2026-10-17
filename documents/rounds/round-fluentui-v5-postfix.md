# Round — FluentUI v5 post-merge UI regressions (three defects)

**Status:** CLOSED
**Mode:** solo (owner instruction, 2026-10-17, Q85) — the tiered `orchestrator-worker-reviewer` roles collapsed into the
parent agent: no worker, no reviewer leg, no UI tester. Q82–Q85 were answered "all as recommended, go solo".
**Base:** `main` @ `2683b7c9` (after the #355/#356/#359/#360 train and specs #358)
**Parent docs:** `documents/solution/fluentui-v5-upgrade-path.md`; residues in
`documents/solution/fluentui-v5-residual-backlog.md`

---

## 1. What the owner reported

Three UI regressions after the v5 port merged:

1. the kebab (⋮) menu on the **Content & Resources** panel of create-assignment was broken;
2. the **active-period pill** in the app header had text in a not-visible colour;
3. the owner-authored **folder-tab chrome** had been replaced by the stock v5 tab look.

All three were reproduced against the running AppHost in a browser **before** any code changed. Two of the three had
been misdiagnosed on the way here, and each correction came from a measurement rather than a review. The clearest
example: the early hypothesis "`FluentHeader` is gone, so the accent tokens are gone" was **refuted** — the header is
still painted accent-blue (`rgb(15,108,189)`) and the pill's *wrapper* computes white. The text was invisible because
it was **blue on blue**, not white on light.

## 2. Root causes (measured, not inferred)

### 2.1 The kebab — `FluentMenu`'s children must live in a `FluentMenuList`

| | measured |
|---|---|
| before | `<fluent-menu>`'s direct children were five bare `fluent-menu-item`s; the menu computed `display:inline-block; position:static; visibility:visible` at `145×128` **before any click**, and clicking the trigger only flipped `aria-expanded` — it never opened and never positioned |
| after | the items sit in a `fluent-menu-list`; that list is `display:none` until the trigger is clicked, then `display:grid`, `position:fixed`, `:popover-open` matching, `183×178` anchored at the trigger (its `y` tracks the trigger's) |

v5's own stylesheet carries `fluent-menu fluent-menu-list:not([popover]) { display:none }`: the popover state lives on the
**list** child, and `FluentMenu`'s JS (`Microsoft.FluentUI.Blazor.Components.Menu.Initialize(Id, Trigger, …)`) flips it
when the trigger is clicked. The port had read v5's removal of the menu *provider* as "the menu now renders inline" and
dropped the list wrapper — so nothing ever hid the items, and the trigger had nothing to toggle.

### 2.2 The period pill — colour the slotted content, not the wrapper

`fluent-link`'s shadow root is just `<slot></slot>`; the visible text is slotted light-DOM content, so the wrapper's
colour does not reach it, and the tokens the component published on the wrapper (`--neutral-foreground-rest`,
`--accent-foreground-rest`) are not read by v5's link. Measured: the `<span class="active-term-name">` computed
`rgb(0,107,183)` (the link's own accent) on a header painted `rgb(15,108,189)` — blue on blue.

Fix: colour the slotted spans and the icon **directly**, with `var(--colorNeutralForegroundOnBrand, #ffffff)` — the
sibling of the very token that paints the header (`--colorBrandBackground` = `#0f6cbd`).

### 2.3 The folder-tab chrome — a v4 token v5 does not define

v5 defines **no** `--neutral-*` or `--accent-*` custom property at all: it ships **481** design tokens under the
Fluent v9 `--color*` vocabulary. A `var()` naming a token that does not exist makes its **whole declaration** invalid at
computed-value time, and the declaration is dropped **silently** — no build error, no console warning, invisible to
bUnit. So `border: 1px solid var(--neutral-stroke-rest)` produced `border-top-width: 0px`, while the same rule's
`border-radius`, `padding` and `background` still applied (the last only because of its fallback): "half-styled, no
folder chrome".

### 2.4 The trap that hid the fix itself (self-inflicted — caught by measuring)

Writing the fix, the comment text `--neutral-*/--accent-*` contains a `*/`; inside a CSS comment that **closes the
comment early**, and the leftover text is parsed as CSS. The base `.active-term-link` rule was consumed as garbage, its
`--term-fg` never reached the DOM, and the component's `var(--term-fg)` declarations became invalid — the pill stayed
blue across two "fixed" builds. What exposed it was the live re-measurement showing `--term-fg` **empty** while the rule
was demonstrably present in the served bundle. Now guarded (§4).

## 3. The wider finding (not fixed here — backlog §M)

The dead-token class is **repo-wide**: **102** `var(--neutral-…)`/`var(--accent-…)` usages **without a fallback** across
the CSS, plus **207** with fallbacks (which now always take the fallback, so the design system no longer drives them).
Two of the 102 were visible defects; the rest are latent. The replacement vocabulary is `--color*`; counts, recipe and
the verification method are in the backlog rather than here, because that work is unstarted.

## 4. Changes

| File | Change |
|---|---|
| `src/SchoolCollab.Admin.Shared/Components/RowActionsMenu.razor` | wrap the items in `<FluentMenuList>`; correct the two comments that asserted "the menu always renders inline" |
| `src/Students/SchoolCollab.Students.Application/Components/Toolbar/ActiveTermToolbar.razor.css` | colour the slotted spans and `::deep svg` with `--colorNeutralForegroundOnBrand`; drop the ignored token push on the wrapper |
| `src/SchoolCollab.Admin.Shared/Components/FluentTabStrip.razor.css` | repoint the tab/panel borders and the selected fill at `var(--colorNeutralStroke1, #d1d1d1)` / `var(--colorNeutralBackground1, #ffffff)` |
| `tests/SchoolCollab.Admin.Tests.Unit/RowActionsMenuTests.cs` | new `Kebab_KeepsItsItemsInsideAFluentMenuList` (structure, not presence) |
| `tests/SchoolCollab.ArchitectureTests.Unit/RazorMarkupHygieneArchitectureTests.cs` | new `NoStrayCommentTerminator_InCss` (holds tree-wide) and `RepointedFiles_UseTheV5ColourTokens_WithNoBareLegacyToken` |

Diff: `documents/rounds/diffs-fluentui-v5-postfix.patch` (5 files, +214 / −36).

## 5. Verification

Build and suite, on the final tree:

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `dotnet test` | **4097 passed, 3 skipped, 2 failed** |
| ArchitectureTests | **116/116** (was 114) |
| `SchoolCollab.Admin.Tests.Unit` | **699/699** (was 698) |

The two failures are the known **opt-in live-external-AI** tests
(`OpenRouterClient_UsesApiKey_AuthenticatesSuccessfully`,
`OpenRouterProvider_WithGoogleGemma4_RespondsToSimplePrompt`) — the same two that fail on the pre-round baseline.

Live, against the running AppHost with the **HTTP cache disabled** (so a stale bundle cannot be mistaken for a fix):

| Check | before | after |
|---|---|---|
| `fluent-menu-list` | absent | present, 5 items |
| menu display (closed → clicked) | `inline-block` → `inline-block` | `none` → `grid`, `:popover-open` |
| menu painted on top? | — | `elementsFromPoint` at its centre returns `fluent-menu-item` |
| pill span / icon | `rgb(0,107,183)` on `rgb(15,108,189)` | `rgb(255,255,255)` on `rgb(15,108,189)` |
| `--term-fg` | *unset* (its rule was swallowed by the stray `*/`) | `#ffffff` |
| tab border / selected bottom | `0px` / — | `1px rgb(209,209,209)` / `rgba(0,0,0,0)` |
| console errors | 0 | 0 |

Parent-side screenshots (`C:\Users\skwar\AppData\Local\Temp\pi-round1\`): `zoom-tabs.png` (2× — folder chrome with v5's
blue active underline), `menu-element.png` (the open kebab), `postfix-pill.png`.

**Guard A/B (honest tests).** Both new guards were proven discriminating by reintroducing each defect and watching them
fail **by name**: `NoStrayCommentTerminator_InCss` reported `ActiveTermToolbar.razor.css:17`, and
`Kebab_KeepsItsItemsInsideAFluentMenuList` failed without the list wrapper. Both files were then restored from
byte-identical backups and re-run green.

## 6. Deliberately not fixed

- **The v5 blue active-underline on the folder tab is kept** (Q84: the folder *structure* with v5 look and feel). v5
  draws its indicator shadow-internally with no part and no exportparts, so it cannot be re-pointed from consumer CSS.
- The repo-wide dead-token sweep (backlog §M), the dialog-shape leg (§C), and the rest of the queue are unchanged.
