# Spec: default-tenant actions — dropdown `Set as default`, `/tenants` clear banner

Status: implemented (2026-10-14) · Round: solo, attended. Verification: `dotnet build SchoolCollab.slnx`
0 errors; `SchoolCollab.Admin.Tests.Unit` 684 passed / 0 failed (`DevTenantSwitcherTests` 17);
`SchoolCollab.Settings.Tests.Unit` 561 passed / 0 failed (`TenantsIndexTests` 7, the page's first
component-level coverage). Implemented in `DevTenantSwitcher.razor(.css)`,
`Components/Pages/Tenants/Index.razor(.css)`, and the two test classes named in §5; §6's doc
updates are in the same change. Two deltas from §3's mechanism note are recorded here:
(1) the separator's disabled attribute comes from `FluentSelect.OptionDisabled`, so the existing
`Items` binding is kept, and (2) rejecting the action entry re-creates the select via a `@key`
epoch, because FluentSelect does not re-read an unchanged `SelectedOption` parameter.
Related: [`../solution/admin-default-tenant.md`](../solution/admin-default-tenant.md) (the
PR #341 feature this reshapes — its §3.4 and §5 must be updated in the same PR, see §6),
[`tenant-gate-component.md`](./tenant-gate-component.md)

## 1. Problem

PR #341 surfaced the two self-service default-tenant actions as two stealth toolbar
buttons beside the dev tenant switcher's `FluentSelect`
(`src/SchoolCollab.Admin/Components/Layout/DevTenantSwitcher.razor`):

- **Default** — pinned "whatever is in view" (`_explicitSelection ?? _effective`), an
  implicit target the operator never chose deliberately;
- **Clear** — rendered only when a preference exists, so it appears and disappears by
  state.

Both are unintuitive and clutter the 50px header. The capabilities move: **set** becomes
an entry *inside* the dropdown (confirm-gated); **clear** moves to the `/tenants`
settings page, which today deliberately excludes the preference.

## 2. Scope

**UI-only.** No server, API, migration, resolver, or store changes — everything needed
already exists: `GET|PUT /api/tenants/me/default` via
`TenantsApiClient.GetMyDefaultTenantAsync` / `SetMyDefaultTenantAsync(Guid?)` (a PUT with
a new value already replaces the previous preference — single upsert per operator), and
`AdminDefaultTenantResolver` for re-publishing the applied default.

In: `DevTenantSwitcher.razor(.css)`, `SchoolCollab.Settings.Application`'s
`Components/Pages/Tenants/Index.razor`, their tests.
Out: everything else (see §7).

## 3. Behaviour contracts

### FR-1 — Toolbar buttons removed

The two `FluentButton`s and all their supporting code leave `DevTenantSwitcher`: markup,
`SetDefaultTitle`, `TargetTenantId`, `SetAsMyDefaultAsync`, `ClearMyDefaultAsync`,
`_busy`, and the `.dev-tenant-pref-button` rules in `DevTenantSwitcher.razor.css`. The
component's non-empty state is the select alone (plus the existing loading/empty states).

### FR-2 — Dropdown layout and the action zone

Option order: `(system / no tenant)` → `(default: X)` → one entry per tenant →
**separator** → **`Set as default`**. The separator and the action entry (together, the
"action zone") render only per FR-3; when they do not, the list ends at the tenants
exactly as today.

Mechanism (keeps the existing `Items`/`OptionText`/`OptionValue` binding):

- `TenantOption` gains an `IsAction` flag and a third kind of sentinel value
  (e.g. `Value = "__set-my-default__"` — never guid-parseable, distinct from `""` and
  `"system"`).
- The **separator** is a decorative, **disabled** entry (native select-like controls
  have no divider primitive; a disabled option with rule-styled text such as
  `──── my default ────` is the portable mechanism). It is never selectable.
- The **action entry is enabled** — a disabled option cannot be clicked, so it must stay
  selectable; the "never selected" invariant is enforced by interception (FR-4), not by
  disabling.

### FR-3 — Visibility of the action zone

The action zone renders iff **both**:

1. the selected option is a **real tenant** entry (its value parses to a non-empty
   `Guid` — i.e. neither `system` nor the `(default …)` entry), and
2. that tenant **differs from the recorded preference** (`_preference` from
   `GET /api/tenants/me/default`; `null` preference → condition holds for any real
   tenant; a `Guid.Empty` preference is a real value, and every real tenant differs
   from it).

When hidden: no separator, no dangling line. The `(default: X)` entry continues to name
the resolved effective default (preference → platform default) — it is the state
indicator for the "already your default" case.

### FR-4 — Trigger → confirmation; the action is never selected

Selecting the action entry fires `SelectedOptionChanged` with the sentinel option. The
handler, **synchronously before any await**:

1. restores `_selectedOption` to the previously selected tenant entry (the action option
   is never assigned to the bound selection — FR-6), then
2. runs the confirm flow.

The prompt uses the repo-sanctioned `DialogServiceExtensions.ShowConfirmDialogAsync`
(modal, Cancel, ESC/overlay-dismiss — never FluentUI's `ShowConfirmationAsync`):

| Piece | Value |
|---|---|
| Title | `Set default tenant` |
| Message | `Open the admin portal on '<tenant name>' by default from now on?` |
| Primary | `Set as default` |
| Secondary | `Cancel` |

**Cancel / dismiss → nothing is written; the selection is unchanged.**

### FR-5 — Confirm → apply (unchanged mechanics from the old button)

On confirm, in order:

1. `TenantsApiClient.SetMyDefaultTenantAsync(selectedTenantId)` — records/replaces the
   preference (a preference for another tenant, including the system context, is
   overwritten — this is how "selecting another preferred clears the old default");
2. `AdminDefaultTenantResolver.ResolveAndApplyAsync(PlatformDefaultOf(_tenants))` —
   re-publishes so the rest of this load speaks for the new default;
3. one `Nav.NavigateTo(Nav.Uri, forceLoad: true)`.

The selection slot (`IDevTenantSelection`) is **not** touched — the explicit pick stays
in force and the dropdown comes back showing the same tenant (FR-6). No success toast:
the forced reload supersedes it (matches the removed button's behaviour). Failures are
logged and non-fatal (existing pattern — the dropdown keeps its state).

### FR-6 — Selection invariant

`Set as default` is never the dropdown's selected value — not before, not while the
dialog is open, and not after confirm/cancel. After the post-confirm reload the dropdown
shows the previously selected tenant still selected (the explicit pick survived), and
the `(default: X)` entry now names the newly recorded preference.

### FR-7 — `/tenants`: page-level clear banner

`SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor`:

- Load `GET /api/tenants/me/default` **in parallel** with `ListTenantsAsync`
  (`Task.WhenAll`). A failed preference read is non-fatal: log, treat as no preference.
- When a preference is recorded, render above the `LandingPage` a banner:
  `Your default tenant: <name>` + a `Clear` button. Name resolved from the loaded list;
  `Guid.Empty` renders as `system / no tenant`; an unknown id falls back to the raw id.
- **Clear** → `SetMyDefaultTenantAsync(null)` → clear the local preference state and
  re-render (banner disappears; grid untouched). No confirmation (not destructive — the
  repo confirms destructive actions only), no forced reload (nothing tenant-scoped
  changes until the next portal load). Failure surfaces on the existing `_error` strip,
  same as `MakeDefaultAsync`.
- No auth-mode gating: under OIDC no preference exists, so the banner never renders.
- Update the page's header comment: the "preference is deliberately NOT here" rationale
  becomes "platform default here; the personal preference is set in the header dropdown
  and released here".

## 4. Dropdown states (reference)

| Selected | Preference | Action zone |
|---|---|---|
| `(system / no tenant)` | any | hidden |
| `(default …)` | any | hidden |
| Alpha | none | shown — set pins Alpha |
| Alpha | Beta | shown — set overwrites Beta with Alpha |
| Alpha | Alpha | hidden — no-op; `(default: Alpha)` states it |
| Alpha | `Guid.Empty` | shown — real tenant differs from the system preference |

## 5. Tests

Rework `tests/SchoolCollab.Admin.Tests.Unit/DevTenantSwitcherTests.cs`:

- **Remove** (button-based): `MakeDefault_RecordsTheCurrentTenant_AndPublishesIt`,
  `ClearIsNotOfferedWhenNoPreferenceExists`, `ClearIsOfferedWhenAPreferenceExists`.
- **Keep/adjust**: option-list and selection-resolution tests (labels, ordering, the
  system-vs-default distinction) — extend the label-order assertions to the new action
  zone where it applies.
- **Add**:
  - action zone shown only for a real-tenant selection (absent for system / `(default …)`);
  - hidden when the selected tenant already is the recorded preference;
  - separator present iff the action entry is present;
  - **never selected**: clicking the entry leaves `SelectedOption` on the tenant entry
    (during the open dialog and after cancel);
  - cancel → no `PUT /api/tenants/me/default` call recorded (confirm via the sanctioned
    end-to-end harness: real `IDialogService` + `FluentDialogProvider`, per
    `.github/copilot/rules/testing.md`);
  - confirm → PUT body contains the selected tenant; applied default re-published;
    selection slot untouched.

New bUnit coverage for the `/tenants` page (none exists today; land in
`SchoolCollab.Settings.Tests.Unit`, alongside the existing Settings RCL component tests):
banner hidden without a preference; shown with the tenant name; system-preference label;
`Guid.Empty`/unknown-id fallbacks; Clear PUTs `null` and hides the banner; failed
preference read degrades to "no banner", failed clear surfaces the error strip.

## 6. Documentation updates (same PR)

- `documents/solution/admin-default-tenant.md`: §3.4 `DevTenantSwitcher` bullet
  ("offers … Default and Clear") → dropdown action + `/tenants` banner; §5 limit bullet
  ("Clearing a preference is required … Hence the 'Clear' action") → points at the
  `/tenants` banner.
- This spec's `Status` line flips to implemented (+ verification results) when done.

## 7. Out of scope / rejected

- Per-row `Make my default` on `/tenants` — duplicates the dropdown action; rejected
  2026-10-14 (the brief scopes set-default to the dropdown, clear to the page banner).
- A `Clear` entry in the dropdown — rejected 2026-10-14; overwrite-by-new-set plus the
  `/tenants` banner cover the lifecycle.
- Any Settings API / resolver / `IDevTenantSelection` / migration change.
- AuthPortal preference UI (unchanged stance from the parent feature).
- Confirmation for Clear, success toasts (see FR-5 / FR-7).

## 8. Acceptance criteria

- `dotnet build SchoolCollab.slnx`: 0 errors; `SchoolCollab.Admin.Tests.Unit` and
  `SchoolCollab.Settings.Tests.Unit`: 0 failures.
- The header select stands alone — no `Default`/`Clear` buttons anywhere in
  `DevTenantSwitcher`.
- FR-1…FR-7 hold; §5's new tests pass.
- `documents/solution/admin-default-tenant.md` reflects the new surfaces.
