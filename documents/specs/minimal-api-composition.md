# Spec — Minimal-API composition guidance adoption (`dotnet-webapi`)

| | |
|---|---|
| Status | **Approved (rounds 1 + 2, owner: "all as recommended") — not implemented** |
| Date | 2026-10-17 |
| Upstream source | `dotnet/skills` → `plugins/dotnet-aspnetcore/skills/dotnet-webapi/SKILL.md` (MIT) |
| Governance docs touched | `AGENTS.md`, `documents/solution/endpoint-organization-pattern.md` |
| Execution mode | **Light round (Tier 2)** — one worker + static diff-only reviewer |

---

## 1. Findings

### 1.1 What the upstream skill prescribes

`dotnet-webapi` is the only ASP.NET Core API skill in `dotnet/skills` (the
`dotnet-aspnetcore` plugin also ships `convert-blazor-server-to-webapp`). Relevant
prescriptions: endpoints grouped **by resource** as a static class with a
`Map<Resource>` method; never mix controllers and minimal APIs; `TypedResults`
over the `Results` factory; OpenAPI metadata chained on endpoints; DTOs as
`sealed record` with XML doc comments; validation via an endpoint filter
returning `ValidationProblem`; centralized exception-mapping middleware; `.http`
files as request artifacts.

### 1.2 Repository reality (measured, `src/**/*.cs`)

| Signal | Count |
|---|---|
| `MapGroup(` | 29 |
| `Results.Ok/Created/NoContent/BadRequest/NotFound` | 558 |
| `TypedResults.` | **0** |
| `.Produces<` / `.WithName(` / `.WithTags(` / `.WithOpenApi(` | **0 / 0 / 0 / 0** |
| `AddEndpointFilter` | 3 |
| `ValidationProblem` / `ProblemDetails` (API hosts) | **0 / 0** |
| `.http` files | **0** |
| hand-rolled `featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth)` **guard** blocks | **21 across 13 files** |
| `AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme)` opt-ins | 18 across 14 files |
| largest route file | `AssignmentRoutes.cs` — 1618 lines, 51 endpoints |

`AddOpenApi`/`MapOpenApi` are already wired (6 call sites), so typed responses have
somewhere to land.

### 1.3 Two corrections to the round-1 discussion (recorded deliberately)

1. **This was never a conflict with `AGENTS.md`.** The repo already implements the
   skill's model, one layer deeper than `AGENTS.md` describes: an
   `Map<Domain>Endpoints(this WebApplication app, IFeatureFlagService)` orchestrator
   per API, delegating to `Map<Specialty>Routes(this RouteGroupBuilder group)`
   specialty files. The existing decision record is
   `documents/solution/endpoint-organization-pattern.md` — it documents both layers,
   the naming table and the "specialty files never call `RequireAuthorization`, never
   read flags" contract. `AGENTS.md` states only the orchestrator half.
2. **The `AssignmentEndpoints.cs` "5 `RequireAuthorization` / 3 Bearer opt-ins"
   asymmetry is deliberate, not drift.** The reader and writer sub-groups pin
   `PortalSessionAuthenticationHandler.GatewaySchemeName` (a policy scheme that routes
   portal-session → portal-session, bearer → Bearer, dev → TestAuth), which is why they
   must NOT use the Bearer-only opt-in. Verified at `AssignmentEndpoints.cs:29–63,118–146`.

So the actionable gap is **duplication and stale documentation**, not a defect:

- the same four-line flag-guard + opt-in block is written **21 times** in 13 files;
- `endpoint-organization-pattern.md` §4's orchestrator template still shows a bare
  `group.RequireAuthorization();` (no Bearer/gateway scheme), i.e. the template an
  agent copying it would produce is **not** the ar-24-correct one;
