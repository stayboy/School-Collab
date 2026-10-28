Provider: pi, Tier 2 (attended) — see `## Not run` for the two seats the parent took itself.
Mode: attended
**Status:** CLOSED — layer 1 of the student-view redesign, ratified in round 13; delivered on branch `feat/student-view-redesign`
in worktree `School-Collab-studentview`. Nothing committed at the time of writing (the owner holds the
commit gate).

# Round — student view redesign, layer 1: the page

Plan of record: `documents/solution/student-view-redesign.md` (§1 findings, §2 proposal, §5 steps,
§7 settled decisions, §8 implementation record). The mock and its renders live in `.pi/mock/`
(`student-view-v1.html`, `state-light.png`, `state-dark.png`, `state-narrow.png`) — `.pi` is gitignored, so
they are review artifacts, not deliverables.

## Plan

The page (`/students/{id}`) carried seven defects, all verified in source rather than inferred:

| # | Defect | Evidence |
|---|---|---|
| 1 | The status badge is unreadable **and** ambiguous | `FluentBadge Appearance="Filled"` with no `Color` paints the host `#0f6cbd` while `app.css:88` repaints the light-DOM `<span>` `#242424` → **2.88:1**; and "Active" meant three different things (record / enrollment / activity group) |
| 2 | `Detail.razor.css` consumes tokens nothing declares | `.student-contact-history__entry` fell back to a literal `#fafafa` → a white card in dark mode |
| 3 | The layout is a stack of tables | 3 `<h3>` + 3 `FluentDataGrid` + a `<ul>`, `min-height: 220px` hacks, nothing with a shape |
| 4 | Nine visible controls, three of them primary | "Back to Students" sat **below** the fold |
| 5 | The page never showed the student's **subjects** | `GET /students/{id}/subjects` and both `RailBand` variants already existed — the union was visible only on the edit form |
| 6 | It showed contact **history** for a list it never showed | `ListContactAuditEntriesAsync(ownerType: Student)` on a page with no current-contact list |
| 7 | No assignment progress at all | — (deferred to layer 2 by Q122) |

Scope was fixed before any code: **the page**, plus the shared tokens/pill it needs. Layers 2 (assignment
stats, its own `gh stack` layer) and the repo-wide badge migration are out.

## Implementation

`Detail.razor` was rewritten on the shared skeleton; `Detail.razor.css` was rewritten to match; the pill and
its palette went into `Admin.Shared/wwwroot/app.css`. Full file-by-file table: design doc §8.1.

