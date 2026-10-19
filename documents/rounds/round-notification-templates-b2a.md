# Round B2a — notification templates: server plumbing

**Status:** IN PROGRESS · Mode: Tier 3 **lean** (no UI tester) · Provider: **pi** ·
Orchestrator/plan: `ollama-cloud/glm-5.3-flash` · Plan gate: `ollama/glm-5.3:cloud` ·
Worker: `ollama-cloud/deepseek-v4.1-flash:1017` · Diff gate: `ollama-cloud/deepseek-v4-pro:0813` ·
Acceptance (plan author): `ollama-cloud/deepseek-v4-pro:0813`
Branch: `feat/notification-templates-b2a` off `main` (`5bef668c`) · Spec: `documents/specs/assignment-notification-templates.md`

Plan-review history: iteration 1 **REWORK** (4 P1) → iteration 2 **REWORK** (3 P1, bound reached) → this
revision, written under the owner's Q63/Q64 rulings **without a third gate run** (owner approval stands in).

## Owner rulings folded into this revision

| # | Ruling |
|---|---|
| **Q63** | **B2a carries no matcher.** The §3.1.1 constraint matcher moves to a new slice **B2c**, together with the settings-db index relaxation and the candidates-with-constraints read it requires. |
| **Q64** | §3.6's E3 half is **in B2a**: ensure-minted codes at sweep render time + sweep links composed through `BuildDeepLink`. |
| **Q61** | Short links live on `AssignmentRecipient.ShortLinkCode` (16-char, global unique); the §3.6 `ShortLink`-table deviation is recorded in the spec's as-built section by this round. |
| **Q49** | The policy-resolver hardening is an owner-directed addition beyond spec §10 — now **both** resolvers. |

## Substrate inventory (taken before planning, per the 2026-10-19 process correction)

| Question | Answer found | Consequence |
|---|---|---|
| Does the template store allow more than one live template per (tenant, kind, channel)? | **No** — unique index on (TenantId, Kind, Channel) filtered `is_deleted = false` (`Settings.Core/Data/Configurations/TenantNotificationTemplateConfiguration.cs:36-38`) | the matcher has no substrate ⇒ **B2c**, not B2a (Q63) |
| Is there an Assignments-side read of templates *with* constraints? | **No** — `ResolveAsync(kind, channel, tenantId)` returns id/subject/body only (`INotificationTemplateResolver.cs:21-26`); the all-rows+`Constraints` wire exists only in Settings | B2a uses the single live template per slot; the candidates read is B2c |
| Are the publish request fields wired? | **No, at two seams** — declared `ContractTypes.cs:674-676` (enum `:680-688`), dropped by the route (`AssignmentRoutes.cs:720`), unused by the handler | WS2/WS4 own both seams |
| Is `families-public-base-url` wired? | **Yes** — `AppHost/Program.cs:236`/`:376`, `appsettings.json:32`, `appsettings.Development.json:16`, `documents/configuration.md:147`/`:1059` | no work; B1's residual was stale |
| Does a Families-side cross-context client exist? | **Yes** — `FamiliesApiClient` (`src/SchoolCollab.Families/Services/FamiliesApiClient.cs:48`), registered `FamiliesModuleServices.cs:17` with `propagateTenant: true` | the no-auth code lookup needs its **own** registration (no tenant to propagate) |

## WS1 — short links `/l/{code}` (spec §3.6 `:153`)

1. **Storage (deviates from §3.6; recorded):** nullable `AssignmentRecipient.ShortLinkCode` with a
   **global** filtered unique index (`WHERE "ShortLinkCode" IS NOT NULL`) — global, not tenant-scoped,
   because the resolution route is unauthenticated and cross-tenant; a per-tenant index would let two tenants
   share a code and resolve to an arbitrary one. 16-char URL-safe values from
   `RandomNumberGenerator` (contact-scoped credentials), collision ⇒ re-roll.
2. **Minting is channel-scoped (§3.2/§3.6):** `AttachDeepLink` (`AssignmentRecipient.cs:115-117`) runs for
   every recipient regardless of channel, so the code is minted **only** for SMS/WhatsApp recipients. Email
   keeps the full `/deeplink/{token}`. Mint atomically with the token: never a token without a code, never a
   code without a token.
