# ADR: Cross-module synchronous calls on the write path

- **Status:** Accepted (architect sign-off approved 2026-08-23)
- **Date:** 2026-08-23
- **Supersedes:** none
- **Related:** `coded-values-architecture.md`, `coded-values-tenancy-impl.md`,
  `global-tenant-filter.md`, `docs/plans/2026-08-22-tenant-propagation-enroll-stream-investigation.md`

## Context

The School-Collab backend is split into bounded-context modules (Students,
Settings, Assignments) each owning its own database and exposing a REST API.
Modules communicate over HTTP via `IHttpClientFactory` typed/named clients,
with Aspire service discovery and two custom tenant `DelegatingHandler`s
(`TenantPropagationDelegatingHandler` on the admin shell,
`TenantForwardingDelegatingHandler` on API hosts).

Several **command handlers** issue synchronous cross-module HTTP calls *on the
write path* — i.e. inside a `HandleAsync` that is persisting a mutation:

| Caller → Callee | Site | Path | Data kind |
|---|---|---|---|
| Students → settings | `EnrollStudentHandler` grade materialize (`:57`) + stream validate (`:186`), `TransferStudentHandler` (`:86`), `CreateStudentWithLinkedDataHandler` (`:299`) | write | reference (coded values) |
| Students → assignments | `ActivityGroupAssignmentQueryHttpClient` (delete-guard, FR-6) | write | live consistency |
| Assignments → settings + students | `NotificationPolicyResolver` (from `PublishAssignmentCommandHandler`) | write | policy=config (reference) + recipients=live |
| Assignments → students | `StudentsContactResolver`, `ActivityGroupLookupHttpClient` | write/read | live consistency |
| AI server → settings | coded-values tools | read/tool | reference |
| Admin shell → students/settings/assignments | UI flows | **originator** | n/a — the shell *is* the client |

Every module already publishes integration events through the outbox
(`Settings`: `CodedValueCreated`/`CodedValueDisabled`; `Students`:
`StudentEnrolled`; `Assignments`: assignment lifecycle events). The
event-driven replication infrastructure is therefore already in place.

### Why this is a problem (evidence from the enroll investigation)

The enroll-to-stream flow (`EnrollStudentHandler.ValidateStreamAsync`) hops to
settings-api to resolve a coded value. This single hop produced, in one work
session, a chain of failures that consumed the whole investigation:

1. **Tenant not propagated API→API** — required a second custom
   `DelegatingHandler` and careful DI lifetime management.
