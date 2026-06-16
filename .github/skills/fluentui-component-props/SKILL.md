---
name: fluentui-component-props
description: |
  Validates FluentUI Blazor component property values to prevent runtime
  ArgumentException and silent rendering failures. Triggers: "FluentBadge",
  "FluentButton", "FluentAnchor", "Appearance", "FluentInputAppearance",
  "MessageIntent", "FluentMessageBar", "component property", "ArgumentException
  FluentUI", "FluentBadge Appearance", "invalid Appearance", "FluentUI
  validation error", "badge appearance error", "button appearance error".
---

# FluentUI Blazor Component Property Validation

Using the wrong property value on a FluentUI component causes either a runtime
`ArgumentException` (crash) or a silent rendering failure (wrong visual). This
skill documents the exact valid values for every component property that has
restricted enum values.

---

## ⚠️ Components That Throw at Runtime

These components validate their property values in `OnParametersSet()` and throw
`ArgumentException` if invalid. **They will crash the page.**

### FluentBadge — `Appearance`

**Valid**: `Accent`, `Lightweight`, `Neutral`  
**Invalid (throws)**: `Outline`, `Stealth`, `Hypertext`, `Filled`

```razor
@* ✅ correct *@
<FluentBadge Appearance="Appearance.Accent">Published</FluentBadge>
<FluentBadge Appearance="Appearance.Neutral">Closed</FluentBadge>
<FluentBadge Appearance="Appearance.Lightweight">Draft</FluentBadge>

@* ❌ throws ArgumentException at runtime *@
<FluentBadge Appearance="Appearance.Filled">Published</FluentBadge>
<FluentBadge Appearance="Appearance.Outline">Draft</FluentBadge>
<FluentBadge Appearance="Appearance.Stealth">Draft</FluentBadge>
```

### FluentButton — `Appearance`

**Valid**: `Neutral`, `Accent`, `Lightweight`, `Outline`, `Stealth`  
**Invalid (throws)**: `Filled`, `Hypertext`

```razor
@* ✅ correct *@
<FluentButton Appearance="Appearance.Accent">Save</FluentButton>
<FluentButton Appearance="Appearance.Outline">Cancel</FluentButton>
<FluentButton Appearance="Appearance.Stealth">Icon-only</FluentButton>

@* ❌ throws ArgumentException at runtime *@
<FluentButton Appearance="Appearance.Filled">Save</FluentButton>
<FluentButton Appearance="Appearance.Hypertext">Link-like</FluentButton>
```

---

## 🔤 Two Different Appearance Enums

FluentUI has **two separate** `Appearance` enum types. Using the wrong one causes
compile errors.

### `Appearance` enum (7 values)

Used by: `FluentBadge`, `FluentButton`, `FluentAnchor`, `FluentSelect`

| Value | Badge | Button | Anchor | Select |
|-------|-------|--------|--------|--------|
| `Neutral` | ✅ | ✅ | ✅ | ✅ |
| `Accent` | ✅ | ✅ | ✅ | ✅ |
| `Lightweight` | ✅ | ✅ | ✅ | ✅ |
| `Outline` | ❌ throws | ✅ | ✅ | ✅ |
| `Stealth` | ❌ throws | ✅ | ✅ | ✅ |
| `Hypertext` | ❌ throws | ❌ throws | ✅ | ✅ |
| `Filled` | ❌ throws | ❌ throws | ✅ | ✅ |

### `FluentInputAppearance` enum (2 values)

Used by: `FluentTextField`, `FluentNumberField`, `FluentTextArea`,
`FluentSearch`, `FluentAutocomplete`, `FluentDatePicker`, `FluentTimePicker`

| Value | All input components |
|-------|---------------------|
| `Filled` | ✅ |
| `Outline` | ✅ (default) |

```razor
@* ✅ correct — input components use FluentInputAppearance *@
<FluentTextField Appearance="FluentInputAppearance.Filled" />
<FluentTextField Appearance="FluentInputAppearance.Outline" />

@* ❌ compile error — Accent is not a FluentInputAppearance value *@
<FluentTextField Appearance="Appearance.Accent" />
```

