Provider: Cline/clinepass (planned: full Tier 3 per D2) · reviewer **`glm-5.3-flash`**, run **externally** by the owner from `documents/rounds/plan-review-b1.packet.md` (decision H1) · **Tier 3** (owner decision E2)
**Status:** **gate CLEARED — implementation IN PROGRESS.** Plan review (reviewer seat
`deepseek-v4.1-flash`, per owner direction): FAIL → revised → delta re-review: **PASS** (2 MINOR,
both fixed in-plan). Per decision F1/Q-D/Q-E/Q-F. Execution
home: worktree `School-Collab-notif-b1`, branch `feat/notification-templates-b1`, base **`4fb36f28`**.

### Implementation progress (2026-10-18)

**Done and verified** (`dotnet build SchoolCollab.slnx` 0 errors; Assignments unit **1183/0**,
Admin unit **699/0**, Architecture **126/0** incl. migration + wiring guards; the single bUnit
load-race flake passed on rerun, consistent with the known `fix/bunit-load-race` issue):

- Q-D extraction: `RichInstructionSanitizer` moved to `SchoolCollab.Core.Abstractions`
  (namespace `SchoolCollab.Core.RichText`), `HtmlSanitizer` PackageReference added (CPM, no
  Version); old Admin.Shared copy deleted; three `_Imports.razor` + the Admin sanitizer test
  re-pointed. Zero new project references (verified direct for Admin.Shared, transitive for
  Assignments.Core).
- Renderer + records: `NotificationTemplateRenderer` (pure, GeneratedRegex substitution),
  `NotificationTagValues`, `RenderedMessage`; §3.2 semantics (case-insensitive, unknown-literal,
  SMS-strip for `{{Instructions}}`, built-in defaults with due-date + excerpt preview shape).
- Resolver: `INotificationTemplateResolver` + `ResolvedNotificationTemplate` (Core);
  `HttpNotificationTemplateResolver` (Api, 5s wall clock, 204→null, fail-open);
  **`NullNotificationTemplateResolver` registered in `AddAssignmentsCore`** so the Worker's
  container always resolves the broadcaster's dependency (MAJOR-1 fix via the reviewer's
  offered alternative); Api overrides with the HTTP impl after `AddAssignmentsCore`.
- Broadcaster: per-channel template cache (one Settings read per channel per broadcast),
  `TenantContext.TenantName` → SchoolName (Q-C), `BuildLink` on `NotificationLinkOptions`
  (`Families__PublicBaseUrl`; relative when unset).
- Queue-time persistence: `NotificationLog.TemplateId` (nullable) on `Queue`/`Requeue`; Worker's
  `SweepNotificationQueuer` passes `null` (built-in default) — the B1/B2 boundary holds.
- Migration `20261018154516_AddNotificationLogTemplateId` — nullable `uuid` ADD COLUMN;
  `has-pending-model-changes` parity green.
- Tests: 12 renderer unit tests covering the §8 rows in this slice.

**Still open in-round:** (1) scope item 7 enrichment — widen the Students API subscription payload
with `ContactName`/`StudentName` (currently degrades to "", the degrade path is what ships);
(2) wire-level fail-open tests for `HttpNotificationTemplateResolver`; (3) integration suite run;
(4) **MAUI head build** (`src/Mobile/SchoolCollab.Mobile` with `-f net10.0-windows10.0.19041.0
-m:1 -p:BuildInParallel=false`) — REQUIRED because `Admin.Shared` changed; (5) full-solution
`dotnet test` before commit. Nothing is committed.

# Round B1 — assignment-notification-templates: render core (assignments-db)

