# Recording materials — live audio & video capture for Content & Resources

**Owner:** stayboy · **Opened:** 2026-10-12 · **Status:** implemented (solo round, 2026-10-12 — build + tests green; see §7)

## 0. What this adds

A **recording** capability — live microphone/camera capture — for the **Content & Resources** section of the
assignment authoring surface. It sits alongside the three existing kebab actions (**Add Text Content**, **Add
Link**, **Upload From Device**) defined in `documents/specs/instructional-materials.md` (D2), and it is also
reachable from a **question's** instruction blocks.

A recording is **not a new kind of material**. It is a second *input path* that produces the same
`InstructionEditorRow` (`Kind = Audio` or `Video`) that an uploaded audio/video file already produces — through
the same staging endpoint, the same `IFileStore`, and the same save payload. The genuinely new surface is the
**capture** (browser `getUserMedia` + `MediaRecorder`) and the **playback** a student needs to consume it.

## 1. The owner's requirement (2026-10-12)

> Add a recording feature — video and audio — for adding to "Resources & Materials", building on the three
> existing features: Text Content, Url and Upload. Explore how to fit it into Blazor for our stack.

## 2. Findings (the ground this stands on)

**The destination and the plumbing already exist.** This is a merge-shaped, not a greenfield, problem:

| Concern | Status today |
|---|---|
| Material kind | `InstructionKindDto` already has `Audio = 2`, `Video = 3`, `Image = 4` (`ContractTypes.cs:108`). **No enum change.** |
| Row shape | `InstructionEditorRow` already carries `FileName`/`ContentType`/`FileSize`/`StoragePath` (`AssignmentQuestionEditorRows.cs:24`). **No row change.** |
| Staging | `StageInstructionMediaAsync → Api.StageAttachmentAsync → POST /assignments/attachments/stage → IFileStore` (`AssignmentRoutes.cs:1370`, local FS; Azure Blob deferred per `AssignmentFileStoreOptions`). **No endpoint, no storage change.** |
| Server allow-list | `AttachmentUploadOptions.AllowedExtensions` already carries `.mp3/.m4a/.wav/.ogg/.aac/.mp4/.webm/.mov/.m4v`; `StagedFileValidator` requires a non-empty content-type. A recording synthesized as `.webm`/`.mp4` with an `audio/*` or `video/*` type passes **as-is**. |
| Kind inference | `InstructionKindInference.ForFile` maps webm→Video, mp4→Video, the audio exts→Audio. |
| Size cap | 25 MiB per file / 100 MiB total today (client `AttachmentUploadPolicy` and server `StagedFileValidator`/`AssignmentContentValidator` agree). **This round raises both** (D7) — see §4 for the Kestrel + config fan-out it forces. |
| Shared editor | `InstructionEditorList` is "one shape, two owners" — the assignment page **and** the question dialog — and its doc-comment records that the question-dialog host *cannot open a dialog of its own (no nesting — dialog-ui §4)*. |

**What is genuinely new:**

- **Capture** — nothing uses `getUserMedia`/`MediaRecorder` anywhere. Greenfield.
- **Playback** — the ward surface renders materials as a **label list only** (`Ward/Assignment.razor:56`), and **no
  endpoint streams an instruction `StoragePath` back** (only certificates stream today —
  `GuardianSignOffRoutes.cs:135`, the `fileStore.OpenReadAsync → Results.Stream` precedent). A recording is
  currently **unplayable** by a student.
- **Ward data is already sufficient** — `WardAssignmentViewDto.Materials` is `IReadOnlyList<InstructionReadDto>`,
  and `GetWardAssignmentViewQueryHandler` already projects `StoragePath`/`ContentType`/`FileSize` onto each
  material. Playback needs **no DTO change** — only a serving endpoint + a ward render.

