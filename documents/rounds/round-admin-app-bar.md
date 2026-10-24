Provider: pi, light round Tier 2, 2-model set (owner 2026-10-23) — plan gate + diff review + acceptance on `ollama-cloud/deepseek-v4-pro:0813`, worker on `ollama-cloud/deepseek-v4.1-flash:1017`. Parent (plan author) runs `clinepass/cline-pass/deepseek-v4.1-flash-1017`, so the gate is never the author.
Mode: attended

# Round — admin app bar: legible brand and page title (#402)

## Goal

The one header every page wears stops lying about where you are. No text in it is truncated at the
default and 1280px viewports, the page title is **the route's own title** — not a second copy of the
brand — and the bar has one hierarchy: brand · page title on the left, contextual controls on the
right. Issue **#402** is the record; this round implements it.

"The route's title" is the **static** route title, not the per-record `<PageTitle>`: the document
title keeps `Edit Student — Jane Doe`, the bar reads `Edit Student`. That divergence is deliberate —
a layout cannot read a page's `<PageTitle>` — and acceptance clause 2 names a list page, so nothing
here promises record-specific text.

Scope is the **chrome only** — `MainLayout`'s header, the `ThemeSwitcher` it hosts, the active-term
chip and the dev tenant switcher. Not the landing action bars, not the rich-text toolbar, not the
panel layout.

## Why it is broken — two independent causes, both confirmed in the source

**C1 — the title text is wrong, so the bar renders the brand twice.**
`ResolveTitle` (`MainLayout.razor:82-100`) is a hand-rolled `path switch` with 14 arms; the repo has
**58 `@page` routes**. `/notification-templates`, `/config-flags`, `/entity-code-rules`, `/tenants`,
`/activity-groups`, `/assignments/{Id:guid}` and most others match nothing, so `_pageTitle` stays `null` and
`@(_pageTitle ?? DefaultTitle)` (`:23`) renders `DefaultTitle`, which is `"School Collab"`
(`:73`) — the brand string. The bar therefore reads `School Collab · School Collab`, and that
doubled string is what the ellipsis then cuts. The screenshot in #402 shows exactly this.

**C2 — nothing in the row yields width, so the title absorbs the whole deficit.**
The 50px row (`:15`) holds, at fixed cost: the `ThemeSwitcher` (a subtle button **plus a 120px
accent `FluentSelect`**, `ThemeSwitcher.razor:38`), the active-term chip (`ActiveTermToolbar`), the
dev-tenant `FluentSelect` (**180px**, `DevTenantSwitcher.razor:70`) and the settings gear. `.brand`
and `.brand-sep` are `white-space: nowrap` (`app.css:241-250`); the page title is the only element
that responds to pressure, and it does so by clipping — `max-width: 32ch; overflow: hidden;
text-overflow: ellipsis` (`MainLayout.razor.css:71-77`). Truncation is therefore *designed in*, not
an accident, and 368px of the row (120 + 180 + gear + paddings) is spent before the title gets any
room. The only width rule that hides anything is the 640px breakpoint (`app.css:273-274`), far below
the 1280px the acceptance names.

## Plan

### W1 — Resolve a real title for every route, and never fall back to the brand
`src/SchoolCollab.Admin/Components/Layout/MainLayout.razor`

- Complete the table so **every** `@page` route in `src/**` resolves to a title. The table stays a
  static map (no JS, no per-page edits, no `document.title` read): it is the only option that needs
  zero page churn — 38 pages already carry `<PageTitle>`, but that text is not reachable from a
  layout, and introducing a title service would mean editing every page, which is not this issue.
- The fallback stops being the brand: an unmapped route renders **no title and no separator**, rather
  than a second "School Collab".
- **Conformance test** (source-level, matching the style `MainLayoutProvidersTests` already uses):
  scan the `@page` templates in **the four assemblies the Admin `Router` actually routes**
  (`Routes.razor:9-10`: `Program`'s assembly + `Settings.Application`, `Assignments.Application`,
  `Students.Application`) and fail when one resolves to no title. **Not** `src/**/*.razor`: Families
  and Mobile routes never render under `MainLayout` (Families is not a project reference of
  `SchoolCollab.Admin`), and their templates collide with Admin's (`/`, `/assignments`, `/Error`).
  That scoping is what stops the table from silently falling behind the next page someone adds,
  without demanding titles for routes this bar never shows.