3. **Link choice is channel-driven:** `NotificationLinkOptions.BuildDeepLink` (`:25-31`) takes the channel.
   SMS/WhatsApp + base + code ⇒ `{base}/l/{code}`; email ⇒ `{base}/deeplink/{token}`; no base ⇒ relative
   `/deeplink/{token}` (fail-closed dev posture unchanged). Single call site today:
   `AssignmentNotificationBroadcaster.cs:173`.
4. **Resolution is public, no-auth, cross-tenant:** a new Assignments endpoint resolves `code → token` with a
   **single-row `IgnoreQueryFilters`** bypass on the indexed code query (the global tenant filter would return
   nothing without a tenant; a broader bypass would be a leak). The Families host consumes it through its own
   `AddCrossModuleHttpClient` registration — **not** the `propagateTenant: true` `FamiliesApiClient`, which
   cannot serve a tenant-less visitor. Route `GET /l/{code}` ⇒ 302 to `/deeplink/{token}`; unknown/expired ⇒
   the friendly page `src/SchoolCollab.Families/Components/Pages/DeepLink/LinkExpired.razor` (**not** 404/410).
5. No external shortener.

## WS2 — medium restriction enforcement (spec §3.8)

The route seam (`AssignmentRoutes.cs:720`) must pass `Channels` / `PublishEmailTemplateId` /
`PublishSmsTemplateId` into `PublishAssignmentCommand`; the handler applies `Channels`
(`ContractTypes.cs:674`, enum `:680-688`) **inside `NotificationRecipientFilter`, after the `ContactIds`
subset filter** (the cap / `MaxNotifications` interplay depends on that order). `PerPolicy` unchanged;
`EmailOnly` ⇒ email only; `SmsOnly` ⇒ SMS + WhatsApp. `BlockedChannels` still wins
(`NotificationRecipientFilter.cs:42`) — the request narrows, never widens. Per-publish, not persisted.

## WS3 — sweep rendering, ensure-mint, resolver hardening

1. `SweepNotificationQueuer.QueueAsync` (`SweepNotificationQueuer.cs:27-43`; the builder-only
   `templateId: null` calls are `:85-115`) renders Reminder/Completion/Overdue through the B1 path
   (`INotificationTemplateResolver` → `NotificationTemplateRenderer`), recording the selected id.
2. **Ensure-mint (§3.6, Q64):** at render time, an SMS/WhatsApp recipient with **no** unexpired code gets one
   minted idempotently (existing rows all predate the feature). Sweep links are composed through
   `BuildDeepLink`, not the raw `/deeplink/{token}` path (`NotificationMessageBuilder.LegacyTags` today).
3. Preserve the unexpired-token skip, the unresolvable-address skip, and `PersistSkipAsync` idempotency (P1-2).
4. **Q49 hardening, both resolvers:** `AssignmentPolicyResolver` catches only `HttpRequestException` (`:62`,
   `:85`) and `NotificationPolicyResolver` carries the identical narrow catch on both fetches (`:52`, `:68`) —
   the latter feeds WS2's own `BlockedChannels`, so a timeout aborts the publish. Widen both to the template
   resolver's fail-open contract (timeout, non-JSON 200, 5xx); genuine cancellation still propagates.

## WS4 — stamps + selection lifecycle (spec §3.7 `:168`, §8 `:291`)

1. `Assignment` gains nullable `PublishEmailTemplateId` / `PublishSmsTemplateId` (+ migration), stamped when the
   author confirms a selection at publish.
2. **Request semantics, pinned:** request `null` = **reuse** the stored stamp. The clear operation's wire
   fields land with the **B2b** dialog; B2a exercises the cleared state by resetting the columns directly in
   tests. (Pinned so the worker does not invent a sentinel.)
