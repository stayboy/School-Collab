# Round — `assignments-cross-module-base-addresses`

**Status:** **accepted** 2026-10-06 — reviewer approved; P2 amendment applied pre-PR and the probe re-run
**Tier:** Light (Tier 2) — one worker run + an independent static diff reviewer
**Models:** parent `ollama-cloud/deepseek-v4.1-flash` (session) · worker `ollama-cloud/deepseek-v4.1-flash` · diff reviewer `ollama-cloud/kimi-k2.7-code`
**Branch:** none — no branch/commit authorized.

## Plan

### The defect (confirmed, from a read-only investigation)

`AssignmentPolicyResolver` — and three sibling resolvers — are injected with an `IHttpClientFactory` and resolve a
**named** client that was registered **without a base address**, so there is nothing for Aspire service discovery to
resolve, and the resolvers then issue **relative** URIs against it.

| Step | Evidence |
|---|---|
| Resolution by name, sanctioned | `AssignmentPolicyResolver.cs:45` → `CreateClient("settings-api")`; `:67` → `CreateClient("students-api")` |
| Request URIs are **relative** | `AssignmentPolicyResolver.cs:48` → `GetAsync("/api/settings/assignment-policy", ct)` |
| The client has **no base address** | `Assignments.Api/Program.cs:148` → bare `builder.Services.AddHttpClient("settings-api")` + bearer handler + `AllowAutoRedirect=false`. The token `BaseAddress` occurs nowhere in that file |
| The sanctioned form, and the blind spot | `Assignments.Api/Program.cs:138` → `AddCrossModuleHttpClient("students-api", "https+http://students-api", …)`, whose in-file comment records `[P1-6] MIGRATED … from a bare AddHttpClient to the documented AddCrossModuleHttpClient pattern` — the identical defect was fixed for the sibling client and left for this one |
| The standard is written down | `documents/solution/cross-module-http-client-pattern.md`: *"Use `AddCrossModuleHttpClient` for every HTTP client that crosses an Aspire service boundary"* |
| Repo-wide census (derived, not guessed) | five named `AddHttpClient(name)` registrations in `src/`; **four without a base address**: `Assignments.Api:148` (`settings-api`), `Assignments.Worker:36` (`settings-api`), `Assignments.Worker:37` (`students-api`), `Assignments.Application/ModuleServices.cs:49` (`url-fetcher`). The last is **not a defect** — it fetches absolute external URLs. `Students.Worker:59` registers `settings-api` **with** a base address — the correct form already exists in-tree to copy |

**Runtime consequence.** A client with a null `BaseAddress` and a relative URI throws `InvalidOperationException`
("…the request URI must be an absolute URI or BaseAddress must be set") — **not** `HttpRequestException` — so it
escapes the resolvers' `catch (HttpRequestException)` and their documented fail-open posture:
`AssignmentPolicyResolver.ResolveAsync` is on `PublishAssignmentCommandHandler:44`,
`ScheduleAssignmentCommandHandler:36` and `AssignmentRoutes.cs:507`; `SignatureConsentTextResolver:23` is on
`SignOffSubmissionCommandHandler:77`, `FinalizeSignOffCommandHandler:73`, `GetSignOffContextQueryHandler:48`,
`AssignmentRoutes.cs:327`; `AiPromptPolicyResolver:22` and `NotificationPolicyResolver`
(`Core/Services/NotificationPolicyResolver.cs:45`, also run by the Worker) are affected too.

**Why two guards missed it.** `CrossModuleWiringTests` (`tests/SchoolCollab.Core.Tests.Unit/Architecture/`) scans
`src/**` for base-address **literals** and checks the AppHost `.WithReference` — a client with *no* address produces
*no* literal, so it is invisible to that rule; `BearerForwardingWiringArchitectureTests` pins these same named clients
only for the **bearer handler** attachment; and the unit tests that cover the resolvers **supply the base address the
production registration omits** (`AssignmentPolicyResolverTests.cs:197-198`, `AiPromptPolicyResolverTests.cs:83`).

### Settled by the owner grill (2026-10-06)

1. **Scope (Q1 → a):** fix the **three** broken cross-module registrations in the Assignments context — `Assignments.Api:148`,
   `Assignments.Worker:36`, `Assignments.Worker:37` — each becoming
   `AddCrossModuleHttpClient("<name>", "https+http://<name>", propagateTenant: false)` while keeping its existing
   bearer-forwarding and `AllowAutoRedirect=false` chain. No repo-wide normalisation: `Students.Worker:59` is already
   correct and `url-fetcher` is not a cross-module client.
