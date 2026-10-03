# Round — snapshot-config-guard

Round `snapshot-config-guard` · **Tier 2 (Light)** · 2026-10-03
Models: worker `ollama-cloud/deepseek-v4.1-flash` · reviewer `ollama-cloud/kimi-k2.7-code`
Base: `0a5e18b6` (main) · Branch: `stack/7-snapshot-config-guard`

## Plan

### Goal

Make **snapshot-vs-config equality for owned-key value generation** machine-enforced instead of
review-enforced, and correct the spec status header that still denies its own implementation.

### Why

R3-1 fixed a stale EF mirror by hand: `AssignmentConfiguration` had gained `ValueGeneratedNever()` on its
four owned keys, but `AssignmentsDbContextModelSnapshot.cs` and the new migration's `.Designer.cs` still
emitted `.ValueGeneratedOnAdd()`. Nothing failed. `dotnet ef migrations has-pending-model-changes`
reports *"No changes have been made to the model since the last migration"* on **both** the stale and the
correct artifact, because EF's model differ is insensitive to this annotation. So the project's own
migration gate — including the runtime `HasPendingModelChanges()` guard in `MigrationGuardTests` — is
structurally unable to see it. It was caught only by a human reading, and §17 row 5 recorded the hole.

A rule enforced only by review is a rule that will be broken again. This round is the test that would
have caught R3-1.

### Deliverables

**D1 — the guard (new).** `tests/SchoolCollab.ArchitectureTests.Unit/OwnedKeyValueGenerationArchitectureTests.cs`,
a **source-inspection** test (it must not use `HasPendingModelChanges()` — that is the blind mechanism).
Locate the repo root with the same walk-up pattern as `AssignmentAuthoringSpecGapsTests.FindRepoRoot()`
(ascend from `AppContext.BaseDirectory` until `documents/specs` exists).

Invariant: in **every** `src/**/Migrations/*ModelSnapshot.cs` **and** every
`src/**/Migrations/*.Designer.cs`, no **owned-type key** property may carry the
`.ValueGeneratedOnAdd()` annotation.

An owned key is a `Property<Guid>("Id")` declared **inside an `OwnsMany(...)` block** — i.e. within the
`bN.OwnsMany("<Type>", "<Collection>", bN => { … })` lambda, whose first `bN.Property<…>("Id")` is the
key. Prefer a brace/block-aware scan over a bare regex so a match cannot leak from a neighbouring
entity. Rationale for the invariant: an owned collection's key is always assigned by the application,
EF never generates it, so `OnAdd` on an owned key is unconditionally wrong — it makes EF track a
newly-attached child as `Modified` and emit `UPDATE … WHERE id = <new id>` with no `INSERT` (the R3 P0).

If the invariant false-positives on a legitimate pre-existing case, **narrow it** to those owned keys
whose configuration declares `ValueGeneratedNever()` and say so in the report — do not weaken it to
nothing.

**D2 — spec status header corrected.** `documents/specs/assignment-authoring-compartments.md` line ~19
currently reads:

> **Implementation status:** **not implemented.** This document is the input to the R1–R3 rounds in
> §14; no code, migration or test has been written from it yet.

That is false: R1, R2 and R3 are merged on `main` (`#288`/`#291`/`#293`). Replace it with a true status
line naming what landed and pointing at §17 for the open gaps.