**Render mode / interop convention.** Both hosts are Blazor Server
(`InteractiveServerRenderMode(prerender: false)` — Admin `App.razor:29`, Families `App.razor:14`), so capture runs
**client-side in the browser via a JS module**, never server-side. The JS-interop convention is a **collocated
`.razor.js` module**: dynamic `import()`, a cached `IJSObjectReference`, `JSDisconnectedException`-safe awaits,
and dispose on teardown — the precedents are `AssignmentAuthoring.razor.js` and `wardPlayer.js`.


## 3. Settled decisions (owner, 2026-10-12, grill round)

| # | Decision |
|---|---|
| **D1** | **A recording is an Audio/Video instruction row, produced by a second input path.** It reuses `InstructionEditorRow`, the `POST /attachments/stage` endpoint, `IFileStore`, the (raised) per-file cap (D7), and the existing allow-list. **No new enum, DTO field, endpoint, or store.** |
| **D2** | **Two kebab items: "Record Audio" and "Record Video"** (mirrors the per-kind symmetry of Add Text Content / Add Link). The kind is known before capture, so the recorder requests exactly the right device constraints. |
| **D3** | **The recorder is a single INLINE presentational panel, not a dialog** *(revised from "dedicated dialog" — see D12; UX-revised 2026-10-12 — see the takeover note below).* Rendered by `InstructionEditorList`. Permission prompt → live preview → record / stop → preview → **retake** → commit, committing through the existing `StageMediaAsync` seam. **Takeover layout (owner 2026-10-12, `round-recorder-takeover.md`):** while a recorder is open, the list hides its whole body (rows, upload dropzone, add affordance) and the panel fills the container; the panel's own header bar carries the title and a **labeled Cancel** (stops capture, discards the take, restores the list, and drops the still-empty row the Record action itself appended — a toggled-open pre-existing row is never touched; owner rework 2026-10-12) — the earlier sibling-`<li>`-beneath-the-row layout and its toggle-only dismissal are superseded. On the page, the kebab appends the row and opens its recorder; in the question dialog the author records from the same takeover (D12's no-nesting rule unchanged). |
| **D4** | **Student playback is in scope.** A minimal `GET` serving endpoint streams the recording's `StoragePath` with its stored `ContentType` (the certificate-route pattern), with **HTTP range processing enabled** so `<audio>`/`<video>` are seekable. |
| **D5** | **Ward render is native inline players.** `<audio controls>` / `<video controls>` for Audio/Video rows bound to the serving URL; `<img>` for Image; Text/Url stay labels. |
| **D6** | **Recordings are instruction-only, never AI-reference** — consistent with the settled instruction-vs-resource split (instructional-materials D10 / INS-3). |
| **D7** | **Raise the per-file cap to 100 MiB and the total to 300 MiB** (Q14/Q15), one shared cap for audio + video + everything (Q16). At ~2.5 Mbps (720p) a 5-min take is ~94 MB — under 100 MiB with headroom; 480p (~1 Mbps) gives ~13 min; audio (Opus ~64–128 kbps) is effectively unlimited. **A byte-based auto-stop at ~95 MiB** (from accumulated `MediaRecorder` chunks) is the guard, not the limiter: it stops a take before it can be lost to a 413, while the raised server cap is the real ceiling. |
| **D8** | **Feature-detect and disable with a pointer to Upload.** At module load, probe `navigator.mediaDevices?.getUserMedia` **and** `window.isSecureContext`; when unavailable, the Record items render disabled with a tooltip pointing at **Upload From Device**. A permission denial at record time shows a named error. Fail-closed, never a dead end. |
| **D9** | **Accept the browser's default `MediaRecorder` output** (webm/opus in Chromium, mp4 in Safari); synthesize a filename with the matching extension (`.webm`/`.mp4`) so kind-inference and the allow-list accept it. **No bitrate/resolution constraint is imposed** (Q13-B raised the cap instead) — broadest device compatibility (esp. Safari/iOS), and both containers are already allowed and map cleanly. |
| **D10** | **Recording is available on BOTH surfaces** — the assignment page kebab **and** a question's instruction blocks (the shared `InstructionEditorList`). |
| **D11** | **A committed recording supports clear-and-record-again**, reusing the existing media-row clear control (`InstructionEditorList.ClearMediaAsync`). A flubbed take is re-recorded, not deleted-and-re-added. |
| **D12** | **No nested dialog.** Because D10 requires recording inside `QuestionEditDialog` (itself a `DialogShellBase` dialog), the recorder **cannot** be a dialog — hence D3's inline-panel revision. One implementation, both owners, no drift. |