### Components with NO Appearance property

`FluentCheckbox` and `FluentSwitch` have **no `Appearance` parameter** at all.
Setting one causes a compile error (unrecognized parameter).

---

## 📋 Other Component-Specific Enums

### FluentMessageBar — `Intent`

| Value | Use for |
|-------|---------|
| `Info` | Informational messages (default) |
| `Warning` | Caution/warning messages |
| `Error` | Error states |
| `Success` | Confirmation/success messages |
| `Custom` | Custom-styled messages |

```razor
<FluentMessageBar Intent="MessageIntent.Error">Something failed.</FluentMessageBar>
<FluentMessageBar Intent="MessageIntent.Success">Saved successfully.</FluentMessageBar>
```

### FluentMessageBar — `Type`

| Value | Use for |
|-------|---------|
| `MessageBar` | Top-of-screen/card banner (default) |
| `Notification` | Notification center item |

### FluentProgressRing — `Stroke`

| Value | Visual size |
|-------|-----------|
| `Small` | Thin ring |
| `Normal` | Standard ring (default) |
| `Large` | Thick ring |

### FluentDataGrid — Key properties

| Property | Type | Values | Default |
|----------|------|--------|---------|
| `DisplayMode` | `DataGridDisplayMode` | `Grid`, `Table` | `Grid` |
| `RowSize` | `DataGridRowSize` | `Smaller`(24), `Small`(32), `Medium`(44), `Large`(58) | `Small` |
| `ResizeType` | `DataGridResizeType?` | `Discrete`, `Exact` | null |
| `GenerateHeader` | `GenerateHeaderOption?` | `None`, `Default`, `Sticky` | `Default` |
| `SelectMode` | `DataGridSelectMode` | `Single`, `SingleSticky`, `Multiple` | — |

### FluentDialog — `DialogType` (via `DialogParameters`)

| Value | Use for |
|-------|---------|
| `Dialog` | Standard modal dialog (default) |
| `MessageBox` | Alert/confirm dialog |
| `Panel` | Side panel |
| `SplashScreen` | Full-screen splash |

### Color enum (used by `FluentIcon.Color`, nav `IconColor`, etc.)

| Value |
|-------|
| `Neutral`, `Accent`, `Warning`, `Info`, `Error`, `Success`, `Fill`, `FillInverse`, `Lightweight`, `Disabled`, `Custom` |

### TextAreaResize (used by `FluentTextArea.Resize`)

| Value |
|-------|
| `Horizontal`, `Vertical`, `Both` |

---

## ✅ Quick Validation Checklist

When writing or reviewing FluentUI Blazor markup, check:

1. **`<FluentBadge Appearance="…">`** → Only `Accent`, `Lightweight`, `Neutral`
2. **`<FluentButton Appearance="…">`** → Not `Filled`, not `Hypertext`
3. **Input components** → Use `FluentInputAppearance.Filled` or `.Outline`, NOT `Appearance.*`
4. **`<FluentCheckbox>` / `<FluentSwitch>`** → No `Appearance` property exists
5. **`<FluentMessageBar Intent="…">`** → `Info`, `Warning`, `Error`, `Success`, `Custom`
6. **No `Appearance.Transparent`** → Does not exist. Use `Stealth` or `Lightweight`.

---

## Common Mistakes

| ❌ Wrong | ✅ Correct | Why |
|---------|-----------|-----|
| `<FluentBadge Appearance="Appearance.Filled">` | `<FluentBadge Appearance="Appearance.Accent">` | Badge only allows Accent/Lightweight/Neutral |
| `<FluentBadge Appearance="Appearance.Outline">` | `<FluentBadge Appearance="Appearance.Lightweight">` | Outline throws on Badge |
| `<FluentButton Appearance="Appearance.Filled">` | `<FluentButton Appearance="Appearance.Accent">` | Filled throws on Button |
| `<FluentTextField Appearance="Appearance.Accent">` | `<FluentTextField Appearance="FluentInputAppearance.Filled">` | Wrong enum type |
| `<FluentCheckbox Appearance="...">` | Remove `Appearance` | Checkbox has no Appearance property |