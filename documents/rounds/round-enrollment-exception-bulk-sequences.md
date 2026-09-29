# Round — enrollment exceptions: bulk sequence selection + a silent derived span (v6)

**Mode: Solo (owner instruction, 2026-09-29).** `AGENTS.md` scopes Solo to *"trivial, non-behavioural
work only"*, and this round is neither: it adds a command, a handler, a route, a repository method, a
client method, a page control change and 16 tests. The owner was offered **Tier 2 (light round)** with
a recommendation and chose Solo explicitly — so the deviation is recorded here rather than the work
being silently reclassified. The mitigations that come with the heavier tiers (an independent plan
review, a separate diff reviewer) did **not** run; what replaced them is an unusually dense test pass
plus a full-solution gate, and both are recorded below.

Spec of record: `documents/specs/subject-period-exception-model.md` — **v6**, decisions 17–19, §11.
Frozen patch: `documents/rounds/diffs-enrollment-exception-bulk-sequences.patch` — **56 files,
+7337/−779** (9,852 lines), covering the **code, spec and tests only**: round docs and run reports
are deliberately excluded, because a patch that contains the document describing it can never be
re-derived byte-identically. **It supersedes `diffs-enrollment-exceptions-page-redesign.patch`**,
which is now stale — neither round's work is committed, so any working-tree diff is cumulative over
v4→v6.

---

## 1. What the owner asked for

> "when not date range is selected, date span section should be hidden"

> "Also allow multiple sequence selection, for bulk insertion. Which is the best route for allow
> multiple sequence selection - keep multiple rows, or single row? My choice is single row, using
> json field or string concatenation. spec this if it involves more backend work"

Then, after the spec was written and the trade-offs laid out:

> "go with all 2 recommended. keep span fully silent"

> "go solo"

---

## 2. Decisions (locked by the owner 2026-09-29)

- **17 — the span is hidden when it is derived.** With a real part chosen and a period of that part
  actually holding the chosen sequence, the range inputs are **removed entirely** — no derived-span
  text either. The inputs stay whenever no period holds the sequence, because v5's documented "type
  the bounds yourself" flow must remain reachable and `CanAdd` requires a bound: hiding them there
  would **dead-end the form** with the button disabled forever. `Any date` always keeps the range.
- **18 — a multi-sequence selection writes ONE ROW PER SEQUENCE.** The owner's instinct about the
  *write* was right — a bulk insert should be one action — but "one row" is not what delivers that; a
  **bulk command** is. Rows and requests are independent axes.
- **19 — the two are coupled.** With several sequences chosen, whether the span is derived is decided
  **per sequence**, so the inputs appear whenever **any** chosen sequence has no period.

**Where the owner was advised against their stated choice, and the reasoning (recorded, not buried):**
the owner proposed a single row holding a JSON/string set. That was recommended against on five
grounds — it contradicts §2.4 ("two spans" for a gapped selection), it makes the existing duplicate key
`(owner, topic, division, span)` ambiguous, it turns the `/check` equality test into a containment
query, it needs a new edit path to remove one sequence, and it needs a migration. A delimited string is
strictly the worst of the three (no containment query, no integrity). The owner accepted route A.

**The cost the owner accepted explicitly** (recorded at §11.2 so it cannot later read as an oversight):
with the span silent, choosing *Term + 1st* writes that period's dates and **shows nothing**. Since the
ordinal is deliberately descriptive while the span is the truth (§2.3), the two are allowed to diverge
and the reader cannot see the recorded dates from the form — only from the list afterwards.

---

## 3. The one design change made during implementation

The spec's §11.5 originally said *"create accepts a set"* — i.e. reshape the existing single-item
route/command. Measuring it showed that would force **45 constructor call sites** in
`CreateSubjectEnrollmentExceptionHandlerTests` (42) and `ListGradeTopicAssignmentsCacheKeyTests` (3) to
be rewritten, because each site's arguments must be **split** between an outer record and a new
per-item record. That is a large mechanical diff over a carefully-reasoned test file, buying no
behaviour.