Shape, as built — main column: Details (borderless fact grid) · Enrollment (placement strip + history
timeline, per-node kebab) · Contacts (the student's own chips) · Guardians (cards with their own kebabs).
Rail, sticky and last: Activity groups · Subjects (two `RailBand`s) · Contact history.

## Review

Self-run (see `## Not run`). The pass produced ten changes, recorded because each one was a defect the
first draft shipped:

| # | Finding | Fix |
|---|---|---|
| R1 | Two retargeted tests passed on a **code comment** containing "Join Group" / `<StudentGuardiansList>` | assert on the action list and on an element matcher (`<StudentGuardiansList\s+[A-Z]`), never on a phrase |
| R2 | Five `@code` comments described the retired grid ("the StudentGuardiansList below the title row", "the grid raises this callback") | rewritten to the card implementation — a comment describing the old shape is how a reader learns the wrong thing |
| R3 | `Class="badge--flag"` referenced a CSS class that has never existed | dropped |
| R4 | `--accent-fill-rest` was declared in `:root` and **not** in the dark block (pre-existing), so my link-styled "View all" would have been ~2.3:1 in dark mode | added to the dark block |
| R5 | `--font-monospace` is declared nowhere; a fallback was hiding it | use `monospace` directly, so the page consumes only declared tokens |
| R6 | `.student-title` had `text-overflow: ellipsis` but no `min-width: 0` — a flex item refuses to shrink below its content, so the ellipsis could never engage and a long name pushed the pills off the bar | added, with the reason in the rule |
| R7 | `.authoring-title-group` was built to hold **one** owner chip; this page puts a pill and two badges there, and at a narrow width they pushed the action header off the edge | `flex-wrap: wrap` + `min-width: 0` on the group |
| R8 | The new contrast test compared foreground against *every* suffix (fg-vs-fg = 1.00:1) | measure fg against bg only |
| R9 | Two fixture bugs in the render tests: this API serialises enums as **numbers** (only `SubjectAssignmentSource` has a string converter), and `ContactDto`'s positional record needs `isVerified` / `isDeleted` / `createdAt` / `updatedAt` | fixture corrected; the wire facts recorded in the test's comments |
| R10 | The pill's dot had to win its colour back explicitly, and `.status-pill > span { color: inherit }` is what stops `app.css:88`'s `span` rule from undoing it | pinned by a test that asserts **both** halves, so removing the global rule and the override together is allowed and removing one is not |

### Finding raised, not fixed (owner decision)

`BadgeColor.Success` does not paint in FluentUI 5.0.1 — the shipped rule is misspelled
`fluent-badge[color=sucess]`, verified against the package's own stylesheet. Every "Success : Subtle" pin in
`StatusBadgeColourArchitectureTests` therefore proves a binding is present, not that it works; those seven
sites render two identical `Filled` chips today. Recorded in that test's doc comment. The fix is a
repo-wide migration to `.status-pill`, which Q118 deferred.

## Rendered proof — measured (2026-10-28)

Playwright/Chromium 1440x950 @DPR2 against the AppHost started **from this worktree** (a second container set;
the main checkout's was already running) and three real seeded students. Full table, including the two
defects the render found and the two it invented: design doc §8.6. Headlines:

- **The page draws**: 4 main blocks, 3 rail blocks, 1 fact grid, 1 timeline node, 1 monogram, 2 pills, 0 tabs.
- **The pill**: `Enrolled` at **8.05:1** light and **8.45:1** dark, dot `aria-hidden`; `<fluent-badge>` elements
  painting the old host blue: **0**.
- **Dark through the app's own switcher**: tokens resolve (`--accent-fill-rest` → `#479ef5`, the gap closed
  here), the monogram disc is dark, and the two `Tint` context labels measure ≈15.5:1 — so they stay badges.
- **Narrow (548px host)**: one track, rail below main, action bar does not overflow.
- **Fixed because of the render**: `"1 days in this placement"` (pluralisation — no test saw it; now
  `DaysText(int)` with a yesterday-dated fixture asserting the singular) and a duplicated ownership note on an
  empty Contacts panel.
- **Not fixed because the render was lying**: a hand-set `data-theme` is not the app's dark mode. It produced a
  white-on-pale-blue `Tint` badge and a `#424242` `Subtle` button; through the real switcher both are legible.
- **Still not live-verified**: guardian **cards** — none of the three seeded students has a guardian, so that
  markup is bUnit-only.

### Flakes recorded

Two earlier runs of the same full gate each failed **one** `Assignments.Tests.Unit` test — **different tests**
(`Edit_LegacyQuestionWithoutAResponseKind_…`, then `Builder_EntryRemoveAction_RemovesTheEntryItNames`). That
project passed **1390/1390** standalone twice, each victim passed **3/3** in isolation, and nothing in this
diff touches Assignments. Recorded as a **flake**, not a pass; the third gate run was clean on every leg.

## Ratified (round 13, 2026-10-28)

| # | Ruling |
|---|---|
| Q128 | **The five plan deviations are ratified** as built (§8.2 of the design doc). |
| Q129 | **`Archived`, not `Deleted`** — "deleted is rather harsh". Applied to the students **list** too: its status badge and the `Show deleted` filter label now say *Archived*, so the same student is not "Deleted" in the list and "Archived" on their page. The **verb** (`Delete` in the row kebab, `ListDeletedStudentsAsync`, the API route) is untouched — renaming the action was not the ruling and is still its own decision. |
| Q130 | **Sub-periods are informational**; their real home is which term an assignment belongs to. So the Subjects panel does not explain sub-periods and does not point at #427 — but it also no longer claims "this student's grade has no subjects assigned", which the page cannot know. It states the outcome: *"No subjects could be resolved for this placement."* |
| Q131 | **Filed** as issue [#442](https://github.com/stayboy/School-Collab/issues/442) — the misspelled `fluent-badge[color=sucess]`, the 16 files, the proof command, and `.status-pill` as the destination. No code touched (Q118). |
| Q132 | **The live probe ran** before the PR — see `## Rendered proof`. |
| Q133 | **Self-run review accepted** for this layer; the two mandated seats stay recorded under `## Not run`. |
| Q134 | **Lesson accepted** — `.github/copilot/rules/agent-lessons.md`, *"A source-string test can pass on a comment"*. |

## Acceptance

| Item | Verdict |
|---|---|
| Defects 1–6 closed | **accepted, and confirmed in the running app** — pill is token-driven and labelled; no literal colours in the scoped CSS; every block is a `PanelSection`; 5 kebabs + 3 inline affordances and no footer button; Subjects in the rail; Contacts in main above Guardians |
| §5 steps 1–8, 10–12 | **accepted** as built (§8.1), with the five deviations in §8.2 |
| §5 step 9 (assignment stats) | **descoped** to the next stack layer by Q122 — not in this diff |
| Tests | **accepted** — 10 re-pointed, 12 new source/palette checks, 7 new bUnit renders |
| Gates | **accepted** — full `gates.sh` (escalated, the diff touches shared chrome); every leg green except the 2 known-red OpenRouter live tests |
| Live browser probe | **run and green** — see `## Rendered proof`; two real defects found and fixed, two false positives rejected |

## Not run

| Step | Reason | Owner |
|---|---|---|
| Independent worker seat (Tier 2's "worker implements") | The session that produced this layer had the plan and the implementation in the **same** seat: the turn resumed a session already mid-implementation. Recorded rather than dressed up as a worker run. | owner |
| Independent static reviewer seat (Tier 2's "reviewer verifies against the plan") | Same cause. The self-run review is written out in `## Review` above with every finding, so the diff can be re-reviewed from those claims rather than from scratch. | owner |
| Guardian cards in the running app | No seeded student has a guardian, so the cards and their per-card contact chips are covered by `StudentViewRendersTests` only. Adding one through the app would not have changed any other row of `## Rendered proof`. | owner |
| PR / merge | Explicit gate: no commit, push, PR or merge without a separate instruction. The working tree is left uncommitted for review. | owner |