3. **Cleared stamp** ⇒ fall back to the single live template for that (kind, channel), else the built-in default.
4. **Deleted or foreign stamp degrades** — publish proceeds, degradation recorded, **never blocks publish**.
5. The selected id is persisted on `NotificationLog.TemplateId` at queue time (B1's existing column).

## WS5 — `GET /assignments/{id}/publish-options`

Returns, per channel: the **single live template** for that slot (id, kind, channel, constraint metadata as
display-only), the effective policy channels, and the stored stamps. With the matcher in B2c there is no
ordering or specificity here — the client's pre-select source is `stored stamp ?? live template`. `TypedResults`
only; read-only; tenant-scoped (404 for a foreign assignment). An explicit request stamp that fails
tenancy/kind/channel validation ⇒ **400**, never a silent fallback.

## Explicitly NOT in this round

- The §3.1.1 matcher — specificity ordering, dimension intersect, ties→default, suggestions → **B2c** (Q63),
  along with the settings-db unique-index relaxation and the candidates-with-constraints read.
- The publish dialog and all UI → **B2b**. The editor page, grade-detail card, `NotificationTemplateApiClient`
  → **B3**.

## Tests

- **WS1:** `BuildDeepLink` matrix (channel × base × code); code minted for SMS/WhatsApp and **not** for email
  (discriminating — fails today); token/code atomicity both directions; `/l/{code}` 302 → `/deeplink/{token}`;
  unknown/expired → `LinkExpired`; unauthenticated access works; a code from another tenant still resolves
  (it is the credential); global uniqueness rejects a duplicate across tenants. Route tests live in
  `tests/SchoolCollab.Families.Tests.Unit` (precedent `DeepLinkLandingServiceTests`).
- **WS2:** three modes × policy-blocked matrix (`blocked-channel-wins` passes today — labelled regression
  guard; the discriminating legs are `EmailOnly`/`SmsOnly`); §3.8 ordering after `ContactIds`; republish under
  `PerPolicy` (regression guard).
- **WS3:** sweep kinds render a tenant template when one exists and the built-in default when not, `TemplateId`
  recorded both ways; **ensure-mint**: a pre-existing SMS recipient with no code gains one and its link is
  `/l/{code}` (discriminating); email sweep recipients keep `/deeplink/{token}`; skip/idempotency preserved;
  fail-open cases + cancellation propagation for **both** widened resolvers.
- **WS4 (§8):** stamp persisted on publish; request-null reuses; cleared → live template, else default;
  deleted/foreign stamp degrades and the publish still succeeds; `NotificationLog.TemplateId` matches.
- **WS5:** response shape; tenant scoping; 404 foreign assignment; foreign/tombstoned explicit stamp ⇒ 400.

## Risks

- Minting at two sites (publish and sweep) must share one generator/validator or the collision posture diverges.
- The `/l/` lookup is cross-tenant **by design**; a broad `IgnoreQueryFilters` is a leak, a missing one is a
  broken link.
- `AssignmentRecipient` is written on every target resolution — minting must be inside that transaction.

## Verification

`dotnet build SchoolCollab.slnx` 0 errors · Architecture · Assignments.Unit · Assignments.Api.Unit ·
Assignments.Integration · Students.Unit · `tests/SchoolCollab.Families.Tests.Unit` · MAUI head only if a
package/shared surface changes.

## Acceptance

**Verdict: ACCEPTED** (2026-10-19, Tier 3 lean). Seat: `ollama-cloud/deepseek-v4-pro:0813`, dispatched pass,
independent of the worker and the diff reviewer. Every criterion below carries discriminating evidence — a diff
location or a test that fails without the change.

| # | Criterion | Evidence |
|---|---|---|
| 1 | ShortLinkCode storage: nullable, **global** filtered unique index, 16 chars from a CSPRNG, collision re-roll | `AssignmentRecipient.cs:52-62`, `AssignmentRecipientConfiguration` + migration `20261019115238`, `ShortLinkCodes.cs:31,44-60`; test `Mint_CollisionsAreReRolled_BeforeACodeIsStored` |
| 2 | Minting channel-scoped, token+code atomic | `PublishAssignmentCommandHandler.cs:322-336`; tests `NewSmsRecipient_MintsTokenAndShortLinkCode_Together`, `NewEmailRecipient_GetsNoShortLinkCode` |
| 3 | Link choice channel-driven; email never short | `NotificationLinkOptions.cs:36-63`; `NotificationLinkOptionsTests` (8 cases incl. `Base_Email_IsAbsoluteDeepLink_EvenWithACode`) |
| 4 | Anonymous cross-tenant resolution, single-row bypass, one route registration, code never logged | `ResolveShortLink.cs:40-49`, `DeepLinkEndpoints.cs:50` (sole `/l` mapping), `ShortLinkRoutes.cs:22-24`; tests `LiveCode_302s_ToDeepLink`, `UnknownCode_302s_ToExpiredPage`, `RouteWorks_WithoutAuthentication_OrTenant` |
| 5 | Medium restriction enforced at both seams, after `ContactIds`, blocked channels win | `AssignmentRoutes.cs:722-724`, `NotificationRecipientFilter.cs:68,81-91`; tests `EmailOnly_DropsSmsAndWhatsApp`, `SmsOnly_KeepsSmsAndWhatsApp_DropsEmail`, `BlockedChannels_StillWin_Under*`, `Restriction_IsAppliedBeforeTheSendoutCap` |
| 6 | Sweep renders templates + records TemplateId, ensure-mint, email keeps long link, both resolvers fail-open with cancellation propagating | `SweepNotificationQueuer.cs:127`, `NotificationPolicyResolver.cs:44-58`; tests `ResolveTemplateAsync_*` (2), `EnsureShortLinkCodeAsync_*` (2), `GenuineCancellation_Propagates` in both resolver suites |
| 7 | Stamp columns + lifecycle (reuse / clear→default / deleted degrades, never blocks) | `Assignment.cs:119-129` + migration; tests `NullRequestStamp_ReusesTheStoredOne`, `ClearedStamps_StayCleared_…`, `StaleStoredStamp_DoesNotBlockThePublish` |
| 8 | `publish-options` shape, `TypedResults`, tenant-scoped, explicit mismatch ⇒ 400 | `AssignmentRoutes.cs:1012-1027`; tests `ForeignAssignment_ReadsAsAbsent` (same-tenant success asserted first), `ExplicitStamp_ThatIsNotTheLiveTemplate_IsRejected_400` |
| 9 | Out of scope correctly absent (no matcher, no settings-db migration, no UI) | constraints are display-only `PublishTemplateConstraintDto`; one assignments-db migration; no `.razor` changes |
| 10 | Test honesty | every test above discriminates or is labelled a regression guard (`BlockedChannels_StillWin_*`, `NullRestriction_KeepsEveryChannel`) |

**Residual found by acceptance and closed before merge — the Settings-outage / no-template collapse.**
`ResolveSlotAsync` returned `null` for both "204 no template" and "Settings outage", so the rework's
`null ⇒ 400` aborted a publish during an outage — contradicting §3.3 ("a template outage can never block a
publish"). Fixed as a **discriminated read**: `NotificationTemplateSlotState { NotSet, Unreadable, Found }`
with `ReadSlotAsync` (`INotificationTemplateResolver.cs:45`, `HttpNotificationTemplateResolver.cs:57-131`,
`NullNotificationTemplateResolver.cs:36`); `ValidateRequestedStampAsync` now returns 400 for `NotSet` and for a
`Found` mismatch, and **accepts the stamp with a warning on `Unreadable`**
(`PublishAssignmentCommandHandler.cs:288-331`). Genuine cancellation still propagates rather than being
swallowed as `Unreadable` (test `GenuineCancellation_DuringTemplateValidation_Propagates`).

**Concurrency note:** `ShortLinkCodes.MintUniqueAsync` is check-then-insert; the global filtered unique index is
the backstop for the theoretical concurrent-mint race, and a collision surfaces as a re-roll, not a corrupt row.

**Authoritative gates (parent run, final tree):** build **0 errors** · Assignments **1246/0** · Assignments.Api
**185/0** · Families **57/0** · Architecture **129/0** · CPM clean (no `Version=` on any `PackageReference`).

**Deferred, with owner rounds:** the §3.1.1 matcher + settings-db index relaxation + candidates read → **B2c**
(Q63); the publish dialog → **B2b**; editor page, grade-detail card, `NotificationTemplateApiClient` → **B3**.

**Round history:** plan gate REWORK ×2 (iteration 2 hit the bound; owner approved the revision) · diff review P1
(5 blockers) → rework on the escalator seat → re-verify P1 (duplicate `/l` mapping, test placement) → parent
applied both → re-verify **PASS** · acceptance **ACCEPTED** with one residual, closed by the discriminated read.
