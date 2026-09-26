# Follow-ups: cross-context dependency rules (recorded during the subject-enrolment spec work)

- **When:** 2026-09-26, while writing `documents/specs/subject-enrollment-requirement-flag.md` and checking its `Assignments.Api` hop against the repo's cross-context rules.
- **Owner:** parent / orchestrator. **Deferred** — deliberately not actioned during the spec pass.
- **Status:** §1 ⬜ open (needs an architect decision before it can be scheduled) · §2 ⬜ open (mechanical, cheap) · §3 ⬜ open (recorded for completeness)

> This doc lives in `documents/solution/` as durable technical memory. Update each
> item's status here as it is completed or explicitly deferred, rather than
> trashing it. It was raised as a side observation while doing unrelated work, so
> nothing here has been changed in code.

## Context

`AGENTS.md` states, under *Architecture reminders*:

> No direct project references between bounded contexts — use MassTransit contracts.

Checking the actual solution graph while scoping the eligibility hop in
`subject-enrollment-requirement-flag.md` §5.3 surfaced a direct
`Assignments.Core → Students.Core` project reference. The rule is not merely
over-stated: round docs show it has been applied as a **real constraint** on
every bounded-context boundary built recently, and the de-facto rule is narrower
than the one-line summary in `AGENTS.md`. One existing edge does not satisfy it.

Nothing is broken today. This is architectural debt plus a documentation
ambiguity, recorded so it is not re-discovered from scratch.

---

## 1. `Assignments.Core` references `Students.Core` directly

**Type:** architecture-rule violation (pre-existing).
**Severity:** medium — no runtime impact, but it widens the coupling surface and
contradicts a rule every recent round enforced.

**Problem.** `AGENTS.md` forbids direct project references between bounded
contexts, and the round docs treat it as binding:

- `documents/rounds/round-ar-14-deep-links.md:94` — "no direct project references
  between bounded contexts (Families→Core/Settings.Core surface references mirror
  Admin — **allowed**; Families→Assignments.Core **would NOT be**)".
- `documents/rounds/round-ar-15-signoff-relocation.md:88` — "Families uses
  `Assignments.Contracts` + `SchoolCollab.Core` only (the ar-13 Option B boundary)".
- `documents/rounds/round-ar-16-channel-delivery.md:88` — "cross-context reads go
  through the existing HTTP-resolver-in-Api pattern".

So the operative rule is: **references to the shared kernel
(`SchoolCollab.Core`, `SchoolCollab.ServiceDefaults`) and to `*.Contracts` are
allowed; a reference to another context's `.Core` is not.** `Students.Core` is
the latter.

**Evidence.**

```
src/Assignments/SchoolCollab.Assignments.Core/SchoolCollab.Assignments.Core.csproj:11
  <ProjectReference Include="..\..\Students\SchoolCollab.Students.Core\SchoolCollab.Students.Core.csproj" />
```

Consumed at `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/PublishAssignmentCommand/PublishAssignmentCommandHandler.cs:10`:

```csharp
using SchoolCollab.Students.Core.Domain;
```

**Decision needed (architect, not implementable without one).** Which is it?

| Option | Action |
|---|---|
| **(A) Remediation** | Remove the reference. Move whatever `PublishAssignmentCommandHandler` needs out of `Students.Core.Domain` into `SchoolCollab.Core` or a shared DTOs/Contracts project, then delete the `ProjectReference` and the `using`. Non-trivial: it touches the publish path. |
| **(B) Accepted exception** | The reference is deliberate. Amend `AGENTS.md` to state the narrow rule explicitly (kernel + `*.Contracts` allowed, other `.Core` not) and record `Assignments.Core → Students.Core` as a known, reviewed exception with its rationale. |

(A) is the cleaner end state; (B) is the cheaper one and is at minimum
*honest*. What is not acceptable is the current state, where the rule and the
graph disagree and nothing records which is authoritative.

**Acceptance check.** Either `Students.Core` is absent from
`Assignments.Core.csproj` and the publish handler compiles without
`Students.Core.Domain`, **or** `AGENTS.md` names the exception and this doc
records the rationale with the reviewer who accepted it.

