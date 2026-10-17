---
name: fluentui-component-props
description: |
  Validates FluentUI Blazor **v5** component property values to prevent compile
  errors and silent rendering failures. v5 gives each component its own appearance
  enum — `BadgeAppearance`, `ButtonAppearance`, `LinkAppearance`, `FluentInputAppearance` —
  so a v4 value is a **compile** error instead of v4's runtime throw. Triggers:
  "FluentBadge", "FluentButton", "FluentLink", "BadgeAppearance", "ButtonAppearance",
  "FluentInputAppearance", "LinkAppearance", "MessageBarIntent", "FluentMessageBar",
  "component property", "invalid Appearance", "FluentUI validation error",
  "badge appearance error", "button appearance error", plus the v4 spellings kept
  as discovery triggers: "Appearance", "MessageIntent", "FluentAnchor", "FluentTextField".
---

# FluentUI Blazor Component Property Validation (v5)

v4 had a single shared `Appearance` enum, so a wrong value was a **runtime**
`ArgumentException`. v5 gives each component its own appearance enum, so the same
mistake is now a **compile** error — the page cannot build, which is friendlier than
crashing at runtime, but it means an unfixed v4 value silently blocks the build of
whatever project it lives in (see the MAUI-head caveat below).

Values below are verified against the installed 5.0.1 package
(`~/.nuget/packages/microsoft.fluentui.aspnetcore.components/5.0.1/lib/net10.0/*.xml`)
and the `v5.0.1` tag sources. Where something was **not** verified in this pass it is
marked *(unverified)* — check it against the XML before relying on it.

---

## 🔤 Per-component appearance enums (this is the big v4 → v5 change)

| Component | v5 enum | Valid values |
|---|---|---|
| `FluentBadge` | `BadgeAppearance?` | `Filled`, `Ghost`, `Outline`, `Tint` |
| `FluentButton` | `ButtonAppearance?` | `Default`, `Outline`, `Primary`, `Subtle`, `Transparent` |
| `FluentLink` | `LinkAppearance` (non-nullable, defaults `Default`) | `Default`, `Subtle` |
| Input components | `FluentInputAppearance` | `Filled`, `Outline` (default) |

There is **no** `Appearance.Accent` / `.Neutral` / `.Lightweight` / `.Stealth` /
`.Hypertext` on these parameters any more. The v4 → v5 value map:

| v4 `Appearance` | v5 replacement |
|---|---|
| `Accent` | `ButtonAppearance.Primary` (buttons) — for a badge, use `BadgeAppearance.Filled` + `BadgeColor` |
| `Neutral` | `ButtonAppearance.Default` / `BadgeAppearance.Filled` |
| `Lightweight` | `ButtonAppearance.Transparent` or `.Subtle` / `LinkAppearance.Subtle` |
| `Stealth` | `ButtonAppearance.Subtle` |
| `Outline` | `ButtonAppearance.Outline` / `BadgeAppearance.Outline` |
| `Hypertext` | `LinkAppearance.Default` (a link, not a button) |
| `Filled` | `BadgeAppearance.Filled` / `FluentInputAppearance.Filled` |

### FluentBadge — `Appearance` + `Color`

```razor
@* ✅ v5 *@
<FluentBadge Appearance="BadgeAppearance.Filled" Color="BadgeColor.Success">Published</FluentBadge>
<FluentBadge Appearance="BadgeAppearance.Outline" Color="BadgeColor.Danger">Failed</FluentBadge>
<FluentBadge Appearance="BadgeAppearance.Ghost">Draft</FluentBadge>
```

- The badge **text** is now the `Content` parameter; `ChildContent` is the element the
  badge *wraps* (e.g. its positioning anchor).
- `Color` is typed `BadgeColor` (a string no longer works): `Brand`, `Danger`,
  `Important`, `Informative`, `Severe`, `Subtle`, `Success`, `Warning`.
- **Removed in v5**: `OnClick` ("a Badge should not be used as a button"), `Fill`,
  `Circular` (use `Shape`), `Width`/`Height` (use `Size`), `DismissIcon`/`DismissTitle`/`OnDismissClick`.

### FluentButton — `Appearance`

```razor
@* ✅ v5 *@
<FluentButton Appearance="ButtonAppearance.Primary">Save</FluentButton>
<FluentButton Appearance="ButtonAppearance.Outline">Cancel</FluentButton>
<FluentButton Appearance="ButtonAppearance.Transparent">Icon-only</FluentButton>
```