- the only guard over this wiring is a count-pinned assertion
  (`BearerForwardingWiringArchitectureTests.BearerOptIn_PerGroup_StudentsAndSettings_NoRouteLevelFlagGate`,
  needle `"AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme)"`, expects
  `StudentEndpoints.cs` = 5 and each Settings file = 1), which a helper extraction
  would silently gut to zero;
- `ProblemDetails`-based validation is the one genuinely divergent upstream
  prescription, and it is unrecorded.

---

## 2. Decisions (round 1 — settled)

| # | Decision | Rejected alternatives |
|---|---|---|
| **D1** | Adopt the skill's composition model as *written* policy: document both layers in `AGENTS.md` (linking the pattern doc), extract one shared `RequireAuthIfOidc` helper, and guard it with an architecture test. | (a) Document-only, no helper — leaves 21 copies and no guard. (b) Helper without docs — agents keep copying the stale template. |
| **D2** | `TypedResults` for **new and touched** endpoints only; no repo-wide sweep. | 558-site sweep: large diff, zero behaviour change, collides with every open branch. |
| **D3** | **Defer** the endpoint-filter + `ValidationProblem` model; record the deferral in the pattern doc. | Adopt now — changes the API error wire shape (0 `ProblemDetails` today, exception-driven validation), which `AGENTS.md` reserves for a human. |
| **D4** | Install `dotnet-webapi` **user-level** (`npx skills`, vendored + refreshable) *and* author a small repo-owned delta skill. | (a) Repo-owned restatement only — loses upstream refresh. (b) No install — the guidance never loads. |
| **D5** (round 2) | Composition rules apply to **new and touched** code; `AssignmentRoutes.cs` is not split in this round. | 51-endpoint mechanical move now: no behaviour change, makes the auth diff unreviewable, high risk. |
| **D6** (round 2) | **Reject** `.http` artifacts as a committed standard; use permitted ad hoc only. Full justification: §8.1. | Adopt per-API `.http` files: duplicates the already-generated OpenAPI document, has no CI runner, and needs out-of-band token/tenant minting for this repo's auth model. |
| **D7** (round 3) | Keep D6, **and** add one OpenAPI-document assertion test so cheap repo-wide coverage exists without `.http`: it parses the generated document from an in-process TestServer host and asserts every mapped path is documented with at least one response schema. No request fan-out, no DI stubs. | (a) No replacement — leaves the `TypedResults`/OpenAPI gap unenforceable. (c) Commit `.http` files — the only option that adds maintenance cost without adding a gate. |

### Execution ownership (Tier 2 round)

| Work | Owner |
|---|---|
| WP1–WP4, WP7 (code + tests) | worker run |
| WP5–WP6 (governance docs, vendored skill install, repo-owned skill) | parent — design-record and machine-level work, not delegable |

---

## 3. Work packages

### WP1 — Shared guard helper (behaviour-preserving)

**New file:** `src/SchoolCollab.Core/Auth/EndpointGroupAuthExtensions.cs`

