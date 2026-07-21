# Unified System Audit via a Base Model + Temporal Period — Discussion

> Status: **Discussion only.** No code changes. Goal is to align on whether (and how) to
> consolidate the repo's scattered audit/history entities behind a single base model that
> references a *temporal period* (timespan), drawing on EF Core temporal-table concepts and
> the repo's existing PostgreSQL "twist".

## 1. The question, restated

> "If we use temporal tables, can't we use a base model with a foreign reference to a
> temporal period (timespan) for logging all audits in the system?"

Two ideas are bundled here, and they should be untangled before deciding anything:

1. **System-versioned change history** (EF Core "temporal tables"): automatically archive
   every INSERT/UPDATE/DELETE of a row into a history table. Captures *what changed* and
   *when*, but **not** *who* or *why* unless you bolt on triggers.
2. **A unified audit base model keyed by a Period** (the academic term / business timespan):
   one shared shape for "who did what, to which entity, in which term, for what reason".

The repo today does **(2)** by hand, per feature, and does **not** do **(1)** at all. The
proposal is essentially: make **(2)** a first-class, generalized mechanism, and decide
whether **(1)** should come along.

## 2. EF Core temporal tables — what they actually are

- **SQL Server provider (first-class):** `entity.IsTemporal()` creates a history table with
  `ValidFrom`/`ValidTo` (`sysStartTime`/`sysEndTime`), and LINQ gains
  `TemporalAsOf` / `TemporalAll` / `TemporalBetween` / `TemporalFromTo` / `TemporalContainedIn`
  for point-in-time and range queries. Fully automatic; the DB maintains history on write.
- **PostgreSQL provider (the catch):** EF Core `IsTemporal()` is **SQL-Server-only**.
  - Native PG temporal support arrived only in **Npgsql EF Core v11 (PostgreSQL 18)** and
    covers **application-time** only: `WITHOUT OVERLAPS` on keys and `PERIOD` on foreign
    keys, built on `tstzrange` / `daterange` columns. **No system-versioning / automatic
    history table.**
  - System-versioning on PG is emulated via triggers or extensions
    (`temporal_tables`, `periods`) — a `sys_period tstzrange` column + a `versioning()`
    trigger that copies old rows to a `*_history` table.
  - Npgsql's own guidance (roji): PG's range types make it easy to build temporal behavior
    *application-side* (a validity `tstzrange` + range filters) without special EF support.

**Implication:** on this repo's PostgreSQL stack, "turn on EF temporal tables" is not a
one-liner. The closest first-class option needs **PostgreSQL 18 + Npgsql v11**, and still
does not give you *who/why* — which is the entire value of the current audit tables.

## 3. The repo's existing "PostgreSQL twist"

Grep for `temporal` across `src/**/*.cs` returns **nothing**. There is no EF temporal table
in use. What exists instead:

| Mechanism | Where | Notes |
|---|---|---|
| `ConfigurePostgresRowVersion()` | `SchoolCollab.Core/Data/EntityTypeConfigurationBase.cs` | Maps PG's `xmin` system column as a row version for optimistic concurrency — PG's replacement for SQL Server's `rowversion`. **This is the real PG-specific twist.** |
| Explicit append-only audit entities | `StudentTransferAuditEntry`, `FlagAuditEntry`, `GuardianNameHistory` | All `: ITenantEntity/IEntity, IAuditableEntity`. Never updated/deleted, so **no row version** is mapped on them. |
| `Period` domain entity | `Students.Core/Domain/Period.cs` | `StartDate`/`EndDate` (DateOnly), `Status`, `NextPeriodId`. An **academic term = a business timespan**. |
| `PeriodId` on the audit | `StudentTransferAuditEntry.PeriodId` | The audit **already references the Period** — but only as a scalar column; there is **no `HasOne<Period>()` relationship / FK constraint** today. |
| `IActorAccessor` | `Students.Core/Services/IActorAccessor.cs` | Supplies `ActorId` + `ActorDisplayName` (claims principal in API, system actor otherwise). This is the *who*. |
| `*Auditor` classes | `StudentTransferAuditor`, `FeatureFlagAuditor` | Take `IActorAccessor` + the `DbContext`, `Add(...)` the entry in the **same transaction** as the mutation. |
| `TenantEntityTypeConfigurationBase<T>` | `SchoolCollab.Core/Data/EntityTypeConfigurationBase.cs` | Strict tenant scoping + `ConfigureAuditProperties()` (CreatedAt/UpdatedAt). Append-only audits skip `ConfigurePostgresRowVersion()`. |

So the building blocks for "a base audit model with a Period FK + actor + reason" are
**already present** — the proposal mostly *generalizes* what `StudentTransferAuditEntry`
already does.

## 4. The proposal, made concrete

A single **`AuditEntry` base** (root of a hierarchy) carrying the shared audit shape, with
per-kind subtypes for the domain-specific columns:

```
AuditEntry (base)
  TenantId            // strict (ITenantEntity) or nullable/hybrid
  PeriodId  -> Period // the temporal period / timespan the event occurred in
  ActorId, ActorDisplayName   // from IActorAccessor
  OccurredAt, CreatedAt, UpdatedAt
  Kind / Discriminator
  Reason? / ChangeDescription?
  Payload? (JSON) or typed subtype columns

StudentTransferAuditEntry : AuditEntry   // FromGradeLevelId, ToGradeLevelId
FeatureFlagAuditEntry     : AuditEntry   // FeatureFlagId, ChangeKind, Previous/NewIsEnabled
...etc
```

