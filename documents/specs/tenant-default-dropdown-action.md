# Spec: default-tenant actions — dropdown `Set as default`, `/tenants` clear banner

Status: implemented. Rounds: solo (initial surfaces) + light Tier 2 `tenant-default-selection`
(dropdown selects the resolved default's own row; the `(default …)` pseudo-entry removed; the
`/tenants` dev session-pin line restores the un-pick). Authoritative verification on default output
paths after that round: `dotnet build SchoolCollab.slnx` 0 errors; `SchoolCollab.Admin.Tests.Unit`
686 passed / 0 failed (`DevTenantSwitcherTests` 19); `SchoolCollab.Settings.Tests.Unit` 566 passed /
0 failed (`TenantsIndexTests` 12); `SchoolCollab.ArchitectureTests.Unit` 112 passed / 0 failed.
Implemented in `DevTenantSwitcher.razor(.css)`,
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

Option order: `(system / no tenant)` → one entry per tenant → **separator** →
**`Set as default`**. There is no longer a separate entry for the resolved default: the
select shows the resolved tenant's **own row** as its selection, so the control's selected
value *is* the tenant the portal is operating under. The separator and the action entry
(together, the "action zone") render only per FR-3; when they do not, the list ends at the
tenants exactly as today.

Which row is selected (from the explicit pick):

| Pick | Selected row |
|---|---|
| `Guid.Empty` (the system context) | `(system / no tenant)` |
| a real id **present in the registry** | that tenant's row |
| `null`, or an id the registry no longer lists | the row of the resolved default (`_effective`) when it is a real tenant present in the registry, otherwise `(system / no tenant)` |

The row is never blank. This resolution is **display-only**: it never writes the selection
slot, so after a no-pick render `GetSelectedTenantIdAsync()` still returns `null`. Only a
user pick writes it.

Mechanism (keeps the existing `Items`/`OptionText`/`OptionValue` binding):

- `TenantOption` gains an `IsAction` flag and a third kind of sentinel value
  (e.g. `Value = "__set-my-default__"` — never guid-parseable, distinct from `"system"`).
- The **separator** is a decorative, **disabled** entry (native select-like controls
  have no divider primitive; a disabled option with rule-styled text such as
  `──── my default ────` is the portable mechanism). It is never selectable.
- The **action entry is enabled** — a disabled option cannot be clicked, so it must stay
  selectable; the "never selected" invariant is enforced by interception (FR-4), not by
  disabling.

### FR-3 — Visibility of the action zone

The action zone renders iff **both**:

1. the selected option is a **real tenant** entry (its value parses to a non-empty
   `Guid` — i.e. not `system`), and
2. that tenant **differs from the recorded preference** (`_preference` from
   `GET /api/tenants/me/default`; `null` preference → condition holds for any real
   tenant; a `Guid.Empty` preference is a real value, and every real tenant differs
   from it).

When hidden: no separator, no dangling line.

**Intended consequence of FR-2** (round `tenant-default-selection`): on a no-pick load the
selection is the resolved default's own row, so the zone is now **visible** where the
removed pseudo-entry used to hide it — which is what makes `Set as default` reachable
without re-picking the tenant first.

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
3. one `Nav.Refresh(forceReload: true)` — a real document load, so the circuit re-enters through
   a new request and recomputes its `tenant_id` claim. The flag is load-bearing:
   `Refresh(forceReload: false)` merges the server HTML into the live document instead of loading
   it, which leaves the claim stale.

The selection slot (`IDevTenantSelection`) is **not** touched — the explicit pick stays
in force and the dropdown comes back showing the same tenant (FR-6). No success toast:
the forced reload supersedes it (matches the removed button's behaviour). Failures are
logged and non-fatal (existing pattern — the dropdown keeps its state).

### FR-6 — Selection invariant

`Set as default` is never the dropdown's selected value — not before, not while the
dialog is open, and not after confirm/cancel. After the post-confirm reload the dropdown
shows the previously selected tenant still selected (the explicit pick survived); the
newly recorded preference is what the `/tenants` banner then names (FR-7).

**Delivered mechanism (owner 2026-10-14):** the switcher rejects the action entry inside
`OnSelectionChanged`, restores the previous selection, and re-creates the `FluentSelect` through a
`@key` epoch. The epoch is load-bearing, not cosmetic: a `FluentSelect` does not re-read an unchanged
`SelectedOption` parameter, so restoring the selection against an otherwise identical option list would
leave the action entry rendered as the selection — the exact state this FR forbids.

### FR-7 — `/tenants`: preference banner and dev session-pin line

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
  becomes "platform default and the personal preference are both set here; the header dropdown
  sets the same preference".

**Dev session-pin line** (added by round `tenant-default-selection`). The banner cannot
carry the release for the header switcher's explicit pick: a pick can exist with **no**
preference recorded, and the banner is absent exactly then. The page therefore also reads
the pick — `IDevTenantSelection.GetSelectedTenantIdAsync`, in the same `Task.WhenAll` as
the registry and the preference — and, when a pick is in force, renders an independent line
above the `LandingPage`:

- `Pinned this session: <name>` — name resolved exactly like the banner's (registry name;
  `system / no tenant` for `Guid.Empty`; raw id when the registry does not list it).