**D3 — §17 row 5 struck and recorded as closed.** Per §17's own stated convention (*"strike an entry
(naming the round that closed it) when it is done"*), remove the
`**Snapshot-vs-config equality is enforced only by review**` row from the table and add a
closed-defect paragraph beneath it naming round `snapshot-config-guard`, the test that now enforces it,
and the reason the EF gate could not (the differ's insensitivity). **The other rows must stay.**

### Acceptance criteria

1. The new guard **fails** if `.ValueGeneratedOnAdd()` is present on an owned key in a snapshot or
   designer, and **passes** on the current tree. Discriminating power must be **proved, not asserted**:
   either a mutation probe (temporarily add the annotation, observe the failure, restore, prove the
   restore with a `sha256` comparison) or a control test that feeds the checker a synthetic bad
   snapshot and asserts it reports the property.
2. `dotnet build SchoolCollab.slnx` → 0 errors.
3. `SchoolCollab.ArchitectureTests.Unit` green, including the new test and the existing
   `AssignmentAuthoringSpecGapsTests` (which requires the §17 section to keep the strings `P2-c`,
   `rollback`, `Integration` and `archived` — striking one row must not remove any of them).
4. The other three test suites unchanged and green.
5. No production code change, no migration, no new dependency.

### Expected files

| File | Change |
|---|---|
| `tests/SchoolCollab.ArchitectureTests.Unit/OwnedKeyValueGenerationArchitectureTests.cs` | **new** |
| `documents/specs/assignment-authoring-compartments.md` | status header + §17 row 5 → closed paragraph |

### Out of scope

§17 rows 1, 3 and 4 (route design; provider-gated structured output; the save-path identity redesign),
TGT-15, and the teacher-portal MVP — all deliberately sequenced elsewhere.

## Worker Report

**Run `88422c9e` (`ollama-cloud/deepseek-v4.1-flash`).** Two files touched; no production code, no migration, no
package change. The round doc and the round's own patch were not edited by the worker.

### D1 — the guard (`OwnedKeyValueGenerationArchitectureTests.cs`, new, 327 lines)

**Invariant:** in every **live generated model artifact** under `src/` — each context's
`Migrations/*ModelSnapshot.cs` plus the **newest** `*.Designer.cs` per `Migrations` directory (newest = the
migration-id timestamp **prefix**, not file mtime) — no **owned key** may carry `.ValueGeneratedOnAdd()`.

- **"Owned key"** = a `Property<Guid>("Id")` declaration whose offset falls inside the balanced parentheses of an
  `OwnsMany(` opener — a paren-depth walk, the statement read to the next `;`, attributed to the **innermost**
  enclosing opener. This matters: a flat `Property<Guid>("Id")` grep produced **103 of its raw hits on ordinary
  entity keys**, so a regex guard would have been ~103 false positives and deleted within a week.
- **It does not use the blind mechanism.** `HasPendingModelChanges` appears only in the class doc-comment, explaining
  that this is the gate that *cannot* see the annotation.
- **Two test methods:** the guard itself, plus `LiveModelArtifacts_CoverEverySnapshotOnDiskAndStillCarryOwnedKeys`
  for anti-vacuity — every `*ModelSnapshot.cs` found by an independent `Directory.GetFiles` glob must be in the
  examined set, ≥1 designer and ≥1 owned key must be examined, and each failure message lists the artifacts examined.
  No exact owned-key count is asserted, so adding a new owned type is not a test failure.
- **Scope narrowed (option A),** and *not* along the axis the plan named. Discovery yields exactly **6 artifacts /
  5 owned keys** (3 snapshots + the newest designer of Assignments / Settings / Students), green today. A block-aware
  scan of all **106** artifacts finds owned-key `ValueGeneratedOnAdd()` in **25 files / 100 sites** — every historical
  Assignments designer up to `20261002065949_AddAssignmentTargets` — i.e. faithful per-migration records written before
  `ValueGeneratedNever()` existed. The plan's named narrowing (filter to keys whose config declares
  `ValueGeneratedNever()`) removes none of them, because those same 4 owned types declare it *today*; so the
  **artifact** set was narrowed, not the key set, and the test stays unconditional for owned keys so it can still fail.
  The exemption is recorded as a reasoned rule in the class doc-comment, with an explicit warning against widening to
  all designers or narrowing to snapshots-only.

**Discriminating power — proved by mutation probe on BOTH halves, with sha256-verified restore:**

| Half | Before | Injected at | Result | Restored |
|---|---|---|---|---|
| live snapshot (`AssignmentsDbContextModelSnapshot.cs`) | `0f0ae91c…` | `.ValueGeneratedOnAdd()` on the `AssignmentAttachment` owned key, line 1235 | **guard FAILED** — `…/AssignmentsDbContextModelSnapshot.cs:1235 — owned key of '…AssignmentAttachment' declares .ValueGeneratedOnAdd()` (76/77 passed, 1 failed) | `0f0ae91c…` **identical** |
| live designer (`20261003154434_…Designer.cs`) | `234b5c4b…` | same annotation, line 1238 | **guard FAILED** on that exact file:line + owned type | `234b5c4b…` **identical** |

No leftover mutation: the tree carries only the two intended paths.

