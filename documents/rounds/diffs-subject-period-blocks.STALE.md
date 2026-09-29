# ⚠️ STALE ARTIFACT — do not use as a v1 baseline

`diffs-subject-period-blocks.patch` in this folder is a **stale** record of the v1 round's diff.

The v1 round doc (`round-subject-period-blocks.md`) was edited **after** that patch froze, so the patch
**omits**:

- the AC-5 architecture guard (`DeprecatedPeriodWritePathArchitectureTests.cs`), and
- the two late-v1 polish fixes (`SubjectBlockLabels.RetiredMarker` and the `"Enrollment exceptions"` column
  title).

V1 nonetheless closed **green at 570/570 with those changes present** — so the patch understates the v1 tree.

## Origin

Found by the **Pass-1 diff review of the next round** (`round-enrollment-exceptions.md`, run `348c789d`),
which had to reconstruct the true v1 baseline from `3e408bf` + the v1 round doc rather than trust this patch.
Recorded there as its finding **F2**.

## What to do instead

**Reconstruct a v1 baseline from `3e408bf` plus `round-subject-period-blocks.md` — do not use this patch
alone.** Re-freezing the patch is no longer possible (v1's tree no longer exists in the working tree), which
is why this note exists instead of a corrected patch.