2. **Handler-lifetime misuse** — `ObjectDisposedException` ("Cannot access a
   disposed object … NetworkStream"), twice, from stale pooled handler chains.
3. **Pipeline misrouting** — 404s from the *wrong host* (InnerHandler overwrite
   across named clients), indistinguishable from genuine misses.
4. **Service-discovery gaps** — "No such host is known" when an API resource
   never published an endpoint.
5. **Handler-rotation race** — the unhealable variant: `IHttpClientFactory`'s
   default 2-minute `HandlerLifetime` disposes a cached pipeline entry *under an
   in-flight request*, surfacing as the same NetworkStream ODE. This is the
   reported live failure and **retry-on-the-same-`HttpClient` cannot fix it**
   (the second attempt reuses the disposed entry). Standard resilience does not
   classify `ObjectDisposedException` as retryable, so the outer pipeline does
   not self-heal it either.

Beyond correctness, each sync hop adds: latency on a user-facing command,
distributed-failure coupling (a peer outage blocks a write whose own data is
local), and test scaffolding cost (integration tests must stub the remote
primary handler — rebuilt three times for the enroll flow alone).

## Decision

### Rule

**No new synchronous cross-module HTTP call on a command's write path for
reference data.** Reference data must be replicated via integration events and
read from a local projection. A new sync hop for reference data is only
acceptable after a replication/caching alternative has been explicitly
considered and rejected on record.

### Two hop classes, two treatments

1. **Reference-data hops → replicate, eliminate the hop.** Coded values,
   notification *policy config*, entity-code rules, feature flags. Changes
   rarely, staleness of seconds is harmless, the owning module already emits
   events. A local read model (or HybridCache seeded and invalidated by events)
   removes the sync call entirely. This covers: all four Students→settings
   coded-value hops, the Assignments→settings policy hops, AI→settings.

2. **Live-consistency hops → keep sync, but own the decision correctly.**
   "Is this student in an activity group?" (delete-guard), "who are this
   assignment's recipients?" These are real-time consistency questions where a
   stale replica would cause an *incorrect write* (allowing a delete that
   should block, notifying the wrong people). For these the hop is justified,
   but the preferred fix is often **relocating authority**: the guard becomes a
   query endpoint owned by the module that owns the data. Where the hop must
   remain, it is permitted only with:
   - an explicit **timeout** (separate from the default 100 s);
   - a **circuit breaker** so a downed peer fails fast;
   - `SetHandlerLifetime` set long enough that rotation cannot land mid-request
     (the root-cause mitigation for the NetworkStream rotation race);
   - `ObjectDisposedException` treated as retryable at the resilience layer
     (bounded retries, fresh attempt each); and
   - a **documented degradation policy** per hop (delete-guard defaults to
     **block**; recipient-resolution defaults to **skip + log**, never fail the
     parent write).

### Out of scope

- **Read/enrichment hops** (list hydration, optional display data) remain
  allowed and must degrade gracefully (null/blanks, never fail the parent
  operation).
- **Originator hops** (admin shell → APIs) are out of scope — that is the
  client; it has no local data to read.

### Review gate

Any PR adding a write-path sync hop carries the `adr-cross-module-calls` tag
and requires architect sign-off (the standing rule from acceptance of this
ADR). The PR description must name the data kind (reference vs live-consistency)
and, for reference data, record why replication was rejected.

## Consequences

- **Positive:** the reference-data write-path hops — the majority, and the
  source of every failure listed above — disappear. No HTTP, no tenant
  forwarding, no rotation race, no 404 ambiguity, no stub-primary-handler test
  scaffolding for those flows. The event bus already in use carries the
  replication.
- **Positive:** the remaining live-consistency hops become a small, named,
  hardened set rather than an implicit pattern; each has a defined degradation.
- **Negative / cost:** a local read model per consumer module (table +
  repository + projection consumer + backfill). Coded values are the first; the
  pattern is reusable. Operational: an event-propagation lag window (seconds)
  replaces strict synchrony — acceptable for reference data, explicitly
  excluded for live-consistency checks.
- **Negative:** one allowed backfill hop (out-of-band at startup) reads from
  the source of truth; it is the only sync reference-data hop permitted, and
  never on a user-facing write path.
- **Migration:** the first projection (Students coded values) ships behind a
  config flag `Students:UseLocalCodedValueProjection` (default off): backfill +
  consumer run with the flag off (table populating, no behavior change) →
  verify row counts and event lag → flip the flag → remove the old client and
  its `TenantForwardingDelegatingHandler` for that path in the next release.

## Implementation phases

The follow-ups below are sequenced. Phases 0–1 eliminate the reference-data
write-path hops; Phase 2 hardens the remaining live-consistency hops; Phase 3
decommissions the old path. The outbox-atomicity follow-up is a prerequisite for
flipping the projection flag live (see end of section).

### Phase 0 — Settings coded-value event-contract enrichment (DONE)

Branch `feat/settings-codedvalue-event-enrichment` (off `main`). The original
event set was too lean to drive a projection (`CodedValueCreated`/`Updated`
lacked `Attributes`/`ParentCode`/`IsDisabled`/`TenantId`; attribute mutations,
bulk create, delete, recover, provisional approval, and tenant overrides
published nothing). Phase 0 completes the contract so a local read model can
stay consistent without calling back to settings-api:

- `CodedValueCreated` / `CodedValueUpdated` enriched with `ParentCode`,
  `IsDisabled`, `Attributes` (as `CodedValueAttributeEvent`), and `TenantId`
  (null = shared blueprint, real = tenant-owned). New parameters are optional
  with defaults, so messages enqueued by a pre-enrichment producer still
  deserialize after rollout.
- New events: `CodedValueDeleted`, `CodedValueOverrideUpserted`,
  `CodedValueOverrideRemoved`, and the `CodedValueAttributeEvent` payload type.
- A shared `CodedValueEventMapper` (`Settings.Core/CQRS/CodedValues/`) is the
  single source of truth for the full-state payloads and parent-code
  resolution; every publisher routes through it.
- Nine handlers now publish full-state events: `Create`, `CreateProvisional`,
  `Update`, `UpsertOverride`, `RemoveOverride`, `SetCodedValueAttribute`,
  `RemoveCodedValueAttribute`, `BulkCreateCodedValues`, `DeleteCodedValue`,
  `RecoverCodedValue`, `ApproveProvisionalCodedValue`.

The contract implies these consumer rules (used by the Phase 1 projection):

| Event | Projection action |
|---|---|
| `Created` (TenantId=t) | upsert row under (t, Id) |
| `Updated` (TenantId=t) | upsert row under (t, Id) as **live** (clears any prior deleted marker); on approval (`TenantId=null`) also drop any stale tenant-owned row for that Id |
| `Disabled` / `Enabled` | toggle `IsDisabled` on the matching row(s) |
| `Deleted` | remove the row (and orphaned override rows for that Id) |
| `OverrideUpserted` (TenantId, GlobalId) | upsert a tenant overlay row; `null` fields keep the global value |
| `OverrideRemoved` (TenantId, GlobalId) | drop the tenant overlay row (fall back to global) |

Attribute-definition changes (`Set/RemoveCodedValueAttributeDefinition`) are
intentionally NOT emitted — the enroll projection reads attribute *values*, not
the schema.

### Phase 1 — Students local coded-value read model (next)

Eliminates the four Students→settings coded-value write-path hops and the
handler-rotation race for them. Ordered, each step a reviewable layer:

1. **Read model + migration.** Entity `LocalCodedValue` + EF config +
   migration `AddLocalCodedValues`. Table `students.local_coded_values`,
   two-tier hybrid-tenant: a nullable `TenantId` with unique `(TenantId, Id)`
   and indexes on `Code`. Columns: `Id`, `TenantId` (null = shared blueprint,
   real = tenant-owned/override), `Code`, `Name`, `Description`, `ParentId`,
   `ParentCode`, `IsDisabled`, `IsDeleted`, `DisplayOrder`, `Attributes`
   (jsonb), `CreatedAt`, `UpdatedAt`, `RowVersion`. Three row kinds share the
   table (disjoint by Id): global rows `(null, Id)`, tenant-owned rows
   `(t, tenantCv.Id)`, and tenant override rows `(t, globalCv.Id)`.
2. **Repository.** `ILocalCodedValueRepository.GetByIdAsync(id, ct)` resolves
   the effective value the way `GetCodedValueByIdHandler` does today: read the
   global row + the tenant row for `(currentTenant, id)`, merge (tenant overlay
   overrides `Name`/`Description`/`Code` where non-null; everything else from the
   global row; a tenant-owned row with no global row stands alone).
   `HybridCache` in front, key `coded-value:{tenant}:{id}`, tag `coded-values`.
3. **Worker consumer.** In `SchoolCollab.Students.Worker`, subscribe to the six
   Settings events and apply the Phase-0 consumer rules above; invalidate the
   `coded-values` cache tag on every change; idempotent via event-Id dedup.
4. **Backfill (the one permitted sync reference-data hop).** A one-time
   MigrationService/worker startup job pages `GET /api/coded-values` and
   hydrates global rows + known tenant overrides. Runs off the enroll path and
   only while the flag is off, so it never blocks a user-facing write.
5. **Flag-gated swap.** Config flag `Students:UseLocalCodedValueProjection`
   (default **off**). `EnrollStudentHandler`, `TransferStudentHandler`, and
   `CreateStudentWithLinkedDataHandler` inject `ILocalCodedValueRepository`
   alongside `ICodedValuesApiClient`; when the flag is on they read locally
   (same `GetByIdAsync` shape, so the diff is a strategy switch). Keep
   `ICodedValuesApiClient` registered while the flag exists (needed when off,
   and for backfill).
6. **Verify.** Unit tests swap stubs from `ICodedValuesApiClient` to
   `ILocalCodedValueRepository`; integration tests seed the local table
   directly and drop the `CapturingSettingsHandler` scaffolding for these
   flows; add a projection-correctness test (publish `CodedValueDisabled` →
   assert the local row is disabled + cache invalidated within N ms). Live:
   run with the flag off (table populating, no behavior change) → verify row
   counts and event lag → flip the flag → confirm enroll no longer hops and no
   `NetworkStream` failures recur.

### Phase 2 — Live-consistency hop hardening

`SetHandlerLifetime` (long), per-call timeout, circuit breaker,
resilience-layer `ObjectDisposedException` retry, and a documented
degradation for `ActivityGroupAssignmentQueryHttpClient` (delete-guard,
block-by-default) and `NotificationPolicyResolver` (recipient resolution,
skip-and-log-by-default).

### Phase 3 — Decommission

Remove `ICodedValuesApiClient` + `TenantForwardingDelegatingHandler` from the
students-api enroll path once the projection is live; flip the flag default on;
then remove the flag and the `ICodedValuesApiClient` path entirely.

### Follow-up — Outbox atomicity (discovered in Phase 0; prerequisite for go-live)

`OutboxIntegrationEventPublisher` uses `IDbContextFactory` to create a
**separate** `DbContext` and runs its own `SaveChangesAsync` — a **separate
transaction** from the entity mutation (`CodedValueRepository.AddAsync`/
`UpdateAsync` each call `SaveChangesAsync`). Its doc-comment claims a
"transactional outbox", but the implementation is not atomic: if the entity
save commits and the outbox save then fails, the mutation persists and the
event is **lost** → the projection goes stale → potential incorrect enroll
validation. This is pre-existing and repo-wide (all handlers enqueue after the
repository save), so Phase 0 makes nothing worse, but the projection's
eventual-consistency guarantee depends on reliable delivery. Fix before
flipping `Students:UseLocalCodedValueProjection` live: enqueue into the
request-scoped `DbContext` before the handler's `SaveChanges`, or share a
transaction.

## References

- `docs/plans/2026-08-22-tenant-propagation-enroll-stream-investigation.md`
  (root-cause analysis of the enroll mid-flight failures)
- `documents/solution/coded-values-architecture.md`
- `documents/solution/global-tenant-filter.md`
- PR #181 (handler lifetime + fault isolation), PR #182 (race-safe
  materialization + 404 observability + disposed-connection self-heal +
  end-to-end coverage + this ADR)
- Phase 0 branch `feat/settings-codedvalue-event-enrichment` (event-contract
  enrichment; PR to follow)