So the batch is a **new** `POST /enrollment-exceptions/bulk` (200 + `{ ids }`), matching the repo's own
bulk idiom (`POST /coded-values/bulk` → `BulkCreateCodedValues` → 200 + a result object), and the
single-item path is **untouched**. The UI still has **one** write path: the page posts to `/bulk` for
every add, one sequence or several. §11.5 was updated with this reasoning in the same round.

---

## 4. Implementation

### Backend

| Path | Change |
|---|---|
| `…/Commands/CreateSubjectEnrollmentExceptions/CreateSubjectEnrollmentExceptions.cs` | **new** — command + per-item record (each item carries its **own** span and ordinal) |
| `…/CreateSubjectEnrollmentExceptionsHandler.cs` | **new** — batch: validate per item, in-batch duplicate check, one `AddRangeAsync`, returns `Guid[]` |
| `I/SubjectEnrollmentExceptionRepository` + impl | `AddRangeAsync` — tracked once, saved **once** |
| `EnrollmentExceptionRoutes.cs` | `POST /enrollment-exceptions/bulk` |
| `StudentsApiClient.cs` | request records + `CreateSubjectEnrollmentExceptionsAsync` + `IdsResponse` |

Two things in the handler are load-bearing rather than defensive:

- **The intra-batch duplicate check.** EF's `AnyAsync` cannot see rows this transaction has not saved,
  so two items covering the same span would both pass the database pre-check and then collide as a raw
  `23505` at save time. The in-memory check turns that into the **409** the caller can act on.
- **`AddRangeAsync` rather than a loop.** `RepositoryBase.AddAsync` saves per call, so looping would be
  N transactions; one save is what makes the batch all-or-nothing. This mirrors
  `CodedValueRepository.AddRangeAsync`.

### Page