## 4. Where it lives

| File | Change |
|---|---|
| `…/Components/Pages/Assignments/RecorderPanel.razor` (+ `.razor.js`) | **New.** The inline capture panel (D3) and its collocated `getUserMedia`/`MediaRecorder` module (blob → stream hand-off, device-track teardown on dispose). |
| `…/Components/Pages/Assignments/InstructionEditorList.razor` | Add the **Record** affordance in the media row's capture slot (beside the existing `FluentInputFile`); host `RecorderPanel`; keep the list the single writer of its rows. |
| `…/Components/Pages/Assignments/AssignmentAuthoring.razor` | Add the two kebab actions (D2) to `MaterialActions`; append the row and auto-expand its recorder. |
| `…/Components/Pages/Assignments/QuestionEditDialog.razor` | No new code to *reach* recording — its inline `InstructionEditorList` slot (D10) gains it via the shared component; it already passes `StageMediaAsync`. |
| `…/Assignments.Api/Endpoints/AssignmentRoutes.cs` | **New** `GET` media-serving route (D4): resolve the instruction, `fileStore.OpenReadAsync(path) → Results.Stream(stream, contentType)` with range processing, mounted beside the ward modules route under the authenticated group. |
| `…/Families/Components/Pages/Ward/Assignment.razor` | Render native players for Audio/Video/Image materials (D5) using the already-present `StoragePath`/`ContentType`. |
| `…/Core/Services/AttachmentUploadOptions.cs` | Raise `MaxFileSizeBytes` 25→**100 MiB** and `MaxTotalSizeBytes` 100→**300 MiB** (D7). |
| `…/Helpers/AttachmentUploadPolicy.cs` | Mirror `MaxFileSizeBytes` to 100 MiB (client/server lockstep); add the ~95 MiB byte-stop constant. Allow-list needs no change (already carries the media exts). |
| `…/Assignments.Api` (Program.cs or appsettings) | **New** `Kestrel:Limits:MaxRequestBodySize` ≈ **120 MiB** (per-file cap + multipart overhead). Today unset (~30 MB framework default) — the reason the old cap was 25 MiB. |
| `src/AppHost/…/Program.cs` + `appsettings.json` `Parameters:` | Raise the `assignment-upload-max-file-bytes` / `assignment-upload-max-total-bytes` parameter defaults in lockstep (`appsettings.json` `Parameters` is the canonical record). |
| `documents/configuration.md` §2/§13 | Update the documented production caps in the same PR (the AppHost comment mandates lockstep). |
| Tests (`…Tests.Unit`) | bUnit seams for the panel (capability-disabled state, byte-stop, retake), the two new kebab actions, the ward player render, and the media route (content-type + range + 404); a lockstep test asserting client/server caps match (AC-11). |

## 5. Acceptance criteria