`BackgroundColor` (a string) overrides `Appearance` for the button background.

### Input components — `FluentInputAppearance`

Used by `FluentTextInput`, `FluentNumberInput`, `FluentTextArea`, `FluentAutocomplete`,
`FluentDatePicker`, `FluentTimePicker` (and `FluentSearch`'s replacement, also
`FluentTextInput`).

```razor
@* ✅ v5 *@
<FluentTextInput Appearance="FluentInputAppearance.Filled" />
<FluentTextInput Appearance="FluentInputAppearance.Outline" />

@* ❌ compile error — a button/link appearance value is not a FluentInputAppearance *@
<FluentTextInput Appearance="ButtonAppearance.Primary" />
```

### Components with NO `Appearance` property

`FluentCheckbox` and `FluentSwitch` have **no `Appearance` parameter** — setting one is a
compile error (unrecognized parameter), not a silent no-op.

---

## 📋 Other component-specific enums

### FluentMessageBar — `Intent` (type `MessageBarIntent`)

| Value | Use for |
|---|---|
| `Info` | Informational messages (default) |
| `Warning` | Caution/warning messages |
| `Error` | Error states |
| `Success` | Confirmation/success messages |
| `Custom` | Custom-styled messages |

```razor
<FluentMessageBar Intent="MessageBarIntent.Error">Something failed.</FluentMessageBar>
<FluentMessageBar Intent="MessageBarIntent.Success">Saved successfully.</FluentMessageBar>
```

> The v4 type was spelled `MessageIntent`; v5 is `MessageBarIntent` (the 5.0.1 package has
> no `MessageIntent` members at all). v5 also exposes `Title`, `Layout` and
> `MessageBarInstance` — *(unverified)* check the XML before using them.

### FluentSpinner — `Stroke`

`FluentProgressRing` is **removed** in v5; the replacement is `FluentSpinner`, which keeps
the `Stroke` sizing property (it also has `Size` and `AppearanceInverted`):

| Value | Visual size |
|---|---|
| `Small` | Thin ring |
| `Normal` | Standard ring (default) |
| `Large` | Thick ring |

### FluentDataGrid — key properties

*(unverified in the v5 pass — confirm against the XML before relying on the defaults)*

| Property | Type | Values | Default |
|----------|------|--------|---------|
| `DisplayMode` | `DataGridDisplayMode` | `Grid`, `Table` | `Grid` |
| `RowSize` | `DataGridRowSize` | `Smaller`(24), `Small`(32), `Medium`(44), `Large`(58) | `Small` |
| `ResizeType` | `DataGridResizeType?` | `Discrete`, `Exact` | null |
| `GenerateHeader` | `GenerateHeaderOption?` | `None`, `Default`, `Sticky` | `Default` |
| `SelectMode` | `DataGridSelectMode` | `Single`, `SingleSticky`, `Multiple` | — |

### FluentDialog — options via `DialogOptions` (v5)

v4 configured a dialog with `DialogParameters` plus a `DialogType` enum. In v5 that is a
single `DialogOptions` object — `Header.Title`, `Header.CloseAction`, `Footer.*Action`,
`Width`/`Height`, `Modal`, `PreventDismissOnEscape`, `Alignment` — and the old `DialogType`
values map as:

| v4 `DialogType` | v5 |
|-----------------|-----|
| `Dialog` | `DialogOptions` with `Modal = true` → renders `type="alert"` |
| `MessageBox` | a modal `ConfirmDialog` (`ShowConfirmDialogAsync`) |
| `Panel` | `DialogOptions.Alignment` + a `<fluent-drawer>` |
| `SplashScreen` | no direct v5 equivalent — render a full-screen overlay |

Two v5 gotchas with no v4 counterpart:
- **No `PreventDismissOnOverlayClick`** — the posture follows from the type (`type="modal"`
  keeps the backdrop and stays dismissible; `type="alert"` swallows the overlay click).
- **The dialog header (title + close-X) renders only inside `<FluentDialogBody>`**
  (`FluentSlot.DialogTitle` / `DialogClose`), and `DialogOptions.Header.CloseAction.Visible`
  defaults to **false**. A dialog that is not wrapped in the repo's shared `DialogShellBody`
  is title-less and X-less with no error — see `.github/skills/dialog-ui/SKILL.md`.

### `Color` enum (used by `FluentIcon.Color`, `IconColor`, …)