2. **Prevention (Q2 → a):** **extend `CrossModuleWiringTests`** with a second rule — a named client whose **name is a
   declared AppHost resource** must set its base address to that resource. Keying off declared resources needs no
   exempt list and closes the blind spot (an *absent* address) that the existing literal scan cannot see.
3. **Fail-open posture (Q3 → a):** the resolvers' catches stay **narrow**. Fail-open is for runtime faults (outage,
   204, malformed payload); misconfiguration must stay loud, because silently degrading to built-in defaults would
   quietly ignore the tenant's configured policy. Prevention belongs to the guard.
4. **Mode and proof (Q4):** Tier 2 light; proof = the widened guard + the existing suites + a **red-then-green probe**
   on the guard. No DI-resolution refactor of the top-level `Program.cs` (rejected in the morning's round) and no
   AppHost smoke (no harness exists).

### Change set

| # | File | Change |
|---|---|---|
| 1 | `src/Assignments/SchoolCollab.Assignments.Api/Program.cs` | `settings-api` → the sanctioned helper, keeping `.AddHttpMessageHandler<BearerForwardingDelegatingHandler>()` and `.ConfigurePrimaryHttpMessageHandler(…)`; comment updated (it currently implies a migration that never happened) |
| 2 | `src/Assignments/SchoolCollab.Assignments.Worker/Program.cs` | both `settings-api` and `students-api` → the sanctioned helper (keeping any handler chain they carry) |
| 3 | `tests/SchoolCollab.Core.Tests.Unit/Architecture/CrossModuleWiringTests.cs` | the new rule, with a named failure reason |
| 4 | `src/AppHost/SchoolCollab.AppHost/Program.cs` | **only if the guard demands it**: the two new `https+http://…` literals make the existing rule require `.WithReference(settingsApi)` / `.WithReference(studentsApi)` on `assignments-worker`. Verify; add only the missing one(s) |
| 5 | `tests/SchoolCollab.ArchitectureTests.Unit/TeacherScopeWiringArchitectureTests.cs` | **pin update — allowlist extended mid-run by the supervisor (2026-10-06)**: its `AssignmentsWorker_StudentsApiClient_IsUntouched` assertion anchored on the *bare* `AddHttpClient("students-api")` spelling as a proxy for its real invariant ("the Worker's own client carries no caller token to forward"). Change 2 moves that registration to the sanctioned form, so the pin is stale **by design** and is re-anchored to the new spelling — same method name, same intent, no new test file. Precedent: `BearerForwardingWiringArchitectureTests`' pinned count 2→3, widened by the round that added a client |

**CHANGE 4 was not needed:** `assignments-worker` already carries `.WithReference(settingsApi)` and `.WithReference(studentsApi)`, so the AppHost is untouched by this round.

### Verification

1. `dotnet build SchoolCollab.slnx` — 0 errors.
2. `dotnet run --project tests/SchoolCollab.Core.Tests.Unit -c Debug` — the guard's own suite.
3. `dotnet run --project tests/SchoolCollab.ArchitectureTests.Unit -c Debug` — 96 tests, 0 failed.
4. `dotnet run --project tests/SchoolCollab.Assignments.Api.Tests.Unit -c Debug` — 158 tests, 0 failed (the resolver
   suites must be undisturbed: they stub the factory, so this checks behaviour, not addresses).
5. **Red-then-green probe on the new rule (mandatory):** temporarily restore one bare `AddHttpClient("settings-api")`,
   show the new rule **fails** with its named reason, restore the fix, show it green. A rule that cannot fail on the
   defect is decoration.
6. Independent static diff review (reviewer, patch by path).

### Out of scope, deliberately

The resolvers' exception filters (Q3), `url-fetcher` and `Students.Worker` (already correct/not applicable), any
repo-wide conversion of `AddHttpClient` uses that are not cross-module, and the `Assignments.Application`
`ModuleServices` client that is not a service-boundary client.

## Worker Report

Delivered, all four named files plus the supervisor-approved pin extension. Nothing staged; the AppHost untouched.

| # | File | Change |
|---|---|---|
| 1 | `Assignments.Api/Program.cs` | `AddHttpClient("settings-api")` → `AddCrossModuleHttpClient("settings-api", "https+http://settings-api", propagateTenant: false)`, with the bearer handler and `AllowAutoRedirect=false` chain **byte-identical** to before; a new comment records the migration and the failure mode. The `students-api` block at `:138` is untouched |
| 2 | `Assignments.Worker/Program.cs` | both `settings-api` and `students-api` → the sanctioned helper (neither carried a handler chain); `using SchoolCollab.Core.Http;` added, since the extension lives there and the file did not import it. The comment claiming discovery "resolves" is now true rather than aspirational |
| 3 | `tests/SchoolCollab.Core.Tests.Unit/Architecture/CrossModuleWiringTests.cs` | new rule `NamedClients_MatchingAnAppHostResource_HaveThatResourceAsBaseAddress`: for every registration whose **name** is in the AppHost's declared resources, the base-address host must equal that name. Scanner covers `AddHttpClient("name")` and `AddCrossModuleHttpClient("name", …)` — non-generic overloads only, so a typed client's name (a type) is out of rule by construction — and inspects the whole statement (configure lambda, chained `ConfigureHttpClient`, or the helper's second argument). No exempt list: `url-fetcher` and `students-core-coded-values` are not declared resources and fall out by keying off the AppHost. **Non-vacuity** assertion before the violation check, and a violation message that distinguishes *no* base address (naming the remedy) from a *wrong* one. Class doc gained a second-rule paragraph; one shared literal regex + one file-enumeration helper extracted so both rules use one definition |
| 4 | `src/AppHost/SchoolCollab.AppHost/Program.cs` | **not needed** — `assignments-worker` already carries both `.WithReference(settingsApi)` and `.WithReference(studentsApi)`; the existing rule passed on the two new literals with no AppHost edit |
| 5 | `tests/SchoolCollab.ArchitectureTests.Unit/TeacherScopeWiringArchitectureTests.cs` | **approved mid-run allowlist extension**: `AssignmentsWorker_StudentsApiClient_IsUntouched` pinned the *bare* spelling as a proxy for its real invariant, so change 2 invalidated it (suite went 95/1). Re-anchored to the sanctioned spelling, same method name, same intent, plus the explicit `NotContain` for the bearer handler; doc-comment bullet updated to match |

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (94 pre-existing warnings) |
| `tests/SchoolCollab.Core.Tests.Unit` | **126/126, 0 failed** |
| `tests/SchoolCollab.ArchitectureTests.Unit` | **96/96, 0 failed** |
| `tests/SchoolCollab.Assignments.Api.Tests.Unit` | **158/158, 0 failed** |

**Red-then-green probe on the new rule (the round's decisive evidence).** With one bare `AddHttpClient("settings-api")` restored in `Assignments.Api`, the new rule **failed** — naming the file and the remedy: *"…registers the named HttpClient \"settings-api\" — an AppHost resource — with NO base address. Use AddCrossModuleHttpClient(\"settings-api\", \"https+http://settings-api\", …)"*. Two things make this more than a formality: the failure text states the blind spot it closes, and **the pre-existing literal-scan rule stayed green throughout the probe** — the blind spot is demonstrated, not merely asserted. Restored: 126/126 green, and the probe edit is fully reverted (`git diff` shows only the sanctioned line).

**Residual risks the worker named:** (i) no AppHost runtime smoke exists, so the fix is proved at registration/wiring level and by the guard only, not by a live call; (ii) the scanner deliberately ignores **const-named** registrations (`ConfigFeatureFlagService.HttpClientName` is a `const string` equal to `"settings-api"`) because symbol evaluation is not available in a source scan — that gap can only ever be a **false negative**, never a false positive, and that registration is already covered by the existing literal rule; (iii) one approved scope extension beyond the original allowlist (item 5).

## Review

Static diff reviewer (`ollama-cloud/kimi-k2.7-code`), read-only, patch passed by path. **Verdict: approve**, one P2 — applied, see the amendment.

| Question | Verdict | Evidence |
|---|---|---|
| Does the fix actually fix it? | **Yes** | `AddCrossModuleHttpClient` sets `client.BaseAddress = new Uri(baseAddress)` (`CrossModuleHttpClientExtensions.cs:62`); `Assignments.Api/Program.cs:150-153` keeps the bearer handler and the `AllowAutoRedirect=false` primary handler; exactly one registration of the name, no chain dropped. The Worker's two registrations get addresses with `propagateTenant: false` — correct, since that host has no inbound caller tenant to forward |
| Can it false-positive on correct code? | **Not today** | Non-resource names (`students-core-coded-values`, `url-fetcher`) are out of rule by construction — the rule keys off `ParseDeclaredResourceNames` |
| Can it pass on the defect? | **No** | The probe demonstrated the failure, and the const-named `ConfigFeatureFlagService.HttpClientName` gap is false-negative-only *and* covered by the pre-existing literal rule |
| Is non-vacuity meaningful? | **Yes** | The scan finds resource-named clients in four hosts (`Assignments.Api`, `Assignments.Worker`, `Students.Api`, `Students.Worker`), so green means "checked and correct", not "checked nothing" |
| Is the pin re-anchor faithful? | **Yes** | Same method name and intent (`TeacherScopeWiringArchitectureTests.cs:102-111`), now asserting the sanctioned spelling plus an explicit `NotContain` for the bearer handler; the doc-comment bullet matches |
| Is the refactor neutral? | **Yes** | `BaseAddressLiteralRegex` and `EnumerateScannedSourceFiles` extracted with the file set unchanged — `src/**/*.cs` minus `bin`/`obj`/`AppHost` |
| Scope | **Correctly bounded** | Only the three Assignments-context registrations changed in production code; the AppHost is untouched and already carried `.WithReference(settingsApi)` / `.WithReference(studentsApi)` on `assignments-worker` (`:396-397`); no new project, package or `.slnx` row |

**Finding 1 (P2, applied).** The statement slice ended at the next `;`, and `ReadCommentStripped` deliberately preserves quoted content — so a `;` inside a string literal, an interpolated string or a lambda body *before* the base-address literal would truncate the slice and make a **present** address read as missing: a false positive against correct code. Not live (no current registration has such a `;`), so the suite was green for the right reason — fragile, not broken.

## Acceptance

**Accepted 2026-10-06.**

| Evidence | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors (94 pre-existing warnings) |
| `tests/SchoolCollab.Core.Tests.Unit` | **126/126, 0 failed** |
| `tests/SchoolCollab.ArchitectureTests.Unit` | **96/96, 0 failed** |
| `tests/SchoolCollab.Assignments.Api.Tests.Unit` | **158/158, 0 failed** |
| **Red-then-green probe** (as delivered) | A restored bare `AddHttpClient("settings-api")` made the **new** rule fail, naming the file and the remedy — while the **pre-existing** literal-scan rule stayed **green throughout**. The blind spot is demonstrated, not asserted |
| **Red-then-green probe** (re-run after the amendment) | Identical failure (126 total / 1 failed, the new rule alone), then reverted → **126/0** and Architecture **96/0**; the probe left no trace in the working tree |
| Independent static diff review | **approve**; no P1 |

**Amendment (2026-10-06, pre-PR — no third review pass).** Finding 1 (P2) was applied after the review, using the repo's established precedent for a reviewer's pre-cleared smallest fix (adoption round, plan revision 2). `ScanNamedClientRegistrations` now slices each registration to the `;` that terminates it **at parenthesis/brace depth zero outside a string literal** (`StatementEnd`), so a `;` inside a lambda or interpolated string can no longer make a present base address read as missing. The reviewer's other suggestion — stop at the call's own `)` — was rejected deliberately: it would miss the legitimate chained `AddHttpClient(name).ConfigureHttpClient(c => c.BaseAddress = …)` spelling, i.e. the opposite failure. The rule's assertion, subject and messages are unchanged, and the probe was re-run because the slicing changed. `diffs-assignments-cross-module-base-addresses.patch` is **not** rewritten and remains the snapshot the reviewer approved.

**Residuals (accepted).** No AppHost runtime smoke exists, so the fix is proved at registration/wiring level and by the guard, not by a live call; and the scanner ignores **const-named** registrations, a gap that can only ever be a false negative and whose one instance is already covered by the pre-existing literal rule.

**Durable fold:** the failure mode and the guard are folded into `documents/solution/cross-module-http-client-pattern.md` (the pattern's own home), so this outcome survives the bulk-trash of `documents/rounds/`.

## UI Tester

N/A — no UI delivered.
