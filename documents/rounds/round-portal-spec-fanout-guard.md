# Round — `portal-spec-fanout-guard`

**Status:** CLOSED — **PR #300** (merged 2026-10-05), merge commit `0245a846`: the portal plan's stale claims were corrected and the corrected facts guarded. *(Status line corrected 2026-10-06 — it had still read "planned" after the round closed.)*
**Tier:** Light (Tier 2) — worker + independent static diff reviewer
**Branch:** to be created from `main` (`b78dc2ae`) — a standalone docs+guard change, not a layer of the (merged) stack #297.
**Preceded by:** the Solo spec correction of 2026-10-04 (`documents/specs/teachers-ward-portal-prefab-plan.md`, `documents/specs/startup-flag-governance.md`) — uncommitted at the time this round starts; the two are one coherent change set.

## Goal

Stop the **fan-out class** from recurring. N4 and N5 in that correction were each a *correction applied in one place while its duplicate survived elsewhere*: the round that fixed the Playwright citation and the flat-module wording left both facts wrong one section away, and the portal plan still claimed *"No code for this plan has landed"* four merged PRs later. Correcting durable prose is a fan-out problem with no guard.

Also close the **N8 gap**: `AGENTS.md` mentions the portal **nowhere**, so an agent editing `src/SchoolCollab.Portals/` gets no pointer to its conventions — and the default C# rule reads as if it covered the Python app.

## Settled rules this round follows (cited)

