# Spec — Minimal-API typed responses (CodedValue pilot)

| | |
|---|---|
| Status | **Implemented (round 5, 2026-10-17) — CodedValue pilot converted; measured results in §1.3 and §6** |
| Date | 2026-10-17 |
| Parent spec | `documents/specs/minimal-api-composition.md` (decisions D1–D7, implemented; PR #363) |
| Upstream source | `dotnet/skills` → `dotnet-aspnetcore/skills/dotnet-webapi/SKILL.md` (MIT), § "Minimal API return types — prefer `TypedResults`" |
| Governance docs touched | `documents/solution/endpoint-organization-pattern.md`, `.github/skills/minimal-api-composition/SKILL.md` |
| Execution mode | **open** — to be chosen before implementation |

Decision numbering continues the parent spec (D8–D15) so the two docs cannot collide.

---

## 1. Findings

### 1.1 The pilot target, measured

`CodedValueEndpoints` **already satisfies** the skill's composition guidance (`SKILL.md:188-199`): one static class
per resource (`CodedValueEndpoints.cs:7`), a `Map<Resource>(this WebApplication app, …)` extension (`:20`), no
inline routes (enforced by `MinimalApiCompositionArchitectureTests`), registered at `Program.cs:63` — plus the
repo's second layer, where the group's policy is applied once (`group.RequireAuthIfOidc(featureFlags)`, `:24`)
for all 24 routes.

What it does **not** adopt is the skill's typing guidance:

| Measure | CodedValue suite |
|---|---|
| Endpoints | **24** (20 in `CodedValueRoutes.cs` + 2 + 2 in the attribute files) |
| `TypedResults` | **0** |
| `Results.*` call sites | **55** (43 + 6 + 6) |
| `.Produces<` / `.WithName(` / `.WithTags(` | **0 / 0 / 0** |
| Largest file | `CodedValueRoutes.cs` — **353 lines, 20 endpoints, 4 resources** |
| Response-schema pin (repo) | **0** of 66 Settings operations, 0 of 146 Students (pinned by `OpenApiSurfaceArchitectureTests`) |

`CodedValueRoutes.cs` is one *class* for one *aggregate* but four *resources*: CRUD/lifecycle, bulk, override,
and the provisional-approval workflow. That is where the skill's stated goal — maintainability as the API grows —
is unmet.

### 1.2 The grouping the Core already implies

`SchoolCollab.Settings.Core/CQRS/CodedValues` holds **23 specialties**: 16 commands
(`CreateCodedValue`, `BulkCreateCodedValues`, `UpdateCodedValue`, `DeleteCodedValue`, `RecoverCodedValue`,
`DisableCodedValue`, `EnableCodedValue`, `CreateProvisionalCodedValue`, `ApproveProvisionalCodedValue`,
`RejectProvisionalCodedValue`, `UpsertCodedValueOverride`, `RemoveCodedValueOverride`,
`SetCodedValueAttribute`, `RemoveCodedValueAttribute`, `SetCodedValueAttributeDefinition`,
`RemoveCodedValueAttributeDefinition`) and 7 queries (`SearchCodedValues`, `ListRootCodedValues`,
`GetCodedValueById`, `GetCodedValueByCode`, `GetCodedValuesByIds`, `GetCodedValuesByParent`,
`ListProvisionalCodedValues`).

Mirroring 23 specialties as 23 route files is over-fragmentation. The four URI clusters the routes already form
are the natural cut, and each cluster fronts a coherent slice of those specialties.

### 1.3 Exact per-endpoint outcome sets (measured from the source)

These are the unions the conversion must reproduce **exactly** — no status code added or removed.

| Endpoint | Current outcomes |
|---|---|
| `GET /search` | Ok |
| `GET /` | Ok |
| `GET /{id:guid}` | Ok, NotFound |
| `GET /by-code/{code}` | Ok, NotFound |
| `GET /by-ids` | Ok |
| `GET /by-parent` | Ok |
| `DELETE /{id:guid}` | NoContent, Conflict, NotFound |
| `POST /{id:guid}/recover` | NoContent, NotFound |
| `GET /deleted` | Ok |
| `POST /` | Created, Conflict |
| `POST /bulk` | Ok, Conflict, NotFound |
| `PUT /{id:guid}` | NoContent, Conflict, NotFound |
| `POST /{id:guid}/disable` | NoContent, NotFound |
| `POST /{id:guid}/enable` | NoContent, NotFound |
| `PUT /{id:guid}/override` | Ok, BadRequest, NotFound |
| `DELETE /{id:guid}/override` | NoContent |
| `POST /provisional` | Created, BadRequest, Conflict |
| `GET /provisional` | Ok |
| `POST /provisional/{id:guid}/approve` | NoContent, BadRequest, Conflict, NotFound |
| `POST /provisional/{id:guid}/reject` | NoContent, BadRequest, NotFound |
| `PUT /{id:guid}/attributes/{key}` | NoContent, Conflict, NotFound |
| `DELETE /{id:guid}/attributes/{key}` | NoContent, Conflict, NotFound |
| `PUT /{id:guid}/attribute-definitions/{key}` | NoContent, Conflict, NotFound |
| `DELETE /{id:guid}/attribute-definitions/{key}` | NoContent, Conflict, NotFound |

**Measured during the round (corrects an earlier draft of this section).** A **409/400 arm that carries a payload also
documents `content`** — the first draft assumed 4xx arms were bodyless and predicted a pin of 12. The true split of the
24 converted operations is:

| Body documented | Operations | Which |
|---|---|---|
| 200/201 body | **12** | the 10 `Ok` + 2 `Created` operations |
| 400/409 message body only | **7** | `DELETE /{id}`, `PUT /{id}`, `POST /provisional/{id}/reject`, and the 4 attribute routes |
| no body at all | **5** | `recover`, `disable`, `enable`, `DELETE /{id}/override`, and the 4-arm `approve` kept on `IResult` |

12 + 7 = **19** operations document a schema. The pin counts *operations*, not arms.

---

## 2. Decisions (round 5 — settled)

| # | Decision | Rejected alternatives |
|---|---|---|
| **D8** | Scope = the **CodedValue pilot only** (6 route files, 24 endpoints). The other 8 Settings orchestrators and the Students/Assignments hosts keep their current shape. | All-Settings (66 ops) and all-hosts (212 ops): repeating an unproven pattern 188 more times, and a pin bump too large to audit. |
| **D9** | Split `CodedValueRoutes.cs` **4-way by URI cluster** (query / command / override / provisional); the two attribute files stay as they are. | 2-way CQRS split: it cuts the provisional workflow in half (list is a Query, approve/reject are Commands) for no reader benefit. No split: leaves a 353-line, 4-resource file. |
| **D10** | **Spike two endpoints first** — `GET /{id:guid}` and `POST /` — measure the generated document, then apply the winning pattern to the remaining 22. | Converting all 24 blind; or going straight to `TypedResults` + `.Produces<T>()` (which adds 24 lines of metadata that may duplicate what the compiler already emits). |
| **D11** | Explicit union return types (`Task<Results<Ok<T>, NotFound>>`); fall back to `IResult` only where a handler has **>3 arms**. | `IResult` everywhere (no schemas); adding `ProblemDetails` to unions (would import the error wire shape D3 deferred). |
| **D12** | The schema pin moves to the **exact measured count** for the Settings host in the converting PR. | A floor (`>= N`): absorbs regressions silently. |
| **D13** | **Fences**: the `Assignments.Api` OpenAPI generation defect and the `AssignmentRoutes.cs` (51-endpoint) split are **out of scope**. | Folding either in: one is a wire-shape decision (human-gated), the other would swamp a 24-endpoint pilot. |
| **D14** | **Behaviour proof**: capture the generated OpenAPI document before and after the conversion and commit the delta in the round doc — `paths`/verbs identical, `responses` gained `content` for exactly the 12 body-bearing operations. | Relying on the suites alone: they prove handler behaviour, not contract shape. |
| **D15** | The spec lives here, cross-linked to the closed parent spec. | A "Phase 2" section in the parent: a reader could no longer tell settled from pending. |

---

## 3. Work packages

### WP1 — Spike (D10)

Convert exactly two endpoints in place (no file split yet):

```csharp
// GET /{id:guid} — spike shape; the .Produces line is added only if Outcome B measures as necessary
group.MapGet("/{id:guid}", async (
    Guid id,
    [FromServices] IQueryHandler<GetCodedValueById, CodedValueDto?> handler,
    CancellationToken ct) =>
{
    var result = await handler.HandleAsync(new GetCodedValueById(id), ct);
    return result is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(result);
})
.Produces<CodedValueDto>(StatusCodes.Status200OK);   // Outcome B only — never both at once
```

Note the shape of the fallback: `.Produces<TResponse>(int statusCode)` takes **one** response type — never
`.Produces<T, Results<…>>()`. A 404 arm documents via `TypedResults.NotFound()` alone (no body) or
`.ProducesProblem(StatusCodes.Status404NotFound)` if a body were ever wanted (it is not: D3 defers `ProblemDetails`).

Then `GET /openapi/v1.json` from the in-process host (the `OpenApiSurfaceArchitectureTests` harness) and read the
two operations' `responses`. Record in the round doc:

- **Outcome A** — both operations already carry a `content` schema: apply `TypedResults` alone, **no `.Produces`**.
- **Outcome B** — no schema appears: add `.Produces<TResponse>(StatusCodes.Status200OK)` (or `Status201Created`) to every converted route and re-measure.

The spike's result is the pattern for WP3 and must be quoted in the round doc, not inferred.

### WP2 — Split (D9)

| New file | Endpoints moved | Expected size |
|---|---|---|
| `Endpoints/CodedValueQueryRoutes.cs` | `GET /search`, `/`, `/{id}`, `/by-code/{code}`, `/by-ids`, `/by-parent`, `/deleted` | ~130 lines |
| `Endpoints/CodedValueCommandRoutes.cs` | `POST /`, `/bulk`, `PUT /{id}`, `DELETE /{id}`, `/{id}/recover`, `/{id}/disable`, `/{id}/enable` | ~140 lines |
| `Endpoints/CodedValueOverrideRoutes.cs` | `PUT /{id}/override`, `DELETE /{id}/override` | ~60 lines |
| `Endpoints/CodedValueProvisionalRoutes.cs` | `POST /provisional`, `GET /provisional`, `POST /provisional/{id}/approve`, `/reject` | ~90 lines |
| *unchanged* | `CodedValueAttributeRoutes.cs`, `CodedValueAttributeDefinitionRoutes.cs` | — |
| *deleted* | `CodedValueRoutes.cs` | — |

`CodedValueEndpoints.cs` changes only in its chain (3 → 6 calls). Each new file is
`public static class <Name>Routes` with `public static RouteGroupBuilder Map<Name>Routes(this RouteGroupBuilder group)`,
relative paths only, `return group;`, and the request records that belong to its routes (`UpdateCodedValueRequest`,
`UpsertOverrideRequest`, `BulkCreate*` in Command/Override; `AttributeValueRequest`, `AttributeDefinitionRequest`
stay in their attribute files).

### WP3 — Convert (D11)

Rules, applied mechanically:

1. `Results.X(...)` → `TypedResults.X(...)`; **the argument list is preserved verbatim** — including
   `TypedResults.Created($"/api/coded-values/{id}", id)` and
   `TypedResults.Created($"/api/coded-values/provisional/{id}", id)` (the `Location` header must not change).
2. Annotate the lambda's return type with the union from §1.3, in the order Ok/Created first, then 4xx.
3. `BadRequest` arms: if the current call passes a payload, use `TypedResults.BadRequest(payload)` and type the
   arm as `BadRequest<TPayload>`; if it passes none, `BadRequest`.
4. **`POST /provisional/{id}/approve` has 4 arms** → it keeps `IResult` (D11). It documents no schema, by design.
5. A handler with exactly one arm (e.g. `GET /search` → `Ok<CodedValueDto[]>`) needs no union — the bare typed
   result is the return type.

### WP4 — Pin bump (D12)

`tests/SchoolCollab.ArchitectureTests.Unit/OpenApiSurfaceArchitectureTests.cs`: the Settings host's
`SchemaPin` moves from `0` to the **measured** value — **19** as implemented (see §1.3's corrected split; the
original "expected 12" here was wrong because it counted 4xx arms as bodyless). The assertion message keeps the
"raise deliberately" clause and states the arithmetically checkable reason: *"24 CodedValue operations converted —
12 document a 200/201 body, 7 a 400/409 message body only, and 5 return no body at all (recover, disable, enable,
DELETE override, and the 4-arm approve kept on IResult) — so 12 + 7 = 19."* Students stays `0` of 146.

### WP5 — Behaviour proof (D14)

In the round doc: the two `GET /openapi/v1.json` documents' diff summary — identical `paths` and verbs; the exact
list of operations that gained a `responses.*.content`; zero operations that lost a response or changed a status
code.

### WP6 — Docs

- `.github/skills/minimal-api-composition/SKILL.md` §3: replace the "return `TypedResults`" advice with the
  **proven** pattern from WP1 (including the >3-arm fallback and the `Location`-preservation rule), and record
  the measured pin ("Settings 66 → 12 documented operations after the CodedValue pilot").
- `documents/solution/endpoint-organization-pattern.md`: add a short "Typed responses" subsection to §4 with the
  union rule, the 3-arm limit and a pointer to this spec.
- No `AGENTS.md` change — its endpoint bullet already requires `TypedResults` for new and edited endpoints.

---

## 4. Verification matrix

| Check | Command | Must be |
|---|---|---|
| Build | `dotnet build SchoolCollab.slnx` | 0 errors |
| Architecture | `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | 0 failures, with Settings pin = measured value |
| Settings API | `dotnet test tests/SchoolCollab.Settings.Api.Tests.Unit` | 0 failures |
| Settings unit | `dotnet test tests/SchoolCollab.Settings.Tests.Unit` | 0 failures |
| Contract | OpenAPI document delta (WP5) | paths/verbs identical; only additive `content` |
| Pin non-vacuity | temporarily revert one converted handler to `Results.Ok` → pin assertion fails; restore | fails, then green |
| Route parity | route count for the CodedValue suite still 24; 6 route files instead of 3 | 24 / 6 |

---

## 5. Risks

| Risk | Mitigation |
|---|---|
| `TypedResults` alone emits no schema (the .NET OpenAPI plumbing may need `.Produces`) | WP1 spike measures it before 22 endpoints depend on the answer (D10). |
| A status code or `Location` header changes silently | §1.3's outcome table is the checklist; the document delta proves statuses; tests assert `Location`. |
| Union annotation churn makes the diff unreadable | Split lands first (WP2) so each converted file is 60–140 lines; review one file per commit group. |
| The 4-arm `approve` handler tempts a `ProblemDetails` union | D11/D3: keep `IResult`; do not introduce a body shape that D3 deferred. |
| Pin arithmetic wrong (12 vs 13) | The pin is set to the **measured** number from WP5, never to a predicted one. |

---

## 6. Acceptance checklist

- [x] `TypedResults` + an explicit union return type **alone** emits response schemas — measured, Outcome A; **no `.Produces<T>` was needed anywhere**.
- [x] `CodedValueRoutes.cs` deleted; 4 cluster files added (96 / 167 / 60 / 104 lines).
- [x] Orchestrator chains 6 `Map*Routes()`; one `RequireAuthIfOidc`; zero inline routes.
- [x] 55/55 `Results.*` → `TypedResults.*`, arguments verbatim; both `Created` `Location` strings unchanged.
- [x] `approve` is the only `IResult` handler (4 arms, D11).
- [x] Anonymous payloads replaced by `IdResponse` / `MessageResponse` / `BulkCreateResponse` (internal sealed records), with 5 wire-shape tests asserting exact body text.
- [x] Settings schema pin = **19** (measured); Students 0; non-vacuity proven by reverting one handler (18 ≠ 19).
- [x] OpenAPI delta: 66 → 66 operations, paths/verbs unchanged, 0 → 19 with `content`, 0 lost, 47 non-CodedValue operations byte-identical.
- [x] `dotnet build SchoolCollab.slnx` 0 errors; Architecture 126, Settings.Api 29, Settings.Tests.Unit 592 — all green.
- [x] Spec corrected (§1.3, §WP4) after the measurement contradicted the draft; both errors recorded in the round doc.

---

## 7. Out of scope (recorded)

| Item | Why |
|---|---|
| The other 8 Settings orchestrators (42 operations) | D8 — the pilot proves the pattern first. |
| Students (146) and Assignments (?) hosts | Same. Assignments cannot generate a document at all (parent spec §7.1). |
| `Assignments.Api` OpenAPI defect | Wire-shape fix, human-gated; pinned by a test that reddens when fixed (D13). |
| `AssignmentRoutes.cs` split (51 endpoints) | Parent spec D5; would swamp the pilot's diff (D13). |
| `.http` artifacts / `ProblemDetails` validation | Parent spec D6 / §7.1 — declined / deferred. |