- `Follow the portal default` → `DevTenant.SetSelectedTenantIdAsync(null)` — the
  **selection** slot the header dropdown's row click writes, never
  `SetMyDefaultTenantAsync` — and then one `Nav.Refresh(forceReload: true)` so the change takes
  effect in this circuit, exactly as a row click does.
- A failed pick read is non-fatal (log, treat as no pick); a failed write surfaces on the
  existing `_error` strip and the line stays.

The line renders independently of the banner, so the two never gate each other.

**Two default columns** (Q10 clarity pass, 2026-10-15). The registry grid carries the two
defaults as separate columns, because they are easy to confuse: **`Platform default`** — the
single shared designation (`Tenant.IsDefault`), badged `Default` on that one row — and
**`Your default`** — the signed-in operator's own recorded preference
(`GET /api/tenants/me/default`), badged `Yours` on that row. Each header carries a tooltip
explaining the difference; FluentUI emits a header tooltip only when the column's `Tooltip`
switch is **also** set (`Tooltip` defaults `false` and gates `HeaderTooltip`), so both columns
pass `Tooltip="true"` beside `HeaderTooltip`. When **no** tenant is designated, the page
renders one muted line above the grid — *"No platform default is designated yet …"* — instead
of leaving a column of bare dashes that reads as a rendering bug. The session-pin line's label
and its button also carry explanatory tooltips.

**`User default` column, and the `Set as my default` row action** (round `tenant-user-default`,
owner grill 2026-10-15). The personal column is titled **`User default`** (was `Your default`) and
badges the preferred row **`Yes`** (was `Yours`); the platform column keeps `Platform default` /
`Default`. The `Tooltip="true"` switch and the two header tooltips stay — only the personal
column's tooltip wording moves to the new vocabulary and names the row action.

The operator's preference is now settable from this page as well as from the header dropdown:
the dropdown is where the tenant in view is, the page is where the registry is. Every row's action
menu carries exactly **two** entries — the platform designation first, the personal one second
(both on every row, so the shared menu renders its kebab for all of them):

| Row | Platform entry | Personal entry |
|---|---|---|
| `tenant.IsDefault` | disabled `Portal default` — *This tenant already opens the admin portal by default.* | `Set as my default` |
| `_preference == tenant.Id` | `Make portal default` | disabled `Set as my default` — *This tenant is already your default.* |
| neither | `Make portal default` | `Set as my default` |

`Set as my default` confirms with the switcher's **exact** wording (FR-4's table: title
`Set default tenant`, message `Open the admin portal on '<name>' by default from now on?`, primary
`Set as default`), then writes through `PUT /api/tenants/me/default` and ends with one
`Nav.Refresh(forceReload: true)` — the same write and the same hard reload as the dropdown's
action, because this RCL cannot re-publish the host's `AdminDefaultTenantResolver`. Cancel and
dismissal write nothing and reload nothing; a failed write logs, sets the page's `_error` (the
shared `LandingPage` error strip — re-rendered explicitly, because a row action runs from the menu's
own event handler) and does **not** reload. The dev session pick (`IDevTenantSelection`) is never
written here: the explicit pick is a separate layer with its own release in the pin line above.

## 4. Surface states (reference)

### 4.1 Dev tenant switcher

| Selected row | Preference | Action zone |
|---|---|---|
| `(system / no tenant)` — an explicit `Guid.Empty` pick, or nothing resolved | any | hidden |
| Alpha — no pick, the row of the resolved default | none | shown — set pins Alpha |
| Alpha — explicit pick | Beta | shown — set overwrites Beta with Alpha |
| Alpha — explicit pick | Alpha | hidden — no-op |
| Beta — explicit pick | `Guid.Empty` | shown — a real tenant differs from the system preference |

A no-pick render selects the resolved default's row but never writes the selection slot.

