# Round — authoring-residuals-closeout

Provider: pi/ollama-cloud · **Tier 2 (Light — worker + static diff-only reviewer)** · mixed test-only + UI
Models: parent (plan owner) · worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code`
Branch: `stack/6-authoring-residuals-mopup` (layer 2 of the stack/5 train)
**Diff base for this round: `2505a213`** — a `git stash create` snapshot of the working tree taken after round
`authoring-residuals-mopup` was accepted. That round's changes are still **uncommitted** (the owner has held all
git actions), so this round's patch must be isolated against the snapshot rather than against `fcfdc217`.
Upstream base of the branch is still `fcfdc217` (= `stack/5` tip, PR #292, unmerged).

## Plan

### Goal

Close the last two **closable** rows of §17 in `documents/specs/assignment-authoring-compartments.md`:
- **(A)** `P2-c — transaction rollback is unproven` (§17 `:438`): prove, on real Postgres, that
  `LinkAssignmentGroupsHandler`'s transaction actually rolls back when the second write faults.
- **(B)** `UX-7 / F17 defence-in-depth` (§17 `:441`): give the reload path its own dirty gate instead of relying
  solely on the upstream location-changing handler.

### Settled decisions

- **D1 — (A) is a test-only change.** No production code. The assertion is what matters, not the fixture.
- **D2 — (B) semantics: the reload still PROCEEDS when dirty; the DISCARD is surfaced, never silent.**
  This is the load-bearing decision, so the reasoning is explicit. §17 words the gap as "`OnParametersSetAsync`
  does not itself refuse a reload while the form is dirty", but literally *refusing* the reload would keep the
  previous assignment's loaded state on screen under the new assignment's URL — reintroducing exactly the
  stale-instance write path that F17 was fixed to remove, and swapping a **silent discard** for a **silent
  mis-attribution**. Neither is acceptable, and they are not equal: mis-attribution can write the wrong content
  to the wrong assignment, whereas a discard loses work the author can retype. So the reload proceeds and the
  discard is **announced**.
  - The already-guarded path must not nag: when the author explicitly chose "discard" in the in-app
    confirmation, that navigation is *consented*, so the reload records consent and suppresses the notice.
  - The unguarded path — a host that swaps `Id` without a `NavigationManager` navigation — is the case §17
    describes; there the notice **is** shown.
- **D3 — no new public API.** Unlike the previous round's `ContentModules` carrier, both items are closable
  without adding one; if a design seems to need one, stop and report.
- **D4 — §17 is edited in the same change**: (A) and (B) rows struck **naming this round**. The
  archived-group *route* half and the read-only-View entry stay untouched.

### Items

**(A) P2-c — rollback proof on real Postgres.**

Add `tests/SchoolCollab.Assignments.Tests.Integration/LinkAssignmentGroupsRollbackPostgresTests.cs`, following
the existing conventions in that project (`AssignmentsPostgres` shared container;
`AssignmentsDbFactory.CreateDatabaseAsync` + `CreateContext(connectionString, tenantId:)`; `Database.MigrateAsync()`;
MSTest + FluentAssertions — see `NotificationLogUniqueIndexTests` as the shape to copy).

The test must:
1. Create and migrate a per-test database; seed an assignment in a known persisted state.
2. Run the **real** `LinkAssignmentGroupsHandler` against real Postgres, with the **second** write
   (`IActivityGroupLinkRepository.ReplaceForAssignmentAsync`) faulted so it throws after the **first** write
   (`IAssignmentRepository.UpdateAsync`) has already executed inside the transaction. A decorator/test double
   around the real repository, or a `DbCommandInterceptor` faulting the link-table statement, are both
   acceptable — pick whichever keeps the real `AssignmentsDbContext` and the real transaction in play.
3. Assert **both**: (i) the first write was rolled back — the assignment row in a **fresh context** matches its
   pre-handler state exactly; (ii) no link rows were written; and (iii) the fault propagates (the handler does
   not swallow it).

**Discriminating requirement:** the test must **fail if the transaction is removed** (i.e. if
`BeginTransactionAsync`/`CommitAsync` are deleted, the first write would persist and the assertion on the
rolled-back assignment must catch it). State explicitly in the test's comment and in the report that you
verified this — a rollback test that passes without a transaction proves nothing.

**(B) UX-7 / F17 defence-in-depth — the reload path's own dirty gate.**

In `AssignmentAuthoring.razor`:
1. Record consent where the in-app confirmation's **discard** path is taken, so a consented navigation is
   distinguishable from an unguarded parameter swap.
2. In the parameter-change reload path (`OnParametersSetAsync` → `LoadAsync`), when `Id` or `Mode` **changed** and
   the form is **dirty** and the change was **not** consented: proceed with the reload (per D2) but record that
   unsaved edits were discarded, and render a visible notice (reuse the existing `authoring-reason` idiom).
   Clear the flag on a subsequent successful load/save so it cannot linger.
3. Do **not** change the F17 gate's "only when `Id`/`Mode` changed" condition, and do not add an unconditional
   reload — the reload must still never re-enter on a re-set of the same values (that is an infinite-loop and
   in-flight-edit hazard).

### Expected files (bound the diff)

| File | Change |
|---|---|
| `tests/SchoolCollab.Assignments.Tests.Integration/LinkAssignmentGroupsRollbackPostgresTests.cs` | **new** — (A) |
| `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor` | (B) consent + notice |
| `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentAuthoringBunitTests.cs` | (B) tests |
| `documents/specs/assignment-authoring-compartments.md` | (D4) strike the two closed rows |

**Explicitly out of scope:** no production change for (A); no route/contract/migration/CPM change; no FluentUI
upgrade; no new public API member (D3); no touching of the archived-group *route* half or the read-only-View
entry; no R3/AI work.

### Acceptance criteria

1. **(A)** the rollback test exists, passes on real Postgres, and **fails when the transaction is removed** —
   demonstrate that by temporarily deleting `BeginTransactionAsync`/`CommitAsync`, recording the failure, then
   restoring them byte-for-byte.
2. **(A)** it asserts the **three** things above (assignment rolled back, no links written, fault propagates),
   not merely "an exception was thrown".
3. **(B)** an unguarded `Id` change on a **dirty** form reloads the new assignment **and** surfaces the discard
   notice — one test each for "notice shown" and "no notice when clean".
4. **(B)** a **consented** discard (the in-app confirmation's discard path) does **not** show the notice.
5. **(B)** a re-set of the *same* `Id`/`Mode` does not reload at all (the existing
   `SameParametersReset_DoesNotReloadTheAssignment` anchor must still pass) — no infinite loop, no in-flight-edit
   loss.

### Reviewer acceptance criteria

- **(B) must not make the discard any quieter, and must not make it any louder than the truth**: exactly one
  notice, only when work was actually discarded, never on a consented discard, and never on a clean form.
- The consent flag must not be able to leak across loads in a way that suppresses a notice the author should
  have seen (the failure to avoid is a *silent* discard).
- `OperationCanceledException` and error posture on the reload path must be unchanged; nothing may be cleared
  before a read succeeds.
- **(A)** must exercise the **real** transaction over a real Postgres connection — not an InMemory provider, and
  not a mocked `Database` — or it proves nothing.
- Conventions: multi-select stays `SelectedOptions`/`SelectedOptionsChanged` (never `@bind-SelectedValues`); no
  inline `<style>`; isolated `.razor.css`; no unrelated overwrites.

## Worker Report

**Run `d3f892cb` (`deepseek-v4.1-flash`) — work completed, delivery failed.** The async runner process again died
*after* the worker had written its report, so the harness marked the run failed on delivery. The parent recovered
the report from the run directory rather than re-dispatching, and verified the artifact independently (build · unit
suites · **the real-Postgres Integration suite**).

Diff isolated against snapshot `2505a213` (the previous round's changes are uncommitted and untouched): **4 files,
+499 / −7**.

| File | Change |
|---|---|
| `tests/…Tests.Integration/LinkAssignmentGroupsRollbackPostgresTests.cs` | **new** — (A) the rollback proof |
| `…/Pages/Assignments/AssignmentAuthoring.razor` | (B) consent record + unconsented-discard notice (+82/−7) |
| `tests/…Assignments.Tests.Unit/AssignmentAuthoringBunitTests.cs` | (B) 4 tests (+160) |
| `documents/specs/assignment-authoring-compartments.md` | (C/D4) two rows struck, closure note names this round |

**(A) — test-only, real transaction.** Seeds a **stream**-targeted assignment (deliberately not a grade target:
D-1's derivation prefers `GradeLevel`, so a grade seed would leave the compat column unchanged), runs the **real**
`LinkAssignmentGroupsHandler` over the real `AssignmentsDbContext` (real `AssignmentRepository` +
`AssignmentActivityGroupRepository`), and faults only the **second** write through a decorator whose
`ReplaceForAssignmentAsync` first *reads the handler's own connection* to prove the first write was already inside
the open transaction before it throws. Assertions from a **fresh** context: (i) the assignment record
(`TargetAudienceType`, `UpdatedAt`) + target rows equal the pre-handler state exactly, (ii) 0 link rows, (iii) the
fault reaches the caller as the same exception instance — plus the ordering guard on the in-transaction read.

**(B) — the reload path's own dirty gate.** `_discardConsented` is set in `OnLocationChangingAsync` only on the
confirm-discard path and consumed at the top of `OnParametersSetAsync` (every parameter set, so it cannot leak past
a consented navigation that reached no change); dirty is sampled **before** `LoadAsync` (afterwards there is nothing
to compare); the reload **still proceeds** per D2; `_unsavedEditsDiscarded` is set after the load and rendered as
`<p class="authoring-reason" id="authoring-unsaved-edits-discarded">` beside the error bar; it is retired in
`CaptureDirtyBaseline()` (the end of a load, and the component's own exit after a save) gated on `_error is null`.
The F17 "only when `Id`/`Mode` CHANGED" condition is byte-for-byte unchanged.

**(C) — §17.** Both rows removed and named in a new closure paragraph; the archived-group *route* half and the
read-only-View entry untouched.

### Discrimination evidence
- **(A) transaction removed** (`BeginTransactionAsync`/`CommitAsync` deleted, then restored byte-for-byte —
  sha256 `1597c562…` identical before/after, `git status` clean for that file):
  `failed SecondWriteFault_RollsBackTheFirstWrite_OnRealPostgres` — expected the `Mixed`/`SelectedGroups` compat state,
  found the surviving group child row + stamped `UpdatedAt`, at the `after.Should().Be(before)` line — and the
  `probe.TargetRowsVisibleAtFault` guard passed in that run, so the fault genuinely landed *after* the first write.
  Restored → green.
- **(B) notice suppressed** (probe: drop `_unsavedEditsDiscarded = true`):
  `failed UnsavedEdits_UnconsentedIdChange_ReloadsAndAnnouncesTheDiscard` ("Expected notices to contain a single
  item … but the collection is empty") and `failed ConsentThatReachedNoParameterChange_DoesNotSilenceALaterUnguardedDiscard`;
  the two fail-safe anchors stayed green.
- **(B) consent gate removed** (probe: `IsDirty && !consented` → `IsDirty`): failed
  `UnsavedEdits_ConsentedDiscard_IdChange_ReloadsWithoutTheDiscardNotice` **only**. Both probes reverted
  (sha256 `fd90d107…` identical to the pre-probe file).

Parent-verified: build **0 errors** · Assignments **824/0** (4 new) · Architecture **75/0** ·
**Integration 6/0 on real Postgres** (Docker 28.3.2, `postgres:16-alpine`).

**Deviations disclosed:** (1) the §17 closure note had to name `tests/SchoolCollab.Assignments.Tests.Integration`
explicitly, because the committed guard `AssignmentAuthoringSpecGapsTests.DeferredGapsSection_NamesTheOpenResidualsWithTheirActionableContext`
requires "P2-c", "rollback", "Integration" and "archived" to remain facts inside §17 — the note was reworded rather
than the guard weakened; (2) the notice sentence is a `private const` (D3: no new public API), so the bUnit suite pins
the element id plus the literal sentence; (3) retiring the notice rides on `CaptureDirtyBaseline()`, explicitly gated
on `_error is null` so a **failed** load retires nothing; (4) consent is consumed on the no-op early-return path too
(leak defence, with its own test) — the one place where "louder" is possible instead of "quieter", deliberately;
(5) a structural-only `return;` after `context.PreventNavigation()`.

**Residual risks disclosed:** consent-consumption ordering — a host re-rendering the component (unchanged
`Id`/`Mode`) between a consented navigation and the route's parameter change would show the notice after a
*consented* discard (louder than the truth, never quieter; unreachable through the Router flow, but it is the
invariant's boundary); (A) faults the second write in the decorator *before* it touches SQL, so the DB-level fault is
the first write (the statement-level `DbCommandInterceptor` variant was the documented alternative and was not also
built); the notice uses the dim `authoring-reason` idiom as the plan instructed rather than a `FluentMessageBar`; and
a cancelled load clears the notice (`_error` stays null), immediately superseded by the next load.

## Review

**Diff review `003d807e` (`kimi-k2.7-code`) — Verdict: OK with notes — no P0/P1; the round can be accepted.**

### Confirmed correct
- **(A) rollback proof** (`LinkAssignmentGroupsRollbackPostgresTests.cs:60`): the real `LinkAssignmentGroupsHandler`
  over the real `AssignmentsDbContext` (real `AssignmentRepository` + `AssignmentActivityGroupRepository`) with an
  open `DbTransaction`; asserts (i) the assignment row + target rows equal the pre-handler state from a **fresh**
  context (`:124`), (ii) zero link rows (`:129`), (iii) the injected fault reaches the caller unwrapped (`:113`).
- **(A) the fixture choice is justified**: the **stream** seed is deliberate — `SyncDerivedTargeting` yields `Mixed`
  for stream-only and `SelectedGroups` once a group target is added, so a grade seed would leave the compat column
  unchanged and weaken the visible rollback signal.
- **(A) the fault genuinely lands after the first write**: the decorator probes `db.AssignmentTargets` (`:186`) and
  confirms the group target row is visible inside the same transaction before it throws (`:191`).
- **(B) notice contract**: `_discardConsented` is set only on the discard-confirmed path
  (`AssignmentAuthoring.razor:1277`) and consumed at the top of every `OnParametersSetAsync` (`:1354-1355`);
  `_unsavedEditsDiscarded` is set only when `IsDirty && !consented` (`:1365`) and only after the reload completes
  (`:1369-1371`); it is retired by `CaptureDirtyBaseline` on a successful load/save (`:1246`); the F17
  "only when `Id`/`Mode` CHANGED" guard is unchanged (`:1357-1359`); and the reload still unconditionally proceeds.
- **(C) §17 closure**: both `P2-c` and `UX-7 / F17 defence-in-depth` rows are struck; the new closure paragraph
  (`assignment-authoring-compartments.md:441`) names round `authoring-residuals-closeout` and still contains the
  `P2-c` / `rollback` / `Integration` / `archived` strings the committed architecture guard requires. The
  archived-group route half and the read-only-View row remain untouched.
- **Conventions**: no new public API member, no inline `<style>`, no route/contract/migration/CPM change,
  multi-select binding untouched.

### P2s — none requires a change
- **P2-1 (reviewer: "`CleanForm_IdChange_…` is a non-vacuity anchor, not a discriminating probe") — checked;
  already correct as written, no change.** The test's own doc-comment says exactly that:
  *"Not on its own discriminating (both trees reload silently); it is the non-vacuity anchor for the notice
  above"* (`AssignmentAuthoringBunitTests.cs:2118-2122`), and the other three (B) tests carry accurate
  `Discriminating: …` / `Non-vacuity anchors: …` labels. The finding is a mis-read of the file — the labelling
  is honest, and this review's own P2 is recorded here as resolved without a code change.
- **P2-2 — the notice sentence is a `private const` pinned by literal in the bUnit suite**
  (`AssignmentAuthoring.razor:618` ↔ `AssignmentAuthoringBunitTests.cs:2072`). Accepted: D3 forbids a new public
  member this round. Recorded as a follow-up — promote to a public const or RESX if the page copy is revised.
- **P2-3 — the notice reuses the dim `authoring-reason` idiom** rather than a `FluentMessageBar`. Intentional per
  the plan; no change.
- **P2-4 — the disclosed "louder than the truth" boundary** (a host re-render with unchanged `Id`/`Mode` between a
  consented navigation and the parameter change). Accepted trade-off; unreachable through the Router flow.
- **P2-5 — (A)'s fault is injected before the second write reaches SQL** (a `DbCommandInterceptor` faulting the
  link-table statement would be a stronger probe). The plan explicitly allowed either; no change.

## Acceptance

**Round CLOSED — accepted.** Tier 2; the ≤1 rework iteration was **not needed** — the review returned no P0/P1, and
every P2 was either "no change required" or (P2-1) already correct as written.

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| Assignments.Tests.Unit | **824 / 0** (+4) |
| ArchitectureTests.Unit | 75 / 0 |
| **Assignments.Tests.Integration (real Postgres)** | **6 / 0** |
| Patch `diffs-authoring-residuals-closeout.patch` | 592 lines · 4 diffs · 0 `deleted file mode` · 1 new |
| §17 | 6 rows → **4** |

**Delivered:** **(A)** the P2-c transaction-rollback proof on real Postgres — real handler, real `DbContext`, real
`DbTransaction`, fault landing after the first write, asserting rollback + no links + fault propagation;
**(B)** the reload path's own dirty gate — consent recorded on the confirmation's discard path, the reload still
proceeding (D2), and the discard **announced** rather than silent; **(C)** §17 hygiene, both rows struck and the
closure named.

**Gate history:** worker `d3f892cb` (its runner crashed *after* it wrote its report — recovered from the run
directory, not re-dispatched) → diff review `003d807e` (**OK with notes**). Three discrimination probes were run and
each reverted with an sha256-verified byte-for-byte restore.

**Decisions recorded at acceptance:**
- **(B)'s semantics (D2).** §17's wording ("refuse a reload while dirty") was implemented as *proceed and announce*
  because literally refusing would keep the previous assignment's loaded state on screen under the new assignment's
  URL — reintroducing the F17 stale-write path. A silent discard and a silent mis-attribution are not equivalent:
  the first loses work the author can retype, the second can persist the wrong content against the wrong assignment.
- The notice's copy stays a `private const` (D3); promoted only if the copy is revised (P2-2 follow-up).

**§17 — still open after this round:** the archived-group **route** half (R1) · the read-only-View entry (R2,
verified benign, no action while its reasoning holds).

**Merge: NOT authorized** — every git action is held at the owner's instruction. PR #292 (`stack/5`) is open and
green; `stack/6` now carries **two** accepted but uncommitted rounds (`authoring-residuals-mopup` and this one).