### D2 — status header corrected

> **Implementation status:** **implemented in the main line.** R1 (#288), R2 (#291) and R3 (#293) are merged on
> `main` — the compartmentalized authoring page, the targeting model and the attachment-grounded AI surface
> specified below exist in code, with tests. What those rounds deferred or found open (and the defects they
> closed by hand) is recorded durably in §17.

**The worker corrected a factual error in this round's plan**, which had named the merged PRs as
`#291`/`#292`/`#293` for R1/R2/R3. R1 is **#288**; `#292` is the UX-7 residuals round. Plan corrected at review time.

### D3 — §17 row 5 struck, per §17's own convention

Removed only the `**Snapshot-vs-config equality is enforced only by review**` row and added a closed-defect
paragraph beneath the table, opening *"Round `snapshot-config-guard` closed the **snapshot-vs-config equality gap**
the R3-1 re-verify found"*, naming the new test, its scope (snapshots + newest designer, with the older-designer
exemption), the owned-key mechanism, and why the EF gate could not hold it (the differ reports "No changes…" on both
the stale and the corrected artifact, hence also invisible to `MigrationGuardTests`). The section now carries the
**remaining 4 gap rows**; the strings `P2-c`, `rollback`, `Integration` and `archived` survive (verified by the
parent, and `AssignmentAuthoringSpecGapsTests` passes).

### Verified (worker, and re-verified by the parent)

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `ArchitectureTests.Unit` | **77 / 0** (new guard ×2 + `AssignmentAuthoringSpecGapsTests`) |
| `Assignments.Tests.Unit` | 871 / 0 |
| `Assignments.Api.Tests.Unit` | 111 / 0 |
| `Assignments.Tests.Integration` | 19 / 0 |

### Deviations from the plan (all deliberate, all reported)

1. **D1 scope narrowed** to snapshots + newest designer per `Migrations` directory (supervisor decision A) — the
   literal "every `.Designer.cs`" false-positives on 25 historical designers / 100 sites.
2. **D1 key set not restricted** to config-declared `ValueGeneratedNever()` owned keys even though the plan named
   that narrowing — it removes none of the false positives, so the artifact-set narrowing was used instead.
3. **A second test method** added for anti-vacuity (a superset of "assert the enumeration is non-empty") so the
   guard's own failure reads as an invariant failure rather than a setup error.
4. **D2 names the verified PRs** `#288`/`#291`/`#293`, correcting the plan's prose.

## Review

**Diff reviewer `af71a636` (`kimi-k2.7-code`) — Verdict: `P2-only`, no P1, "round can be accepted: yes".**

| Check | Verdict |
|---|---|
| Guard discriminates | yes — injecting `.ValueGeneratedOnAdd()` on any owned `Property<Guid>("Id")` inside a live snapshot or newest designer is reported |
| Scan correctness | ok |
| Scope + anti-vacuity | ok — snapshots + newest designer per dir (by migration-id prefix); exemption documented with warnings against widening/narrowing; non-vacuity asserted without a brittle owned-key count |
| Spec | truthful + convention followed; R1 `#288`, R2 `#291`, R3 `#293`; row 5 struck with a closed-defect paragraph; required strings survive |
| Best practices | no production/migration/package change; no unrelated reformatting; matches neighbour test style |

**P2 (reviewer):** `FindClosingParenthesis` was string/comment-naive — it counted `(`/`)` inside literals and comments. Generated artifacts are fine today, but an unbalanced parenthesis in a future `.HasComment(…)` literal would shift every block span and make the guard report **green while reading the wrong bytes**.

**Parent decision: fix it rather than carry it — and that overrode the reviewer's own "accept".** The round's entire deliverable is a guard, and a guard whose only value is that it cannot silently lie does not get to have a silent-failure mode. The P2 sits in exactly that path. This was the round's first and only rework (within the Tier-2 bound of one).

**Re-check `1ffc4f52` (`kimi-k2.7-code`, same model — a P2 fix, not an escalation, so the higher model is not mandated) — Verdict: `PASS`, no P1, "round can be accepted: yes".**