### W2 — Give the row a priority order, and buy back the fixed cost
`MainLayout.razor`, `MainLayout.razor.css`, `SchoolCollab.Admin.Shared/wwwroot/app.css`,
`src/SchoolCollab.Admin.Shared/Components/Theme/ThemeSwitcher.razor`

- **Left cluster** (brand · separator · title) becomes one flex child: `flex: 0 1 auto; min-width: 0`.
  The title loses its `max-width: 32ch` cap and keeps `text-overflow: ellipsis` only as a defensive
  last resort — no rule may clip it before every other element has yielded.
- **Right cluster** keeps `flex: 0 0 auto` and a fixed reading order: term chip · tenant switcher ·
  theme · settings.
- **Recover the accent picker's 120px — but only if W2's measurement says it is needed.** The mode
  button stays in the bar and still cycles the theme in one click. If, after the title table and the
  flex rules, a real deficit remains at 1280px, the accent `FluentSelect` moves behind that button
  (popover/menu) — the repo-standard pattern, and the least-used control in the row. If the deficit
  is already gone, the cheaper move wins: shrink the accent control to an icon-only swatch (~90px
  back) or leave it. **A measured deficit decides, not this paragraph.**
- **Height does not change** — the bar stays 50px, so `--app-bar-height` and the sticky offsets
  (`panel-layout-pattern.md` §6) are untouched. If the worker finds a height change unavoidable, the
  same change **must** update `--app-bar-height` in `Admin.Shared/wwwroot/app.css` — issue acceptance
  requires it, not a follow-up.
- **Tokens only, no literal colours** — the bar already paints `--colorBrandBackground`; text uses
  `--colorNeutralForegroundOnBrand` with the literal fallback the term chip established
  (`ActiveTermToolbar.razor.css:28-33`).

### W3 — Tests
`tests/SchoolCollab.Admin.Tests.Unit/` — new `MainLayoutAppBarTests` beside the existing
`MainLayoutProvidersTests` / `RoutesShellRenderTests`:

| Assertion | Why |
|---|---|
| A long title's **text node equals the full route title** and the brand renders exactly once (bUnit cannot see CSS clipping — the screenshot is the truncation proof) | pins C1 |
| An unmapped route renders no page title and **no** separator | pins the fallback rule |
| Source-level: every `@page` route in `src/**` resolves to a non-empty title | pins the table against drift |
| No `.brand-page` rule in any `.razor.css`/`app.css` sets a `max-width` clip | pins C2's CSS half |
| `--app-bar-height` still matches the header `Height` | pins the sticky contract (`§6`) |

### W4 — Evidence for the PR
One screenshot of `/notification-templates` (the longest real title the issue names) at **1280px** and
at the default viewport, light and dark. The bUnit suite proves text and rules; only a rendered frame
proves "not truncated", so the screenshot is the acceptance evidence, produced with the repo's existing
Playwright probe (`school-collab-ui-smoke-probe`) rather than a new harness.

## Out of scope — do not touch