Spec: `documents/specs/assignment-notification-templates.md` §3.3, §3.4, §3.12, §4, §8 (workstream 2, minus sweep wiring).
Base: **`4fb36f28`** (post-#364, so both #363's authorization write-point and #364's CodedValue cluster split are in place).

## Plan

**Goal.** Replace the hardcoded render path with a template-driven one, resolved cross-context and
rendered at queue time. This is the highest-risk round in the feature: it rewrites the code path
every outbound notification already uses, and it `ALTER`s a live table.

### Scope

1. **`NotificationTemplateRenderer`** — pure static renderer replacing `NotificationMessageBuilder`
   *as the render path*: resolve template → substitute tags → escape → re-sanitize (email) → emit
   subject/body. `NotificationMessageBuilder` survives as the **built-in default source**, its texts
   upgraded to the §3.3 preview shape.
2. **Tag substitution** per §3.2: case-insensitive; an unknown tag stays **literal** (visible in
   preview, never silently dropped); values HTML-escaped for email, plain text for SMS/WhatsApp;
   `{{Instructions}}` is email-only and stripped from SMS/WhatsApp.
3. **Channel-appropriate `{{Link}}`** — absolute `/deeplink/{token}` for email when
   `Families__PublicBaseUrl` is set, relative otherwise. B1 renders the deep link only; `/l/{code}`
   short links are B2.
4. **`INotificationTemplateResolver`** — interface in `Assignments.Core`, HTTP implementation in
   `Assignments.Api` on the documented `AddCrossModuleHttpClient("settings-api", …)` client already
   registered at `Assignments.Api/Program.cs:152`. **Fail-open**: any Settings outage or non-success
   response falls back to built-in defaults — a template outage must never block a publish (§3.3).
   Mirrors the `IAssignmentPolicyResolver` posture.
5. **Queue-time render preserved** (ar-16 (b)) — a template edit never mutates an already-queued row;
   `AssignmentNotificationBroadcaster` keeps its upsert-in-place path and re-renders with the freshest
   template on republish.
6. **`NotificationLog.TemplateId`** (nullable Guid) — the send-time record of which template rendered
   each message; `null` = built-in default. Persisted alongside the render.
7. **Name enrichment** (§3.4) — widen the existing subscription-resolution payload (`SubscriberInfo`
   in the Students API response) with `ContactName` and `StudentName`, populated where the rows are
   already read (**no second hop**). `GuardianRole` rides the existing `Role`; `SchoolName` from the
   tenant context, in-process. **Every name degrades to empty string** — enrichment never fails the
   parent operation.
8. **Email re-sanitize** (§3.12) — the outbound body passes the same sanitizer policy the editor
   writes with; one sanitizer, two applications. **Home (Q-D, resolved)**: the tag-allowlist
   sanitizer is **extracted from `Admin.Shared/Components/RichText/RichInstructionSanitizer.cs`
   into `SchoolCollab.Core.Abstractions`** — both consumers already reference that project
   (`Admin.Shared`'s csproj; Assignments.Core's CQRS abstractions), so zero new project references
   and `Assignments.Core` stays UI-free. `Admin.Shared` keeps calling the extracted class.
9. **Renderer input contract (Q-E, resolved)** — `AssignmentPublishedContext` is widened with
   `Description`, `DueDate`, `Instructions` (the publish handler already holds the entity — zero
   extra reads); the broadcaster assembles a single **`NotificationTagValues`** record (content +
   enriched names + link) and passes it to the pure renderer. The renderer takes one input type,
   never nine parameters.

### Out of scope (later rounds)

| Deferred | Round |
|---|---|
| `ShortLink` minting, Families `/l/{code}`, SMS-appropriate short links | B2 |
| `Assignment` template stamps, `publish-options`, `Channels` medium restriction | B2 |
| Sweep (E3) auto-resolution | B2 |
| Editor page, publish dialog, grade-detail card, `NotificationTemplateApiClient` | B3 |

### Files (expected)

| Area | File |
|---|---|
| Render | `Assignments.Core/Services/NotificationTemplateRenderer.cs` **new** |
| TagValues | `Assignments.Core/Services/NotificationTagValues.cs` **new** — the renderer's single input record |
| Sanitizer | `SchoolCollab.Core.Abstractions/…/RichInstructionSanitizer.cs` **moved** from `Admin.Shared/Components/RichText/` — namespace updated, `Admin.Shared` call site re-pointed; **requires `<PackageReference Include="HtmlSanitizer" />` in `SchoolCollab.Core.Abstractions.csproj`** (currently has zero packages; version is CPM-pinned at 9.2.1039 — no `Version` attribute, per CPM rules) |
| Defaults | `Assignments.Core/Services/NotificationMessageBuilder.cs` — kept, texts upgraded |
| Tags | `Assignments.Core/Domain/NotificationTemplateTags.cs` — already on `main` from Round A |
| Resolver | `Assignments.Core/Services/INotificationTemplateResolver.cs` **new**; `Assignments.Api/Services/HttpNotificationTemplateResolver.cs` **new** |
| Wiring | `Assignments.Api/Program.cs` **and `Assignments.Worker/Program.cs`** — register the fail-open HTTP resolver on each host's existing `settings-api` client, explicit timeout + fail-open. The Worker already calls `AddAssignmentsCore` (`Worker/Program.cs:31`), so the broadcaster's `INotificationTemplateResolver` constructor dependency must resolve there too — registering only in the Api leaves a latent DI crash that detonates at B2's sweep wiring |
| Broadcast | `Assignments.Core/Services/AssignmentNotificationBroadcaster.cs` — render via the resolver; persist `TemplateId` |
| Entity | `Assignments.Core/Domain/NotificationLog.cs` — `TemplateId`, set on `Queue`/`Requeue` |
| Context | `Assignments.Core/Services/IAssignmentNotificationBroadcaster.cs` — `AssignmentPublishedContext` widened with `Description`/`DueDate`/`Instructions` (Q-E) |
| Enrichment | Students API subscription response + the Assignments recipient model |
| Migration | `Assignments.Core/Migrations/*_AddNotificationLogTemplateId.cs` **new** |
| Worker ripple | `Assignments.Worker/Services/SweepNotificationQueuer.cs` — `Queue`/`PersistSkipAsync` calls gain a `TemplateId: null` argument (built-in default) until B2 wires the sweep resolver |

### Migration

`assignments-db`: one nullable column, `notification_logs.template_id uuid null`. No data migration —
existing rows stay `null`, which the spec already defines as "built-in default". Per
`.github/copilot/rules/ef-migrations.md`; `MigrationGuardTests` parity must stay green.

### Verification (§8 rows in this slice)

| Contract | Discriminating test |
|---|---|
| Tag substitution: case-insensitivity, unknown-literal, escaping, `{{Instructions}}` email-only | renderer unit tests |
| Base URL: set ⇒ absolute, unset ⇒ relative | renderer tests |
| Resolver fail-open (Settings outage ⇒ defaults, publish proceeds) | resolver tests |
| Queue-time semantics unchanged + `TemplateId` persisted (null = default); retry idempotency; republish re-render | broadcaster/queue tests |
| Name enrichment degrades to empty on absence/failure | enrichment tests |
| Built-in defaults render at the upgraded §3.3 preview shape | renderer default tests, per kind × channel |
| Model/snapshot parity | `MigrationGuardTests` in CI |

**B1/B2 boundary note.** In B1 only the Publish path renders through the resolver. The Worker's E3
kinds (Reminder/Completion/Overdue via `SweepNotificationQueuer`) remain builder-rendered until B2
wires the sweep — "replace the hardcoded render path" in the Goal means the Publish path in this
round; §6's "all four render through the resolver" completes in B2.

### Gates

- **Plan review is external** (H1). `glm-5.3-flash` reviews `documents/rounds/plan-review-b1.packet.md`
  + this plan + the spec, in a session the owner drives; the orchestrator transcribes the returned
  block into `## Review` verbatim. No implementation starts before that block is on the round doc.
- **Why not in-session dispatch.** `spawn_agent` takes no model parameter and inherits the session
  model, which `~/.pi/agent/settings.json` sets to `cline-pass/deepseek-v4.1-flash` — the *worker*
  id. Forcing `defaultModel` to glm-5.3-flash would make the worker glm too, collapsing seat
  diversity to zero, and `run-history.jsonl` records no model, so the result could not even be
  verified. The team-dispatch path is account-broken (`Unauthorized` on a bare liveness probe).
- The migration is `ALTER` on a live table, not `CREATE` — the least-rehearsed surface in this round,
  and the reason Tier 3 was chosen over Tier 2 for Round B.

### Execution home (H2)

Dedicated worktree `School-Collab-notif-b1`, branch `feat/notification-templates-b1`, base
`4fb36f28`. The main checkout is left free for other rounds: three collisions in the prior session
all traced to one checkout shared by several sessions (a stray `git add -A` that swept another
round's spec into a commit; a post-merge checkout that could not proceed; two round docs sitting
untracked in another agent's tree). The owner already isolates rounds as `-lint`,
`-mobile-closeout`, `-mobile-train` and `-r4fix`; B1 follows that practice.


### Questions — all settled

1–3 (**Q-A/Q-B/Q-C**): reviewer **AGREE** on all three leanings — renderer takes the resolved
template + tag values as arguments; `TemplateId` travels on a `RenderedMessage` record;
`SchoolName` from the tenant context in-process. Closed 2026-10-17.
4. **Q-D (sanitizer home)**: resolved by owner — extract to `SchoolCollab.Core.Abstractions` (scope 8).
5. **Q-E (renderer input contract)**: resolved by owner — widen the context, broadcaster assembles
   `NotificationTagValues`, renderer stays pure (scope 9).

### Carried corrections (ride this branch, not their own PRs)

1. **Round A round-doc status** (E3) — flip
   `documents/rounds/round-assignment-notification-templates-round-a.md` from "in review — PR #362"
   to `CLOSED — PR #362 (merged as d3328320)`.
2. **Model-registry id is stale** — `.pi/skills/orchestrator-worker-reviewer/references/models.md`
   names `cline-pass/mimo-v2.6-flash` as the clinepass **reviewer**, but that id is **absent from
   `~/.pi/provider-cache.json`** and does not resolve here, while `glm-5.3-flash`, `glm-5.3`,
   `kimi-k2.7-code` and `deepseek-v4.1-flash` all do. That stale entry sent an earlier reviewer
   attempt down the wrong path. **Owner direction (2026-10-17):** review work dispatches to
   **`deepseek-v4.1-flash`** via `spawn_agent` (which resolves the pi `defaultModel` — see the
   provenance note under `## Review`); correct `models.md` to record that as the reviewer
   dispatch path so the next round does not rediscover this.

## Review — `deepseek-v4.1-flash`, independent `spawn_agent` run (2026-10-17)

Transcribed verbatim from the reviewer's output block. 13 iterations, 422,622 input tokens.
**Model provenance (owner correction, 2026-10-17):** review dispatches via `spawn_agent` resolve to
the pi `defaultModel` — **`cline-pass/deepseek-v4.1-flash`** — regardless of the parent session's
model. The owner directs that review work dispatches to that worker seat; the earlier assumption
that the reviewer inherited the session model (`glm-5.3-flash`) was wrong, and with it the
same-family caveat: seat diversity (session worker ≠ dispatched reviewer model) **is** satisfied.

```
VERDICT: FAIL

FINDINGS
1. [MAJOR] Plan §Wiring / `Assignments.Api/Program.cs` row: the resolver is registered only in the
   Api host, but `AssignmentNotificationBroadcaster` is registered process-agnostic in
   `AddAssignmentsCore` (`Assignments.Core/Extensions.cs:62`), and the **Worker** calls
   `AddAssignmentsCore` too (`Assignments.Worker/Program.cs:31`). Q-A's leaning puts resolution
   inside the broadcaster, so the broadcaster gains an `INotificationTemplateResolver` constructor
   dependency — the Worker's DI container then holds a broadcaster whose required dependency is
   unregistered (the Worker has the `settings-api` named client at `Worker/Program.cs:39` but no
   resolver implementation). Nothing resolves the broadcaster in the Worker today (sweeps use the
   static `SweepNotificationQueuer`), so it is a latent runtime crash that detonates the moment
   anything in the Worker resolves it — i.e., exactly at B2's sweep wiring, where nobody will be
   looking at B1's DI.
   Fix: plan must register the fail-open HTTP resolver on the Worker's existing `settings-api`
   client as well (or scope resolution so the broadcaster's dependency is optional/defaults-only
   in hosts without it). State it in the Wiring row.
2. [MAJOR] Plan scope item 8 (§3.12 render-time re-sanitize): the one sanitizer lives in
   `SchoolCollab.Admin.Shared/Components/RichText/RichInstructionSanitizer.cs`; neither
   `Assignments.Core` nor `Assignments.Api` references `Admin.Shared` (verified in both `.csproj`
   files), and Assignments.Core must stay UI-free. The render happens in the broadcaster at queue
   time in the Api process, so scope item 8 has **no implementation home**: the implementer must
   invent either a cross-context project reference (architecture-policed surface) or a sanitizer
   extraction, both unreviewed. Note the precedent exists — `Students.Api` already references
   `Admin.Shared` — but the plan must pick, not the worker mid-round.
   Fix: name the mechanism in the plan (extract the tag-allowlist sanitizer into
   `SchoolCollab.Core`, or add `Assignments.Api → Admin.Shared` on the Students.Api precedent).
3. [MINOR] Plan §Files (Entity row): changing `NotificationLog.Queue`/`Requeue` to carry
   `TemplateId` (scope item 6) forces compile-touching
   `Assignments.Worker/Services/SweepNotificationQueuer.cs` (its `PersistSkipAsync`/queue calls
   and `NotificationMessageBuilder.SubjectFor` path construct/queue rows too). The Files table
   lists no Worker file, so the worker discovers the ripple mid-round.
   Fix: add the Worker file to the Files table, passing `null` TemplateId (built-in default)
   until B2.
4. [NIT] Plan §Questions Q-B rationale: "`Queue`/`Requeue` already take four positional strings" —
   `Requeue` takes 4 (`NotificationLog.cs:139`), but `Queue` takes **10**
   (`NotificationLog.cs:65-75`). The leaning survives (the record is still right, and `Requeue`
   can re-stamp from it), but the rationale understates the existing problem it argues against.

DECISIONS
Q-A: AGREE — §3.3 itself specifies "a pure static `NotificationTemplateRenderer`";
argument-injection mirrors the existing builder's documented posture and confines fail-open to the
resolver + one broadcaster call site, both independently testable with a fake resolver (the Worker
DI gap above is a wiring omission, not a reason to abandon the cut).
Q-B: AGREE — a `RenderedMessage` record maps cleanly onto `Queue`/`Requeue`'s payload restamp and
kills the swap-argument class; only the plan's supporting fact is imprecise (NIT 4).
Q-C: AGREE — the spec mandates it: §3.4 states `SchoolName` comes from the tenant context
(`tenant_name` claim, in-process); passing it via `AssignmentPublishedContext` would contradict the
binding spec.

MISSING FROM THE PLAN
- Source of the assignment-content tag values: `AssignmentPublishedContext` is only
  `(AssignmentId, Title, PublishedAt, Recipients)` (`IAssignmentNotificationBroadcaster.cs:22-26`),
  yet §3.2 requires `{{Description}}`, `{{DueDate}}`, `{{Instructions}}` and §3.3's upgraded
  default preview shape needs the due date + description excerpt. The plan never says whether to
  widen the context, load the assignment in the publish handler, or pass a tag-value bag — the
  renderer's input contract is undefined.
- The §8 verification row "Built-in defaults = upgraded preview shape | renderer default tests per
  kind × channel" is dropped from B1's verification table even though scope item 1 upgrades the
  builder texts — the binding §8 slice is incomplete.
- A one-line interim-behavior note: in B1 the Worker's E3 kinds (Reminder/Completion/Overdue via
  `SweepNotificationQueuer`) remain builder-rendered with no resolver — the Goal's "replace the
  hardcoded render path" reads as all four kinds, and §6 of the spec says all four render through
  the resolver. Stating the B1/B2 boundary prevents a worker from assuming the sweep path is in
  scope.

TRIED-TO-BREAK
- The `ALTER` on the live table: nullable ADD COLUMN is safe — `NotificationDispatchService`
  re-sends the persisted `Subject`/`BodyHtml` payload (decision (b)) and never reads `TemplateId`;
  in-flight `Queued` rows keep byte-identical payloads, and `null` = "built-in default" is even
  audit-accurate for them (they were rendered by the then-current default). Nothing breaks.
- Two-sources-of-truth drift between renderer and surviving builder: defaults stay single-sourced
  in the builder (renderer consumes it), so Publish (resolver, fail-open→builder) and E3 (builder
  direct) cannot drift in B1.
- The B1/B2 scope cut: shipping Publish-only template resolution is coherent since unconstrained
  defaults ≡ current behavior; no half-built path that cannot ship.
- Fail-open achievability at the chosen seam: the `IAssignmentPolicyResolver` posture exists
  (`AssignmentPolicyResolver.cs`, `Program.cs:152` named client, explicit timeout planned) —
  consistent with §3.3.
- Tag rules vs §3.2: case-insensitive, unknown-literal, email-escape/SMS-plain,
  `{{Instructions}}` email-only, `{{Link}}` deep-link-only for B1 — plan matches the spec verbatim;
  `{{LinkExpiry}}` is derivable from the recipient's `DeepLinkExpiresAt` the broadcaster already
  handles.
```

### Disposition of findings

| Finding | Disposition |
|---|---|
| MAJOR 1 (Worker DI gap) | **Fixed in plan** — Wiring row now registers the resolver on both hosts |
| MAJOR 2 (sanitizer has no implementation home) | **Open → grill Q-D** — mechanism must be named in the plan before implementation |
| MINOR 3 (Worker ripple) | **Fixed in plan** — `SweepNotificationQueuer.cs` added to Files with `TemplateId: null` |
| NIT 4 (Q-B rationale) | **Addressed in substance** — the `### Questions` rewrite removed the inaccurate rationale from the operative plan entirely (the corrected figures live only in the review transcription); disposition originally overstated this as an in-place correction |
| MISSING 1 (renderer input contract) | **Open → grill Q-E** — the renderer's input contract must be defined |
| MISSING 2 (§8 row dropped) | **Fixed in plan** — row restored to the verification table |
| MISSING 3 (B1/B2 boundary) | **Fixed in plan** — boundary note added under Verification |

Q-A/Q-B/Q-C: all three **AGREE** — the plan's leanings stand and are no longer open questions.


### Diff-review attempts (provenance, 2026-10-18)

- **Attempt 1 — owner-named seat: FAILED before reading the patch.** `deepseek-v4-pro:0813-cloud`
  resolved to `cline/deepseek/deepseek-v4-pro-0813` (provider `cline`); the provider returned
  `insufficient_credits` (balance −$0.07). Not a model problem — Round A's D3 gate re-materialised
  as a *balance* block rather than an auth block. The owner must top up Cline credit for that seat.
- **Attempt 2 — sanctioned substitution (skill substitution rule).** Reviewer on the round's own pi
  profile: **`ollama-cloud/kimi-k2.7-code`** — the Tier-3-lean designated verifier, different family
  from the `deepseek-v4.1-flash` worker (seat diversity holds). Recorded so the two seats are never
  blended in the provenance.
- **Authoritative parent pass** (parent numbers only; child-reported numbers discarded):
  `dotnet build SchoolCollab.slnx` **0 errors** (281 warnings) · Architecture **126/0** ·
  Assignments.Unit **1187/0** · Assignments.Api.Unit **180/0** · Assignments.Tests.Integration
  **26/0** · Students.Unit **628/0**. The worker-reported bUnit flake did not reproduce here.
- **Parent finding P1 (pre-review)** — §3.2 tag-vocabulary drift: the shipped vocabulary is
  `AssignmentTitle` / `DescriptionExcerpt` (spec: `Title` / `Description`), and
  `GuardianName`, `GuardianRole`, `SchoolName`, `StudentName`, `LinkExpiry` are absent from
  `NotificationTemplateTags.All` (which the B3 editor chips read) while the renderer serves
  `ContactName`/`StudentName`/`SchoolName`/`LinkExpiry` as bare string literals. A template authored
  to the spec renders literal `{{Title}}`/`{{GuardianName}}` braces into a parent's inbox. Queued for
  the rework pass with the reviewer's findings.
- **Attempt 1 — resolution correction (owner, 2026-10-18).** Resolving the owner-named reviewer into the
  `cline` provider was **wrong**: the seat lives in the **`ollama-cloud`** provider block.
  `provider-cache.json` carries the bare `deepseek-v4-pro:0813` under that provider, so the dispatch id
  is **`ollama-cloud/deepseek-v4-pro:0813`** (the `:cloud` in the owner's naming is the ollama tag form).
  The `insufficient_credits` failure therefore belonged to a seat that should never have been chosen —
  not to the owner-named one.
- **Attempt 3 — authoritative seat:** `ollama-cloud/deepseek-v4-pro:0813` (owner-named). Attempt 2
  (`ollama-cloud/kimi-k2.7-code`) runs in parallel as a **cross-check only**: if both return verdicts the
  owner-named seat is authoritative and the other is recorded as a second opinion — never blended.

## Acceptance — written by the plan author (parent), per the owner rule of 2026-10-17

**Verdict: CLOSED.** Tier 3 lean: plan authored in-round, diff reviewed by two static seats,
acceptance transcribed by the parent as the plan's author. No UI tester — the diff carries no UI
surface. Rework loop used **1 of ≤2** reviewer iterations.

### Criteria checklist (evidence = diff line or named test)

| Criterion (plan scope + §8 rows) | Evidence | Met |
|---|---|---|
| Pure static renderer: resolve → substitute → escape → re-sanitize | `NotificationTemplateRenderer`; 13 renderer tests | ✔ |
| Ten spec tags; `All` in spec order; Round-A aliases still resolve | `NotificationTemplateTags` (patch:750–763); `SpecTags_AreTheFullSpecSet_ForTheEditorChips`, `LegacyAlias_*` | ✔ |
| `{{Description}}` renders the full value; 120-char cap only in the builder preview | patch:3374; `DescriptionTag_RendersTheFullValue_NotThePreviewExcerpt` | ✔ |
| `{{GuardianName}}` / `{{GuardianRole}}` sourced from the recipient; empty for student-owned | patch:2597; `GuardianNameAndRole_ComeFromTheRecipient_EmptyForStudentOwned` | ✔ |
| Plain-text values HTML-escaped in email, not SMS; markup tags raw, body re-sanitized | patch:3292/3305; `PlainTextValues_AreHtmlEscapedInEmailBody_NotInSms`, `Description_Tag_IsHtmlEscapedInTheEmailBody` (parent-applied) | ✔ |
| Single built-in default source: builder owns §3.3 preview texts, 4 kinds × 2 channels | `NotificationMessageBuilder.BuildEmailBody/BuildSmsBody/SubjectFor`; renderer delegates; 4 dead `Build*Html` removed | ✔ |
| Resolver in Core (one copy), Core-local wire mirror, no `Settings.Core` reference, both hosts register AFTER `AddAssignmentsCore` | patch:2662–2747; `NotificationTemplateResolverWiringArchitectureTests` | ✔ |
| Fail-open: 204 / 500 / timeout / non-JSON 200 → defaults; genuine cancellation propagates | `HttpNotificationTemplateResolverTests` (7, incl. `NonJsonPayload_ResolvesNull`) | ✔ |
| `NotificationLog.TemplateId` nullable (null = default); queue-time render preserved; republish re-renders | migration `20261018154516`; broadcaster/republish suites | ✔ |
| Name enrichment degrades to empty at four layers | `StudentsContactResolverNameEnrichmentTests`, `ListSubscribedContactsNameEnrichmentTests`, + 3 more | ✔ |
| Sanitizer extraction: one copy, no new project reference, Admin.Shared re-pointed | patch:3539 (**rename** entry); Architecture 129/0 | ✔ |

### Gates — parent-run, authoritative (child-reported numbers discarded and superseded)

`dotnet build SchoolCollab.slnx` **0 errors** · Architecture **129/0** · Assignments.Unit **1196/0** ·
Assignments.Api.Unit **181/0** · Assignments.Tests.Integration **26/0** · Students.Unit **628/0** ·
MAUI head **0 errors / 0 warnings** (`-f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false`).

### Review loop

- Plan gate (pre-B1): FAIL → plan revised → delta re-review PASS.
- Diff review iteration 0, two seats: **owner-named `ollama-cloud/deepseek-v4-pro:0813` — REWORK**
  (P1 tag drift + 3 P2: raw substitution, `NotSupportedException` gap, two default sources) and
  **cross-check `ollama-cloud/kimi-k2.7-code` — REWORK** (same P1 + vacuous-vs-spec tests + Worker
  wiring). Union = 1 P1 + 5 P2.
- Rework pass 1: all six closed; FIX 5 escalated to supervisor and approved as **option (A)** —
  resolver relocated Api → Core on the `NotificationPolicyResolver` precedent.
- Re-verify on the owner-named seat: **VERIFY: PASS**, six closed, 1 new P2 (`{{Description}}` raw).
- Parent-applied tiny rework: `{{Description}}` now escaped + a discriminating test row
  (Assignments.Unit 1195 → 1196). Build and all suites re-run green.

### Accepted residuals (carried, not fixed — each has an owner round)

1. SMS/WhatsApp `{{Link}}` renders the long `/deeplink/{token}`; short `/l/{code}` links are **B2 (WS3)**.
2. E3 kinds (Reminder/Completion/Overdue) still queue via `SweepNotificationQueuer` with `TemplateId: null`; resolver wiring is **B2 (WS6)**.
3. `families-public-base-url` fan-out + `documents/configuration.md` §2/§11 rows are **B2**.
4. Editor page, publish dialog, grade-detail card, `NotificationTemplateApiClient` are **B3 (WS5)**.
5. The older `AssignmentPolicyResolver` still catches only `HttpRequestException` (same hardening gap closed
   here for templates) — noted for a later round, not a B1 defect.

### Provenance and corrections recorded in-round

- **Reviewer seat:** owner correction — the seat lives in the **ollama-cloud** provider
  (`deepseek-v4-pro:0813`), not cline. The earlier cline resolution produced an `insufficient_credits`
  failure; it is recorded as a resolution error and never attributed to the named seat.
- **Stale ledger:** the "Still open in-round" list (enrichment, resolver wire tests) was already in the
  diff; the diff was ahead of the ledger. Corrected here.
- **Patch artifact:** generated from the index, **UTF-8**, 5059 lines. The sanitizer move appears as a
  git **rename** (`diff --git a/…Admin.Shared/…RichInstructionSanitizer.cs b/…Core.Abstractions/RichInstructionSanitizer.cs`,
  patch:3539), which is why no `deleted file mode` entry exists — and none is needed.
- **Nothing is committed, staged-for-commit, pushed or PR'd.** Commit / push / PR remain gated on the
  owner's explicit instruction (merge policy).