- **Scanner correctness: ok.** One shared `ScanNonCode(source, index, out end)` now yields the exclusive end of any non-code span, covering `//`, `/* */`, regular `"…"` (with `\` escapes), verbatim `@"…"` (where `""` is an escaped quote), raw `"""…"""` and longer fences, and `'x'` char literals. `FindClosingParenthesis` and a new `IndexOfInCode` jump those spans, so only real-code parens move depth and a quoted or commented-out `OwnsMany(` can never open a block. `OwnedTypeOf` reads the first literal through the scanner plus `DecodeStringLiteral`, replacing the `"([^"]+)"` regex an escaped quote would cut short.
- **Control test real: yes.** The reviewer verified it cannot pass vacuously by reasoning about a *mutated* scanner, not by trusting the narrative: a scanner returning `None` everywhere yields four fake blocks and `OwnedType == "unknown"`, failing `ContainSingle`, `OwnedType` **and** `End`; a closer-only revert fails `End` (864 vs 1009). No assertion can pass while the scanner silently ignores non-code.
- **Guard still discriminates: ok.** The violation message still emits `{file}:{line} — owned key of '{OwnedType}' declares .ValueGeneratedOnAdd()`.
- **Residual classification: false-positive (acceptable), NOT a silent lie.** `KeyDeclaration` still matches `Property<Guid>("Id")` inside comments, so a commented-out declaration inside a real block is reported. Block boundaries are correct, so this is **loud** — the guard goes red and someone investigates — rather than green-while-wrong.

**One parent note, and a correction of the parent's own acceptance text:** the residual was disclosed in the worker's report but was **not** recorded in the test file — and the file's own wording near that fixture (`"the commented-out declarations are not code and must not be attributed to an owned block"`) implies the opposite of the residual, since `KeyDeclaration` is a raw-text pattern. Because `documents/rounds/` is ephemeral, the parent added a short *known-limitation* paragraph to the guard's doc-comment, stating explicitly that the mismatch is a **false positive** (red on a phantom) rather than a false negative, and that generated artifacts carry no comments so it cannot arise from EF output today. **That is a post-review change to a reviewed artifact** — comment-only, no behaviour change, with `dotnet build` and `ArchitectureTests.Unit` re-run green afterwards (78/0) and the patch refrozen. Disclosed rather than slipped in.

## Acceptance

**Verdict: CLOSED.**

**Tier 2 (Light)** — parent authored the plan; worker `88422c9e` implemented; reviewer `af71a636` reviewed (`P2-only`); one bounded rework `1ad17b5a`; re-check `1ffc4f52` (`PASS`).

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `ArchitectureTests.Unit` | **78 / 0** (guard + anti-vacuity + control test; was 75 before the round) |
| `Assignments.Tests.Unit` | 871 / 0 |
| `Assignments.Api.Tests.Unit` | 111 / 0 |
| `Assignments.Tests.Integration` (real Postgres) | 19 / 0 |
| Patch | `diffs-snapshot-config-guard.patch`, isolated against `0a5e18b6` |

**Acceptance criteria — all met.** (1) the guard discriminates, proved by a mutation probe on **both** halves with `sha256`-verified restore, and by a control test the reviewer verified cannot pass vacuously; (2) build 0 errors; (3) Architecture green incl. the existing `AssignmentAuthoringSpecGapsTests`; (4) all suites unchanged; (5) no production code, no migration, no new dependency.

**What this round bought:** §17 row 5 recorded that snapshot-vs-config equality was enforced **only by review** — the defect class that cost R3 a rework cycle because `has-pending-model-changes` and the runtime `HasPendingModelChanges()` guard are both structurally blind to the annotation. That gap is now closed by a machine check, and the spec's status header no longer denies its own implementation.

**Carried (loud, acceptable):** `KeyDeclaration` matches declarations inside comments, so a commented-out owned-key declaration inside a real block yields a **false positive**, never a false negative. Recorded in the test file and here; not worth a second rework.

**Deviations, all deliberate and reported:** the D1 artifact scope was narrowed to snapshots + the newest designer per `Migrations` directory (option A of the supervisor decision) because the literal "every `.Designer.cs`" false-positives on 25 historical designers / 100 sites; the plan's named key-set narrowing was not used because it removes none of those; an anti-vacuity test was split into its own method; and D2's PR numbers were corrected from the plan's `#291`/`#292`/`#293` to `#288`/`#291`/`#293` (R1 is `#288`; `#292` is the UX-7 residuals round).