v5 members: `Neutral`, `Accent`, `Primary`, `Default`, `Lightweight`, `Disabled`,
`Custom`, `Warning`, `Info`, `Error`, `Success`, `Fill`, `FillInverse`, `ErrorInverted`,
`InfoInverted`, `SuccessInverted`, `WarningInverted`.

Note both `Neutral` **and** `Default` exist here (unlike the appearance enums, where
`Neutral` is gone), and there is **no `Red`/`Green`** — use the semantic members.

> For **badge** colour use `BadgeColor`, not `Color`. The repo's own convention for an
> Active/Inactive badge is `BadgeAppearance` + `BadgeColor` (see
> `StatusBadgeColourArchitectureTests`).

### TextAreaResize (used by `FluentTextArea.Resize`)

`Horizontal`, `Vertical`, `Both` — *(unverified)*.

---

### `FluentMenu` — the items belong in a `FluentMenuList`

v5 hides `fluent-menu-list:not([popover])` in its own stylesheet, and `FluentMenu`'s JS toggles that popover state from
the trigger (`Trigger="<element-id>"` — `Anchor` was renamed, and `UseMenuService` no longer exists). Items placed
**directly** under `<FluentMenu>` are therefore never hidden: every menu renders as an always-visible inline list and the
trigger does nothing — the post-merge “kebab menu is broken” defect (2026-10-17).

```razor
<FluentMenu Id="@_menuId" Trigger="@_buttonId" OpenedChanged="@(o => _open = o)">
    <FluentMenuList>
        <FluentMenuItem Label="Edit" OnClick="@(async () => await EditAsync())" />
    </FluentMenuList>
</FluentMenu>
```

For the CSS side of v5 — `--color*` tokens only, and the silent death of an unresolvable `var()` — see
`.github/copilot/rules/blazor-components.md` § "FluentUI v5 tokens: only `--color*` exists".

## ✅ Quick validation checklist

1. **`<FluentBadge Appearance="…">`** → only `BadgeAppearance.*` (`Filled`/`Ghost`/`Outline`/`Tint`); colour via `BadgeColor`, text via `Content`.
2. **`<FluentButton Appearance="…">`** → only `ButtonAppearance.*` (`Default`/`Outline`/`Primary`/`Subtle`/`Transparent`).
3. **`<FluentLink Appearance="…">`** → only `LinkAppearance.*` (`Default`/`Subtle`).
4. **Input components** → `FluentInputAppearance.Filled` / `.Outline`, never a button/link value.
5. **`<FluentCheckbox>` / `<FluentSwitch>`** → no `Appearance` property exists.
6. **`<FluentMessageBar Intent="…">`** → `MessageBarIntent.*` (`Info`/`Warning`/`Error`/`Success`/`Custom`).
7. **Any `Appearance="Appearance.…"`** → a **v4 leftover**. It is a compile error; fix it
   with the map above. Beware: it can hide in a project outside the main solution (the MAUI
   head `src/Mobile/SchoolCollab.Mobile`, which builds only from its own `.slnx`), so a green
   `dotnet build SchoolCollab.slnx` does not prove it is gone.

---

## Common mistakes

| ❌ Wrong | ✅ Correct | Why |
|---------|-----------|-----|
| `<FluentBadge Appearance="Appearance.Accent">` | `<FluentBadge Appearance="BadgeAppearance.Filled" Color="BadgeColor.Brand">` | `Appearance` is not a v5 badge enum |
| `<FluentBadge Appearance="BadgeAppearance.Filled" Color="red">` | `Color="BadgeColor.Danger"` | `Color` is a typed enum, not a CSS colour string |
| `<FluentButton Appearance="Appearance.Accent">` | `<FluentButton Appearance="ButtonAppearance.Primary">` | value renamed |
| `<FluentButton Appearance="Appearance.Outline">` | `<FluentButton Appearance="ButtonAppearance.Outline">` | enum type changed |
| `<FluentTextInput Appearance="ButtonAppearance.Primary" />` | `Appearance="FluentInputAppearance.Filled"` | wrong enum type |
| `<FluentMessageBar Intent="MessageIntent.Error">` | `Intent="MessageBarIntent.Error"` | type renamed in v5 |
| `<FluentCheckbox Appearance="…">` | remove `Appearance` | Checkbox has no such property |
| `<FluentProgressRing Stroke="…">` | `<FluentSpinner Stroke="…">` | component renamed in v5 |
| `<FluentAnchor …>` | `<FluentLink …>` (or `FluentAnchorButton`) | component removed in v5; only `FluentLink` has `OnClick` |
