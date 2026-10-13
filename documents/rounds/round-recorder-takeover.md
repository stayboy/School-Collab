# Round — recorder takeover (full-container inline recorder)

**Status:** CLOSED (2026-10-12 — acceptance below; changes left **uncommitted** on `feat/recorder-takeover` pending the owner's commit gate).
**Mode:** light (Tier 2 — parent plans/accepts, one worker run + static diff-only reviewer) · **attended**.
**Provider:** Cline/clinepass (models: `deepseek-v4.1-flash` worker, `mimo-v2.6-flash` reviewer — owner 2026-10-05 rulings; teammate mechanism carries no model parameter, ids recorded for traceability).
**Base:** `6cb415ec17029949415f5e6d92dcbe123766bff3` (main) · **Branch:** `feat/recorder-takeover`.
Tree at round start: clean except untracked `push-out.txt` (not round-related; excluded from the round patch by base-sha diffing).

**Follows:** `recording-materials.md` (implemented, PR #339 / `18872f80`) — D3 stays **inline, not a dialog** (the owner re-confirmed 2026-10-12: "all as recommended" on the grill below). This round is a **UX revision of the inline panel**, not a spec reversal.

## This round's grill round (owner: "all as recommended", 2026-10-12)

| # | Decision |
|---|---|
| Q1 | **Light round (Tier 2)**, attended. |
| Q2 | Takeover lives **inside `InstructionEditorList`** — both hosts (authoring page section + question dialog) get it; `PanelSection` header/kebab untouched. |
| Q3 | Panel gains a **header bar**: title "Recording audio"/"Recording video" + **labeled Cancel** (icon+text, Neutral), rendered in **all** states incl. mid-recording; new `OnClose` seam notifies the host. |
| Q4 | **Fluid sizing**: preview `width:100%`, `aspect-ratio:16/9`, `max-height:min(50vh,480px)`, `object-fit:contain` (today's 320px cap + `cover` go); review `<audio>` full-width up to a cap; panel `min-height:160px`. |
| Q5 | **Cancel stops capture, discards the take, restores the list**; the kebab-appended placeholder row stays; reuses the existing `stopCapture`/`clearBlob` dispose path. |

Owner's defect framing: the current recorder is a cramped sibling-`<li>` (preview capped at 320px, 10px padding) with **no close control at all** — dismissal is only the tiny row "Record" toggle. The takeover uses the whole list container (rows + upload dropzone area) and hides that section content while recording.

## Plan

### Goal

While a recorder is open, `InstructionEditorList` renders **only** the recorder, filling the component's whole container (the space the row list + dropzone + add-button occupy today). The recorder gets a prominent header with a labeled Cancel. No dialog anywhere (dialog-ui §4; D12 stands).

### Slice A — `RecorderPanel.razor`

1. New header block at the top of `.recorder-panel`, rendered **unconditionally** (before the `_probed` branches, so Cancel is reachable in the unsupported and pre-probe states too):
   - `<span class="recorder-title">` → `@(IsVideo ? "Recording video" : "Recording audio")`, id `{Id}-title`.
   - `FluentButton` `Appearance.Neutral`, `Id="{Id}-cancel"`, `IconStart` Dismiss, **text "Cancel"**, `OnClick=CancelAsync`.
2. New `[Parameter] public EventCallback OnClose`.
3. `CancelAsync`: guard `_disposed`; stop ticker; if module loaded → `stopCapture`, `clearBlob`, revoke `_previewUrl` (reuse existing helpers); reset `_state`/`_recorded*`/`_previewUrl`/`_error`/`_elapsed`; then `OnClose.InvokeAsync()`. Do **not** dispose the component here — the host unmounts it, and `DisposeAsync` is already idempotent-guarded.
4. Review-state `<audio>` gains `class="recorder-audio"` (today it is unstyled).

### Slice B — `RecorderPanel.razor.css`

- `.recorder-panel`: add `min-height: 160px;`; `padding: 10px` → `16px`.
- New `.recorder-header`: `display:flex; align-items:center; justify-content:space-between; gap:8px;`.
- New `.recorder-title`: `font-weight:600;`.
- `.recorder-preview`: **remove** `max-width:320px`; change `object-fit:cover` → `contain`; add `max-height:min(50vh,480px);`.
- New `.recorder-audio`: `width:100%; max-width:560px;`.
- Update the file-header comment (the panel is no longer "inside a media row's capture slot").

### Slice C — `InstructionEditorList.razor`

1. Container: `class="cq-instructions"` gains ` cq-instructions-recording` while `_recordingRow is not null`.
2. When `_recordingRow is not null`, render **only** the takeover; the empty-`<p>`, the rows `<ul>`, the "Add instruction" actions block, and the error `<p>` are suppressed:
   ```razor
   <div class="cq-instruction-recorder" id="@($"{Id}-recorder-takeover")">
       <RecorderPanel Id="@($"{Id}-recorder-{Rows.IndexOf(_recordingRow)}")"
                      IsVideo="@(_recordingRow.Kind == InstructionKindDto.Video)"
                      OnRecorded="@(media => OnRecordedAsync(_recordingRow!, media))"
                      OnClose="CloseRecorder" />
   </div>
   ```
3. **Delete** the sibling-`<li>` recorder block (current lines ~145–157) and its "own list item" comment; replace the comment with the takeover rationale (owner 2026-10-12: whole-container takeover, close affordance in the panel header).
4. New `CloseRecorder()`: `_recordingRow = null; _error = null; StateHasChanged();`.
5. `ToggleRecorderAsync` / `AddRowWithRecorderAsync` / `OnRecordedAsync` semantics unchanged (`OnRecordedAsync` already nulls `_recordingRow` before staging, so commit restores the list and the staged row appears).

### Slice D — `InstructionEditorList.razor.css`

- Redefine `.cq-instruction-recorder` from "own `<li>` beneath the row" to the takeover frame: `display:flex; flex:1 1 auto;` (fills the container column); update its comment.
- `.cq-instructions-recording` needs no visual rule beyond what the above gives (container is already a flex column) — add only if a rule is genuinely needed; no speculative CSS.

### Slice E — tests (`tests/SchoolCollab.Assignments.Tests.Unit/`)

`RecorderPanelBunitTests.cs`:
- Header renders in the unsupported path (loose JSInterop): title text + `{Id}-cancel` present.
- Cancel click invokes `OnClose` (callback counter).

`InstructionEditorListBunitTests.cs` (adapt the 3 existing Record-affordance tests + add):
- Open (row Record button or `AddRowWithRecorderAsync`): `{Id}-rows`, `fluent-input-file`, `{Id}-add`, row Record buttons **absent**; `{Id}-recorder-takeover` + panel `{Id}-recorder-{index}` **present**; container carries `cq-instructions-recording`.
- Cancel click → list + add-button restored, panel absent, container class gone.
- One-recorder invariant preserved (second `AddRowWithRecorderAsync` moves the takeover).

Each new assertion must fail against the pre-change markup (discriminating). CSS values are not bUnit-assertable — the class/element seams are the pins; the razor-hygiene architecture gates must stay green (no inline `<style>`/`<script>`).

### Expected files (≤ 7)

`src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/{RecorderPanel.razor, RecorderPanel.razor.css, InstructionEditorList.razor, InstructionEditorList.razor.css}`, `tests/SchoolCollab.Assignments.Tests.Unit/{RecorderPanelBunitTests.cs, InstructionEditorListBunitTests.cs}`.

### Acceptance criteria

- **AC-1** — With a recorder open, the list body (rows, dropzone, add button, error line) is not rendered and the panel fills the container (Q2 — both hosts by construction).
- **AC-2** — Panel header with title + labeled Cancel renders in every state, including the unsupported-message state (Q3).
- **AC-3** — Cancel resets the panel (state back to Idle, take discarded) and fires `OnClose`; the host restores the list; the placeholder row remains (Q5).
- **AC-4** — Preview/review elements carry the new fluid sizing classes; the 320px cap and `object-fit:cover` are gone (Q4).
- **AC-5** — Commit flow unchanged: "Use this recording" stages through the seam, list restores, row shows file name + size; existing recorder/list tests still pass.
- **AC-6** — `dotnet build SchoolCollab.slnx` → 0 errors; `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` → 0 failures (parent re-verifies authoritatively).
- **AC-7** — No dialog introduced; no JS module change (`stopCapture`/`clearBlob` already exist); no bytes-over-interop regression; hygiene/best-practices gates green.

### Out of scope (this round)

Spec amendment in `documents/specs/recording-materials.md` (D3 wording, AC-1, §7 layout note) — **parent folds it at acceptance**. No changes to `AssignmentAuthoring.razor`, `QuestionEditDialog.razor`, `RecorderPanel.razor.js`, ward playback, caps, or any API.

### Open decisions

None — the grill above settled the whole frontier.

## Worker Report

WORKER REPORT (persisted by the parent)
Changed files: `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/{RecorderPanel.razor, RecorderPanel.razor.css, InstructionEditorList.razor, InstructionEditorList.razor.css}`, `tests/SchoolCollab.Assignments.Tests.Unit/{RecorderPanelBunitTests.cs, InstructionEditorListBunitTests.cs}` (exactly the expected-files list — parent scope-check: confirmed, 214+/34−, no stray files).
Build: 0 errors — `dotnet build SchoolCollab.slnx` (full solution; warnings pre-existing).
Tests: SchoolCollab.Assignments.Tests.Unit: 1123 passed, 0 failed (5 new: 2 panel header/OnClose, 3 list takeover/cancel/one-recorder).
Deviations from plan:
- Slice A.3: `CancelAsync` reuses the existing `Reset()` helper (ticker stop, `_recorded*`/`_elapsed`/state reset, preview revoke) plus explicit `stopCapture`/`clearBlob`, then clears `_error` and invokes `OnClose` — functionally the plan's reset, not duplicated verbatim.
- Slice C: the error `<p>` moved from after the `@code` block into the new `@else` body (`</div>` now closes before `@code`) — required to suppress the error line while recording; DOM contract unchanged.
- Slice B: also updated the in-rule comment inside `.recorder-panel` that described the old sibling-`<li>` layout (comment-only).
- Slice E: the 3 existing Record-affordance tests needed no adaptation (they pin list-state behaviour the takeover preserves); only new tests were added.
- Build gate: early build attempts hit MSB3021/3027 output locks from a running Aspire dev session + VS (zero compile errors at any point); locks cleared (processes had exited) and the final full build reached 0 errors.

## Review

REVIEW (static diff pass, `mimo-v2.6-flash`-slot reviewer teammate; persisted by the parent)
Verdict: **PASS**
P1: (none)
P2: (none)
Best-practices: no overwrites (deletions confined to the in-plan sibling-`<li>` block and the pre-existing error-after-`@code` oddity, both required by Slice C; comment updates in-plan) / skills honored (no dialog per dialog-ui §4; CSS-isolation scoped-stylesheet conventions intact — no inline style/script, no needless `::deep`; C# follows the repo's existing JS-interop catch and `_disposed`-guard conventions with XML-documented seams) / readable (naming matches repo conventions — `CloseRecorder`/`CancelAsync` mirror `RetakeAsync`/`StopAsync`; `{Id}-` id stem preserved; 214+/34− focused diff; declared deviations (1)–(4) all reviewed and conformant).

Reviewer also confirmed specifically: header precedes all `_probed` branches; `CancelAsync` guards `_disposed`, reuses `Reset()`, catches `JSDisconnectedException`/`ObjectDisposedException` like `DisposeAsync`, invokes `OnClose` last without self-disposal; `InstructionEditorRow` is a sealed class so `Rows.IndexOf` is sound; the else-branch wraps exactly the empty-p/rows/dropzone/add/error body; Slice B CSS values match verbatim; no `::deep` needed for any new rule; exactly the 6 planned files.

## Acceptance

**Verdict: CLOSED** — clean; no residual grill needed (no P1, no P2, no descoped criterion). Transcribed by the parent (Tier 2) on the reviewer's PASS verdict + the parent's authoritative pass. UI-round trigger fired (.razor/.css changed) but Tier 2 dispatches **no UI tester** (tester is a Tier-3 pass by design).

| AC | Result |
|---|---|
| AC-1 takeover hides list body, panel fills container | ✅ static conformance (reviewer) + 3 new list tests |
| AC-2 header + labeled Cancel in every state | ✅ panel test asserts title + cancel on the unsupported path; header precedes `_probed` branches |
| AC-3 Cancel resets panel, fires `OnClose`, list restored, placeholder row stays | ✅ panel OnClose test + list cancel-restore test |
| AC-4 fluid sizing classes; 320px cap + `cover` gone | ✅ reviewer verified Slice B values verbatim in `RecorderPanel.razor.css` |
| AC-5 commit flow unchanged, existing tests pass | ✅ pre-existing recorder/list tests green inside 1123/1123 |
| AC-6 build 0 errors; unit tests 0 failures | ✅ parent-run: `dotnet build SchoolCollab.slnx` → **0 errors** / 15 warnings (all pre-existing: ASPIRE010, NU1903 advisories); `SchoolCollab.Assignments.Tests.Unit` → **1123 passed / 0 failed** (26s); `SchoolCollab.ArchitectureTests.Unit` → **105 passed / 0 failed** (razor-hygiene + best-practices gates cover the new files) |
| AC-7 no dialog, no JS-module change, hygiene gates green | ✅ reviewer confirmed; ArchitectureTests green |

Durable outcomes folded at acceptance: `documents/specs/recording-materials.md` amended — **D3** (takeover layout + labeled Cancel, superseding the sibling-`<li>`/toggle-only wording), **AC-1** (takeover phrasing), **§7 layout note** (amended, original rationale retained for history). Round artifacts: `documents/rounds/diffs-recorder-takeover.patch` (frozen from base `6cb415ec`).

**Gates ahead:** changes are **uncommitted** on `feat/recorder-takeover` — commit/push/PR only on the owner's explicit instruction (repo hold rule). The happy path (permission → record → Blob → Cancel) remains browser-only scope — bUnit covers the seams, not `getUserMedia`.

### Post-acceptance rework (owner, 2026-10-12): Cancel leaves no residue

Owner-reported after live hot-reload preview: Cancel restored the list **with the kebab's appended placeholder row still in it** — recording residue. Q5's "placeholder row stays" clause is **revised**: the row the RECORD ACTION itself appended is now dropped on Cancel while still unstaged (`_recorderCreatedRow` tracking + `DropOrphanedRecorderRow()`, `Changed` fired on removal). A toggled-open pre-existing row and any committed (staged) row survive; switching kebab Record actions also drops the earlier still-unstaged placeholder. Spec D3 amended to match.

Solo bounded rework per the skill's post-round UI-rework rule. Interlude: the owner's VS debug session ran `SchoolCollab.Assignments.Worker`, and because `SchoolCollab.Assignments.Tests.Unit` ProjectReferences the Worker the test build failed MSB3027 until the owner stopped it (lock surfaced per AGENTS.md, not killed). Re-verified after: `dotnet build SchoolCollab.slnx` → **0 errors**; `SchoolCollab.Assignments.Tests.Unit` → **1124 passed / 0 failed** (new `CancelRecorder_RemovesTheRowTheRecordActionAppended`; `…KeepsThePlaceholderRow` renamed to `…KeepsTheToggledRow`; `SecondAddRowWithRecorder_MovesTheTakeover` → `…_AndDropsTheFirstPlaceholder`, its `HaveCount(2)` pin flipped to `ContainSingle(…Video)`); `SchoolCollab.ArchitectureTests.Unit` → **105 passed / 0 failed**. Patch refreshed to include the rework.

### Rework 2 (owner, 2026-10-12): page dropzone hides while the takeover shows

Owner: "hide dropzone too, when recording container shows". The authoring page's `#authoring-materials-dropzone` sits in the same PanelSection *below* the list and survived the takeover. The list now raises a new `[Parameter] EventCallback<bool> RecorderStateChanged` (`NotifyRecorderStateAsync` fired from every open/close path: kebab add, row toggle, Cancel, commit); `AssignmentAuthoring` wires it to `_materialRecorderOpen` and wraps the dropzone **and its refusal bar** in `@if (!_materialRecorderOpen)`. The question dialog leaves the parameter unwired (no sibling surface). New pins: `RecorderState_Changes_AreSignalledToTheHost` (list) and `RecorderTakeover_HidesTheMaterialsDropzone_UntilItCloses` (page). Untouched by design: the separate `ResourcesSection` upload tile (a different card, not part of the recording container).

### Rework 3 (owner, 2026-10-12): no leftover live pane beside the take's player

Owner: after Stop, "pane for live recording is left, showing side-by-side with preview player… fix for video and audio as well". Grounded diagnosis: `endRecording` already releases the tracks (AC-10), so in Review the live `<video>` was a dead frozen frame; and the Review player rendered **inside** the flex `.recorder-controls` row, squeezing it beside the badge/buttons (the audio case). Fix in `RecorderPanel.razor`(+css): the live preview stays in the DOM through Review (RetakeAsync's `startCapture` re-binds it by id) but carries `hidden` there — with a `.recorder-preview[hidden]{display:none}` kill-switch, since the class's `display:block` would beat the UA rule; the take's player (video **and** audio) renders as its own full-width block above the controls row, `.recorder-audio` gaining `display:block`. **Honest gap:** the Review state is unreachable under bUnit (`RecordedMediaResult` is a private nested type; the happy path stays browser-scope per the spec's §6 stance) — verified by build + the owner's live preview, not by a test.

