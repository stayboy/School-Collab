# Round B2b — notification templates: the publish dialog

**Status:** PLANNING · Mode: Tier 3 **full** (UI — a UI tester bug-hunts the delivered dialog) · Provider: **pi**
Orchestrator/plan: `ollama-cloud/glm-5.3-flash` · Plan gate: `ollama/glm-5.3:cloud` ·
Worker: `ollama-cloud/deepseek-v4.1-flash:1017` · Diff gate: `ollama-cloud/deepseek-v4-pro:0813` ·
Acceptance: `deepseek-v4-pro:0813` · UI tester: `minimax-m3`
Branch: `feat/notification-templates-b2b` off **`feat/notification-templates-b2c`** (layer 2 of stack #375) · Stack: this is **layer 3**
Spec: `documents/specs/assignment-notification-templates.md` §3.7 (Q16 picker + stamps), §3.8 (Q19 medium), §10 as-built (**B2a only** — the B2c contract facts this plan leans on live in `ContractTypes.cs:693-736` and `documents/rounds/round-notification-templates-b2c.md`)
Skills the plan must follow: `.github/skills/dialog-ui/SKILL.md`, `.github/skills/dropdown-ui/SKILL.md`, `input-width-scale`, `fluentui-component-props`, `fluentui-icons`

Owner rulings folded: **Q63** (B2b = publish dialog, after B2c) · **Q68** (ordering B2c → B2b → B3) · **Q16** (picker pre-selects most-specific; a tie pre-selects the default; the confirmed choice is stamped) · **Q19** (medium radio: Per policy / Email only / SMS only).

## Substrate inventory (taken before planning)

| Question | Answer found | Consequence |
|---|---|---|
| Does the dialog exist, and what does it carry? | `PublishDialog.razor` + `PublishDialogTypes.cs`: `PublishFormModel(Contacts, AudienceSummary, MandatoryReview)` → `PublishResult(ContactIds)` | WS2/WS3 add medium + per-channel template picks to the model and result |
| Does the client send `Channels`/stamps today? | **No** — `AssignmentsApiClient.PublishAsync(id, contactIds)` posts `new PublishAssignmentRequest(contactIds)`, leaving `Channels`/both ids at their defaults (`AssignmentsApiClient.cs:258-262`). The shared `_jsonOptions` are `JsonSerializerDefaults.Web` **with no ignore condition**, so those three fields go on the wire as **explicit nulls** today | WS1 extends the client — the last inert seam of the B2a wire. **No serializer change**: flipping `WhenWritingNull` on the shared options would change the shape of every request this client sends |
| Is there a `publish-options` client method? | **No** — only the API endpoint (`AssignmentRoutes.cs:1014`) | WS1 adds `GetPublishOptionsAsync` |
| Is the contract ready? | **Yes, fully** — `PublishOptionsDto` (channels + `PolicyPreferredChannelOrder` + `PolicyBlockedChannels` + both stored stamps), `PublishChannelOptionsDto.Templates` (**ordered**, most specific first), `PublishTemplateCandidateDto(TemplateId, Constraints, IsDefault)`, `PublishTemplateConstraintDto(Dimension, Value)` — `ContractTypes.cs:704-736`. `ContractTypes.cs:693-696` already pins the client's pre-select source: **"stored stamp ?? first applicable candidate"** | **no contract change this round** |
| Where are the call sites? | `AssignmentAuthoring.razor:3734` `PublishAsync()` (method) → posts at `:3752` with `ct` **positionally**; **`Index.razor:228`** `await Api.PublishAsync(id)` — the row-action quick publish, one-arg overload, null body (→ per-policy + stamp reuse), dialog-less | WS3 wires the dialog path; **`Index.razor:228` keeps working unchanged**. (The mobile client has its own `MobileAssignmentsApiClient.PublishAsync` and never calls this class — untouched by construction.) |
| Medium enum? | `PublishChannel { PerPolicy = 0, EmailOnly, SmsOnly }` (`ContractTypes.cs:680-690`), SMS-only = SMS **+ WhatsApp** per Q19 | radio group maps 1:1, no new enum |
| Is medium enforcement already server-side? | **Yes** — B2a WS2 enforces at the route and inside `NotificationRecipientFilter`, blocked channels winning | this round is **presentation only**; no server change |
| Dialog + dropdown conventions? | `DialogShellBase<TModel,TResult>` + `DialogShellFooter`, no nested dialogs (`.github/skills/dialog-ui/SKILL.md`); `.github/skills/dropdown-ui/SKILL.md` is the goto for selects — its wrapper table says "parent already holds a list of domain DTOs" ⇒ `DropdownComponent`, and it carries the v5 binding facts (`TValue` required, `@bind-Value`, tests target `fluent-dropdown` + `selected`) | follow them exactly |
| Where do dialog bUnit tests live? | **`tests/SchoolCollab.Assignments.Tests.Unit/`** — `QuestionEditDialogBunitTests.cs`, `MaterialEditDialogBunitTests.cs`, `TargetsAndAudienceDialogTests.cs`, and the existing `RegisterPublishDialog` scaffolding at `AssignmentAuthoringBunitTests.cs:2291-2303` | WS4 tests land **there**, next to the scaffolding (Admin.Tests.Unit compiles but is the wrong home) |

## Workstreams

**WS1 — the client seam (`AssignmentsApiClient`).**
1. `GetPublishOptionsAsync(Guid id, CancellationToken)` → `PublishOptionsDto?`. **Fail-open contract, pinned to the B1/B2a taxonomy:** a non-success status (204/404/500), a timeout, a non-JSON 200, **and a thrown transport failure (`HttpRequestException`, or a `TaskCanceledException` that is not the caller's token)** all log and return `null`; a genuine caller cancellation still propagates. An options outage must never block publishing.
2. A **new overload** — not optional params on the existing signature, because `AssignmentAuthoring.razor:3752` passes `ct` positionally: `PublishAsync(Guid id, IReadOnlyList<Guid>? contactIds, PublishChannel? channels, Guid? emailTemplateId, Guid? smsTemplateId, CancellationToken ct = default)`. It puts the three fields on `PublishAssignmentRequest`; the two existing overloads stay byte-identical (the one-arg `Index.razor:228` quick publish and the mobile client keep their null body).
3. **No serializer change.** `_jsonOptions` keeps writing explicit nulls — the wire shape every existing request already has.

**WS2 — the dialog (`PublishDialog.razor` + `PublishDialogTypes.cs`).**
1. **Medium radio group** (Q19): *Per policy* (default), *Email only*, *SMS only*, bound to `PublishChannel`. Blocked channels from `PolicyBlockedChannels` are **annotated, never disabled** — all three states stay selectable, because the server's blocked channels still win ("Email only" on a tenant-blocked email reads *blocked by policy — stays blocked* beside it). The radio is disabled **only** when both channel groups (email, and SMS/WhatsApp) are blocked, with the footer explaining that publishing reaches no one by those channels.
2. **Template picker per stamped slot** (Email, SMS — the two stamp columns; WhatsApp is not author-selectable in v1): a `DropdownComponent` per slot (`.github/skills/dropdown-ui/SKILL.md` — the parent already holds the list, so there is no async-load window; the wrapper table's "parent already holds a list of domain DTOs" case), listing that slot's `Templates` **verbatim in the order received** (the evaluator's: most specific first, tie → default), with `OptionText` = the constraint summary plus the *Tenant default* marker on the `IsDefault` entry. **Pre-select rule (`ContractTypes.cs:693-696`: "stored stamp ?? first applicable candidate")**: the slot's **stored stamp** (`PublishOptionsDto.PublishEmailTemplateId` / `PublishSmsTemplateId`) when it appears in that slot's `Templates`, otherwise the **first entry** — an unpublish→republish reuses the author's confirmed choice instead of silently overwriting it, and a **stale stamp absent from `Templates` is never sent** (an explicit non-live stamp 400s, `AssignmentRoutes.cs:739-744`): it falls back to the first entry. When `Templates` is empty the slot shows a read-only note ("No templates configured — the built-in default will be used") and nothing is stamped. A slot whose channel is excluded by the chosen medium is disabled but **keeps its selection**.
3. `PublishFormModel` carries `Options` + the two selections (+ the medium); `PublishResult` gains `PublishChannel Channels`, `Guid? PublishEmailTemplateId`, `Guid? PublishSmsTemplateId` **with defaults** (the `PublishAssignmentRequest` shape), so the existing construction sites keep compiling — `RegisterPublishDialog` at `AssignmentAuthoringBunitTests.cs:2299` (`new PublishResult(null)`) is the one that is **not** otherwise rewritten this round.
4. Width/labels/icons follow `input-width-scale`, `fluentui-component-props`, `fluentui-icons`.

**WS3 — wiring (`AssignmentAuthoring.razor`).**
1. `PublishAsync()` fetches `GetPublishOptionsAsync` alongside the contacts; on `null` the dialog still opens with **no pickers** and *Per policy*, with a warning message bar ("Template options are unavailable right now — publishing continues with policy defaults.") — an outage must never look like a normal dialog, and never like "no templates configured" (that note belongs to a *successful* read with empty `Templates`).
2. Forward `result.Channels` / both stamp ids to the **new** `Api.PublishAsync` overload.
3. Nothing else on the page changes: no new section, no new page state, no persistence. `Index.razor:228`'s one-arg quick publish is untouched.

**WS4 — tests (bUnit + wire).**
- **Dialog (bUnit, `tests/SchoolCollab.Assignments.Tests.Unit` — beside `RegisterPublishDialog`, `AssignmentAuthoringBunitTests.cs:2291-2303`; render `PublishDialog` **directly**, the `QuestionEditDialogBunitTests` pattern)**: first candidate pre-selected when no stamp is stored (kills "select the default" and "select the last"); **a stored stamp that is not the first candidate pre-selects the stored one** (the P1 case — kills the stamp-overwriting bug); **a stale stamp absent from `Templates` falls back to the first entry and is never sent**; **the rendered option order equals the server's order verbatim** (the direct kill for re-sorting — the pre-select alone is vacuous here because Q67's default-first tie makes an IsDefault-first re-sort look identical), with a fixture where a plausible re-sort would change the winner; empty `Templates` ⇒ note shown + null stamp in the result; **`Options` null ⇒ the warning bar renders and no pickers appear** (the Risks section's load-bearing case — it must live here, because a page test mocks the dialog and can never render its markup); blocked channel **annotated while the option stays selectable**, and the radio disabled only when both channel groups are blocked, with the footer explanation; a medium-excluded slot is disabled **and keeps its selection**; the `IsDefault` entry renders the *Tenant default* marker and a constraint summary; confirmed result carries the picked ids + medium; cancel returns `null` (regression guard — it passes today).
- **Client (wire)** — beside the existing `AssignmentsApiClientInstructionMediaTests` precedent (stub handler + real client), i.e. also in `Assignments.Tests.Unit`: the new overload's body carries `channels` + both ids, and the null case is asserted **as explicit JSON nulls** (today's shape — no serializer change); `GetPublishOptionsAsync` GETs `/assignments/{id}/publish-options`, returns `null` on a non-success status **and on a thrown transport failure**, and propagates a genuine cancellation; the one-arg `PublishAsync` still posts a null body (guards `Index.razor:228`).
- **Page**: options `null` ⇒ publishing posts null stamps (the warning-bar half belongs to the Dialog set above — a page test intercepts `ShowDialogAsync` and never renders the dialog's markup).

## Explicitly NOT in this round

The editor page, grade-detail applicability card and `NotificationTemplateApiClient` (**B3**); `applicable/{gradeLevelId}` suggestions (B3); any server-side change (medium + stamps are enforced since B2a/B2c); the mobile publish surface; per-recipient template splitting (deferred).

## Contract / migration / secrets

No contract change (consumes B2c's shipped DTOs); no migration; no secrets. The only wire behaviour that changes is that the desktop publish request now actually **fills** fields that already exist.

**Plan-review history:** iteration 1 — **REWORK** (1 P1, 6 P2, zero open decisions) → folded: the picker's **stored-stamp pre-select** (`ContractTypes.cs:693-696` "stored stamp ?? first applicable candidate"; a stale stamp never sent), the fail-open taxonomy pinned (transport throws included), the **new overload** (existing signatures keep `ct` positionally; `Index.razor:228` named as an untouched consumer), `DropdownComponent` per the dropdown skill, WS4 moved to `Assignments.Tests.Unit` beside `RegisterPublishDialog`, the wire test restated as **explicit nulls**, the radio "annotate, never disable the option", the missing assertions added (stored stamp, medium-excluded slot, `IsDefault` label, both-blocked footer, outage warning bar), and the citation slips corrected (§12 does not exist; §10 is B2a-only). Iteration 2 — **REWORK** (1 P1, 3 P2) — **the ≤2 bound is spent**; folded: the outage warning-bar assertion moved into the **Dialog** set (a page test mocks the dialog and can never render its markup — the one assertion that could not fail), the stamp-400 citation corrected to `AssignmentRoutes.cs:739-744`, `PublishResult`'s new fields given **defaults** plus the one unre written construction site named (`AssignmentAuthoringBunitTests.cs:2299`), the re-sort test pinned to **rendered option order** (the pre-select alone is vacuous under Q67's default-first tie), and the wire tests homed beside `AssignmentsApiClientInstructionMediaTests`. No third gate run — the round proceeds to the worker on the owner's "go onto next", per the ≤2 bound rule.

## Risks

- **Fail-open vs. silent degradation**: an options outage must show the warning, never a bare dialog that looks normal (a teacher who picked nothing must know why).
- **Re-sorting in the UI**: the dialog must render `Templates` verbatim; sorting by constraint count in the component would silently undo the Q67 tie rule the server just implemented.
- **Radio vs. blocked channels**: "Email only" with email blocked must not read as a way to force email — the annotation is part of the acceptance criteria, not decoration.

## Verification

`dotnet build SchoolCollab.slnx` 0 errors · `SchoolCollab.Assignments.Tests.Unit` (dialog bUnit + page + client wire — one project run covers all three) · `SchoolCollab.ArchitectureTests.Unit`.
UI verification beyond bUnit: the UI tester runs the real app per `school-collab-ui-smoke-probe` (bUnit loose mode masks missing assets and circuits).

## Acceptance — ACCEPTED (2026-10-19, `deepseek-v4-pro:0813`)

Criterion-by-criterion over the working-tree diff + named tests; no criterion accepted on narrative.

| Criterion | Evidence |
|---|---|
| WS1 fail-open across every class + new overload + unchanged overloads | `AssignmentsApiClient.cs:232-240` catch filter (`HttpRequestException`/`JsonException`/`NotSupportedException` + `OperationCanceledException && !ct.IsCancellationRequested`); `:301-320` new overload; `:294-299` pre-existing overloads byte-identical; `_jsonOptions` untouched. Wire tests: 204/404/500, non-JSON 200, thrown transport, bare-OCE **timeout**, cancellation propagation, explicit nulls, one-arg `"null"` body |
| WS2 medium radio | `PublishDialog.razor:41-74` — `EmailGroupBlocked`/`SmsGroupBlocked` (SMS **and** WhatsApp)/`AllChannelGroupsBlocked`; `BlockedChannel_IsAnnotated_WhileTheOptionStaysSelectable`, `BothChannelGroupsBlocked_DisablesTheRadioGroup_WithTheFooterExplanation` |
| WS2 picker: verbatim order, stored-stamp pre-select, stale never sent | `PublishDialog.razor:86-157` passes `Templates` verbatim (no sort); `PreSelect` (`:249-259`) = `storedStamp ?? candidates[0]`; `StaleStamp_FallsBackToTheFirstEntry_AndIsNeverSent`, `RenderedOptionOrder_EqualsTheServersOrder_Verbatim` (default **last** in the fixture — non-vacuous), `StoredStamp_ThatIsNotTheFirstCandidate_IsPreSelected` (the P1 kill) |
| WS2 empty `Templates` vs `Options` null | `:90-96`/`:120-126` (note) vs `:27-34` (warning bar); `EmptyTemplates_ShowTheReadOnlyNote_AndStampNothing`, `OptionsNull_RendersTheWarningBar_AndNoPickers` |
| WS2 medium-excluded slot; `IsDefault` marker + summary | `EmailSlotDisabled`/`SmsSlotDisabled` (`:206-211`); `MediumExcludedSlot_IsDisabled_AndKeepsItsSelection`; `TemplateOptionText` (`:213-225`) + its test |
| WS3 wiring | `AssignmentAuthoring.razor:3745-3762` — `optionsTask` launched, awaited after contacts; forwards medium + both stamps; `Publish_ForwardsTheDialogsMediumAndTemplatePicks`, `Publish_OptionsReadFails_PostsNoStamps`; `Index.razor:228` untouched |
| Records | `PublishDialogTypes.cs:42-47` defaults keep `new PublishResult(null)` (`AssignmentAuthoringBunitTests.cs:2355`) compiling |
| Tests | 12 dialog bUnit + 9 wire + 2 page, mapped 1:1 to WS4; timeout / cancellation / rendered-order / stale-stamp / outage-bar each pin a branch that fails pre-change |
| Hygiene | diff = 4 `.Application` src files + 1 test + 2 new test files; `ContractTypes.cs` untouched; no `.Api`/`.Core`/migration; no secrets; no `.pi/skills`/`AGENTS.md`/specs; `models.json` untracked-only; `DialogShellBase`/`DialogShellFooter` + `DropdownComponent`/`FieldWidth.W9` per the skills |

**Untested / undiscriminating:** none load-bearing. (One sub-assertion was case-vacuous — `NotContain("No templates configured")` against a lowercase note — **fixed in-round** to `NotContainEquivalentOf`.) `Cancel_ReturnsNull` and `Publish_OneArgumentOverload_StillPostsANullBody` are honestly labelled regression guards.

**Deviations recorded:** none outstanding (the worker's seven were all adjudicated **accept** by the diff gate).

**Gate trail:** plan gate REWORK ×2 (1 P1 + 6 P2, then 1 P1 + 3 P2; bound spent, folded, no third run) → worker → diff gate **PASS** (2 P2: the `TaskCanceledException`→`OperationCanceledException` narrowness and the untested timeout branch — **both closed by the parent**, +1 test) → acceptance **ACCEPTED**.

**Patterns to carry forward:** (1) a fail-open filter keys on the **base `OperationCanceledException` gated by `!ct.IsCancellationRequested`** — `TaskCanceledException` misses stream-read timeouts and the page's own `catch (OperationCanceledException)` then swallows them silently; (2) a picker's correctness hinges on **rendered option order**, not the pre-select — a default-first tie makes the pre-select assertion vacuous.

## Verification (measured)

`dotnet build SchoolCollab.slnx` **0 errors** · Assignments.Tests.Unit **1299/0** · Architecture **129/0** · Admin.Tests.Unit **699/0**.

## UI verification — NOT COMPLETED (recorded honestly)

Two delegated real-browser hunts (`minimax-m3`, per `school-collab-ui-smoke-probe`) were attempted and **both timed out at the 30-minute cap during app bring-up**; neither produced a bug report.

Evidence gathered by the parent between attempts:
- Aspire dashboard `https://localhost:17160/` answered **302** — the AppHost starts and `dotnet watch` loads all 27 projects.
- Docker resources were up (`postgres`, `keycloak`, `cache`, `mailpit`, `pgadmin`).
- The Admin app's port is **not fixed** and must be discovered from the running process or the AppHost log; the first attempt hung on an un-timeout'd `curl`, the second one still exhausted its budget.
- `MigrationService` reported `Exited` in the AppHost log — the app may come up with an unmigrated database, which would make an empty-data dialog look like a broken one.

**Consequence, stated plainly:** the round's UI evidence is the **12 bUnit dialog tests** (markup-level: warning bar vs empty-list note, rendered option order, stored-stamp pre-select, blocked-channel annotation, both-blocked disable, medium-excluded slot) plus the acceptance pass. **No real-browser proof exists** that the dialog renders with its assets, that the pickers appear at a sane width, or that the footer stays reachable.

**Before merge, one of these must happen:** (a) the owner runs the probe via the dev hot-reload loop (`documents/runbooks/dev-loop-hot-reload.md`) and confirms the five behaviours listed above in a browser, or (b) the round is merged with this gap explicitly accepted and the browser pass scheduled as a follow-up on the B2b layer. It must not be silently treated as verified.

### Parent-side probe attempt (Q71 (a), 2026-10-19, after a full AppHost stop/restart)

Owner instructed a full kill of the AppHost processes and ports, then a restart. What the restart produced:

- `dotnet run --project src/AppHost/SchoolCollab.AppHost` starts; **only the Aspire dashboard** ever logs `Now listening on: https://localhost:17160`.
- The AppHost log stops at 84 lines with dashboard output and **no per-resource startup line at all**.
- DCP **proxy listeners bind** the launch-profile ports (`7300` https / `5300` http for `SchoolCollab.Admin`, plus 5199/5243/5400/5450/5460/5700/7199/7400/7450/7460), and a raw TCP connect to `7300` succeeds — but **every HTTP(S) request returns `000`** (connection accepted, then closed with no response), i.e. the proxy is up and the app behind it is not.
- 23 `dotnet` processes alive, docker resources healthy (`postgres` 4 days; `keycloak`/`cache`/`pgadmin`/`mailpit` recreated by the AppHost).
- No error, exception or migration line appears in the AppHost's stdout.

**Reading:** the ports answering nothing while DCP proxies bind is the signature of services that never start — consistent with the earlier hunt's observation that `MigrationService` reported `Exited` and with a `WaitForCompletion(migration)` gate in front of the apps. The AppHost's stdout does not carry the resource logs, so the failing resource has to be read from the dashboard (still running at `https://localhost:17160`) or by running the migrator standalone.

**Conclusion for this round:** the real-browser probe **still did not complete**, now with a reproducible blocker rather than a timeout: services never become reachable through the DCP proxies. Next diagnostic step, if the owner wants the browser pass: run `SchoolCollab.MigrationService` standalone against the dev postgres (the repo's own `run-migration-service-standalone` skill) and fix/report the migration failure, then re-run the probe. The AppHost was left running so the dashboard can show which resource is stuck.