- **AC-1** — On the page, "Record Audio"/"Record Video" append an Audio/Video row and open the recorder as a **full-container takeover** (the list body hides; owner 2026-10-12 revision).
- **AC-2** — The recorder requests mic-only vs mic+camera per the chosen kind; a live preview renders before recording.
- **AC-3** — Record → stop → preview → **retake** loops without leaving the panel; commit stages through the same endpoint an upload uses, and the row shows the recorded file name + size.
- **AC-4** — A **5-minute 720p** video (~94 MB) and a 10-minute audio note both record, stage, and save **without** hitting any limit (D7's raised 100 MiB per-file / 300 MiB total caps).
- **AC-4b** — The ~95 MiB byte-based auto-stop halts a take before the per-file cap; a take that still exceeds 100 MiB (or pushes the assignment past 300 MiB total) is rejected with a **named error**, and the stage request never trips Kestrel's raised `MaxRequestBodySize`.
- **AC-5** — On an insecure context or a device with no mic/camera, the Record items are disabled with a tooltip pointing at **Upload From Device**; a denied permission shows a named error.
- **AC-6** — Recording is reachable from a question's instruction blocks (D10) with no nested dialog.
- **AC-7** — A committed recording supports clear-and-record-again.
- **AC-8** — A student's ward surface renders the recording in a native, **seekable** player (HTTP range works); the media URL is authenticated like the ward data it serves.
- **AC-9** — A recording does **not** enter the AI-reference set.
- **AC-10** — The recorder disposes its device tracks and `IJSObjectReference` on teardown (camera/mic light off after navigating away).
- **AC-11** — Client (`AttachmentUploadPolicy`), server (`AttachmentUploadOptions`), AppHost parameter defaults, and `documents/configuration.md` all report the same raised caps (lockstep — D7); Kestrel's `MaxRequestBodySize` exceeds the per-file cap so a max-size take stages cleanly.

## 6. Open / deferred

- **Python portal playback** (families/portals surface) — deferred to its own round, as with the rest of D11 in
  `instructional-materials.md`.
- **Transcription / AI grounding of a recording** — out of scope (D6); a teacher who wants a recording summarised
  into questions is a *resources*-side feature, not this one.
- **Azure Blob** backing for `IFileStore` — already deferred (`AssignmentFileStoreOptions`); recording inherits
  whatever store is configured, no special-casing. **Note for ops (D7):** the raised caps quadruple peak staging
  disk on the local FS (up to 300 MiB per assignment, 48 h retention). Fine for dev/single-tenant; on the Azure
  Blob move this becomes a non-issue, and it is a reason to prioritise that migration once recording ships.
  Blob move this becomes a non-issue, and it is a reason to prioritise that migration once recording ships.

## 7. Implementation (solo round, 2026-10-12)

**Shipped** (all changes uncommitted on `main`, pending the owner's review/commit gate):

| Area | Files |
|---|---|
| D7 cap raise (100/300 MiB) + Kestrel 120 MiB | `AttachmentUploadOptions.cs`, `AttachmentUploadPolicy.cs` (+`RecorderAutoStopBytes`), API `appsettings.json`, AppHost `Program.cs` + `appsettings.json`, `documents/configuration.md` |
| D4 media-serving route | `AssignmentRoutes.cs` — `GET /assignments/{id}/students/{studentId}/materials/{instructionId}`, `Results.Stream(…, enableRangeProcessing: true)`, 404s for missing assignment/instruction/file |
| D5 ward playback | `FamiliesApiClient.GetInstructionMediaAsync`, new `wwwroot/js/mediaPlayer.js` (bytes → Blob URL), new `Ward/WardMaterialPlayer.razor` (native `<video>/<audio>/<img>`, revoke-on-dispose), `Assignment.razor` routes media kinds to it |
| D2/D3/D10 capture | new `RecorderPanel.razor` (+ `.razor.js` collocated module, `.razor.css`), `InstructionEditorList.razor` (Record affordance + sibling-`<li>` recorder + `AddRowWithRecorderAsync`), `AssignmentAuthoring.razor` ("Record Audio"/"Record Video" kebab actions) |
| Tests | `AttachmentUploadCapLockstepTests` (AC-11), `RecorderPanelBunitTests` (AC-5 both sides), 3 Record-affordance tests in `InstructionEditorListBunitTests`, media-player test in `WardAssignmentPlayerBunitTests`, 3 media-route tests in `WardRoutesTests`, oversized-cap expectation updated |

**Deviation found & fixed during review:** the collocated module import initially used a bare
`./Components/…` path — an RCL's `*.razor.js` is served from `_content/{assembly}/…` (it would have 404'd
at runtime; bUnit loose mode masks this). Now `ModulePath` with the `_content` prefix + a `JSException`
fallback to the fail-closed capability message.

**Critical fix — the recording never crosses the circuit as one `byte[]` (review, 2026-10-12).** The
first cut returned the whole take from `endRecording` as a single JS→.NET `byte[]`. That is **blocked in
Blazor Server**: `RemoteJSRuntime.ReceiveByteArray` enforces `HubOptions.MaximumReceiveMessageSize`
(**default 32 KB**) and throws `Exceeded the maximum byte array transfer limit for a call`
(dotnet/aspnetcore#38941 — raising it to 100 MB still failed; the team's answer: *stream, don't marshal*).
A real 5-minute take (~94 MB) would have thrown on the first genuine use — invisible to bUnit, which
never runs the JS. Refactored so **no bytes cross interop**:
- `endRecording` returns **metadata only** (content-type, name, size); the take stays in the browser as a
  module-held `Blob`.
- `createPreviewUrl()` builds the AC-3 playback URL straight from that Blob (still fully client-side).
- `commitBlob(selector, name)` wraps the Blob as a `File` and writes it into a **hidden `<InputFile>`**
  (assign `input.files` + dispatch `change` — the exact technique `FluentInputFile.initializeFileDropZone`
  already uses). The component's `OnChange` → `IBrowserFile.OpenReadStream(100 MiB)` streams it in bounded
  chunks — **the same proven path this repo uses for its 100 MiB uploads**, which is exempt from the
  per-call cap by design.
- The host seam `OnRecorded(Stream, …)` is unchanged, so `InstructionEditorList` needed no change.

**Layout decision (amended 2026-10-12, `round-recorder-takeover.md`):** the `RecorderPanel` is no longer a sibling `<li>` beneath the media row — while open it takes over the **whole `InstructionEditorList` container** (rows, dropzone and add affordance hidden), with a header bar carrying the title and a labeled Cancel. The original sibling-`<li>` rationale below is retained for history: it motivated why the panel is not inside the row's non-wrapping flex line.

**Disposal race (browser-only):** staging success unmounts the panel *while* its commit is still awaiting
`OnRecorded`; the component now guards `StateHasChanged` + every JS call behind `_disposed`/null-module
checks so it never touches a disposed module (AC-10).

**Verification:** `dotnet build SchoolCollab.slnx` → 0 errors / 15 warnings (all pre-existing).
Test runs: Assignments.Unit **1118/1118**, Families.Unit **47/47**, Api.Tests.Unit **169/169**,
Assignments.Integration **25/25**, ArchitectureTests **105/105** (razor-hygiene + best-practices gates
cover the new files), Admin.Unit green (one pre-existing `EnrollStudentDialog` bUnit flake passes on
re-run, unrelated to this work).

**Not covered (honest gaps):** the recorder's *happy path* (permission → record → Blob) is browser-only
and untestable under bUnit/CI — it is UI-tester/manual scope, as the spec's §6 stance on JS pipelines
already sets. No UI-tester round has run against this change yet.

## 8. Media listing & metadata round (Tier 3, 2026-10-14 — `round-media-pill-listing.md`)

**The listing shape is NOT a "pill".** The owner corrected the solo `round-media-item-pill` invention: a
staged Audio/Video row follows the Text-row shape of `instructional-materials.md` **AC-14** —
`[kind glyph] [readout] [action] [×]` — with the row's standard leading kind glyph and trailing `×` **kept**,
the **readout** showing the duration, and the **action slot** holding a **play** icon button exactly where
Text/Link render their edit pencil. The `.cq-media-play` rounded-pill CSS is retired.

| # | Decision |
|---|---|
| M1 | **Readout.** `2:34 [Title]` (ellipsis-truncated) when titled; `2:34` untitled with a known duration; `—:—` when the duration is unknown; Image → `[Title]` when titled, else today's size-text readout. The file name rides the native `title` tooltip. |
| M2 | **Media metadata — optional Title + optional Body/Description for Audio/Video AND Image.** `Title`/`Text` **already existed** on `AssignmentInstruction`, both DTOs and the ward DTO, and the validator's media branch never forbade them ⇒ **no migration and no contract widening for the metadata**. Title and body are independent and both optional. |
| M3 | **Editing surface.** `MaterialEditDialog` extended to the media kinds, opened from the row's **readout** click on the Dialog host; the Inline host (question dialog — no nested dialogs) edits the same pair **inline on the row**. Recorder commit ("Use this recording") **pops that dialog before staging** on the Dialog host; the Inline host stages directly. The seeded review carries a link-styled "Edit title/description" control **only when metadata exists**, effective immediately on both hosts. **Preview toggle (post-close rework, owner 2026-10-14):** the dialog's description slot doubles as the media preview — an eye-icon toggle (rendered only when a preview can resolve) swaps Description ⇄ native `<video>/<audio>/<img>`; the Blob URL resolves lazily through the list's session/served seam on the first press, so opening the dialog never streams a file. **Post-close correction (owner 2026-10-14):** the media Description IS the shared rich editor — Q10's letter ("mirroring its Text branch's Title + rich Body") — read and sanitised at submit exactly like a Text body; the toggle captures the editor's HTML before the preview unmounts it, so an edit survives either order; the ward prints the media body through `RichInstruction` (sanitised markup, the tree's one MarkupString); the Inline host's pair stays plain. |
| M4 | **Saved playback.** New teacher-authed, range-enabled route `GET /assignments/{id}/materials/{instructionId}` (mirrors the ward media route minus `studentId`; role-less bearer, tenant-scoped by the assignment row). `AssignmentsApiClient.GetInstructionMediaStreamAsync` + **`DotNetStreamReference`** stream .NET→JS (never a `byte[]` interop array) into a Blob URL; the play button reopens the `RecorderPanel` seeded review (playback only). |
| M5 | **Duration persistence.** Nullable `duration_seconds` on `assignment_instructions` (`AddAssignmentInstructionDurationSeconds`, additive, **no backfill** — pre-migration rows read `null` and render `—:—`). The record-time probe is reused; uploads are probed client-side (best-effort, `—:—` fallback). Threaded through **eight** instruction-tuple projection sites — Create ×2, Update ×3, Duplicate ×2, QuestionsDraft ×1; the **duplicate and draft-confirm** sites are the silent-null traps (a trailing-defaulted ctor param compiles unchanged). |
| M6 | **Ward body render.** `WardMaterialPlayer` renders the media body (`Material.Text`) under the player/label. |

**Gates (parent-authoritative):** `dotnet build SchoolCollab.slnx` 0 errors; Assignments.Unit **1151/0**,
Assignments.Api.Unit **172/0**, Families.Unit **48/0**, ArchitectureTests **111/0**, Assignments.Integration
**26/0** (real-Postgres migration pin ran). Plan ACCEPT; diff PASS (one P1 fixed — the AC-4 metadata-dialog
cancel path); UI tester PASS.

**Accepted residuals (owner, 2026-10-14):** the post-cancel `_reviewSeed` re-seed is defensive and not
separately discriminated by its pin; the upload-duration probe depends on `input.files` surviving
`FluentInputFile`'s completion (falls back to `—:—`; recordings unaffected); a served play on a
create/unsaved row returns the named inline error (nothing server-side to serve yet).

**Still open / deferred (unchanged from §6):** Python portal playback, transcription/AI grounding, and the
Azure Blob `IFileStore` migration.


