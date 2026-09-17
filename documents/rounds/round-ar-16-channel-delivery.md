Provider: pi/ollama-cloud (models: light-mode worker `ollama-cloud/deepseek-v4.1-flash`, reviewer/orchestrator `ollama-cloud/glm-5.3-flash`) — **Tier 2 LIGHT ROUND**, Phase 4 slice **E2-core** (channel delivery + NotificationLog; Admin failure-surfacing UI deferred to ar-17)

# Round ar-16-channel-delivery (WS-E2 core)

**Round base:** branch `stack/16-ar-16-channel-delivery` (cut from `main` @ `1479ed32`; PR #240, base `main` — single-layer stack). Carries two doc commits before this round (`877cebe6`, `4496148e`, `fa31b875`).

**Execution mode:** light round (Tiers 1–2) — owner choice 2026-09-16. Parent writes the plan and accepts; **one** worker pass; one static diff-only reviewer (Tier 2), ≤1 rework iteration.

**Owner scope decision (2026-09-16) — Option A:** light mode carries no UI round (the skill's deterministic UI trigger sends any `.razor`/`.js` change to Tier 3), so this round delivers the **delivery core only**. The Admin/author dashboard **failure-surfacing UI** is deferred to **ar-17 (Tier 3)**. The read-side data it needs (a failed-notifications query + endpoint) **is** delivered here so ar-17 is purely presentational.

**Sources read & verified at plan time:** spec §4 `NotificationLog`, §5 notification rules, §6 NFR; breakdown §3 WS-E + Phase 4 rows (E2 acceptance) + D-2 email decision; impl-details §2 WS-E (Delivery paragraph — the column list and "publisher v1.1" note); `notification-delivery-plan.md` (policy half shipped; delivery deferred here); code: `AssignmentRecipient` (has `DeepLinkToken`/`DeepLinkExpiresAt`, `Channel`, `ContactId`, `WardStudentId`), `AssignmentNotificationBroadcaster` (v1 enqueues the outbox event only), `NotificationRecipientFilter` (policy filter, pure), `INotificationPolicyResolver`, `IContactResolver`/`SubscriberInfo`, `DeepLinkTokenMinter` (minted in the publish handler), `AssignmentsDbContext` + `Core/Migrations/`, `ArchiveSweepService` (hosted-service precedent); rules: `dotnet-best-practices`, `ef-migrations`, `configuration-documentation`, `testing`; skills: `orchestrator-worker-reviewer`, `bounded-context`.

## Plan

### Goal (E2-core)

Turn the v1 "enqueue one abstract event" publisher into **real, logged, retryable channel delivery**: a provider abstraction with a MailKit SMTP implementation and a null-sender dev fallback, a tenant-scoped `NotificationLog` written per policy-filtered recipient (one consolidated per-contact message carrying that contact's deep link), store-driven retry with backoff, and a read-side query + endpoint exposing failures so the ar-17 UI can render them. **No UI in this round.**

### In scope

1. **Channel-provider abstraction** — new folder `src/Assignments/SchoolCollab.Assignments.Core/Services/Delivery/`:
   - `IEmailSender` + `EmailMessage` record (`To`, `Subject`, `BodyHtml`, optional `From`); `ISmsSender` + `SmsMessage` record.
   - `MailKitEmailSender` — MailKit `SmtpClient`, throwing a **typed** `EmailDeliveryException` on failure (never a bare `Exception`; no `InvalidOperationException` from new code).
   - `NullEmailSender` — log-and-skip, reports success (dev/standalone default so nothing blows up without SMTP).
   - `LogAndSkipSmsSender` — stub for SMS/WhatsApp (D-2 decision: log-and-skip; policy `BlockedChannels` already filters).
   - `SmtpOptions` (`Host`, `Port`, `User`, `Password`, `FromAddress`, `UseStartTls`) bound from the `Smtp` configuration section.
   - Deterministic provider selection in `Core/Extensions.cs`: **`Smtp:Host` set → `MailKitEmailSender`; unset/blank → `NullEmailSender`** (a pure, unit-testable choice + comment). Register the choice so a test can assert both branches.
   - Message building must be a **pure static method** (recipient/subject/body → `MimeMessage`) so it is unit-testable without SMTP.
2. **`NotificationLog` entity** (`Core/Domain/NotificationLog.cs`) — standalone tenant entity (copy the `BaseTenantEntityWithAudit` conventions; `Guid Id`, `TenantId`, audit stamps, `MarkAsDeleted`/`Recover` where the base provides it): `AssignmentId`, `RecipientId`, `ContactId`, `Channel` (`SchoolCollab.Core.Notifications.NotificationChannel`), `Kind` (new enum `NotificationKind { Publish, Reminder, Completion, Overdue }`), `Attempt` (int, default 0), `DeliveryStatus` (new enum `{ Queued, Sent, Failed, Skipped }`), `SentAt?`, `FailureReason?`, `NextRetryAt?`, plus the **rendered payload** (`ToAddress`, `Subject`, `BodyHtml`) so a retry re-sends exactly the same message without re-resolving anything. State transitions as intention-revealing methods (`MarkSent`, `MarkFailed(reason, nextRetryAt)`, `MarkSkipped(reason)`) — no public setters.
   - EF configuration (fluent, following the existing per-entity configuration style in `AssignmentsDbContext`) + `DbSet<NotificationLog> NotificationLogs` + **migration in `src/Assignments/SchoolCollab.Assignments.Core/Migrations/`** (that is where the snapshot lives; follow `.github/copilot/rules/ef-migrations.md` and the `DesignTimeAssignmentsDbContextFactory`). Index on `(TenantId, DeliveryStatus, NextRetryAt)` for the drain.
3. **Publisher v1.1** — `AssignmentNotificationBroadcaster` (or a v1.1 implementation behind the same interface) now, **for each recipient handed to it**: resolve the destination address, render the consolidated per-contact message **including that recipient's deep link** (`/deeplink/{DeepLinkToken}`), and insert one `NotificationLog` row (`Queued`, `Kind = Publish`, `Attempt = 0`, `NextRetryAt = now`). Keep the existing `AssignmentPublishedIntegrationEvent` outbox enqueue unchanged (E3's future consumer).
   - Recipients with **no** `DeepLinkToken` (or `DeepLinkExpiresAt` in the past) are recorded as `Skipped` with a reason, never sent.
   - **Verify where the policy filter is applied** (`NotificationRecipientFilter` / `INotificationPolicyResolver` in the publish handler) and ensure **only the filtered set** produces log rows (blocked channels dropped, `MaxNotifications` cap honoured, preferred-channel ordering preserved). If the filter is not yet applied on that path, apply it there and record the deviation in the worker report.
   - Address resolution: if no existing resolver exposes a contact's email address, add a small `IContactAddressResolver` in `Core/Services/Delivery` with the Api-side HTTP implementation (mirroring how `IContactResolver`/`INotificationPolicyResolver` keep HTTP out of Core). Reuse an existing Students-API contact endpoint if one already returns the address — the worker must find it rather than invent one.
4. **Dispatcher + retry/backoff** — mirror the `ArchiveSweepService` shape (Core service class + thin `BackgroundService` wrapper in `Assignments.Api/Services/`):
   - Core `NotificationDispatchService.DispatchPendingAsync(ct)`: load a bounded batch of `Queued`/retryable rows with `NextRetryAt <= now`, send by channel, then `MarkSent` or `MarkFailed(reason, NextRetryAt)`; increment `Attempt`; on reaching the retry cap mark terminal `Failed` (no further `NextRetryAt`).
   - Backoff as a **pure static function** (e.g. `NotificationRetrySchedule.Next(attempt)` → 1m, 5m, 15m, 60m, then cap) so it is unit-testable; cap `MaxAttempts` as a named constant.
   - Api `NotificationDispatchSweepService : BackgroundService` — interval loop, per-candidate error isolation, cancellation-aware shutdown (exactly the `ArchiveSweepService` pattern; register it where that one is registered in `Program.cs`).
5. **Failure read-side (enables ar-17, no UI here)** — a query + `GET /assignments/{id}/notification-failures` returning `NotificationFailureDto(RecipientId, ContactId, Channel, Kind, Attempt, FailureReason, NextRetryAt)`, **tenant-scoped**, failed rows only. Endpoint goes in the existing assignment endpoint group (endpoint-grouping rule) — never inline in `Program.cs`.
6. **AppHost + configuration** — MailPit **dev container** (`axllent/mailpit`, SMTP 1025 + web UI) plus the repo's **first secret parameter**: `smtp-host`, `smtp-port`, `smtp-user`, `smtp-password` (`secret: true`), `smtp-from-address` in the AppHost `Parameters:` block, fanned out to `assignments-api` via `WithEnvironment("Smtp__Host", …)` etc. Update `documents/configuration.md` (§2 rows, §11 env-var rows) in the same PR per the configuration-documentation rule.
7. **CPM** — `MailKit` added to `Directory.Packages.props` (version pinned there) + `PackageReference` **without** `Version` in the owning `.csproj`.
8. **Tests** — new unit tests (see the binding list below) covering: provider selection both branches; null-sender + SMS stub behaviour; pure MimeMessage building (To/Subject/Body/From); pure backoff schedule; dispatcher transitions (Queued→Sent; failure→Attempt++/Failed/NextRetryAt; retry cap → terminal Failed); broadcaster row-per-recipient with deep link, and `Skipped` when the token is absent/expired; failure-query tenancy + failed-only filtering.

### Out of scope (do NOT touch)

- **Any UI**: no `.razor`, `.razor.css`, `.css`, `.js`, nothing under `wwwroot/`, no Blazor/ApiClient project changes. The Admin failure-surfacing surface is **ar-17**.
- The **`SchoolCollab.Assignments.Worker` project** and the reminder / overdue / completion / archive sweeps (**E3**).
- Real SMS/WhatsApp providers, bounce/webhook ingestion, per-tenant branding, delivery receipts.
- `Assignments.Application`, `Assignments.Contracts` other than the one new DTO, and any other bounded context.

### Decisions (parent)

- **(a) Delivery drain lives in `Assignments.Api` for E2**, not a new worker project — the `ArchiveSweepService`/`ScheduledPublishSweepService`/`StagedFileSweepService` precedent already ships hosted services there; the `Assignments.Worker` project is E3's deliverable. Recorded so E3 can move it.
- **(b) Render-and-persist at queue time** (`ToAddress`/`Subject`/`BodyHtml` on the log row): retries stay idempotent, need no cross-context lookups, and the message a guardian receives cannot change under them.
- **(c) `Skipped` is a distinct terminal status** from `Failed` (no token / blocked) — failures are the ar-17 surface; skips are expected policy outcomes.
- **(d) Null-sender is success, not failure** — an unconfigured SMTP host must not fill the failure surface.
- **(e) One consolidated message per contact** (v1.1 note already written into the broadcaster) — not per-ward, not per-channel-duplicate.

### Expected files (indicative — the worker may add/rename within this scope)

- `src/Assignments/SchoolCollab.Assignments.Core/Services/Delivery/` — `IEmailSender.cs`, `ISmsSender.cs`, `EmailMessage.cs`/`SmsMessage.cs`, `MailKitEmailSender.cs`, `NullEmailSender.cs`, `LogAndSkipSmsSender.cs`, `SmtpOptions.cs`, `EmailDeliveryException.cs`, `IContactAddressResolver.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/NotificationLog.cs` (+ `NotificationKind.cs`, `NotificationDeliveryStatus.cs`)
- `src/Assignments/SchoolCollab.Assignments.Core/Services/NotificationDispatchService.cs`, `NotificationRetrySchedule.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Data/AssignmentsDbContext.cs` (DbSet + config), `Core/Migrations/<new migration>`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/AssignmentNotificationBroadcaster.cs` (v1.1), `Core/Extensions.cs` (DI)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/…/GetNotificationFailuresQuery` (+ handler) and the Api endpoint extension file
- `src/Assignments/SchoolCollab.Assignments.Api/Services/NotificationDispatchSweepService.cs`, `Program.cs` (registration + HTTP address resolver)
- `src/AppHost/SchoolCollab.AppHost/Program.cs`, `appsettings.json` (MailPit + smtp params)
- `Directory.Packages.props`, the owning `.csproj`
- `documents/configuration.md`
- tests under `tests/SchoolCollab.Assignments.Tests.Unit/…`

### Binding test list (round acceptance)

1. Provider selection: `Smtp:Host` set → MailKit sender; unset/blank → `NullEmailSender` (both branches asserted).
2. `NullEmailSender` returns success without touching SMTP; `LogAndSkipSmsSender` logs and succeeds.
3. `MimeMessage` building: To/From/Subject/HTML body exactly as given (pure method, no SMTP).
4. `NotificationRetrySchedule.Next(attempt)` — deterministic sequence and cap.
5. Dispatcher: `Queued` + `NextRetryAt <= now` → `Sent` + `SentAt`; sender throws → `Attempt` 1, `Failed`, `NextRetryAt` = now + backoff, reason recorded; at the cap → terminal `Failed` with no `NextRetryAt`.
6. Broadcaster: one `NotificationLog` per filtered recipient, `Queued`, `Kind = Publish`, body contains that recipient's `/deeplink/{token}`; recipient without a token (or expired) → `Skipped` with reason; blocked/capped recipients are absent entirely.
7. Failure query/endpoint: returns failed rows only, tenant-scoped (a second tenant's rows are invisible).

### Constraints (non-negotiable)

- Read and follow `.github/copilot/rules/dotnet-best-practices.md` (+ its skill), `ef-migrations.md`, `configuration-documentation.md`, `testing.md` **before** writing code. ArchitectureTests enforce the "Never" list.
- CPM: no `Version` on any `PackageReference`; net10.0; no MediatR (CQRS via `ICommandHandler`/`IQueryHandler` + Scrutor); XML docs on new public members; primary constructors; typed exceptions only.
- Tenancy: the new entity is a tenant entity; every query is tenant-scoped; tenant-isolated cache keys if any cache is added.
- No direct project references between bounded contexts; cross-context reads go through the existing HTTP-resolver-in-Api pattern.
- `dotnet build SchoolCollab.sln` must be 0 errors; run the affected test projects **plus `SchoolCollab.ArchitectureTests.Unit`**.
- **Output discipline:** exactly one `dotnet test tests/<X> 2>&1 | grep -E "^\s*failed |total:|failed:" | head -40` per project per read; no ad-hoc pipelines, no `zz*.log` debug files; ≤2 attempts per result read; 5-minute cap per item. **MANDATORY:** write `documents/rounds/.ar-16-worker-report.md` (changed files, build/test verdicts, deviations) **before** finishing — a pass without that report is a failed pass.
- 30-minute cut-line: if you must stop, stop **with** the report written and the tree buildable.

### Acceptance criteria (parent)

- Build 0 errors; affected suites + ArchitectureTests green; full 10-project matrix run by the parent at freeze.
- Every binding test above exists and passes.
- `NotificationLog` rows are created only for policy-filtered recipients; the null-sender path never creates failures.
- No UI files touched (`git diff --name-only` contains no `.razor`/`.css`/`.js`/`wwwroot`/ApiClient path) — this keeps the round a legitimate light round.
- `documents/configuration.md` updated for every new parameter/section; CPM respected.

### Reviewer acceptance criteria (Tier 2, static)

- The five decisions (a)–(e) are implemented as stated; deviations flagged.
- Idempotency: retries re-send the persisted payload; no duplicate log row per (recipient, kind, attempt) for a single publish.
- Failure paths are typed and never swallow: a send failure is recorded with a reason and a retry time, and reaches a terminal state.
- Tenancy: the new entity is filtered by `TenantId`; the failure query cannot read across tenants.
- No `InvalidOperationException` from new code; no VERSION drift in CPM; no UI files; no cross-context project reference.
- Tests genuinely exercise the behaviour (not vacuous), including the null-sender branch and the retry cap.

## Execution log

| When | Event |
|---|---|
| 2026-09-17 | Plan authored (Tier 2 light, Option A scope). Branch `stack/16-ar-16-channel-delivery` @ `fa31b875`; PR #240. |
| 2026-09-17 | Worker pass 1 dispatched on `clinepass/cline-pass/deepseek-v4.1-flash`, run `9be569c1`. |
| 2026-09-17 | **Owner paused the run** mid-research (16 turns, 264k tokens, at the point of starting implementation). **Zero files changed** — verified clean tree, no report; nothing to reconcile. |
| 2026-09-17 | **Owner correction (Reading B): the round's provider PROFILE is `ollama-cloud`**, not clinepass — "switch orchestrator to ollama-cloud" meant the whole round's profile, not a per-role override. The paused clinepass run `9be569c1` is **stopped/discarded** (zero files changed). Light-mode set on this profile: worker `ollama-cloud/deepseek-v4.1-flash`, reviewer/orchestrator `ollama-cloud/glm-5.3-flash`. Worker pass 1 re-dispatched on `ollama-cloud/deepseek-v4.1-flash`. |

## Worker Report

_(parent persists the worker's report here)_

## Review

**Reviewer (Tier 2 static, `ollama-cloud/glm-5.3-flash`, run `3ed0ddaf`) — Verdict: P1, patch-capture only; reviewer's merge verdict: "OK once the P1 patch re-freeze is done — no code change required".**

Confirmed correct: decisions (a)–(e) all implemented; policy filtering is real (`PublishAssignmentCommandHandler.cs:56` applies `NotificationRecipientFilter.Apply` **before** broadcasting); retry re-send genuinely idempotent (tests assert the sender receives exactly the persisted `ToAddress`/`Subject`/`BodyHtml`); failure paths typed (`EmailDeliveryException`, rethrow-preserving) and terminal at the cap; tenancy matches the `ArchiveSweeper` precedent exactly (`IgnoreQueryFilters(["Tenant"])` candidate read + per-row `RunWithExplicitTenantAsync`, re-fetch tenant-filtered, wrong-tenant row ⇒ skipped); no `InvalidOperationException` in new code; no UI files; no new cross-context project reference; endpoint in the existing group; CPM respected in the tree; XML docs present; all 7 binding tests genuine — both provider-selection branches asserted pure *and* through DI.

| Finding | Disposition |
|---|---|
| **P1** — frozen patch missing `Directory.Packages.props` (CPM `NU1008` if applied standalone) | **FIXED (parent):** a patch-capture defect of the parent's freeze filter (the CPM file is root-level), not a code defect. Patch re-frozen → **42 files**, CPM change included. |
| **P2** — `MailKitEmailSender` TLS limited to STARTTLS/plaintext; `SslOnConnect` (port-465 implicit TLS) unreachable | **FIXED (parent, documentation route — the reviewer's offered smallest fix):** explicit TLS-limitation note in `configuration.md` §11. |
| **P2** — cancellation swallowed: resolver caught only `HttpRequestException`, and the broadcaster's broad catch converted cancellation into a terminal `Skipped` row (silent notification loss) | **FIXED (parent):** both now `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }`; a transport timeout (token not cancelled) still degrades to `Skipped`, non-aborting. New test `CancellationDuringAddressResolution_Propagates_AndPersistsNoRow` pins it. |
| **P2** — no uniqueness guard on `(TenantId, AssignmentId, RecipientId, Kind)` | **Residual → E3** (recorded): the single-publish guarantee holds only because the handler broadcasts once; add a partial unique index if a re-broadcast path appears. |

**Deviations 1–6 adjudicated acceptable** (`EmailProviderKind` + pure selection; existing `GET /contacts?...` with client-side id match rather than inventing a Students endpoint; unresolvable address ⇒ `Skipped`; MailKit 4.18.0; Core-vs-Api extension split; `smtp-user`/`smtp-password` deliberately uncommitted).

**Residual risk accepted (light round):** no live MailPit send — the transport path is a thin MailKit call over a fully-tested pure `BuildMimeMessage`; the first real send is exercised when the AppHost runs (flagged for manual/ar-17 verification). `StudentsContactAddressResolver`'s HTTP path is untested (failure mode = non-destructive `Skipped`). The new route is only compile-verified (its handler is unit-tested).

## Acceptance

**Parent verdict: ACCEPTED — round CLOSED (Tier 2 light round).**

| Gate | Result |
|---|---|
| Build | `dotnet build SchoolCollab.sln` — **0 errors** (6 pre-existing advisory warnings) |
| Authoritative matrix (post-fix) | **2,337 / 0** — Core 84, Assignments **623**, Assignments.Api 78, Families 42, Students 422, Students.Api 1, Settings 519, Settings.Api 1, Admin 546, Architecture 21 |
| Frozen artifact | `diffs-ar-16-channel-delivery.patch` — **42 files / +3,364**, drift-checked against the tree (identical) |
| Light-round UI guard | **clean** — no `.razor`/`.razor.css`/`.css`/`.js`/`wwwroot`/ApiClient path in the change set |
| Reviewer | P1 (patch-capture) + 3 P2 → all fixed/recorded → **re-verification PASS, no P1/P2/regressions** |

**Pipeline:** parent plan (Option A scope) → worker pass 1 on `ollama-cloud/deepseek-v4.1-flash` (run `42cda44c`; an earlier clinepass run `9be569c1` was paused and superseded by the owner's provider-profile switch — Reading B, zero files changed) → parent freeze + build + matrix → static reviewer `ollama-cloud/glm-5.3-flash` (run `3ed0ddaf`, P1 + 3 P2) → parent fixes (P1 patch re-freeze; two P2 code/doc fixes; one P2 recorded as an E3 residual) → re-verification (run `2ae6aaae`, **PASS**).

**Deviations accepted (6):** `EmailProviderKind` + pure `EmailSenderSelection`; contact id matched client-side against the existing `GET /contacts?ownerType=..&ownerId=..` (no Students endpoint invented); unresolvable address ⇒ `Skipped`; MailKit pinned 4.18.0 (4.14.0 → NU1902); Core-vs-Api DI extension split (`Add{Layer}()` rule); `smtp-user`/`smtp-password` deliberately not committed (secret posture).

**Residuals carried forward:** (1) no live MailPit/SMTP send yet — the transport path is a thin MailKit call over a fully-tested pure `BuildMimeMessage`, first real send to be exercised by running the AppHost (manual / ar-17 verification); (2) `StudentsContactAddressResolver`'s HTTP path untested (failure mode = non-destructive `Skipped`); (3) the new route is compile-verified only (its handler is unit-tested); (4) `NotificationLog` `(TenantId, AssignmentId, RecipientId, Kind)` unique-index guard → **E3**; (5) implicit-TLS `SslOnConnect`/port-465 unsupported (documented in `configuration.md` §11).

**Follow-on rounds:** **ar-17 (Tier 3)** = the Admin/author failure-surfacing UI over the delivered `GET /assignments/{id}/notification-failures`; **E3** = `Assignments.Worker` (reminders / overdue / completion trigger / archive sweep) consuming the unchanged `AssignmentPublishedIntegrationEvent` and carrying the uniqueness-guard residual.