### 4.2 `/tenants`

| Pick | Preference | Rendered |
|---|---|---|
| none | none | neither line |
| none | Alpha | banner only |
| Alpha | none | pin line only — `Pinned this session: Alpha` + `Follow the portal default` |
| Alpha | Beta | pin line + banner |

Independently of the two lines, the registry grid names both defaults in their own columns
(`Platform default`, `User default`) and, when nothing is designated, shows the empty-state note
above the grid. The personal default is also **settable** from the grid: the preferred row's
`Set as my default` entry is disabled with its reason, every other row's is enabled.

## 5. Tests

`tests/SchoolCollab.Admin.Tests.Unit/DevTenantSwitcherTests.cs` (round
`tenant-default-selection` reshaped these; the button-era tests are gone):

- **Option list:** `RendersSystemAndEveryTenant_WithNoDefaultPseudoEntry` — the labels are
  exactly `(system / no tenant)`, `Alpha`, `Beta`, and no label carries the removed
  pseudo-entry's marker.
- **Selected-row resolution:** `WithNoExplicitPick_SelectsTheResolvedDefaultTenantsRow`
  (plus the display-only assertion that the stub's selection slot is still `null`),
  `WithAPreference_SelectsThePreferredTenantsRow`,
  `WithNoDefaultDesignated_SelectsTheSystemRow`,
  `WithAResolvedDefaultNotInTheRegistry_SelectsTheSystemRow`,
  `WithAnExplicitSystemPick_SelectsTheSystemEntry`, `WithAnExplicitTenantPick_SelectsThatTenant`.
- **Action zone:** shown for a real-tenant selection that is not the recorded preference
  (including `ActionZone_IsShown_OnANoPickLoad_WhenTheResolvedDefaultDiffersFromThePreference`,
  the no-pick load FR-2's new selection makes reachable); hidden for the system context and
  for the already-recorded preference; separator present iff the action entry is and disabled
  while the action entry is not.
- **Never selected / confirm flow:** clicking the action entry leaves `SelectedOption` on the
  tenant entry (during the open dialog and after cancel); cancel → no
  `PUT /api/tenants/me/default` call recorded; confirm → the PUT body contains the selected
  tenant and the circuit reloads once; a failed write keeps the selection and does not reload;
  no success toast. Harness: the real `IDialogService` + `FluentDialogProvider` on the cancel
  path, a mocked dialog outcome plus a scripted `HttpMessageHandler` for the write-back
  assertions, per `.github/copilot/rules/testing.md`.
- **Circuit reconciliation:** `ReconciliationReload_IsRequested_OnAnInitialRender_WhoseClaimIsEmpty`
  — a first render whose claim is empty while a default has resolved asks for exactly one reload
  (the path the feature exists to repair). The navigation spy must override
  `NavigateToCore(string uri, NavigationOptions options)`: the reload reaches navigation as a
  forced replacement navigation, so a spy on the `NavigateToCore(string, bool)` overload never
  sees it. `ReconciliationReload_IsRequested_WhenTheDefaultResolvesAfterTheFirstRender` pins the
  production timing the first-render-only check missed: the Settings API is held open by a gated
  scripted handler, so the first render happens while `_effective` is still null, and the reload
  must still be requested once the async resolution completes. The check is gated on a
  `_defaultsResolved` flag (set after the resolve attempt), not on `firstRender`.

`tests/SchoolCollab.Settings.Tests.Unit/Tenants/TenantsIndexTests.cs` covers the `/tenants`
surfaces; the harness registers a stub `IDevTenantSelection` (the page injects it), a mocked
`IDialogService` (the confirm dialog, pinned at a fixed outcome and capturing the content it was
handed) and a `<FluentMenuProvider />` root (every row now renders the kebab, whose menu needs the
provider the layout supplies). Row actions are asserted through the public
`LandingPage<TenantDto>.RowActions` delegate — `FluentMenu` does not materialise its items while
closed, so menu markup would test nothing.

- **Banner (FR-7, unchanged):** hidden without a preference; shown with the tenant name;
  system-preference label; raw-id fallback; Clear PUTs `null` and hides the banner; a failed
  preference read degrades to "no banner" while the grid survives; a failed clear surfaces the
  error strip and keeps the banner.
- **Pin line (B7):** `Pin_IsHidden_WhenNoExplicitPick`;
  `Pin_NamesThePinnedTenant_AndOffersFollowThePortalDefault`;
  `Pin_IsShown_EvenWithNoPreferenceRecorded` (the reachability case that motivated the move);
  `FollowThePortalDefault_ClearsThePick_AndReloads` — asserts the **selection** slot is cleared,
  that the preference endpoint was not touched, and that the circuit reloads once;
  `FollowThePortalDefault_RequestsAForcedReload_OfTheCurrentUri` — the reload is a forced document
  load of the current URI (the recorded `NavigationOptions.ForceLoad`), not a merged HTML refresh;
  `FailedFollowThePortalDefault_SurfacesTheError_AndKeepsThePin`.
- **Two default columns (Q10):** `Grid_NamesBothDefaultColumns` (the personal column reads
  `User default`, and no header reads `Your default`);
  `Grid_HeaderTooltips_ExplainTheTwoDefaults` (the tooltips actually render — the
  `Tooltip`/`HeaderTooltip` gate); `Grid_BadgesTheDesignatedPlatformDefault_AndOmitsTheEmptyStateNote`;
  `Grid_NotesTheEmptyPlatformDefault_WhenNoneIsDesignated`; `Grid_BadgesTheOperatorsOwnDefault`
  (the badge reads `Yes`); `Grid_ShowsNoYesBadge_WhenNoPreferenceIsRecorded`.
- **`Set as my default` row action:** `RowActions_OfferSetAsMyDefault_ForANonPreferredTenant`
  (exactly two entries, platform first, personal enabled);
  `RowActions_DisableSetAsMyDefault_ForThePreferredRow` (disabled, with its reason, on the row
  that already holds the preference); `SetAsMyDefault_Confirmed_WritesThePreference_AndReloads`
  (exactly one PUT naming the row's tenant, exactly one forced reload, and the confirm content is
  pinned to the switcher's wording); `SetAsMyDefault_Cancelled_WritesNothing_AndDoesNotReload`;
  `FailedSetAsMyDefault_SurfacesTheError` (the strip is set, nothing reloads);
  `SetAsMyDefault_DoesNotTouchTheSessionPick` (a pick in force survives the write).

## 6. Documentation updates (same PR)

- Round `tenant-apply-reload`: `documents/solution/admin-default-tenant.md` §3.3/§5 now name
  `Refresh(forceReload: true)` as the apply mechanism.
- `documents/solution/admin-default-tenant.md`: §3.4 `DevTenantSwitcher` bullet
  ("offers … Default and Clear") → dropdown action + `/tenants` banner; §5 limit bullet
  ("Clearing a preference is required … Hence the 'Clear' action") → points at the
  `/tenants` banner.
- This spec's `Status` line flips to implemented (+ verification results) when done.
- Q10 clarity pass (2026-10-15): this spec's FR-7/§4.2 and
  `documents/solution/admin-default-tenant.md` §3.4 record the two default columns, their
  tooltips, and the empty-platform-default note.
- Round `tenant-user-default` (owner grill 2026-10-15): FR-7/§4.2/§5 and
  `documents/solution/admin-default-tenant.md` §3.4 record the `User default` / `Yes` rename and the
  `Set as my default` row action.

## 7. Out of scope / rejected

- Per-row `Set as my default` on `/tenants` — proposed 2026-10-14 as a duplicate of the dropdown
  action and rejected then; **re-adopted 2026-10-15** (owner grill, round `tenant-user-default`):
  the two surfaces answer different questions — the dropdown sets the tenant in view, the page sets
  a row of the registry — so the page now offers it alongside the platform-default action. A
  `Clear` entry in the dropdown stays rejected; overwrite-by-new-set plus the `/tenants` banner
  cover the lifecycle.
- Any Settings API / resolver / `IDevTenantSelection` / migration change.
- AuthPortal preference UI (unchanged stance from the parent feature).
- Confirmation for Clear, success toasts (see FR-5 / FR-7).

## 8. Acceptance criteria

- `dotnet build SchoolCollab.slnx`: 0 errors; `SchoolCollab.Admin.Tests.Unit` and
  `SchoolCollab.Settings.Tests.Unit`: 0 failures. The page's existing banner tests stay
  unmodified.
- The header select stands alone — no `Default`/`Clear` buttons anywhere in
  `DevTenantSwitcher`.
- FR-1…FR-7 hold; §5's tests pass.
- No rendered dropdown label carries the removed pseudo-entry's marker, and a no-pick render
  selects the resolved default's own row without writing the selection slot.
- `/tenants` renders the session-pin line when a pick exists, and its follow action clears the
  **selection** slot (never the preference endpoint) and reloads.
- `documents/solution/admin-default-tenant.md` reflects the new surfaces.
