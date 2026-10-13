# Round — media item pill (staged audio/video redesign) (solo)

**Status:** CLOSED (2026-10-12 — acceptance below; changes left **uncommitted** on `feat/recorder-takeover` pending the owner's commit gate).
**Mode:** solo (owner-opted F1: "go solo") · **model:** session model. Follows `round-recorder-takeover.md` (same branch train; its cumulative `diffs-recorder-takeover.patch` carries this round's delta too — there is no commit boundary to split them yet).

## This round's grill round (owner, 2026-10-12)

| # | Decision |
|---|---|
| Q1 | **No endpoint rework** — the owner recognised the existing client-side player (the panel's Review state) and declined the priced Tier-3 serving-endpoint round. Q5 then pinned: the pill click **switches to that same preview player**. |
| Q2 | Pill on all staged Audio/Video rows (recorded AND uploaded), both hosts; Images keep their readout. |
| Q3 | Duration client-side at record time (bytes in hand; `mediaDuration` probe); persisting = named schema follow-up. |
| F1 | **Solo.** |
| F2 | Playback boundary confirmed after the owner asked how save/playback works: pill-play is **session-scoped** (the kept Blob URL — the server copy is on disk via `IFileStore` but no teacher-authed serving route exists; the ward route is the precedent). Uploaded + post-reload rows show size-text pill, no play. **Named follow-up:** one teacher-authed `Results.Stream` sibling route + `AssignmentsApiClient` byte fetch → Blob URL (ward `mediaPlayer.js` pattern) lights up post-reload play. |
| F3 | Clear-file × **retired** (remove × is the single close); **Record again stays on the review panel only** — never on the listed item. |

## What shipped

- **`RecorderPanel`** (+`.js`): `ReviewSeed` parameter + `OnParametersSet` reopen — renders AHEAD of the capability branches (playback needs no getUserMedia); reopened review shows the same player + Record again, **never** the commit button; `RecordedMedia` gains `PreviewUrl`/`DurationSeconds`; `StopAsync` probes duration via new `mediaDuration(url)` JS (5 s timeout → null); URL ownership (`_ownsPreviewUrl`) so seeded/handed-off URLs are never revoked by the panel.
- **`InstructionEditorList`** (+ new collocated `.razor.js`, `.css`): per-row `_sessionMedia` map (Blob URL + duration) filled by the now-public `OnRecordedAsync` seam; staged rows render the **play pill** (`new Icons.Regular.Size20.Play()` + `m:ss`, "Play" fallback) or size text; `ToggleReviewAsync` reopens the panel seeded; close keeps the entry, `RemoveAsync`/`ClearMediaAsync` revoke via `revokeMediaUrl`; `IAsyncDisposable` for the module; pill CSS (rounded, hover lift, single-line rhythm); clear-file × removed from markup.
- **`ResourcesSection`** (+page): `SuppressUploadTile` parameter driven by `_materialRecorderOpen` — the **audio-dropzone fix**: the resources upload tile now hides during any recording (the tall video panel had merely pushed it off-screen; the short audio panel left it visible).
- **Tests**: `SeededReview_RendersPlayerAndRetake_WithoutCommit` (panel), `RecordedRow_Pill_ShowsDuration_AndReopensTheReviewPlayer` (list), `RecorderTakeover_HidesTheResourcesUploadTile` (page); `StagedMedia_RendersTheFileNameAndItsClearAction` → `…RendersTheSizeText_WithNoClearAction`; `DialogMode_LeavesMediaRowsOnTheStagedFileFlow` re-pinned.

## Worker Report

(solo — the parent implemented; deviations: two compile-fix iterations on the Play icon attribute (`@new …` → `@(new …)`), first caught by the build gate and fixed before tests ran.)

## Acceptance

**Verdict: CLOSED** — all round decisions implemented; no P1s. Verification (parent-run): `SchoolCollab.Assignments.Tests.Unit` → **1129 passed / 0 failed**; `SchoolCollab.ArchitectureTests.Unit` → **105 passed / 0 failed**. Full-solution `dotnet build` still pending the owner's running Admin (MSB3027 — same lock posture as the takeover round; the Assignments graph itself compiles clean).

**Honest gaps:** (1) duration probe + real Blob playback are browser-scope (bUnit can't run `getUserMedia`/media metadata); (2) pill-play is session-only — uploaded rows and post-reload rows show size text without play (the named follow-up above fixes that); (3) durations don't persist (schema follow-up, same bucket as the endpoint).

### Post-close defect pass (owner, 2026-10-12): pill missing in preview + remove blow-up

Owner reported: the pill didn't show after accepting a recording, and removing a staged media row on a new assignment threw. Investigation: **one shared root cause plus one real defect.** The owner's running Admin predates this round's rude edits (new `Seed` parameter, changed `RecordedMedia` shape, a **new static asset** — hot reload applies none of these), so the old circuit passed the 4-arg `RecordedMedia` (no `PreviewUrl` → size-text pill) and the new module import 404'd. The 404 blew up `RemoveAsync` because `RevokeMediaUrlAsync` did the module `import` **outside** its try/catch — a real robustness defect, now fixed (ONE guard around import + call; revocation is best-effort). New pin: `RemoveRow_WhenTheMediaModuleFails_StillRemovesTheRow` (module call throws → row still removed). The pill seam itself was already pinned (`RecordedRow_Pill_ShowsDuration_AndReopensTheReviewPlayer`); the single panel→list `PreviewUrl` hand-off line stays browser-scope. Re-verified: **1130 passed / 0 failed**. Owner action: **restart the preview** (not hot reload) to get the new assemblies + static asset.

### Final acceptance gate (owner-requested, 2026-10-12): verdict in — **HOLD**

The owner requested a final verdict from **`ollama-cloud/glm-5.3`**. The first dispatch parked (direct/`oracle` slots → the known Cline account-layer `Unauthorized`; the `worker`/`reviewer` teammates on `xiaomi/mimo-v2.6-flash` → `INFERENCE_CAP_ERROR` 429, ~15 h); re-dispatched the preserved brief once the account's current model was glm-5.3 (owner confirmed 2026-10-12). Verdict below — every finding was independently re-grounded against the code by the orchestrator before transcription.

**HOLD — 1×P1, 3×P2.** Everything the docs claim checked clean: all 11 named tests exist verbatim and pin ids/seams (not cosmetics); the takeover reworks (orphaned-row drop, `RecorderStateChanged` wiring, review-state layout), the seeded reopen (player + Record again, never commit), the best-effort revoke fix, the pill markup, and the Mic/capture-strip polish all match the code; the spec's D3 amendment matches; no convention violations. The defects:

1. **P1 — the panel's `DisposeAsync` revokes the very URL the commit just handed to the session map** (`RecorderPanel.razor:503–507` vs `:376–381`). The commit path awaits `OnRecorded` carrying `_previewUrl` and flips `_ownsPreviewUrl = false` only **after** the await returns; the host stores the URL in `_sessionMedia` (`InstructionEditorList.razor:602`) and nulls `_recordingRow` (`:591`) before staging — so the takeover-removing render unmounts the panel while its commit is still awaiting (the disposal race the spec itself records). `DisposeAsync` revokes `_previewUrl` with **no `_ownsPreviewUrl` guard** (the ownership migration patched only `Reset():419–424`), so every committed take's pill is seeded a dead URL — not a rare race, the mainline commit flow. Fix: flip ownership + null the field BEFORE the await, and guard `DisposeAsync`.
2. **P2 — stale `_reviewSeed` reaches a fresh kebab recorder** (`InstructionEditorList.razor:312–319` — `AddRowWithRecorderAsync` never clears `_reviewSeed`, unlike `CloseRecorder:489` / `OnRecordedAsync:592`; panel instance reused, no `@key` at `:19–24`, and the `ReferenceEquals` guard at `RecorderPanel.razor:218` keeps the old review state). Symptom: kebab "Record Video" while a pill review is open shows the previous take's player + "Record again" under the new header. Recoverable via Cancel, but wrong UI. Fix needs BOTH halves: clear `_reviewSeed` on the kebab path AND `@key` the panel (the `Seed` doc already assumes "a new instance reopens") — clearing the seed alone leaves the reused instance's state.
3. **P2 — video retake from a seeded review records blind** (`RecorderPanel.razor:42–63` vs `RecorderPanel.razor.js:62–66`). The reopened branch renders `{Id}-preview-take`, so `{Id}-preview` is absent from the DOM when `RetakeAsync → StartAsync` binds by `getElementById` at call time — the live preview never gets `srcObject` (audio unaffected; the bind is optional by design). Fix shape: split the preview bind out and re-bind after the recording branch renders (OnAfterRenderAsync flag + a `bindPreview` JS export).
4. **P2 — session Blob URLs leak on dialog/nav teardown** (`InstructionEditorList.razor:567–575`). The list's `DisposeAsync` disposes only the JS module — `_sessionMedia` URLs are never revoked; the "browser purges Blob URLs on teardown" comment (`:542–544`) is true only for whole-page teardown. Closing the question dialog or navigating within the page leaks every staged take's URL until reload. Fix: revoke all `_sessionMedia` entries in `DisposeAsync`, best-effort under the same guards as `RevokeMediaUrlAsync`.

**Not verified by the reviewer** (accepted by the orchestrator): build/test re-runs (read-only audit; owner services running — the gates on record stand), the commit-dispose interleaving argued statically from the spec's recorded race (not browser-reproduced), browser-scope behavior (getUserMedia, real playback, duration probe, the input-file grid CSS), and whether a committed take's pill was ever played after the preview restart (unrecorded — exactly the seam finding 1 breaks).

Reviewer's bottom line: fix finding 1, address findings 2–4, re-run the gates — "this is an accept."

### Post-acceptance polish (owner go-ahead, 2026-10-12)

Two visual fixes off the owner's screenshot (which showed the UNSTAGED capture slot — the pill cannot render before a take is accepted, and the preview still owed its restart):

1. **Tall dashed capture box ("height is overly wide").** The library's `.fluent-inputfile-container` is a grid whose auto-placed opacity-0 `<input type=file>` lands in a **second row**, inflating the media row. `InstructionEditorList.razor.css` now stacks the input over the content (`grid-row/column: 1`), caps the strip (`max-height: 48px`, `overflow: hidden`, `align-content: center`) and tightens the default hint (`0.8rem`, `6px 8px`). Scoped to `.cq-instruction-capture` — the page dropzone and the resources tile keep their full-height affordance.
2. **Camera glyph on audio record affordances.** Both buttons hard-coded `Icons.Regular.Size20.Video()`. New shared constant `FluentIcons.Mic` (Size20, `SchoolCollab.Admin.Shared`): the row's Record button picks `Mic` for `InstructionKindDto.Audio` rows; the panel's Start button keys off `IsVideo`. `MusicNote2` stays the staged-file *kind* glyph — Mic says "record now".

Presentational only (record-affordance tests pin ids, not glyphs — no new tests); re-verified: **1130 passed / 0 failed** + ArchitectureTests **105 passed / 0 failed**; scoped build 0 errors.

### HOLD remediation (owner-approved "all as recommended", 2026-10-12): solo fix pass

The four HOLD findings were fixed in one solo pass on `feat/recorder-takeover` (parent-implemented;
delegate dispatch was unavailable — both the `deepseek-4.1` and `worker` runs failed at the Cline
account layer with `Unauthorized` before any edit landed, so the orchestrator executed directly).

1. **P1 — dispose revoked the committed pill's URL.** `RecorderPanel.OnFileChosenAsync` now flips
   `_ownsPreviewUrl = false` and nulls `_previewUrl` **before** `OnRecorded.InvokeAsync`, carrying the
   URL to the host in a local; `DisposeAsync`'s revoke is guarded by `&& _ownsPreviewUrl` (the `Reset()`
   guard at `:419` already existed from the earlier migration — only `DisposeAsync` was unguarded).
   `RecorderPanel.razor` ~`399–410`, ~`540–547`.
2. **P2 — stale `_reviewSeed` reached a fresh kebab recorder.** Both halves: `AddRowWithRecorderAsync`
   and `ToggleRecorderAsync` clear `_reviewSeed` on entry, and the takeover panel is now `@key`ed on
   `_recordingRow` (the "a new instance reopens" semantics `OnParametersSet` already assumed).
   `InstructionEditorList.razor` ~`19–27`, ~`318–321`, ~`480–483`.
3. **P2 — video retake from a seeded review recorded blind.** New `_needPreviewBind` field + a
   `RetakeAsync` that flags when the retake came from `_reopened`; `OnAfterRenderAsync` performs the
   deferred `bindPreview` once the recording branch has rendered the live element. New JS export
   `bindPreview` (idempotent, mirrors `startCapture`'s bind/play shape). `RecorderPanel.razor`
   ~`202–206`, ~`242–258`, ~`433–442`; `RecorderPanel.razor.js` (new export).
4. **P2 — session Blob URLs leaked on teardown.** `InstructionEditorList.DisposeAsync` now revokes
   every `_sessionMedia` URL (best-effort via `RevokeMediaUrlAsync`) and clears the map before
   disposing the JS module. `InstructionEditorList.razor` ~`583–595`.

**Tests (4 new pins; total 1134):** `SeededReview_Dispose_DoesNotRevokeTheSeededUrl`,
`SeededReview_Retake_RebindsTheLivePreviewAfterTheRecordingBranchRenders` (RecorderPanelBunitTests);
`KebabRecorder_AfterAnOpenPillReview_IsAFreshPanel`, `Dispose_RevokesEverySessionHeldTakeUrl`
(InstructionEditorListBunitTests).

**Gates (re-run):**

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (21 pre-existing warnings; `-m:1` confirms — a first parallel run hit a transient MSB4166 child-node crash, not a compile error) |
| `dotnet test SchoolCollab.Assignments.Tests.Unit` | **1134 passed / 0 failed** |
| `dotnet test SchoolCollab.ArchitectureTests.Unit` | **105 passed / 0 failed** |
| Mutation probe (P1) | reverting the `DisposeAsync` guard makes `SeededReview_Dispose_…` **fail** (`revokePreviewUrl` wrongly called) — the pin genuinely binds the defect |

**Honest gap:** finding 1's positive side (an owned URL IS revoked on dispose) stays browser-scope —
the in-session Review state is unreachable under bUnit (`endRecording`'s private return type), so only
the seeded/no-revoke half is pinned. Findings 3's DOM re-render (`#rec-preview`) is likewise
browser-scope: loose mode probes unsupported, so the recording branch does not render the preview
element; the deferred `bindPreview` **call** is what the pin asserts.

`diffs-recorder-takeover.patch` regenerated (63883 bytes) over the five touched files.


### Delta re-review (glm-5.3, 2026-10-12): ACCEPT

Re-dispatched on the account's current model after the account-layer `Unauthorized` park (both earlier
delegate attempts had died before any edit; this run completed). Verdict transcribed below; both
residual claims were re-grounded against the code by the orchestrator before transcription.

**VERDICT: ACCEPT** — all four findings verified closed:

- **P1** — interleaving traced end-to-end: the host stores the URL in the session map
  (`OnRecordedAsync` `:627`), `_recordingRow = null` (`:616`) unmounts the panel mid-await, and the
  disposed panel's revoke is now guarded — the committed take's URL survives to the pill. Post-await
  `Reset()` is a no-op on both URL fields; the seeded path (`OnParametersSet` `:227`) is safe on both
  `Reset()` and dispose. The mutation probe is structurally consistent with the pin's design.
- **P2 (stale seed)** — both halves pinned: every recorder-open path clears `_reviewSeed` (`:328`,
  `:490`; `ToggleReviewAsync` sets fresh by design; `CloseRecorder`/`OnRecordedAsync` clear,
  pre-existing); `@key` (`:26`) forces the fresh instance; the discarded seeded instance's teardown is
  guarded by the P1 fix (ownership false at `OnParametersSet`).
- **P2 (bind)** — `_needPreviewBind` is cleared before use (no stale re-binds), `bindPreview` is
  idempotent (no stream/element → no-op) and matches the `{Id}-preview` id used by `startCapture`;
  the in-session retake still binds directly; a second retake after a reopened retake is in-session.
- **P2 (teardown)** — revoke → clear-map → module-dispose ordering verified; the ownership partition
  has no dual-revoke path (`OnRecordedAsync` revokes+replaces a row's prior entry, `RemoveAsync` /
  `ClearMediaAsync` revoke-then-remove, so every URL is revoked exactly once).

**Nuance ruling (failed commit): CONFIRMED.** Re-owning in the catch is unsafe in *every*
caught-throw case, not merely plausibly so: `OnRecordedAsync` stores the URL into the map (`:627`)
before any throw can surface (`NotifyRecorderStateAsync` `:630`, `StageMediaFileAsync` `:631`), so
re-owning would create a dual owner and recreate the P1. Two refinements to the record: (a) the
residual cost is smaller than stated — in the caught-throw cases the map entry is eventually cleaned
(row removal, a later re-commit's revoke+replace at `:622-627`, or the list's `DisposeAsync`); a true
page-lifetime orphan only arises on the defensive `StageMediaAsync is null` branch (`:610-614`), which
no real host reaches; (b) "retry the commit" is panel-level only (`_recordedFileName` survives,
`commitBlob` deliberately keeps `lastBlob`) — the host has already closed the takeover in the
throwing cases; that is pre-existing host ordering, unchanged by this fix.

**Two non-blocking P3 residuals (recorded, not fixed — deferred to the owner):**

1. `RecorderPanel.razor:247-248` — `OnAfterRenderAsync` clears `_needPreviewBind` before the
   `_module`/`_state == Recording` guards, so a render landing in the narrow window while `StartAsync`
   is still awaiting interop (state still Idle) would consume the flag without binding — a cosmetic
   blind preview only; no render source exists in that window in the delivered flows, and the take
   itself is unaffected. Fix shape: clear the flag only when the bind actually runs, or re-set it
   when the guards fail.
2. `RecorderPanel.razor:404-408` — on the defensive `StageMediaAsync is null` refusal (unreachable
   for both real hosts), the pre-await flip strands one Blob URL for the page lifetime with no owner —
   the accepted cost of the P1 fix, strictly better than the dual-owner alternative. Fix shape (if
   ever worth closing): have the host report "not stored" back through the `RecordedMedia` result
   shape.

The oracle also hunted the fix diff for new defects beyond the residuals: keyed-instance swap and
shared ES-module state is sound (the disposed instance's `stopCapture`/`clearBlob` cleans shared
state before the fresh instance can capture), no id collisions (reopened and in-session branches are
mutually exclusive), no double-revoke paths, no other `RecorderPanel` host exists.

Round status: accepted. Working tree uncommitted on `feat/recorder-takeover` — commit awaits the
owner's explicit instruction.
