# Round — enrollment exceptions: the span belongs to the free window (v7)

**Mode: Solo (owner instruction, 2026-09-29).** `AGENTS.md` scopes Solo to *"trivial,
non-behavioural work only"*, and this round is a deliberate behavioural change to a shipped page.
The owner was offered the tiered modes and chose **Solo** explicitly, so the deviation is recorded
here rather than the work being silently reclassified. The mitigations that come with the heavier
tiers (an independent plan review, a separate diff reviewer) did **not** run; what replaced them is
a dense page-test pass plus a full-solution gate, both recorded below.

Spec of record: `documents/specs/subject-period-exception-model.md` — **v7**, decisions 20–21, §11.7.
Frozen patch: `documents/rounds/diffs-enrollment-exception-span-hidden.patch` — code, spec and tests
only; round docs are deliberately excluded, because a patch that contains the document describing it
can never be re-derived byte-identically.

---

## 1. What the owner asked for

> "Position is showing as spec intended, but date range must hide when position shows"

Asked to settle the consequence (a position with no matching period could no longer be given typed
bounds), the owner chose option **(a)**: render the structural ladder as now, but **disable** the
positions the tenant has no period for.

> "agreed. go solo"

## 2. The decision

- **Decision 20 — the Position row and the range are mutually exclusive.** The "Not offered from /
  to" range belongs to **`Any date` alone**. With a real part (`Term` / `Semester`) chosen the range
  is removed entirely — v6 decision 17 hid it only when every chosen sequence resolved to a period;
  v7 hides it **unconditionally**. v6's silence (no derived-span text) is kept.
- **Decision 21 — an unbacked position is DISABLED, not hidden.** The ladder keeps its structural
  shape (Q5: 1st–4th, extended by any higher declared position), but a position with no period
  behind it renders disabled. This is what makes decision 20 safe: every *selectable* position is
  backed by a period, so `CanAdd` is satisfied on the choice alone and v6's dead-end guard is
  designed out rather than handled.

**The cost, accepted explicitly and recorded:** the Q1 flow — *"record a 3rd term before the calendar
is built and type its dates"* (spec §0 decision 15, 2026-09-28) — is no longer reachable. A position
the tenant has not periodised cannot be written at all from this page. This is an owner-accepted
reversal, not an oversight.

## 3. Implementation (`EnrollmentExceptions.razor`)

| Change | Detail |
|---|---|
| `SpanIsDerived` | now just `Division != AcademicYearDivision.None` (was: every chosen position resolves to a period) |
| `PositionIsDerivable(position)` | **new** — `PeriodForPosition(Division, position) is not null`; drives each checkbox's `Disabled` |
| Position checkboxes | gained `Disabled="@(!PositionIsDerivable(position))"` |
| Pre-write note | **new** third branch in the notes block: a real part with no position ticked renders *"Choose a position to add an exception."* (polite live region) so the disabled button is explained |
| `BuildItems()` / `CanAdd` | unchanged in shape — a real part with no position produces an unbounded item, which `CanAdd` rejects; every selectable position derives its span |

No backend, domain, API or migration change. `BuildItems` still writes one item per chosen sequence
through the existing `POST /enrollment-exceptions/bulk`.

## 4. Tests (`EnrollmentExceptionsPageTests.cs`)

| Test | Fate |
|---|---|
| `PositionsWithoutAPeriod_AreDisabled_AndTheRangeStaysHidden` | **new name** — replaces v6's `PositionWithNoMatchingPeriod_LeavesTheRangeEmpty_AndTakesTheTypedDates`; keeps multi-select coverage via the fixture's two backed positions (1st + 5th), plus an un-tick step |
| `OpenEnds_ReadAsAnyStartAndAnyDate_AndGateTheWrite` | rewritten — open ends and the write gate now live on `Any date`; a real part hides the range and the 4th term (no period) is asserted disabled |
| `ChosenPart_IsAControl_OfferedOnlyWhereTheTenantHasPeriods` | re-targeted — label-below row count for a real part is **3** (Fill from / Position / Reason), not 5 |
| `Positions_AreOneToFourPlusAnyHigherDeclared_NotOnlyTheExistingPeriods` | re-targeted — labels unchanged (ladder shape), plus a `Disabled` mask `[false,true,true,true,false]` |
| `FailedAdd_…`, `ChangingOwner_…`, `PickersCheck_…`, `Positions_TwoYears…`, `Add_…`, `ScopedEmptyState_…` | **unchanged** — every one already drives a backed position or lands on `Any date` |

No test deleted.

## 5. Verification (parent-run, in-session)

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (101 warnings, all pre-existing) |
| `Admin.Tests.Unit` — `EnrollmentExceptionsPageTests` | **33 / 33** |
| `Admin.Tests.Unit` — full project | **595 / 595** — one run initially failed `EnrollStudentDialogBunitTests.Grade5Selected_StreamPickerLoads_NonNullStreamGuids`; it passes in isolation (15/15) and the full project passes 595/595 on re-run, i.e. a pre-existing ordering flake in an unrelated dialog test |
| `Admin.Tests.Unit` — full project, 2nd run | **595 / 595** (flake did not reproduce) |
| **Full solution** — `dotnet test SchoolCollab.slnx` | **2975 total / 2972 succeeded / 3 failed / 0 skipped** — the SAME 3 failures as the recorded baseline: the pre-existing environmental `CodedValueAIServiceLiveTests.ChatAsync_WithOpenRouter_*` live-HTTP tests (Settings.Tests.Integration), untouched by this round. `Students.Tests.Integration` **86 / 86** on real Postgres (Testcontainers); `ArchitectureTests.Unit` **72 / 72** |

**Not verified: there is no browser here.** Every layout claim (a row of checkboxes, the range
vanishing, the disabled boxes) is static — the build validates the FluentUI surface, bUnit validates
DOM structure. Rendered geometry is not verified.

## 6. Residual risks and open items

- **No independent review** (Solo, above) — the design was pressure-tested with the owner in one
  question round; the code has no second reader.
- **The retired Q1 flow** (above) — a position with no period cannot be written from the page.
- **No browser pass** — the one verification this round cannot supply.