Position became a row of **`FluentCheckbox`** in a native `FluentStack` (`Width=""` to disable the
Stack's default `width:100%`, which is a circular percentage inside a content-sized cell), grouped by
`role="group" aria-label="Position"`. `BuildItems()` turns the selection into one item per sequence —
each span taken from its own period, falling back to the typed bounds when it has none — and
`SpanIsDerived` drives the range's presence. `CanAdd` now requires **every** item to carry a bound.

---

## 5. Two real bugs the suite caught before this could ship

1. **`FluentCheckbox.CheckState` throws.** Its setter raises unless `ThreeState` is true — it is the
   *three-state* parameter. It was chosen because the package XML documents `CheckState` /
   `CheckStateChanged`, but the class is `FluentCheckbox : FluentInputBase<bool>`, so the real
   two-state pair (`Value` / `ValueChanged`) is declared on the **base type** and could never appear in
   a `P:FluentCheckbox.*` query. Caught by `QueryString_PreSelectsGroupOwner`, which failed with an
   unhandled render exception rather than an assertion mismatch. Settled by reading
   `FluentCheckbox.razor.cs` @ v4.14.2 — the same "grill the source, not the docs" rule that had
   already corrected `FluentStack`.
2. **The test harness's canned POST response was registered against the old URL.** Once the page
   started posting to `/bulk`, no mapping matched — so there was no 2xx, no list re-read and no server
   sentence. Three failures traced to that single line, plus a fourth from a test's own second
   `MapDynamic` registration of the same old URL.

---

## 6. Test fates (nothing deleted; every touched test named)

The page suite went **14 failures → 0**, and the cascade is worth recording because the causes were
much fewer than the symptoms:

| Cause | Failures |
|---|---|
| Three shared helpers still driving the old radio group (`PositionGroup`, `PositionLabels`, `ChoosePositionAsync`) | 3 |
| `c.Url == ExceptionsUrl` — **exact** equality, replicated 5× | 5 |
| The harness's canned POST registered on the old URL | 3 |
| A test's own `MapDynamic` registering the old URL a second time | 1 |
| Two tests asserting v5's "the chosen position fills the range", which v6 makes silent | 2 |

**Changed, not removed:** `PositionGroup` → `PositionBoxes`; `PositionLabels` reads `fluent-checkbox`;
`ChoosePositionAsync` gained an `UntickPositionAsync` sibling (a checkbox can be un-picked, which the
radio could not); four `fluent-radio` emptiness assertions became `fluent-checkbox`; five POST URL
assertions became `/bulk`; `OpenEnds_…` and `ChangingOwner_…` and `Positions_TwoYears…` had their
"range is filled" assertions moved to where the derived span is **written**;
`PositionWithNoMatchingPeriod…` became the multi-select scenario (ticking 3 now *adds* to the selection
rather than replacing it) and carries the assertion that **both** ordinals travel; `FailedAdd_…` asserts
the surviving **choice** rather than a surviving typed range.

**Added:**

| Suite | Tests |
|---|---|
| `CreateSubjectEnrollmentExceptionsHandlerTests` (**new**) | 13 — N rows in one transaction; intra-batch duplicate → 409 with nothing written; a valid first item does not survive a bad second; the ordinal is not part of the duplicate key; empty batch, no-bound item, ordinal on a free window, undefined division, both owners, unknown topic → 422/404; the group window checked **per item** |
| `EnrollmentExceptionAvailabilityEndpointTests` (+3) | two sequences persist two rows with their own spans; an intra-batch duplicate is a 409 with **no partial write**; a re-posted batch is a 409 and adds no rows |

---

## 7. Verification (parent-run, in-session)

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| **Full solution** | **2975 total / 2972 succeeded / 3 failed / 0 skipped** |
| `Admin.Tests.Unit` | **595 / 595** (was 14 failing) |
| `Students.Tests.Unit` | **539 / 539** (526 → +13) |
| `Students.Tests.Integration` | **86 / 86** (83 → +3, on real Postgres via Testcontainers) |
| `ArchitectureTests.Unit` | **72 / 72** |

Baseline was 2959/2956/3, so this round adds **+16 tests with the same 3 failures** — the pre-existing
environmental `ChatAsync_WithOpenRouter_*` live-HTTP tests, unrelated to this work.

**Not verified, unchanged from the previous round: there is no browser here.** Every layout claim (a
row of checkboxes, the span vanishing, labels beneath inputs) is static: the build validates the
FluentUI API surface, bUnit validates DOM structure, and Testcontainers validates the writes. None of
those validates rendered geometry. §11's original "no browser pass" caveat still stands for the whole
feature.

---

## 8. Spec sync performed in this round

- Decisions 17–19 and the §11 status moved from PROPOSED/LOCKED-but-outstanding to **IMPLEMENTED**.
- §5.1 rewritten for the multi-select and the conditional range.
- **§11.3 correction.** Route A had been justified partly by *"rendering a contiguous run of ordinals
  as a range ('1st–4th') in the list"*. **The list never rendered ordinals** — it shows part + span +
  reason. That justification is withdrawn as untrue; route A stands on the schema, key, `/check`,
  delete-granularity and atomicity arguments alone.
- **§11.6 correction.** The plan predicted a same-span batch would collide on the duplicate key
  (`23505`). It cannot: the in-batch check catches it first, so it is the same **409** as every other
  duplicate. Asserted on real Postgres, including that the valid first item leaves no row behind.

---

## 9. Residual risks and open items

- **No browser pass** (above) — the one verification this round cannot supply.
- **The accepted silence cost** (§2): a derived span is written but never shown before the write.
- **A `sequence` with no period behind it is only reachable via the guard.** If a future change hides
  the range unconditionally, the form dead-ends exactly as §11.2 describes. The guard is asserted, but
  it is a rule a later edit could remove without any test failing for the wrong reason — worth a
  comment at the site (it has one).
- **Product choices still open** from the previous round: an auto-split year and the atomic
  year+sub-periods create leave sub-periods unpositioned (a legal NULL);
  `SubPeriodDefinitionRequest` carries no `Sequence`.
- **P2-1 still open**: no database-level `CHECK` on `sequence`/`ordinal`, so the migration is the only
  writer that bypasses the entity validators.

## 10. Delivery

Not committed at the time of writing. The intended delivery is a **`gh stack`** submission as layer 3
of the existing train — base `stack/2-subject-specs` (PR #267), following
`stack/1-subject-authoring` (#266) — which the owner instructed explicitly, rather than a standalone
`gh pr create`. The commit/push gates in `AGENTS.md` apply unchanged: the change set is to be shown
and confirmed first.
