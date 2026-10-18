# Round — codedvalue-test-pruning

**Status:** CLOSED — verified, **uncommitted**
**Tier:** 1 (light, solo — parent ran plan, edits and its own check; no child dispatched, so no model was inherited: the session model executed).
**Round base:** `8c135e57` + the four typed-response commits (PR #364 open, uncommitted on this branch).
**Trigger:** owner grill round 6 ("all as recommended"): Q23–Q28.

## Plan

Prune the CodedValue test surface *without* weakening signal. No production code changes.

| Q | Decision |
|---|---|
| Q23 | Collapse the Admin/AI duplication in `CodedValuesApiClientNotFoundTests` |
| Q24 | Keep the route's blank-search guard, cut the handler's 3 tests to 1, and add the missing **route** test |
| Q25 | Merge the chat-mirror files **only if** they are exact duplicates |
| Q26 | Keep the status-only integration tests (no prune) |
| Q27 | No duplicate-prevention guard |
| Q28 | This round, Tier 1 |

## Worker Report (parent-run)

**Q23 — `tests/SchoolCollab.Settings.Tests.Unit/CodedValuesApiClientNotFoundTests.cs`: 12 → 7 tests.**
One behaviour per test with both clients asserted inside it (`GetByCode`/`GetById` × 404/200 + `GetByCode` on 500),
plus the two Admin-only surfaces (`GetChildrenByParentCode` endpoint, `UpsertOverride` body). All twelve original
assertions survive.
*Deviation:* implemented as "both clients per test" rather than `[DataRow]` cases — the two clients have **different DTO
types** (`Admin.Shared.Services.CodedValueDto` vs `AI.Tools.CodedValues.CodedValueDto`), so a `DataRow` needs a factory
switch that obscures more than the rows save. Same coverage, less indirection; recorded here rather than done silently.

**Q24 — `tests/SchoolCollab.Settings.Tests.Unit/Handlers/SearchCodedValuesHandlerTests.cs`: 3 → 1 test.**
`HandleAsync_BlankSearchText_ReturnsEmptyArray` keeps the strongest representative (`"   "` — `IsNullOrWhiteSpace`
covers `""`, `"   "` and `null`). `SearchCodedValues` is constructed only by the route and by these tests, so the
handler guard is defence-in-depth that no HTTP path can reach.

**New — `tests/SchoolCollab.Settings.Api.Tests.Unit/CodedValueQueryTests.cs` (2 tests).** The contract the pruned
handler tests were standing in for:
- `Search_WithBlankText_Returns200EmptyArrayWithoutCallingTheHandler` — `?text=`, `?text=%20%20`, `?text=%20` → `200 []`,
  with a stub that **throws if invoked**, so a route that lost its guard fails instead of quietly passing.
- `Search_WithoutTheRequiredTextParameter_Returns400` — **found while writing the first test**: omitting `text` entirely
  is a 400 (the parameter is non-nullable in the route signature and binding rejects it before the short-circuit), not a
  200 empty array. Pinned so a later change to `string? text` is deliberate.

**Q25 — verified, no merge.** `Components/CodedValuesIndexChatMirrorTests` renders `CodedValuesPageHost` (whole page,
inline chat → drawer wiring); `Components/CodedValuesChatPanelTests` renders the drawer/panel component against the hub.
Different levels, not exact duplicates — merging would hide which level regressed.

**Q26 / Q27 — no action**, as decided.

## Acceptance

| Project | Before | After |
|---|---|---|
| `Settings.Tests.Unit` | 592 | **585** (−7: −5 client, −2 handler) |
| `Settings.Api.Tests.Unit` | 29 | **31** (+2 route-contract) |
| `ArchitectureTests.Unit` | 126 | 126 (unchanged) |
| `dotnet build SchoolCollab.slnx` | — | **0 errors** |

Net **−5 test methods, coverage unchanged or stronger**: the blank-search contract is now asserted at the HTTP boundary
(the layer that owns it) instead of three times inside the handler, and one undocumented behaviour (missing `text` → 400)
is now pinned.

Critically, the *first* draft of the new query test failed (`expected 200, found 400`) — the test caught a real contract
detail before it could become a false assumption in the suite. That failure is the reason the round has two tests instead
of one.

Files touched (all tests):
- `tests/SchoolCollab.Settings.Tests.Unit/CodedValuesApiClientNotFoundTests.cs` (rewritten, 12 → 7)
- `tests/SchoolCollab.Settings.Tests.Unit/Handlers/SearchCodedValuesHandlerTests.cs` (3 → 1)
- `tests/SchoolCollab.Settings.Api.Tests.Unit/CodedValueQueryTests.cs` (new, 2)

**Residual:** uncommitted. PR #364 (typed responses) is also open and uncommitted-to-merge; these test edits belong with it
or in a follow-up commit on the same branch — owner's call.