`SchoolCollab.Core/Auth` is the right home: it already owns `AuthTenancyExtensions.BearerScheme`,
`PortalHandshakeExtensions` (which calls `app.MapGroup`) and is referenced by every API host.

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.Constants;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// The single write-point for a minimal-API group's flag-conditional authorization policy.
/// Applies the policy exactly while real OIDC auth is on (<c>FEATURE:DisableOIDCAuth</c> off).
/// Guarded by <c>MinimalApiCompositionArchitectureTests</c>; pattern and rationale:
/// documents/solution/endpoint-organization-pattern.md §4.
/// </summary>
public static class EndpointGroupAuthExtensions
{
    /// <summary>Authenticated user on the Bearer scheme — the ar-24 receiving-side opt-in.</summary>
    public static RouteGroupBuilder RequireAuthIfOidc(
        this RouteGroupBuilder group, IFeatureFlagService? featureFlags)
        => group.RequireAuthIfOidc(featureFlags, static policy => policy
            .RequireAuthenticatedUser()
            .AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme));

    /// <summary>
    /// Caller-supplied policy (gateway-scheme role gates, the portal-facing admin policy).
    /// A null flag service means "gate on" — the production posture, never gate-open.
    /// </summary>
    public static RouteGroupBuilder RequireAuthIfOidc(
        this RouteGroupBuilder group,
        IFeatureFlagService? featureFlags,
        Action<AuthorizationPolicyBuilder> policy)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(policy);

        if (featureFlags is null || !featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))
        {
            group.RequireAuthorization(policy);
        }

        return group;
    }
}
```

Acceptance:
- The default overload's policy delegate is **character-identical** to the block it replaces
  (`RequireAuthenticatedUser()` then `AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme)`).
- The `null` flag service path reproduces `AuthEndpointGroup`'s existing `featureFlags is null ||` semantics exactly.
- No route URI, HTTP method, request/response shape or status code changes anywhere (see §5).

### WP2 — Migrate the 21 guard sites

| File | Blocks | Current shape | After |
|---|---|---|---|
| `src/Students/SchoolCollab.Students.Api/StudentEndpoints.cs` | 5 | Bearer (5) | `group.RequireAuthIfOidc(featureFlags);` ×5 |
| `src/Assignments/SchoolCollab.Assignments.Api/AssignmentEndpoints.cs` | 5 | Bearer (3) + gateway policies (2) | 3 × default; `readerGroup.RequireAuthIfOidc(featureFlags, RequireAssignmentReader);`; same for `RequireAssignmentWriter` |
| `src/Settings/SchoolCollab.Settings.Api/{AssignmentAiPrompt,AssignmentPolicy,CodedValue,Config,EntityCodeRule,NotificationPolicy,NotificationTemplate,SignatureConsentText,Tenant}Endpoints.cs` | 9 | Bearer (8) + a `bool oidcEnabled` local derived from the same flag (1, `ConfigEndpoints.cs`) | default ×9 — the local survives for `MapConfigResolveRoutes(oidcEnabled)` |
| `src/SchoolCollab.Auth/Endpoints/AuthEndpointGroup.cs` | 1 | `featureFlags is null \|\| !flag` → `ConfigurePortalFacingAdminPolicy` | `adminGroup.RequireAuthIfOidc(featureFlags, ConfigurePortalFacingAdminPolicy);` |

**Explicitly NOT migrated (exemptions, pinned by the guard):**

| Site | Why |
|---|---|
| `src/Settings/.../Endpoints/ConfigResolveRoutes.cs` | takes `bool oidcEnabled`, not `IFeatureFlagService` — different mechanism; keeps its inline `if (oidcEnabled)` + Bearer opt-in. |
| `src/Assignments/.../Endpoints/AssignmentRoutes.cs:157` | `var isRealAuth = !featureFlags.IsEnabled(...)` inside a handler body — a per-request principal decision, not a group gate. |
| `MediatedReadEndpoints.cs` | uses `RequireAuthorization` without a flag guard by design (portal pickers). |
| `DeepLinkEndpoints.cs` (Families host) | separate host, no flag-conditional group policy. |

Acceptance: zero occurrences of the literal guard `if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))`
remain in the four migrated contexts; every migrated call site is a single statement.

### WP3 — Rewrite the existing bearer guard so it cannot go vacuous

**File:** `tests/SchoolCollab.ArchitectureTests.Unit/BearerForwardingWiringArchitectureTests.cs`

After WP1 the `OptIn` needle (`"AddAuthenticationSchemes(AuthTenancyExtensions.BearerScheme)"`)
exists **once**, in the helper — so the current per-file counts (5 / 1-each) would have to
become `.Be(0)`, which asserts nothing. Required changes:

1. Keep `OptIn` const; add
   `Count(Read("src","SchoolCollab.Core","Auth","EndpointGroupAuthExtensions.cs"), OptIn).Should().Be(1, "the helper is the single write-point for the Bearer opt-in; deleting it must fail this guard.")`.
2. Replace per-file `OptInCount(...)` assertions with a new needle
   `private const string Gate = "RequireAuthIfOidc("` and expect: `StudentEndpoints.cs` = 5,
   `AssignmentEndpoints.cs` = 5, each of the 9 Settings `*Endpoints.cs` = 1.
3. Add the absence assertion that makes the migration stick:
   `Count(source, "if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth))").Should().Be(0)`
   over the 13 migrated files — a re-introduced hand-rolled block fails immediately.
4. Keep the exemption for `ConfigResolveRoutes.cs`: assert `OptInCount == 1` and `Count(Gate) == 0` there.
5. Update the class XML doc `<item>` that states "exactly five blocks in StudentEndpoints.cs and
   one per Settings.Api `*Endpoints.cs` file" to describe helper call counts.

### WP4 — New composition guard

**New file:** `tests/SchoolCollab.ArchitectureTests.Unit/MinimalApiCompositionArchitectureTests.cs`
(MSTest + FluentAssertions, `FindRepoRoot()` walk-up — same shape as WP3's class).

Three assertions, each with an explicit exemption list and a message naming the offending file:

1. **Orchestrator/receiver contract.** For every `src/**/<Domain>Endpoints.cs`, any
   `Map<Specialty>Routes(` call target file must declare `Map<Specialty>Routes(this RouteGroupBuilder group)`.
   Exemption: `ConfigResolveRoutes.cs` (WebApplication receiver, bool flag param).
2. **No inline routes in the orchestrator.** `Count(orchestrator, ".MapGet(" | ".MapPost(" | ".MapPut(" | ".MapDelete(") == 0`.
3. **No route-level authorization in specialty files.** `*Routes.cs` must contain zero
   `RequireAuthorization(`. Exemptions: `ConfigResolveRoutes.cs` (documented bool-gated tenant read).

> Deliberately *not* asserted: `TypedResults` usage (558 legacy `Results.*` sites make a
> source-level guard impossible without the sweep D2 rejected) and `.http` files (round 2).

### WP5 — Documentation

**`AGENTS.md`** — replace the `API Endpoint Grouping` bullet (currently line 438) with:

```
- **API Endpoint Grouping** — two layers, full pattern and templates in
  `documents/solution/endpoint-organization-pattern.md`: (1) a `<Domain>Endpoints`
  orchestrator, `Map<Domain>Endpoints(this WebApplication app, IFeatureFlagService featureFlags)`,
  owns `MapGroup(prefix)`, the flag-conditional authorization policy and the chained
  `Map*Routes()` calls; (2) one `<Specialty>Routes.cs` per specialty,
  `Map<Specialty>Routes(this RouteGroupBuilder group)`, maps relative paths only and never
  touches authorization or feature flags. Never define routes inline in `Program.cs`. Apply a
  group's policy through `RequireAuthIfOidc(...)` — never a hand-rolled
  `if (!featureFlags.IsEnabled(FeatureFlagKeys.DisableOIDCAuth)) { … }` block; enforced by
  `MinimalApiCompositionArchitectureTests`. New or edited endpoints return `TypedResults`, not
  the `Results` factory, so OpenAPI emits real response schemas.
```

Also add a row to the *Specialty instructions* table:

```
| Minimal-API composition (grouping, auth helper, typed results) | `.github/skills/minimal-api-composition/SKILL.md` |
```

**`documents/solution/endpoint-organization-pattern.md`:**

- §4 orchestrator template → use `group.RequireAuthIfOidc(featureFlags);` and state that a
  caller-supplied policy goes through the second overload; **delete** the bare
  `group.RequireAuthorization();` template (it is ar-24-incorrect as written).
- §6 audit checklist → add "policy applied via `RequireAuthIfOidc`, no inline flag guard" and
  "new/edited endpoints return `TypedResults`".
- New §8 **Deferred — ProblemDetails validation** (this is D3's on-record rejection):

  > The `dotnet-webapi` skill prescribes data-annotation validation via an endpoint filter
  > returning `ValidationProblem` (RFC 7807). Deferred 2026-10-17: the solution returns no
  > `ProblemDetails` today and validates inside handlers (e.g. `ValidationException` in
  > `AssignmentRoutes.cs`), so adopting the filter model is an API **error wire-shape** change
  > and needs a human decision. Revisit as its own round with a client-compatibility inventory
  > (`SchoolCollab.Admin.Shared` API clients, portal Python clients, bUnit/Playwright assertions).

- Update §5's "`*.http` files" reference to note that none exist today (round 2 item).
- New §8.2 **Declined — `.http` request artifacts** (D6's on-record rejection), text below.
- **§8.1 recorded rejection text (`.http`)** — insert as §8.2 in the pattern doc:

  > The `dotnet-webapi` skill recommends `.http` files as request artifacts. Declined as a
  > committed standard 2026-10-17, for four reasons: (1) every API host already maps OpenAPI
  > (`AddOpenApi`/`MapOpenApi` in `Assignments.Api/Program.cs:210,271`,
  > `Settings.Api/Program.cs:43,49`, `Students.Api/Program.cs:85,94`), so a `.http` file is a
  > hand-maintained duplicate of a generated artifact and can only drift from it — silently,
  > since nothing executes it; (2) there is no first-party CI runner for the format (the VS Code
  > and JetBrains clients run in-IDE only), so it cannot act as a gate, unlike the MTP test
  > standard this repo requires; (3) the need it serves — issuing real HTTP requests against an
  > endpoint — is already met executably by `tests/SchoolCollab.Assignments.Api.Tests.Unit/*RouteTests.cs`
  > (reader/writer/portal-session policies, guardian token filter, guardian sign-off) and by the
  > Students/Settings integration + Playwright suites; (4) it fits this repo's hardest axis worst:
  > every call needs a Keycloak bearer or portal-session credential plus a tenant id, which `.http`
  > can only satisfy by out-of-band token minting and a pinned tenant, whereas `TestAuthHandler`,
  > the dev tenant switcher and `WebApplicationFactory` solve it in code. A throwaway `.http` file
  > for a local probe remains permissible — nothing forbids it — but it is not committed, reviewed
  > or counted as coverage.

### WP6 — Skill distribution

1. Install user-level (vendored, installer-managed — **never hand-edit**):

   ```shell
   npx --yes skills add dotnet/skills -s dotnet-webapi -g -y --copy
   ```

   (A bare skill name is read as a repository: `skills add dotnet-webapi -g` fails with
   `Failed to clone dotnet-webapi: repository 'dotnet-webapi' does not exist`. The package is
   `dotnet/skills` and the skill is selected with `-s`. A trailing `Failed to install 1 →
   PromptScript does not support global skill installation` line is benign.)

   Verify: `~/.agents/.skill-lock.json` gains an entry with `sourceUrl`
   `https://github.com/dotnet/skills/tree/main/plugins/dotnet-aspnetcore/skills/dotnet-webapi`;
   canonical copy lands in `~/.agents/skills/dotnet-webapi/`. No ACL/lock dance is required
   (nothing is write-locked as of 2026-10-12); refresh later via the
   `refresh-vendored-agent-skill` skill.

2. **New file:** `.github/skills/minimal-api-composition/SKILL.md` — repo-owned deltas only,
   pointing at the pattern doc for the full pattern:

   - `description`: triggers a user would type — "add an endpoint", "new API route",
     "minimal API", "MapGroup", "RequireAuthorization", "Results vs TypedResults".
   - Body: the two-layer shape with both templates; `RequireAuthIfOidc` usage and its two
     overloads; the exemptions table from WP2; "return `TypedResults`, not `Results`";
     "validation stays exception-driven until the §8 deferral is lifted"; verification =
     `dotnet build SchoolCollab.slnx` + `MinimalApiCompositionArchitectureTests`.
   - One clause citing this spec as provenance (evidence, not changelog).

### WP7 — OpenAPI surface guard (D7)

**New file:** `tests/SchoolCollab.ArchitectureTests.Unit/OpenApiSurfaceArchitectureTests.cs`

One test per API host that maps OpenAPI (`Assignments.Api`, `Settings.Api`, `Students.Api`):

1. Build an in-process host with `WebApplication.CreateBuilder()` + `UseTestServer()`, register the
   minimum services the host needs to start OpenAPI (`AddOpenApi`, stub `IFeatureFlagService`), map
   **only** `<Domain>Endpoints` (the `AssignmentDraftRoutesTests.StartHostAsync` precedent — Program is
   never started, so no Postgres/RabbitMQ).
2. `GET /openapi/v1.json`, deserialize with `JsonDocument`.
3. For every entry under `paths`, for every operation: assert `responses` is non-empty.
4. **Schema baseline, not a quality gate (amended during the round).** Assert the count of operations
   that document a response schema equals the measured baseline — **0** for Settings (66 operations) and
   **0** for Students (146) — because every handler returns untyped `Results.*`. The message must state
   that converting handlers is D2 (new/touched only) and that this number must be **raised deliberately**
   in the PR that converts handlers. The original criterion ("every operation documents a schema") was
   unachievable without the sweep D2 rejected; it is recorded here as debt, not silently dropped.
5. **Assignments.Api: pin the defect, do not degrade to path presence.** Its document cannot be generated
   at all (`JsonException: The JSON value could not be converted to System.Guid`, from
   `JsonSchemaExporter.GetJsonSchemaAsNode` → `OpenApiDocumentService.GetJsonRequestBody`), so the host's
   *published* OpenAPI document is unavailable — a pre-existing defect unrelated to this round. The test
   therefore asserts that generation currently fails, and goes red the moment it starts succeeding
   (delete the pin then, do not adjust it). Fixing it touches a request DTO (wire shape) ⇒ its own round.
6. Non-vacuity guard: assert the path count equals the route count the host maps (±an explicitly named
   exemption list; normalise a group-root trailing slash, e.g. `/contacts/` vs `/contacts`), so an empty
   document fails rather than passes.

Scope guard: the assertion is over the **document**, never over handler behaviour — it replaces no route
 test, it only makes a missing one visible. If a host's OpenAPI startup needs services the minimal host
 cannot supply, the test falls back to asserting only path presence for that host and records the
 degradation in a comment (never `[Ignore]`).

Acceptance: the three tests fail if a host stops mapping OpenAPI, if a route is mapped but absent from the
 document, or if a mapped route documents no response schema.

---

## 4. Verification matrix

| Check | Command | Must be |
|---|---|---|
| Build | `dotnet build SchoolCollab.slnx` | 0 errors |
| Architecture guards | `dotnet test tests/SchoolCollab.ArchitectureTests.Unit/SchoolCollab.ArchitectureTests.Unit.csproj` | 0 failures (incl. rewritten WP3 + new WP4) |
| Regression | `dotnet test SchoolCollab.slnx` | 0 failures |
| Behaviour identity | `git diff` review: no `Map(Get|Post|Put|Delete|Patch)` path template changed in WP2 | true |
| Guard non-vacuity | Temporarily delete one `RequireAuthIfOidc(` call in `StudentEndpoints.cs` → WP3 fails; restore | fails, then green |
| Vendored skill | `ls ~/.agents/skills/dotnet-webapi/SKILL.md` + lock entry present | present |
| Mobile head | **not required** — no shared component and no package version changes | n/a |

---

## 5. Risks

| Risk | Mitigation |
|---|---|
| `AuthEndpointGroup`'s null-flag-service semantics ("missing service ⇒ gate on") get lost in the helper | Second overload treats `null` as gate-on; the auth-service endpoint tests exercise the unregistered case (`AddAuthAndTenancy` absent). |
| A migrated group accidentally widens/narrows its policy | Helper copy is character-identical; the reader/writer sub-groups keep their gateway-scheme policies via the callback overload. |
| Extracting the helper makes `BearerForwardingWiringArchitectureTests` pass vacuously | WP3 inverts the assertion onto the helper + adds the absence check; §4 includes a deliberate-deletion probe. |
| WP2 touches 13 auth files in one PR | Mechanical, single-statement replacement; no logic edits; reviewed as a whole-diff PR with the exemption table. |
| Over-strict WP4 guard blocks legitimate future shapes | Only three assertions; every exemption is explicit and named. |

---

## 6. Out of scope (round 2 — settled)

| Item | Ruling |
|---|---|
| Split `AssignmentRoutes.cs` (1618 lines / 51 endpoints) into specialty route files | **Deferred (D5).** Composition rules apply to new and touched code; split when the file is next materially changed. A 51-endpoint mechanical move is high-risk, no behaviour change, and would make this round's auth diff unreviewable. |
| Adopt `.http` request artifacts | **Rejected (D6).** Justification recorded in the pattern doc §8.2 — see WP5. |

---

## 7. Follow-up defects discovered during implementation

### 7.1 `Assignments.Api` cannot generate its OpenAPI document (pre-existing)

`GET /openapi/v1.json` on an in-process `Assignments.Api` host throws
`JsonException: The JSON value could not be converted to System.Guid`, from
`JsonSchemaExporter.GetJsonSchemaAsNode` → `OpenApiSchemaService.CreateSchema` →
`OpenApiDocumentService.GetJsonRequestBody` (a request-body schema). The other two hosts generate fine, so
this is a defect in that host's own surface, not in the test harness — **the published OpenAPI document
for the Assignments API is unavailable**, independent of this round.

Found 2026-10-17 during the `minimal-api-composition` round; out of scope there because the fix touches a
request DTO (wire shape). `tests/SchoolCollab.ArchitectureTests.Unit/OpenApiSurfaceArchitectureTests.cs`
**pins the failure**: it asserts generation does not return 200 and that the exception names `System.Guid`,
so it goes red the moment the defect is fixed — delete the pin then, do not adjust it.

---

## 8. Acceptance checklist (round outcome, 2026-10-17)

- [x] `EndpointGroupAuthExtensions.cs` exists in `src/SchoolCollab.Core/Auth/` with both overloads.
- [x] 20 guard blocks in 12 files replaced by `RequireAuthIfOidc` calls (`ConfigResolveRoutes` bool exemption; the 21st count in §1.2 was the in-handler read in `AssignmentRoutes.cs`).
- [x] `BearerForwardingWiringArchitectureTests` asserts helper opt-in (1) + per-file gate counts + zero hand-rolled guards + zero per-file opt-ins.
- [x] `MinimalApiCompositionArchitectureTests` passes with its 6-entry exemption list (1 receiver + 5 leaf groups).
- [x] `OpenApiSurfaceArchitectureTests` passes for Settings and Students; Assignments pins its document-generation defect instead (see §7.1).
- [x] `AGENTS.md` bullet + specialty-table row updated; `endpoint-organization-pattern.md` §4 (helper) / §6 (checklist) / §7.1 (validation deferral) / §7.2 (`.http` rejection) updated.
- [x] `dotnet-webapi` installed user-level, lock entry verified; `.github/skills/minimal-api-composition/SKILL.md` authored.
- [x] `dotnet build SchoolCollab.slnx` 0 errors; 572 tests green across ArchitectureTests, Auth, Assignments.Api, Students.Api and Settings.Api unit suites.
- [x] No route URI, HTTP verb, request/response shape or status code changed (diff reviewed hunk-by-hunk by the static reviewer).
- [x] Guard non-vacuity proven by four parent-run probes (delete helper call, re-introduce hand-rolled guard, strip the helper's opt-in, add a redundant inline opt-in).
