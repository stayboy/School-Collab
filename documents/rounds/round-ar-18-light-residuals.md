# Round ar-18 — light residuals (enum wire + failures count + EditorRequired)

Provider: pi **ollama** profile — worker `ollama-cloud/deepseek-v4.1-flash`, reviewer `ollama-cloud/glm-5.3-flash` (light-mode set, owner 2026-09-16: reviewer must not share the worker's model). **Tier 2 light round**, base `c8c0b077` (= merged ar-17; tree clean at round start). *Tier deviation recorded:* the deterministic UI trigger nominally fires (L2 touches `Detail.razor` / the section), but the owner explicitly chose the light tier for this scope on 2026-09-17; no UI-tester pass — the only UI surface is a tab label string, covered by bUnit.

## Plan

Goal: land the three light residuals carried from ar-16/ar-17 before E3.

### L1 — API-side string-enum registration (the real ar-17 residual ②)

`src/Assignments/SchoolCollab.Assignments.Api/Program.cs` registers **10** sibling
`JsonStringEnumConverter<T>`s but **neither** `NotificationKindDto` nor
`ContactChannelDto`, so those two delivery enums serialize as **numbers** on the
wire. The ar-17 client converter is read-side tolerance only — this closes the
real inconsistency.

- Add `JsonStringEnumConverter<NotificationKindDto>` and
  `JsonStringEnumConverter<ContactChannelDto>` alongside the existing
  registrations (same block, ~line 21–47).
- Add a **discriminating** test in `tests/SchoolCollab.Assignments.Api.Tests.Unit/`
  that fails without the two registrations and passes with them. Follow the
  project's established pattern for asserting API JSON (endpoint-level test if
  one exists for the failures endpoint; otherwise the closest established
  pattern). Pin the actual emitted casing (whatever the sibling enums produce —
  do not invent camelCase if siblings emit PascalCase).
- Safe because no consumer predates ar-16 and the client already tolerates strings.

### L2 — F5: `Failures (N)` tab count (owner-deferred in ar-17, now in scope)

- `NotificationFailuresSection.razor`: add
  `[Parameter] public EventCallback<int> <count-callback> { get; set; }` — raise it
  **once per successful load** with the number of failure rows (including 0). Do
  **not** raise it when the load errors (the label must not claim a count the UI
  cannot see). XML-doc the parameter.
- `Detail.razor`: hold `private int _notificationFailureCount;`, wire the callback,
  and render the tab label as `Notifications (N)` when N > 0, else `Notifications`
  (line ~342). No Detail-side fetch — the count travels on the callback (the
  re-verify proved that shape works).
- bUnit tests: the section raises the callback with 2 for two seeded rows and with
  0 for an empty list; a Detail test where the (method-specific — see pitfalls)
  failures GET returns two rows asserts the label reads `Notifications (2)`, and
  the existing empty/visibility tests keep asserting plain `Notifications`.

### L3 — F10/F11 nits: `[EditorRequired]` on the section's parameters

`[Parameter, EditorRequired]` on `AssignmentId` (line ~79) and `CanHaveFailures`
(line ~87) in `NotificationFailuresSection.razor`.

### Expected files (≤7)

