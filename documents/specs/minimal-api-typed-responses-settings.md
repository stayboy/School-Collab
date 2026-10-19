# Spec — Finishing Settings: typed responses for the remaining 42 routes

| | |
|---|---|
| Status | **Approved (round 8, owner: "all as recommended") — not implemented** |
| Date | 2026-10-18 |
| Parent spec | `documents/specs/minimal-api-typed-responses.md` (D8–D15, implemented in PR #364 — CodedValue pilot) |
| Upstream source | `dotnet/skills` → `dotnet-aspnetcore/skills/dotnet-webapi/SKILL.md`, § "prefer `TypedResults`" |
| Governance docs touched | `.github/skills/minimal-api-composition/SKILL.md` §3, `documents/solution/endpoint-organization-pattern.md` §4 |
| Execution mode | **Tier 2 (light)** with the owner override recorded on round-doc line 1 (Q41) |

Decision numbering continues the parent spec (D16–D23).

---

## 1. Findings

### 1.1 The remaining surface, measured (2026-10-18)

Settings has **66 operations**; the CodedValue pilot converted **24** and pinned **19** of them. The remaining
**42 routes live in 11 route files**:

| Route file | Routes | Body-bearing | `Results.*` | Anonymous payloads |
|---|---|---|---|---|
| `EntityCodeRuleRoutes` | 10 | 6 | 24 | 9 |
| `ConfigFlagRoutes` | 9 | 3 | 19 | 4 |
| `NotificationTemplateRoutes` | 5 | 4 | 8 | 2 |
| `TenantRoutes` | 4 | 2 | 8 | 1 |
| `ConfigTenantFlagOverrideRoutes` | 3 | 2 | 7 | 1 |
| `AssignmentAiPromptRoutes` | 2 | 2 | 4 | 1 |
| `AssignmentPolicyRoutes` | 2 | 2 | 4 | 1 |
| `NotificationPolicyRoutes` | 2 | 2 | 4 | 1 |
| `SignatureConsentTextRoutes` | 2 | 2 | 4 | 1 |
| `ConfigResolveRoutes` | 2 | 2 | 2 | 0 |
| `ConfigAuditRoutes` | 1 | 1 | 1 | 0 |
| **Total** | **42** | **28** | **85** | **21** |

### 1.2 Pin after the round: **50 of 66** (measured; the earlier prediction of 52 was wrong)

The first draft of this section counted all five four-xx-only operations as contributing `+5`. **Two of them cannot
contribute**: `EntityCodeRule` `PUT /{id}` and `Tenant` `PUT /me/default` are the two 4-arm handlers that keep
`Task<IResult>` (D19), and a handler declared `IResult` loses its arm types in the generated document — exactly as
the pilot's `approve` did. Measured after conversion: **50**, not 52.

| Bucket | Count | Running total |
|---|---|---|
| CodedValue pilot (already pinned) | 19 | 19 |
| Remaining operations with a 200/201 body | +28 | 47 |
| Remaining operations whose **only** body is a 4xx payload, and which convert | +3 | **50** |
| Remaining 4xx-only operations that cannot contribute (the two `IResult` 4-arm handlers) | 0 of 2 | 50 |
| Remaining operations with no body at all | 9 (never contribute) | 52 |

The five 4xx-only operations — the same trap that broke the parent round's arithmetic: `EntityCodeRule` `PUT /{id}`,
`EntityCodeRule` `PUT /{id}/overrides`, `ConfigFlag` `PUT /flags/{key}`, `NotificationTemplate`
`DELETE /notification-templates/{kind}/{channel}`, `Tenant` `PUT /me/default`.

The nine body-less operations: `EntityCodeRule` `DELETE /{id}` + `POST /{id}/activate`; `ConfigFlag`
`PUT /flags/{key}/enabled` + `POST archive` + `POST unarchive` + `DELETE` + `POST recover`; `Tenant`
`PUT /{id}/default`; `ConfigTenantFlagOverride` `DELETE`.

### 1.3 Per-route outcome sets (the exact conversion checklist)

**`EntityCodeRuleRoutes`** — `GET /` {Ok} · `GET /{id}` {Ok, NotFound} · `GET /by-code/{code}` {Ok, NotFound} ·
`POST /` {Created, BadRequest, Conflict} · `PUT /{id}` {NoContent, BadRequest, Conflict, NotFound} ·
`DELETE /{id}` {NoContent, NotFound} · `POST /{id}/activate` {NoContent, NotFound} · `GET /{id}/overrides`
{Ok, NotFound} · `PUT /{id}/overrides` {NoContent, BadRequest, NotFound} · `POST /generate` {Ok, NotFound}.

**`ConfigFlagRoutes`** — `POST /flags` {Created, BadRequest, Conflict} · `GET /flags` {Ok} · `GET /flags/{key}`
{Ok, NotFound} · `PUT /flags/{key}` {NoContent, BadRequest, NotFound} · `PUT /flags/{key}/enabled`
{NoContent, NotFound} · `POST /flags/{key}/archive` {NoContent, NotFound} · `POST /flags/{key}/unarchive`
{NoContent, NotFound} · `DELETE /flags/{key}` {NoContent, NotFound} · `POST /flags/{key}/recover`
{NoContent, NotFound}.

**`NotificationTemplateRoutes`** — `GET /notification-templates` {Ok} · `GET /notification-templates/{kind}/{channel}`
{Ok, NoContent} · `PUT …` {Ok, BadRequest} · `DELETE …` {NoContent, BadRequest} · `GET …/audit` {Ok}.

**`TenantRoutes`** — `GET /` {Ok} · `PUT /{id}/default` {NoContent, NotFound} · `GET /me/default` {Ok} ·
`PUT /me/default` {NoContent, BadRequest, NotFound, Unauthorized}.

**`ConfigTenantFlagOverrideRoutes`** — `GET /flags/{key}/overrides` {Ok, NotFound} · `PUT /flags/{key}/overrides/{tenantId}`
{Ok, BadRequest, NotFound} · `DELETE …` {NoContent, NotFound}.

**`AssignmentAiPromptRoutes`** — `GET /assignment-ai-prompt` {Ok, NoContent} · `PUT /assignment-ai-prompt`
{Ok, `Results.Json` 400}.

**`AssignmentPolicyRoutes`** — `GET /assignment-policy` {Ok, NoContent} · `PUT /assignment-policy` {Ok, BadRequest}.

**`NotificationPolicyRoutes`** — `GET /notification-policy` {Ok, NoContent} · `PUT /notification-policy` {Ok, BadRequest}.

**`SignatureConsentTextRoutes`** — `GET /signature-consent-text` {Ok, NoContent} · `PUT /signature-consent-text`
{Ok, `Results.Json` 400}.

**`ConfigResolveRoutes`** — `GET /api/features/global` {Ok} · `GET /api/features/{tenantId}` {Ok} (route-level
`oidcEnabled` gate, `WebApplication` receiver — not a `RouteGroupBuilder` specialty).

**`ConfigAuditRoutes`** — `GET /audit` {Ok}.

### 1.4 Three shapes the pilot did not exercise

1. **`Results.Json` with an explicit status and a distinct envelope** — both sites are
   `Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status400BadRequest)`. Note `error`, not
   `message`: naming them needs a **new** `ErrorResponse(string Error)` record, and `TypedResults.Json` must keep
   the `statusCode:` argument (the JSON options are the defaults at both sites — verify, do not assume).
2. **Two 4-arm handlers** — `EntityCodeRule` `PUT /{id}` and `Tenant` `PUT /me/default` → `IResult` fallback (D11).
3. **One route-level-gated, `WebApplication`-receiver file** — `ConfigResolveRoutes` is exempt from the
   `RouteGroupBuilder` specialty shape (parent spec §WP3/§WP5) but still an endpoint surface to type.
4. **`TypedResults.Json(…, statusCode:)` does not document its response** — measured during the round:
   `JsonHttpResult<ErrorResponse>` contributes no response metadata, so a `Json` 400 arm would leave the operation
   documenting only its 200. `.Produces` is forbidden this round (D20), so the two `Json` sites instead use
   **`TypedResults.BadRequest(new ErrorResponse(ex.Message))`**: wire-identical (400, `application/json`, same
   `{"error":…}` body, same app `JsonOptions` — proven by the two exact-body payload tests) and it *does* document
   the 400. D20's letter said "convert to `TypedResults.Json`"; the measured gap is why this round did not.

---

## 2. Decisions (round 8)

| # | Decision | Rejected alternatives |
|---|---|---|
| **D16** | **One round, all 11 files / 42 routes.** | Two batches: would produce a partial pin twice and run the same review twice. |
| **D17** | **One shared `Settings.Api/Endpoints/ApiResponsePayloads.cs`** holding the cross-cutting shapes — `MessageResponse(string Message)` (moved out of `CodedValueResponsePayloads.cs`) and the new `ErrorResponse(string Error)` — with aggregate-specific composites named in the file that uses them. | Nine copies of `MessageResponse`; one payloads file per aggregate (eleven files for two shared shapes). |
| **D18** | **Single host pin, raised to the measured value** — **50** as implemented, never per-file pins. | Eleven constants that drift independently. |
| **D19** | The two **4-arm handlers** keep `Task<IResult>` with a comment naming the arms. | `Results<…>` with 4 arms (unreadable); trimming an arm (a wire change). |
| **D20** | **`Results.Json` → `TypedResults.Json`**, options preserved verbatim (defaults at both sites — confirmed by reading, not assumed), `statusCode:` kept. | Leaving two untyped holes in an otherwise complete host. |
| **D21** | **One new payload test file** (`SettingsResponsePayloadTests.cs`, ~8 assertions) covering the newly named records and the two `Json` sites — **not** a blanket route suite for nine aggregates. | Nine aggregate route suites (large, and the pin already covers status sets at document level). |
| **D22** | **Tier 2 light** with the owner override recorded (pattern and its two traps are already measured and reviewed). | Tier 3 lean: a second plan review over a repeated template. |
| **D23** | **Fences:** `Assignments.Api`'s OpenAPI defect and Students (146 operations) stay out. | Blurring the first complete per-host pin with a wire-shape DTO fix or a 3× larger host. |

---

## 3. Work packages

### WP1 — Shared payload shapes (D17)

`src/Settings/SchoolCollab.Settings.Api/Endpoints/ApiResponsePayloads.cs`:

```csharp
namespace SchoolCollab.Settings.Api.Endpoints;

/// <summary>Cross-aggregate 4xx envelope — serializes <c>{"message":…}</c>.</summary>
internal sealed record MessageResponse(string Message);

/// <summary>Cross-aggregate 400 envelope — serializes <c>{"error":…}</c> (distinct from MessageResponse).</summary>
internal sealed record ErrorResponse(string Error);
```

`MessageResponse` **moves** out of `CodedValueResponsePayloads.cs`, which keeps `IdResponse` and
`BulkCreateResponse`. The CodedValue routes and its 5 payload tests must compile against the new location **in the
same commit**; the OpenAPI schema name `MessageResponse` does not change.

Aggregate-specific composites (e.g. `EntityCodeRule`'s generation result, `ConfigFlag`'s create result) are named
in the route file that uses them — they have exactly one consumer.

### WP2 — Convert the 42 routes, file by file

Rules are the parent spec's §WP3, with three additions:

1. **`BadRequest`/`Conflict` payloads** → `TypedResults.BadRequest(new MessageResponse(ex.Message))`, arm typed
   `BadRequest<MessageResponse>`.
2. **`Results.Json(new { error = … }, statusCode: 400)`** → `TypedResults.Json(new ErrorResponse(ex.Message), statusCode: StatusCodes.Status400BadRequest)`,
   arm typed `JsonHttpResult<ErrorResponse>`; **if** a site passes `JsonSerializerOptions`, pass the same instance.
3. **Two 4-arm handlers** → `Task<IResult>` + a comment naming all four arms (D19).

Order: `ConfigAudit` → `ConfigResolve` → `AssignmentPolicy` → `NotificationPolicy` → `SignatureConsentText` →
`AssignmentAiPrompt` → `ConfigTenantFlagOverride` → `Tenant` → `NotificationTemplate` → `ConfigFlag` →
`EntityCodeRule` (smallest first, so the pattern is re-read before the 24-site file).

### WP3 — Pin (D18)

`OpenApiSurfaceArchitectureTests.cs`: `SettingsSchemaPin` **19 → measured** — **50** as implemented (see §1.2's
correction: the two `IResult` 4-arm handlers cannot document a schema). The message states the
complete split and must be arithmetically checkable: 66 operations = 24 pilot + 28 body-bearing + 3 four-xx-only +
11 with no body; 19 + 28 + 3 = 50. Students stays 0; Assignments keeps its defect pin.

### WP4 — Payload tests (D21)

`tests/SchoolCollab.Settings.Api.Tests.Unit/SettingsResponsePayloadTests.cs`, one test per named shape and per
`Json` site (~8): the exact body text for `MessageResponse`, `ErrorResponse`, and each aggregate-specific composite,
plus the two `TypedResults.Json` 400 bodies. Reuse the `CodedValueRoutePayloadTests` TestServer-with-stubs harness.

### WP5 — Docs

- `.github/skills/minimal-api-composition/SKILL.md` §3: update the pin sentence to **52 of 66** (Settings complete)
  and add the `{"error":…}` vs `{"message":…}` distinction and the `TypedResults.Json` rule.
- `documents/solution/endpoint-organization-pattern.md` §4 "Typed responses": same two additions.
- No `AGENTS.md` change.

### WP6 — Verification

Build, the three Settings suites, Architecture tests, and the OpenAPI before/after delta (D14 precedent): 66
operations before and after, no path/verb change, `content` count 19 → measured, zero operations losing a response.

---

## 4. Verification matrix

| Check | Command | Must be |
|---|---|---|
| Build | `dotnet build SchoolCollab.slnx` | 0 errors |
| Architecture | `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | 0 failures, Settings pin = measured |
| Settings API | `dotnet test tests/SchoolCollab.Settings.Api.Tests.Unit` | 0 failures (incl. the new payload file) |
| Settings unit | `dotnet test tests/SchoolCollab.Settings.Tests.Unit` | 0 failures |
| Contract | OpenAPI delta | paths/verbs identical; `content` 19 → measured; none lost |
| Pin non-vacuity | revert one converted handler → pin fails; restore | fails, then green |
| Route parity | Settings operation count | 66 before and after |

---

## 5. Risks

| Risk | Mitigation |
|---|---|
| Moving `MessageResponse` breaks the CodedValue routes/tests | Same commit; `dotnet build` before any conversion; the 5 payload tests must stay green. |
| `ErrorResponse`'s `error` key gets normalised to `message` | Distinct record, distinct property; body text asserted (WP4). |
| A `TypedResults.Json` conversion drops a `JsonSerializerOptions` instance | WP2 rule 2 requires reading each site first and passing the same instance; both current sites were read and use defaults. |
| Pin arithmetic wrong again (the 4xx-with-payload trap) | Measured split in §1.2; the pin is set to the **measured** value, not the predicted one (the parent round's lesson). |
| 4-arm handlers drift to 5 arms during conversion | Comment names the arms; the reviewer checks arm counts against §1.3. |

---

## 6. Acceptance checklist

- [ ] All 42 routes converted; `Results.*` count in the 11 files is 0 except where a 4-arm `IResult` handler keeps one.
- [ ] `ApiResponsePayloads.cs` holds `MessageResponse` + `ErrorResponse`; `CodedValueResponsePayloads.cs` keeps `IdResponse` + `BulkCreateResponse`; schema names unchanged for `MessageResponse`.
- [ ] The two former `Results.Json` sites emit the same 400 `{"error":…}` body via `BadRequest<ErrorResponse>`, which (unlike `JsonHttpResult<T>`) documents the 400 schema.
- [ ] Both 4-arm handlers are `Task<IResult>` with a comment naming their arms.
- [ ] `SettingsSchemaPin` = measured (**50**) with the checkable message; Students 0.
- [ ] `SettingsResponsePayloadTests` asserts the exact bodies of every newly named record and both `Json` sites.
- [ ] OpenAPI delta: 66 → 66 operations, no path/verb change, `content` 19 → measured, none lost.
- [ ] Build 0 errors; Architecture + Settings API + Settings unit suites green.
- [ ] Repo skill §3 and pattern doc §4 updated (complete-host pin, `error` vs `message`, `TypedResults.Json`).

---

## 7. Out of scope

| Item | Why |
|---|---|
| Students (146 operations) | D23 — the next host after Settings is complete. |
| `Assignments.Api` OpenAPI defect | Wire-shape DTO fix, human-gated; still defect-pinned. |
| Schema-*content* assertions (`CodedValueDto`'s properties must appear) | Deferred until this round lands and the document stops moving. |
| `.http` artifacts, `ProblemDetails` validation, `AssignmentRoutes.cs` split | Parent spec D5/D6/§7.1 — declined or deferred. |