**Not to be bundled.** This is unrelated to the three subject-enrolment specs and
must not ride along with them; the flag spec deliberately returns `Guid[]` across
the boundary (FR-ER-14a) so it does not deepen the coupling.

---

## 2. No mechanical guard for the "no cross-context reference" rule

**Type:** missing enforcement.
**Severity:** low — it is why §1 went unnoticed.

**Problem.** `CrossModuleWiringTests`
(`tests/SchoolCollab.Core.Tests.Unit/Architecture`) enforces the **AppHost**
half of the cross-context rules: it scans every cross-module base address in
`src/**/*.cs` against the AppHost `.WithReference(...)` wiring, and CI runs a
dedicated **Cross-module wiring guard** job. It already carries a
`BuildProjectReferenceGraph` helper (`CrossModuleWiringTests.cs:281`) for
library attribution — so the csproj graph is already being parsed.

Nothing enforces the **project-reference** half. `SchoolCollab.ArchitectureTests.Unit`
contains `AppHostSettingsDbWiringArchitectureTests` and `MigrationGuardTests` but
no bounded-context reference test.

**Decision needed.** Extend `CrossModuleWiringTests` (or add a sibling in
`SchoolCollab.ArchitectureTests.Unit`) with a rule: a project under
`src/{Context}/` may reference `SchoolCollab.Core`, `SchoolCollab.ServiceDefaults`,
its own `*.Contracts`, and projects in its own context — and nothing else. Seed
it with §1's edge as an explicit allow-list entry so the guard goes green today
and the exception becomes visible rather than implicit.

**Acceptance check.** The test fails on `Assignments.Core → Students.Core`
unless allow-listed, and the allow-list names the rationale.

---

## 3. "use MassTransit contracts" is a misleading summary of the real rule

**Type:** documentation accuracy.
**Severity:** low — costs an agent a wrong reading, which is exactly what
happened here.

**Problem.** The `AGENTS.md` one-liner bundles two different things. MassTransit
is the mechanism for **cross-context events** (via the transactional outbox —
`dotnet-best-practices.md:28`, "never publish to a bus from a handler"). It is
**not** the sanctioned mechanism for a synchronous cross-context *read*: the
established pattern there is a port interface in the calling context's `.Core`
with the HTTP client in its `.Api`, over Aspire service discovery
(`cross-module-http-client-pattern.md`, `adr-cross-module-calls.md`,
`round-ar-16-channel-delivery.md:88`). `ITopicAssignmentLookup` →
`TopicAssignmentLookupHttpClient` and `IContactResolver` →
`StudentsContactResolver` are both instances.

An agent that takes the one-liner literally will look for a MassTransit contract
where an HTTP port belongs. This spec was corrected only because the ADR was
read late.

**Decision needed.** Split the `AGENTS.md` bullet into the two rules with the
governing docs named, e.g.:

- Cross-context **events** → transactional outbox + MassTransit contracts.
  Never publish to a bus from a handler.
- Cross-context **synchronous reads** → port interface in the calling context's
  `.Core`, HTTP client in its `.Api`, named client via
  `AddCrossModuleHttpClient`, and an AppHost `.WithReference` (enforced by
  `CrossModuleWiringTests`). A new **reference-data** hop on a command write path
  additionally requires the replication alternative to be rejected on record
  (`adr-cross-module-calls.md`).

**Acceptance check.** A reader can answer "MassTransit or HTTP port?" correctly
from `AGENTS.md` alone, without opening `documents/solution/`.

---

## 4. Also found while verifying §5.3 (informational, already handled)

`subject-enrollment-requirement-flag.md` originally implied a new
`ITopicEligibilityLookup` port. That was wrong under the repo pattern and is now
specified as an implementation detail of the existing `IContactResolver`
(FR-ER-13a), with the endpoint returning `Guid[]` so no Students type crosses
the boundary (FR-ER-14a). Recorded here only because it is the second instance
of the §3 misreading; the spec itself is correct.