Mapped via `TenantEntityTypeConfigurationBase<AuditEntry>` + `ConfigureAuditProperties()`
(no row version), reusing the existing `*Auditor` → `IActorAccessor` flow.

## 5. How it fits the existing infrastructure

- **Tenant scoping:** inherit `TenantEntityTypeConfigurationBase<AuditEntry>` exactly as
  `StudentTransferAuditEntryConfiguration` already does. Handles both strict-tenant (non-null
  `TenantId`) and hybrid (nullable `TenantId`, e.g. `FlagAuditEntry` today) via a
  `IHybridTenantEntity` variant that already exists in `EntityTypeConfigurationBase`.
- **Actor:** unchanged — every auditor passes `actorAccessor.ActorId/DisplayName` into
  `AuditEntry.Create(...)`.
- **Period link:** promote `PeriodId` from a bare column to a real `HasOne<Period>()`
  relationship + FK constraint, so "all audits in period X" is a navigable, indexed join.
- **Append-only guarantee:** keep the "never updated/deleted" rule (no row version), preserving
  the existing audit-integrity property.

## 6. Trade-offs

**A. Unified base vs. per-feature tables**
- ✅ One query answers "everything that happened this term" (`WHERE PeriodId = :p`).
- ✅ Consistent shape; far less boilerplate per new audit kind.
- ⚠️ Loses rich typed columns unless you use a `Payload` JSON column or wide nullable
  columns; per-kind relational FKs (e.g. `ToGradeLevelId → grade_levels`) become awkward.
- ⚠️ A single wide `audit_entries` table (TPH) vs. base + per-kind tables (TPT) is a real
  modeling decision (see open questions).

**B. Period FK (business timespan) vs. EF system-versioning (system timespan)**
- These are **complementary, not competing**:
  - *Period FK* = "which academic term was this audit logged in?" (business time).
  - *EF temporal / history table* = "what did this row's columns look like at time T?"
    (system time / point-in-time reconstruction).
- EF system-versioning **does not capture actor or reason**, so it cannot *replace* the audit
  tables — at best it *adds* column-level history underneath them.

**C. PostgreSQL reality**
- Enabling true EF temporal tables on PG means either the `temporal_tables` extension
  (trigger + `*_history` table, `sys_period tstzrange`) or PostgreSQL 18 + Npgsql v11
  application-time. Both are heavier and newer than the current explicit-audit approach, and
  neither supplies *who/why*.
- The repo's pragmatic PG "temporal" today is `xmin` row-version + explicit audits — that
  already works and is well understood.

**D. The "who/why" gap is the whole point**
- The current value is precisely `ActorId` + `Reason`. Any move toward EF temporal tables must
  preserve that; temporal tables alone would be a regression for auditability.

## 7. Recommended direction (for discussion)

1. **Short term — generalize the base model, not the DB engine.** Introduce `AuditEntry` as a
   TPH/TPT base with `TenantId`, `PeriodId → Period`, actor fields, `OccurredAt`, and a
   `Reason`/kind discriminator (+ optional JSON `Payload`). Migrate `StudentTransferAuditEntry`
   and `FlagAuditEntry` onto it. This directly delivers "log all audits in the system, keyed by
   the temporal period", and reuses `TenantEntityTypeConfigurationBase` + `IActorAccessor`.
2. **Keep EF Core temporal tables out of scope for now.** PG support is recent/limited and
   captures neither actor nor reason. Revisit only if there is a concrete need for
   point-in-time *state* reconstruction (not just *event* logging).
3. **Strengthen the Period link.** Add the real `HasOne<Period>()` + FK constraint (today
   `PeriodId` is an unconstrained scalar), and add a tenant+period index for the
   "audits in this term" query path.

## 8. Open questions

1. **Physical shape:** one wide `audit_entries` table (TPH) or a base table + per-kind tables
   (TPT)? TPH is simpler/cheaper to query; TPT keeps each kind's columns clean.
2. **Payload:** shared columns only in the base + a JSON `Payload` for kind-specific data, or
   fully typed subtypes with only `TenantId/PeriodId/actor/time/reason` in the base?
3. **Temporal scope:** is the "temporal period" the academic `Period` (business timespan), or
   do you also want *system-time* versioning of the domain rows themselves (point-in-time
   reconstruction)? The two serve different needs.
4. **Tenant scoping:** strict (`ITenantEntity`, non-null `TenantId`) for every audit, or hybrid
   (some system audits are global, as `FlagAuditEntry` currently uses nullable `TenantId`)?
5. **Naming/term:** to avoid confusion with EF "temporal tables", should we call this the
   *AuditEntry / Period-scoped audit log* rather than "temporal tables"?

## 9. References

- EF Core SQL Server temporal tables: <https://learn.microsoft.com/en-us/ef/core/providers/sql-server/temporal-tables>
- Npgsql temporal constraints (PG 18 / v11): <https://www.npgsql.org/efcore/misc/temporal-constraints.html>
- Npgsql 11.0 release notes: <https://www.npgsql.org/efcore/release-notes/11.0.html>
- `temporal_tables` extension: <https://github.com/arkhipov/temporal_tables>
- Repo: `Students.Core/Domain/Period.cs`, `.../StudentTransferAuditEntry.cs`,
  `SchoolCollab.Core/Data/EntityTypeConfigurationBase.cs`,
  `Students.Core/Services/IActorAccessor.cs`.