1. `src/Assignments/SchoolCollab.Assignments.Api/Program.cs`
2. `tests/SchoolCollab.Assignments.Api.Tests.Unit/` — new or existing test file
3. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/NotificationFailuresSection.razor`
4. `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Detail.razor`
5. `tests/SchoolCollab.Assignments.Tests.Unit/NotificationFailuresSectionBunitTests.cs`
6. `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentDetailBunitTests.cs`

### Acceptance criteria

- `dotnet build SchoolCollab.sln` — 0 errors.
- Affected suites 0 failures: `Assignments` (≥635), `Assignments.Api` (≥78),
  `ArchitectureTests` (21), plus `Core` (84) since L1's DTOs live in Contracts/Core
  surfaces.
- L1's test is discriminating (fails without the registrations).
- L2's tests pin: callback N=2 / N=0; label `Notifications (2)` / plain `Notifications`.
- No contract-shape change beyond enum casing; no files beyond the expected list
  (unrelated reformatting = a finding).

## Worker Report

Run `93cb4bca` (`ollama-cloud/deepseek-v4.1-flash`). **6 files, +209/−6, all within the plan's expected list** — Program.cs +7, Detail.razor +13/−2, NotificationFailuresSection.razor +10/−2, new `ProgramJsonEnumConverterTests.cs` +96, AssignmentDetailBunitTests.cs +25/−2, NotificationFailuresSectionBunitTests.cs +58. **Build 0 errors; 639/0 (was 635, +4), Api 80/0 (was 78, +2), Architecture 21/0.**

Key decisions (all within plan): L1's discriminating half is a **source scan** of the `ConfigureHttpJsonOptions` block plus a wire-shape pin (`"kind":"Publish"` PascalCase names, camelCase properties; asserts `"kind":\d`/`"channel":\d` never match) — because every existing host test builds its **own** minimal `TestServer` and registers converters itself (which is exactly how the ar-15/ar-17 gap hid), and `WebApplicationFactory<Program>` boots Postgres/RabbitMQ/Redis, absent from unit CI. L2: callback named `OnFailureCountLoaded` (repo convention), `await InvokeAsync(_failures.Count)` **inside the success path only**, zero reported (reported-0 vs never-reported is pinned by its own test); label extracted to a `NotificationsTabLabel` computed property; Detail issues no extra fetch. Fixture helpers extended (`_apiJsonOptions` + the two converters, `SetupGetAssignment(params NotificationFailureDto[])`) because MockHttp is first-match-wins and a test-local re-registration would have been shadowed.

**Discrimination proven by revert:** removing both converter registrations → Api suite `80 / 1` (`HostOptions_RegisterBothDeliveryEnumsAsStrings` failing on the missing-string assertion), restored byte-identical → `80 / 0`.

## Review

Run `73fceb52` (`ollama-cloud/glm-5.3-flash`, static diff-only over the frozen patch). **Verdict: ACCEPT — 0 P1, 2 P2.** Scope PASS (exactly the 6 expected files, counts match). All four focus areas verified: (a) the L1 source scan reads `Program.cs` **from source** (5 levels up from the test assembly; never `bin/`, which cannot fool it) and requires both registrations *inside* the `ConfigureHttpJsonOptions` block; (b) the PascalCase/camelCase wire pin is consistent with `JsonSerializerDefaults.Web` + `JsonStringEnumConverter<T>` with no naming policy, and the negative regexes target the right shapes; (c) the callback sits after both tasks resolve and before the catches, `NotificationsTabLabel` handles never-raised → plain `Notifications`, and both behaviours are pinned; (d) no MockHttp shadowing — the fixture *modifies* the existing failure-GET payload rather than re-registering the URL. Best-practices check clean (no CPM `Version`, no MediatR, no inline routes, XML docs present).

**P2-1 (accepted, errs safe):** the L1 scan's block slicing (`IndexOf` method name + first `\n});` terminator) is delimiter-brittle against future nested lambdas or a repeated method-name comment — but both failure modes make the test **fail**, never false-pass, and the revert proof demonstrates discrimination today. **P2-2 (pre-existing, out of scope):** `AssignmentId` lacks an XML doc (unchanged by this diff).

## Acceptance

**Verdict: CLOSED** (parent adjudication, Tier 2). On two independent confirmations — the worker's numbers matched by the parent's own authoritative pass, and a 0-P1 static ACCEPT:

| Criterion | Result |
|---|---|
| `dotnet build SchoolCollab.sln` | **0 errors** |
| Assignments / Api / Architecture / Core | **639/0 · 80/0 · 21/0 · 84/0** (+6 tests vs ar-17's 635/78/21/84) |
| L1 discriminating | **proven by revert** (worker: 80/1 → restored → 80/0) + reviewer confirmed the scan reads source, not `bin/` |
| L2 label/callback pinned | 2-row → `Notifications (2)`, empty → reported-0, error → never-raised → plain `Notifications` |
| L3 `[EditorRequired]` | both parameters |
| Scope | exactly the 6 expected files, no reformatting |

P2-1 (delimiter-brittle block slice, errs safe) and P2-2 (pre-existing `AssignmentId` doc gap) are accepted, not fixed — the first fails loudly if it ever bites, the second predates the round. Patch: `documents/rounds/diffs-ar-18-light-residuals.patch` (6 files, +209/−6, drift-free). Residuals now carried to **E3**: the `(TenantId, AssignmentId, RecipientId, Kind)` unique index + F1 duplicate-row class, F7's two extra HTTP calls per Detail view, F9 ward-in-DTO.