| Settled by | Rule applied |
|---|---|
| `snapshot-config-guard` (merged, #295 → layer 1) | Precedent for **machine-enforcing a durable doc claim** from a hermetic source-inspection test — the same shape this guard takes. |
| `AGENTS.md` §Feature Flags / the guard ethos | Guards are **hermetic source scans** (no process launch), and any guard must **fail when its subject is absent** rather than pass vacuously. |
| Q1 (this round) | The guard asserts a **positive fact** (the spec carries the corrected statement), never a string ban — see the negation trap below. |
| Q2 (this round) | It reads the **portal plan only**, for **two** facts. Not a general "docs must be true" test; not status currency (every merge would break that). |

## The negation trap (why the guard is shaped this way)

The first draft of this guard would have **failed on the correction that prompted it**. The corrected T1 row reads:

> *"the module shipped as a flat `views/teacher.py`, **not** a `views/teacher/` package"*

A string ban on `views/teacher/` flags the very line that fixes the lie. Hence a positive assertion: **the spec must contain the flat-module statement**, which honest prose that quotes the old wording (or annotates a supersession) can never trip.

## Deliverables

| # | Deliverable |
|---|---|
| **D1** | Two positive assertions in the **existing portal guard file** `tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs` (no new file — the file already reads the portal's source and solution): (a) `documents/specs/teachers-ward-portal-prefab-plan.md` still carries the **flat-module** statement; (b) it records the portal **CI job as landed**. Both hermetic (`File.ReadAllText` + assertion, no process launch), each with a real failure message naming the fact and the file, and each **failing when the statement is removed**. |
| **D2** | One row in `AGENTS.md`'s **specialty instructions table**: "Portal (Python/FastAPI + Prefab UI)" → `documents/solution/portals-service-client-pattern.md` + the pytest / `httpx.MockTransport` / `TestClient` story + the `.slnx` solution-items tripwire — and an explicit note that **the C# rules do not apply** to `src/SchoolCollab.Portals/`, since the default `.NET` rule would otherwise be read as covering it. |

## Acceptance criteria

1. `dotnet build SchoolCollab.slnx` → **0 errors**; `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` → green with the new assertions (+2).
2. **Discrimination probe:** delete each guarded statement from the spec → the corresponding assertion goes **red** with its named message; restore → green.
3. **The negation trap is passed:** the guard is **green on the corrected T1 row** (which contains `views/teacher/` inside a negation). State this explicitly in the report.
4. `AGENTS.md` carries the row, and it points at paths that exist.
5. **No product code changed** — a test file and a docs row only.

## Expected files

| File | Change |
|---|---|
| `tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs` | D1 (the two assertions) |
| `AGENTS.md` | D2 (one table row) |

## Out of scope

- **The portal credential path** (Q4) — its own pair of steps, after this.
- Any other durable doc; any `.slnx` change (adding a test *file* to an existing project needs none — confirm).

## Worker Report

**Worker `54f9f36c` (initial) + fix-up run `1209134b`** — the latter was killed by a provider quota error (HTTP 429) **after editing but before reporting**, so its edits are present in the tree and were verified by the parent rather than self-reported.

| # | Result |
|---|---|
| **D1** | Two assertions in the existing class. `TeacherPortalSpec_RecordsFlatModule` (`:171`) extracts the **`| T1 |` row** (`ExtractTeacherPortalT1Row`, regex `\|\s*T1\s*\|.*?(\r?\n)`) and asserts it carries the **token** `views/teacher.py`, **plus** two on-disk facts (`File.Exists(.../views/teacher.py)`; `Directory.Exists(.../views/teacher/)` must be false). `TeacherPortalSpec_RecordsCiJobLanded` (`:186`) asserts `` `portals-python` `` inside `ExtractOpenQuestionsSection` (§7 → EOF) and cross-checks `.github/workflows/ci.yml` hermetically for `portals-python:`. Both extractions carry anti-vacuity assertions naming the spec; `SpecPath`/`Spec` hoisted and read once. |
| **D2** | `AGENTS.md:121` — one specialty-table row: Portal (Python/FastAPI + Prefab UI) → the service-client pattern doc + the pytest / `httpx.MockTransport` / `TestClient` story + the `.slnx` tripwire; qualified so the C# **code** rules do not govern the Python source in `Portals/` **or** `AuthPortal/`, while their `.slnx` items and AppHost wiring remain .NET-policed. |

**Evidence:** build **0 errors** · architecture suite **87 / 0** · the two guarded tests **2 / 0**.

## Review

**Reviewer `5117239e` (`deepseek-v4.1-flash`) — Verdict: `P1 ×2`, `P2 ×3`, "round can be accepted: **no** — one small fix-up pass".**

- **P1-1 — the guard was vacuous in exactly the way it existed to prevent.** The flat-module assertion was a whole-file `Contain` for `"flat `views/teacher.py`"`, which the spec's **Date history line** (`spec:4`) already satisfied — so it was **green on the un-corrected spec** and blind to the recurrence this round targets. The discrimination probe had passed **for the wrong reason**: red was reachable only by deleting the phrase *everywhere*, not by reverting the guarded statement. The reviewer cited the repo's own precedent — `AssignmentAuthoringSpecGapsTests.cs:28-36`, which extracts the section under test so "an assertion cannot be satisfied by text from a neighbour" — a file I had not read before writing the guard.
- **P1-2** — the asserted strings pinned **prose** (`flat`, the markdown backticks, the trailing word "job"; `spec:4` already writes "`portals-python` **CI** job"), so a legitimate editorial rewrite would redden the guard and train people to edit the guard instead of the doc.
- **P2s** — duplicated spec-path literal; the AGENTS.md note misleading (the .NET-side solution-items guard *does* reach that folder) and too narrow (missing `AuthPortal`); the class summary stale.
- **Confirmed sound:** hermetic, **negation-immune** (positive assertions, no string ban anywhere), failure messages naming fact + file, scope, placement.

**Fix-up implemented and parent-verified.** The parent ran the decisive probes the first pass had only pretended to run:

| Probe | Result |
|---|---|
| Revert **only** the T1 row (Date-line phrase intact) | `TeacherPortalSpec_RecordsFlatModule` **FAILED** (`:174`) — restore → green |
| Remove `portals-python` from **§7 only** (two other occurrences intact) | `TeacherPortalSpec_RecordsCiJobLanded` **FAILED** — restore → green, 2 / 0 |

Both are precisely the discrimination the whole-file form lacked, so P1-1 is closed on evidence rather than assertion. Re-review of the fix-up dispatched (run `35026cbf`).

### Fix-up re-review — reviewer `35026cbf` (`deepseek-v4.1-flash`, run on **clinepass** after the ollama quota blocked the first attempt)

**Verdict: `P2-only`, no P1 — "Round can now be accepted: **yes**".**

- **P1-1 closed: yes.** The row regex is correctly single-line-scoped — `Singleline` lets `.` cross newlines but the lazy `.*?` stops at the earliest `\r?\n`, so the match is exactly one row and cannot swallow the table or the next line. Mis-row risk is nil today (exactly one `| T1 |`-shaped row exists). The anti-vacuity `Success` guard is **stronger than the precedent it was told to follow** (`AssignmentAuthoringSpecGapsTests` returns `string.Empty` and leans on downstream assertions). The negation trap still passes: the row's `views/teacher/` negation cannot satisfy a `views/teacher.py` token assertion.
- **P1-2 closed: yes.** Both assertions target identifiers, both are scoped so the Date line cannot satisfy them — and the reviewer notes *why* the §7-only probe is real evidence: the same token still exists outside §7 at `spec:4` and `spec:295`, so going red proves the slice.
- **New issues: none.** Scope clean; the reviewer read **every spec hunk** and confirmed the seven correction sites with no residue, duplicated fragments or probe markers.
- **Brittleness: acceptable** (noisy-but-safe, never a false green).

**Three report-only P2s were fixed** rather than carried, because each was an instance of the very defect classes this round exists to prevent:

| P2 | Fix |
|---|---|
| `:191` still pinned markdown backticks (P1-2's surviving residue) | assert the bare identifier `portals-python` |
| `:217` coupled the guard to the section **number** `7` (the precedent uses `\d+`) | `^## \d+\.\s+Open questions` + a comment on why |
| `AGENTS.md:121` claimed "AppHost wiring" is policed for **both** portals; only the auth portal's is pinned | narrowed to say so explicitly |

**Fourth P2 carried (report-only):** `:205` — `Regex.Match` is first-match-wins and not anchored to §1's table, so a future earlier `| T1 |`-shaped row would silently redirect the assertion. Today safe (exactly one such row), so not fixed.

**Post-fix verification (parent-run):** build **0 errors**; suite **87 / 0**; and both guards re-probed after the P2 edits — reverting **both** guarded statements with the neighbour occurrences (Date line, §6 risks) left intact turned **2 / 2 red**, and restoring returned **2 / 0 green**.

## Acceptance

**Verdict: ACCEPTED (closed).** Re-review verdict `P2-only`, "round can now be accepted: yes"; its three actionable P2s were fixed and re-verified, and the fourth is carried above. Commit/push/PR remain gated on an explicit instruction.

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `ArchitectureTests.Unit` | **87 / 0** (85 before the round) |
| The two guards | red on a **scoped** revert, green restored — both probed by the parent |
| Scope | 2 spec corrections + the guard file + the `AGENTS.md` row; **no product code, no `.slnx` change, no workflow change** |

**Acceptance criteria:** **AC1** met · **AC2** met (both facts probed red→green, and the probes revert *only* the guarded statement) · **AC3** met — green while the corrected T1 row contains `views/teacher/` inside a negation · **AC4** met (the row points at paths that exist) · **AC5** met.

**The lesson worth keeping:** the first guard design was **vacuous in exactly the way it existed to prevent**. It tested a fact the spec asserted in two places, so the correction it was meant to protect could be reverted while the guard stayed green — and my own probe missed it because deleting the phrase *everywhere* is not the same test as reverting the guarded statement. Independent review caught it, not the parent. The repo also already contained the correct pattern (`AssignmentAuthoringSpecGapsTests`); reading the precedent before writing the guard would have avoided the entire cycle. Both rules — *scope a doc assertion to its section and assert identifier tokens* and *probe by reverting only the guarded statement* — are now recorded in project memory.

**Carried (report-only):** the §6-risks Playwright correction is not itself guarded (guarding two facts was the agreed scope); and the `:205` regex anchoring noted above.