- The rich-text editor toolbar (`RichInstructionEditor.razor`, `ql-*` are Quill's handler contract) —
  #402 fences it explicitly.
- The panel layout pattern and the authoring surfaces (`panel-layout-pattern.md` is settled).
- Moving the page title out of the bar into a shell/breadcrumb — that is the panel-rollout story, not
  this fix.
- `IPageTitleService` / per-page title edits (38 pages), `document.title` JS interop.
- The tenant switcher's behaviour, its 180px width, or `tenant-default-dropdown-action.md`'s FR-1…FR-6.
- The nav menu, the `page-toolbar`/`page-footer` slots, and every page's own toolbar contents.
- The 640px breakpoint that hides the title on phones.

## Acceptance (issue #402, restated as checks)

1. No text in the bar is truncated at the default and at 1280px — proved by the rendered frames in
   W4 for the longest real title, stated as representative rather than universal (bUnit cannot see CSS
   ellipsis; one screenshot cannot prove every page).
2. The page title renders in full for a long title (`Notification templates`).
3. Legible in light and dark, tokens only, no literal colours.
4. `--app-bar-height` updated in the same change **if** the bar's height changed.
5. A bUnit assertion that the page title renders in full and the brand is present.

## Plan review iteration 1 — gate PASS-WITH-FINDINGS (2026-10-24)

Independent Tier-2 plan gate (`ollama-cloud/deepseek-v4-pro:0813`). Verdict: **PASS-WITH-FINDINGS**;
the findings above are already folded in, so this revision is what the worker implements.

| # | Finding | Resolution in this revision |
|---|---|---|
| P1 (major) | The conformance test scanned `src/**` while the Admin `Router` routes only four assemblies, so Families/Mobile routes and their template collisions would pollute the table | W1 now scopes the scan to `Routes.razor:9-10`'s four assemblies |
| P2 (minor) | Two C1 examples were wrong: `/students/periods` resolves via the `StartsWith("/students/")` arm, and `/families` is not an `@page` route at all | C1's example list replaced with genuinely unmapped routes (`/notification-templates`, `/config-flags`, `/entity-code-rules`, `/tenants`, `/activity-groups`, `/assignments/{Id:guid}`) |
| P3 (minor) | W3's "renders in full in the markup" overclaims — bUnit cannot see CSS ellipsis | Reworded to "text node equals the full route title"; the screenshot carries the truncation claim, and acceptance clause 1 now says so explicitly |
| P4 (minor) | "The page's real title" promised per-record titles the static map cannot produce | Goal now names the **static route title** and records the divergence from `<PageTitle>` as deliberate |

Adjudications carried into the plan: **R-a** the popover is a conditional fallback for a measured
deficit, not a default; **R-b** the worker reports per-element computed widths — an unaccounted fixed
cost is a finding; **R-c** the route matcher wildcards every `{…}` segment and shares `ResolveTitle`'s
predicate.

## Risks the gate should weigh

- **R-a — the popover is a UX change, and it is conditional.** Moving the accent picker behind the
  mode button changes a control no issue names, so it happens **only** when the measured deficit at
  1280px requires it; otherwise the icon-swatch (or no change) wins. The gate's ruling stands: the
  popover is the fallback for a real deficit, not a default.
- **R-b — 1280px arithmetic, and the unaccounted cost.** The row's fixed cost is **not** 120+180+gear+
  paddings — that does not reproduce the clip the issue captured, and the active-term chip is
  variable-width (`2025–2026 · Term 1` / `No tenant` / `Loading term…`) while the tenant switcher is
  dev-mode-only. The cramped condition is therefore *dev mode + real tenant + a long term name*. The
  worker **must measure per-element computed widths at 1280px and report them**; "a 21-char title
  fits" is a hypothesis until those numbers exist, and any unaccounted fixed cost is a finding, not a
  rounding error.
- **R-c — the conformance test's route scan.** `@page` templates include parameterised routes
  (`/students/{id:guid}/edit`), and parameter **name and case vary** (`{id:guid}` vs `{Id:guid}`), so a
  literal-template comparison would both over- and under-match. The rule to encode: reduce every
  `{…}` segment to a wildcard, then a route resolves iff some arm's predicate matches its segment
  shape — the same predicate `ResolveTitle` uses (invoke it reflectively, or share it). Never demand
  one literal arm per route.

## Implementation (worker, `ollama-cloud/deepseek-v4.1-flash:1017`)

| Workstream | Landed |
|---|---|
| W1 | `ResolveTitle` is `public static`, covers **46 / 46** routed `@page` templates across the four Admin-Router assemblies; `DefaultTitle` deleted, so an unmapped route renders no title and no separator |
| W2 | brand · separator · title are one `.app-bar-title-cluster` (`flex: 0 1 auto; min-width: 0`); brand and separator `flex: 0 0 auto`; `.brand-page`'s `max-width: 32ch` **deleted**; `ThemeSwitcher` moved into the trailing cluster |
| W3 | `MainLayoutAppBarTests` — title text + brand-once, unmapped route, route-table conformance, no `max-width` cap, header height == `--app-bar-height` |
| W4 | **not yet produced** — the rendered frames need the running Admin rebuilt (see *Blocked gates*) |

**R-b measurement (source-derived, 1280px viewport):** inner row 1248px against ≈1001px of fixed cost
→ **≈247px slack, no deficit**, so the accent picker stays in the bar (R-a resolved as "no change").
Unaccounted cost found and reported, not silently fixed: the settings gear is **~96px** (`fluent-button`
`min-width`; `.toolbar-icon-button` has no CSS rule anywhere), not the ~32px the plan assumed.

## Diff review (iteration 1) — APPROVE-WITH-NOTES

`ollama-cloud/deepseek-v4-pro:0813`, static diff-only. All five acceptance clauses met; clause 4 is
N/A-satisfied (header `Height="50"` == `--app-bar-height: 50px`); no real route resolves to null (it
re-enumerated all 46 templates). Three minor findings, all fixed by the parent in the same change:

| # | Finding | Fix |
|---|---|---|
| D-1 | `/students/grade-levels/{Id}` (a read-only detail page) rendered **"Edit Grade Level"** — the bar lying about where you are | the arm now splits on `/edit`: `Edit Grade Level`, else `Grade Level` |
| D-2 | the conformance test asserted only *non-null*, which is how D-1 passed | new `ResolveTitle_Names_A_Detail_Route_And_Its_Edit_Route_Distinctly` pins detail-vs-edit and a template detail route |
| D-3 | the CSS scanner's flat regex skipped **every** `@media`-scoped rule — and `app.css` already has a media block for `.brand-page` | replaced with a brace-counting `CssBlocksFor`, which reaches a nested rule at any depth |

## Blocked gates (owner action, not a code defect)

| Gate | State |
|---|---|
| `dotnet build SchoolCollab.slnx` (literal) | **blocked by file locks** — `MSB3027`: `Microsoft Visual Studio` (3024), `SchoolCollab.Auth` (33664), `SchoolCollab.AI.Server` (38840) hold `bin/**`. Same build to a redirected output dir: **succeeded, 0 errors** |
| `MainLayoutAppBarTests` suite | built and **818 / 0** on the pre-D-1/D-2/D-3 tree; the three review fixes compile (redirected build) but **the suite has not been re-run against them** — a redirected-output run fails 180 source-scanning tests, because they resolve the repo relative to `AppContext.BaseDirectory` |
| W4 rendered frames | **not produced** — needs the Admin rebuilt and restarted with this branch, which the same locks prevent |

## Owner amendment (2026-10-24) — the bar carries context only

> "make the themeswitcher a menu contextmenu dropdown. keep settings + themeswitcher in the bottom of
> the left sidebar of the portal, to clean out the top toolbar."

This supersedes W2's "recover the accent picker's 120px, conditionally". The bar now holds **context
only**, and both portal-wide utility controls moved out of it:

| Control | Before | Now |
|---|---|---|
| Theme mode + accent | mode button + 120px accent `FluentSelect` in the bar | one **menu** in the sidebar footer: three modes, then six accents, tick on the active one; trigger text reports the live mode (`☾ Dark`) |
| Settings | 96px gear button in the bar | 96px icon button beside the theme menu in the sidebar footer |
| Bar's right side | chip · tenant · theme · gear (measured **1039px**, over a 1248px row) | chip · tenant (**338px**) |

Why this also fixes a defect the first pass only half-fixed: the first rendered probe found the theme
stack claiming the **whole 629px** trailing cluster (FluentStack's inline `width:100%` beat the
non-important override), stretching the accent select and pushing the gear off-screen. Chasing that
with `!important` would have kept 216px of bar space for two rarely-used controls. Moving them out
deletes the contest instead: the rule and its `!important` are gone with the control.

### Second rendered pass (evidence, 1280px and 1600px, light + dark)

| Route | Title rendered | Clipped | Row overflow |
|---|---|---|---|
| `/notification-templates` | `Notification Templates` | **no** | **0px** |
| `/students/grade-levels` | `Grade Levels` | **no** | **0px** |
| `/students/periods` | `Periods` | **no** | **0px** |
| `/config-flags` | `Feature Flags` | **no** | **0px** |
| `/tenants` | `Tenants` | **no** | **0px** |

- `headerHasTheme: false`, `headerHasSettings: false` — the bar carries no utility control.
- `.nav-footer` (sidebar, bottom): 224×49px, text `☾ Dark`, hosting the theme menu + the settings
  shortcut; menu items `System · Light · ✓ Dark · Teams · Word · Excel · PowerPoint · Outlook · OneNote`.
- **Live theme flip proven**: opening the menu and choosing Dark set `body[data-theme]` from `null` to
  `dark`; 0 console errors.
- Frames: `C:/Users/skwar/AppData/Local/Temp/pi-round1/v2-*` (bar at 1280 for three routes, bar at
  1600 light/dark, sidebar light/dark, menu open).

### Gates after the amendment

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `SchoolCollab.Admin.Tests.Unit` | **818 / 0 failed** |
| Mobile head (`-m:1 -p:BuildInParallel=false`) | **0 errors** — `ThemeSwitcher` is an `Admin.Shared` component, so the head compiles it |
| Full `dotnet test SchoolCollab.slnx` | **not re-run** after the amendment; before it, 4661 passed with the 2 known live-OpenRouter environment failures |

**Test moved, not deleted silently:** the bUnit interaction test for picking a mode was removed —
`FluentMenuItem` publishes through FluentUI's own `onmenuitemchange` event and a bUnit `Click()` cannot
drive it (`MissingEventHandlerException`). The interaction is proven **live** in the rendered probe
above instead, and the unit suite keeps the structural test (three modes + six accents present, trigger
reports the mode).

## UX theming defect found in the delivered bar (owner report, 2026-10-24)

> "Portal Name + page title color and background doesn't meet UX theming."

Verified against the running app in both themes — the report is correct, and it is a **contrast**
defect, not a taste one:

| Theme | Bar background | `.brand` colour | Brand contrast | Page-title contrast |
|---|---|---|---|---|
| light/system | `#0f6cbd` | **`#242424`** | **2.88 : 1** ✗ | **2.55 : 1** ✗ |
| dark | `#115ea3` | `#ffffff` | 6.66 : 1 ✓ | 5.30 : 1 (marginal) |

**Cause.** The *background* is themed — FluentUI paints the header region with the brand surface and
it follows the mode/accent. The *text* was never tied to it: `.brand`, `.brand-sep`, `.brand-page`
(`app.css:241-257`) declared **no `color`**, so they inherited `--colorNeutralForeground1`, which is
`#242424` in light mode. Dark mode passed only because that token happens to be white there. On top of
that, `.brand-page`'s `opacity: 0.85` cost a whole contrast point even once the colour was right
(white-on-`#0f6cbd` = 5.33 → 0.85 alpha composites to 4.32, still under AA).

**Fix.**
- `.brand`, `.brand-sep`, `.brand-page` take `var(--colorNeutralForegroundOnBrand, #ffffff)` — the
  token FluentUI already defines (measured `#ffffff` in both modes).
- `.layout-header-stack` sets the same colour on the bar's container, so a future child that declares
  no colour inherits the on-brand foreground instead of the page's.
- `.brand-page`'s `opacity: 0.85` **removed**; hierarchy now comes from weight (400 vs 600) and size
  (0.9 vs 0.95rem). `.brand-sep` keeps `opacity: .5` — it is `aria-hidden` decoration.
- Guard test `AppBar_Text_Takes_TheOnBrandForeground_InBothThemes`: the on-brand token must appear in
  `.brand` and in `.layout-header-stack`, and **no** rule for `.brand-page` may carry `opacity`.

**Re-verified after the fix:**

| Theme | Brand | Page title | Separator (decorative, aria-hidden) |
|---|---|---|---|
| light | **5.38 : 1** ✓ | **5.38 : 1** ✓ | 2.50 : 1 |
| dark | **6.66 : 1** ✓ | **6.66 : 1** ✓ | 2.87 : 1 |

Both text elements now clear WCAG AA (4.5:1) in both themes; the separator is excluded by 1.4.3 as
decorative. Frames: `ux-bar-light.png`, `ux-bar-dark.png`. Suite 819 / 0.
