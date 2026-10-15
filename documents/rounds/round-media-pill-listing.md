# Round — media pill listing (audio/video in Content & Resources)

**Status:** CLOSED (2026-10-14 — changes left **uncommitted** on `feat/media-pill-listing` pending the owner's commit gate; plan ACCEPT · diff PASS · UI-tester PASS)
**Mode:** Tier 3 full (owner-confirmed 2026-10-14) · **provider:** `ollama-cloud` (pi `ollama` profile, one provider) · **models:** orchestrator `ollama-cloud/glm-5.3-flash`, plan gate `ollama-cloud/glm-5.3`, worker `ollama-cloud/deepseek-v4.1-flash`, diff reviewer `ollama-cloud/kimi-k2.7-code`, UI tester `ollama-cloud/minimax-m3`
**Base:** `95949cbd1c8bfbbfd1d05d48dd6e401d31451c35` (origin/main at round start) · **Branch:** `feat/media-pill-listing` · **Tree at start:** clean (only untracked `push-out.txt`)

## Owner grill (2026-10-14) — settled decisions

> **Owner correction (2026-10-14, post-grill — authoritative):** the one-pill shape (old Q3) is
> **superseded** by C1; media metadata (C2) and the ward body render (C3) are added. The
> `## Plan` below is written to the corrected decisions; the stale one-pill rows are marked superseded,
> not deleted (provenance). **Correction date:** 2026-10-14.

| # | Decision |
|---|---|
| Q1 | Surface = the **instruction-materials list** (`InstructionEditorList`) inside the **Content & Resources** `PanelSection` (`AssignmentAuthoring.razor:471`) |
| Q2 | **Saved/uploaded playback is in scope** — a new teacher-authenticated serving route; uploaded and post-reload rows become playable |
| Q3 | **SUPERSEDED by C1 (owner correction, 2026-10-14):** the staged Audio/Video row follows the **Text-row shape** (instructional-materials.md AC-14 / round-material-row-editing): `[kind glyph] [readout] [action] [×]` — the row's standard leading kind glyph and its standard trailing `×` are **kept** (NOT absorbed into anything; `×` keeps its remove meaning). **No pill** — the `.cq-media-play` rounded-pill CSS is retired (no rounded pill, no border). The **readout** shows the duration `m:ss`, or `—:—` when the duration is unknown. The **action slot** holds a PLAY icon button (Appearance=Stealth, IconStart=Play, aria-label="Play", id `{Id}-play-{index}`) exactly where Text/Link render their edit pencil; Text/Link rows keep their pencil; Image rows keep their standard shape |
| Q4 | **Audio/Video only** — Image keeps its readout |
| Q5 | Mode = **Full Tier 3** |
| Q6 | Play opens the **`RecorderPanel` seeded review** (F2 reuse) |
| Q7 | **Persist** a nullable `DurationSeconds` (entity + `InstructionReadDto` + save payload + editor row) → **migration**; record-time probe reused, upload probed client-side |
| Q8 | Bytes cross .NET→JS via **`DotNetStreamReference`** (stream, never a giant `byte[]`) |
| Q9 | Route = `GET /assignments/{id}/materials/{instructionId}`, range-enabled, teacher bearer, mirroring the ward route |
| Q10 | **(C2, owner correction 2026-10-14) Media metadata** — optional Title + optional Body/Description for Audio/Video **and** Image. `Title`/`Text` **already exist** on `AssignmentInstruction`, `NewInstructionDto`, `InstructionReadDto` and the ward DTO, and `InstructionDtoValidator`'s media branch requires only file name/content-type/storage path (it does not forbid title/body) ⇒ **NO migration and NO contract widening for the metadata — only `DurationSeconds` is new**. Readout: titled → `2:34 [Title]` with **ellipsis truncation** on the readout; untitled → `2:34`; unknown duration → `—:—`; Image → `[Title]` (no duration); file name rides the native `title` tooltip. Editing = **extend `MaterialEditDialog`** to the media kinds (Title + Body/Description, mirroring its Text branch's Title + rich Body), opened **from the row's readout** (click `2:34 [Title]`) — the play button stays purely preview; `ApplyEditAsync` already writes only Kind/Title/Text/Url and preserves the media file fields, so the extension is safe. Recorder commit: **"Use this recording" pops the metadata dialog first**; only on confirm is the take committed carrying title/body; a cancel stays in the review. Seeded preview keeps a **link-styled** title/description edit control, rendered ONLY when a title/description exists. Title and body are independent and both optional — **no new validator rule** |
| Q11 | **(C3, owner correction 2026-10-14) Ward/student surface in scope:** `WardMaterialPlayer.razor` (Families) renders the media **body** under the player/label (the DTO already carries `Text`) — one render + one bUnit pin |
| Q12 | **(OD-1, owner-pinned 2026-10-14, option a):** the Inline-mode host (question dialog, no nested dialogs) edits media metadata via an **INLINE Title/Body field pair ON THE ROW** (mirroring the inline fields Text/Url rows already render in Inline mode); the recorder commit **pops nothing there** — the take stages as today and the freshly staged row's metadata is edited from its readout click. The Dialog-mode host (authoring page) gets the **full C2 dialog flow** |
| Q13 | **(OD-2, owner-pinned 2026-10-14, option a; refines Q10's Image clause):** a **TITLED** Image row reads `[Title]`; an **UNTITLED** Image row keeps today's size-text readout |

## Plan

### Goal

Every staged Audio/Video instruction row in the Content & Materials list (`InstructionEditorList`, both
hosts: the authoring page's `PanelSection` and the question-edit dialog) reads exactly like a Text row
reads (C1 — instruction-materials.md AC-14 / round-material-row-editing):
`[kind glyph] [readout] [action] [×]`. The row's **standard leading kind glyph and standard trailing
`×` are kept** — nothing is absorbed. The **readout** shows the persisted/probed duration `m:ss` (or
`—:—` when unknown; a titled Image shows `[Title]`, an untitled one keeps today's size-text readout
— Q13) with ellipsis truncation when a title is shown (`2:34 [Title]`); the file name rides the native
`title` tooltip. The **action slot** holds a PLAY icon
button (Stealth, IconStart=Play, aria-label="Play", id `{Id}-play-{index}`) where Text/Link render
their edit pencil — Text/Link keep their pencil, and **no pill survives**: the `.cq-media-play` rounded
CSS is retired. **The readout is the edit affordance**: clicking `2:34 [Title]` opens a
`MaterialEditDialog` extended to the media kinds (optional Title + optional Body — Title/Text already
exist on the entity, DTOs and validator, so **only `DurationSeconds` is new**: no metadata migration, no
contract widening) on the Dialog-mode host — the Inline-mode host edits the same optional Title + Body
inline ON the row (Q12). Every **staged (saved/uploaded) row plays**, not only takes recorded in the
current session: saved bytes are served by the **teacher-authed range-enabled route**
(`GET /assignments/{id:guid}/materials/{instructionId:guid}`) and stream .NET→JS as a
`DotNetStreamReference` into a Blob URL; the play button reopens the **`RecorderPanel` seeded review**
(Q6, F2 reuse) — playback only. On the Dialog-mode host the **recorder commit flow** pops the same
metadata dialog before committing a take ("Use this recording" → dialog → confirm commits with
title/body, cancel stays in the review); on the Inline-mode host the commit pops nothing — the take
stages as today and its metadata is edited from the staged row's readout click (Q12). The
**ward/student surface renders the media body** under the player (C3 — one render + one bUnit pin). A
nullable `DurationSeconds` is persisted (Q7): the record-time probe is reused and uploads are probed
client-side at staging. Image rows keep their standard row shape (Q4; titled-image readout per Q13);
Text/Url rows are untouched.

### In scope

- Entity + EF config + migration: nullable `duration_seconds` on `assignment_instructions`. **This is
  the only schema/contract change of the round** — media Title/Body requires none (Q10).
- Contract widening: `NewInstructionDto` / `InstructionReadDto` gain `int? DurationSeconds = null`;
  all three instruction read-mappers and both save paths thread it; `InstructionEditorRow` carries it
  (`ToDto()` + `FromRead` round-trip). (Their `Title`/`Text` fields already exist — the ward DTO included.)
- Teacher media serving route + `AssignmentsApiClient.GetInstructionMediaStreamAsync` (headers-read
  stream, 404→null) + a new JS stream→Blob-URL helper.
- `InstructionEditorList` row shape (C1): the staged Audio/Video branch renders the Text-row shape —
  leading kind glyph kept, readout (`m:ss` / `—:—` / `2:34 [Title]` ellipsis / Image `[Title]`),
  a PLAY stealth icon button in the action slot, trailing `×` kept; `.cq-media-play` pill CSS retired;
  session takes play from the kept Blob URL (unchanged), every other staged Audio/Video row plays
  through the served route; served-fetch failures surface a named inline error.
- Media-metadata editing (C2): `MaterialEditDialog` extended to Audio/Video(Image) — optional Title +
  optional Body/Description, mirroring its Text branch (Title + rich Body) and treating both as optional
  in the dialog's validator mirror; opened from the row's READOUT via the list's existing
  `EditRowRequested` seam (the play button stays purely preview). The list stays dialog-free — the
  hosts already own the dialog. The Inline-mode host (question dialog) edits the same pair inline ON
  the row instead (Q12 — no nested dialog).
- Recorder commit flow (C2): `OnRecordedAsync` intercepts "Use this recording" — the take's metadata
  dialog opens first; on confirm the take stages carrying title/body; on dialog cancel the review stays
  open with the take uncommitted. Dialog-mode host only (Q12): the Inline-mode host pops nothing — the
  take stages as today and the freshly staged row's metadata is edited from its readout click.
- Seeded-preview metadata control (C2): a link-styled title/description edit control on the seeded
  review panel, rendered only when a title/description exists.
- Ward/body render (C3): `WardMaterialPlayer.razor` renders the media body under the player/label —
  one render + one bUnit pin (Families in scope).
- Upload-path duration probe (client-side, best-effort) for both upload entries: the row capture slot
  and the page dropzone.
- Host wiring: page + question dialog supply a `LoadInstructionMediaAsync` delegate (the list stays
  presentational — it holds no API client).

### Out of scope

- Ward playback transport: `FamiliesApiClient.GetInstructionMediaAsync` keeps its byte[] fetch — only
  the body render (C3) is Families work this round; the player's transport and Blob-URL pipeline are
  already shipped and working.
- Metadata server-side validation: title/body stay optional and independent — no new validator rule
  (Q10); no ward-side edit surface for metadata either (Families is read/preview only beyond the C3 body).
- Python portals, AI-reference use (D6), clear-file revival, `RecorderPanel` capture behaviour,
  cap changes, and any write-back route for probed durations (a reload-fetched duration is
  display-only and is NOT persisted; persisting it would need a PATCH endpoint — named non-goal).
- Enum widening, resources, questions-draft staging logic (duration and any media title/body already
  ride `NewInstructionDto` through the draft re-serialization verbatim; no draft-logic change).

### Expected files

| File | Why |
|---|---|
| `…/SchoolCollab.Assignments.Core/Domain/AssignmentInstruction.cs` | nullable `int? DurationSeconds` + trailing default ctor param (the `title` threading pattern) |
| `…/SchoolCollab.Assignments.Core/Domain/Assignment.cs` | thread the field through `AddInstructionItems` / `SetAssignmentInstructionItems` / `SetInstructionItems` tuples → ctor |
| `…/SchoolCollab.Assignments.Core/Data/Configurations/AssignmentConfiguration.cs` | map `DurationSeconds` inside the `OwnsMany(InstructionItems)` block (snake `duration_seconds`, nullable) |
| `…/SchoolCollab.Assignments.Core/Migrations/AddAssignmentInstructionDurationSeconds.cs` (+ `.Designer.cs`, `AssignmentsDbContextModelSnapshot.cs`) | additive nullable column; named per `{Verb}{Entity}{Property}` |
| `src/Assignments/SchoolCollab.Assignments.Contracts/ContractTypes.cs` | `DurationSeconds` on `NewInstructionDto` + `InstructionReadDto` (additive, defaulted) |
| `…/CreateAssignmentCommand/CreateAssignmentCommandHandler.cs` | thread the field into BOTH instruction-tuple projections — assignment-own (`:107`) and question rows (`:188`) |
| `…/UpdateAssignmentCommand/UpdateAssignmentCommandHandler.cs` | all THREE projections: null→preserve (`:79`, must copy `existing.DurationSeconds`), command rows (`:90`), question rows (`:192`) — or an unrelated edit strips saved durations |
| `…/DuplicateAssignmentCommand/DuplicateAssignmentCommandHandler.cs` | duplicate copies the assignment-own (`:94`) and question-row (`:124`) tuples — the trailing-defaulted ctor param means omitting it **compiles unchanged** yet silently nulls every duplicated duration; listed explicitly so it cannot be forgotten |
| `GetAssignmentByIdQueryHandler.cs` (:140), `GetAssignmentAuthoringChildrenQueryHandler.MapInstruction`, `GetWardAssignmentViewQueryHandler.cs` (:68) | the three read-mappers emit `DurationSeconds` |
| `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` | `GetInstructionMediaStreamAsync`: `GetAsync` + `HttpCompletionOption.ResponseHeadersRead` + `ReadAsStreamAsync`, 404→null (the scripted-`HttpMessageHandler` pattern `WardRoutesTests.cs` already uses — never `ReadAsByteArrayAsync`) |
| `…/AssignmentQuestionEditorRows.cs` | `InstructionEditorRow.DurationSeconds`; `ToDto()` carries it; new `public delegate Task<Stream?> LoadInstructionMediaAsync(InstructionEditorRow row)` |
| `…/AssignmentEditFormModel.cs` | `FromRead` maps the duration (dropping it would silently null the column on the author's next save) |
| `…/InstructionEditorList.razor` | C1 row shape for the staged media branch (glyph + readout + play action + trailing `×`; readout-click → `EditRowRequested`); the served-play fetch path; `LoadInstructionMediaAsync` parameter; probe call after staging; the recorder-commit metadata-dialog pop |
| `…/MaterialEditDialog.razor` | C2: extended to the media kinds — Audio/Video/Image render Title (optional) + Body/Description (optional), mirroring the Text branch's Title + rich Body; the validator mirror treats both as optional for media (file fields untouched) |
| `…/MaterialEditDialogModels.cs` | C2: `MaterialEditFormModel`/`FromRow` drop the “media never comes through here” restriction (Title/Body/Url fields already cover media; `MaterialEditResult` is unchanged and already carries Title + Body) |
| `src/SchoolCollab.Families/Components/Pages/Ward/WardMaterialPlayer.razor` | C3: render the media body under the player/label (the DTO already carries `Text`); native `title` tooltip keeps the file name on the ward readout too |
| `…/InstructionEditorList.razor.js` | new exports `createMediaUrlFromStream(streamRef, contentType)` (DotNetStreamReference → Blob URL) and `probeDurationFromInput(selector)` (reads the File behind a staged `<input type=file>`). **The duration probe is NOT a third copy:** `RecorderPanel.razor.js:185` already exports `mediaDuration(url)` (consumed by the recorder's own `StopAsync`) — the probe stays single-location in the recorder module and the list/page import it from there. Three consumers, one implementation |
| `…/InstructionEditorList.razor.css` | **retire the `.cq-media-play` pill styling** (no rounded pill, no border — C1); readout ellipsis truncation (`text-overflow: ellipsis`) for `2:34 [Title]`; no `::deep`, no inline styles for new rules |
| `…/AssignmentAuthoring.razor` | wire `LoadInstructionMediaAsync` (page → `Api.GetInstructionMediaStreamAsync`) + call the module's `probeDurationFromInput` with the `#authoring-materials-dropzone input[type=file]` selector on material drops |
| `…/QuestionEditorSection.razor` + `QuestionEditDialog.razor` (+ `QuestionEditDialogTypes.cs` `QuestionEditModel.ForEdit`) | thread the load seam exactly as `StageInstructionMediaAsync` already threads (one seam, two owners) |
| `tests/SchoolCollab.Assignments.Tests.Unit/InstructionEditorListBunitTests.cs` | row-shape/play/readout-click pins (Slice F) |
| `tests/SchoolCollab.Assignments.Tests.Unit/MaterialEditDialogBunitTests.cs` (existing — extended) | media-kind dialog pins: opens seeded with title/body, optional-fields validator, result carries Title/Body for media (Slice F) |
| `tests/SchoolCollab.Families.Tests.Unit/WardAssignmentPlayerBunitTests.cs` (existing — extended) | one ward body-render pin (C3, Slice G) |
| `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentInstructionsTests.cs` | aggregate/save round-trip pins for the field |
| `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentsApiClientInstructionMediaTests.cs` | scripted-`HttpMessageHandler` client pins (404→null, stream semantics) |
| `tests/SchoolCollab.Assignments.Api.Tests.Unit/TeacherMaterialRouteTests.cs` | route pins (mirror `WardRoutesTests` media tests) |
| `tests/SchoolCollab.Assignments.Tests.Integration/AddAssignmentInstructionDurationSecondsMigrationTests.cs` | real-Postgres migration pin (`AssignmentInstructionsResponseKindsMigrationTests` shape) |

### Slice A — persistence + migration (Q7)

1. `AssignmentInstruction` gains `int? DurationSeconds` (whole seconds — the readout renders `m:ss` and
   `FormatDuration` already rounds; `double?` would carry probe precision no surface reads). The ctor
   gains a **trailing, defaulted `int? durationSeconds = null`** parameter — the exact pattern the
   `title` threading used, so the existing constructions keep compiling and the round-trip tests are
   the gate, not the compiler.
2. `AssignmentConfiguration`: inside the instruction `OwnsMany`, alongside `Title` — a nullable
   property, no max length, no default. `MigrationGuardTests.NoUncommittedModelChanges` (existing)
   fails the run until the migration exists.
3. Migration **`AddAssignmentInstructionDurationSeconds`** — an additive
   `AddColumn(nullable: true, "duration_seconds")` on `assignment_instructions`. **No backfill
   transform**: server-side duration backfill would require a media-parsing dependency the repo does
   not have; **old rows therefore read `DurationSeconds = null`** and the UI falls back (Slice E.5,
   AC-2). `Down()` drops the column (inverse order). Review the generated script against the
   rule-file trap list (no accidental column drop/recreate); commit the snapshot alongside.

   **Metadata requires none of this** (Q10): `Title`/`Text` already live on `AssignmentInstruction`,
   `NewInstructionDto`, `InstructionReadDto` and the ward DTO — verified: `InstructionDtoValidator`'s
   media branch (InstructionDtoValidator.cs:66) requires only the file name/content-type/storage path
   and does not forbid title/body.

### Slice B — contract + handler wiring

`NewInstructionDto` / `InstructionReadDto` gain trailing `int? DurationSeconds = null` (additive,
mirrors `Title`). **All SEVEN instruction-tuple projection sites thread it** —
`CreateAssignmentCommandHandler.cs:107` (assignment-own) and `:188` (question rows);
`UpdateAssignmentCommandHandler.cs:79` (the **null→preserve** branch must copy
`existing.DurationSeconds` verbatim), `:90` (command rows), `:192` (question rows);
`DuplicateAssignmentCommandHandler.cs:94` (assignment-own) and `:124` (question rows). These are the
only ctor-feeding projections — the only ctor call sites are `Assignment.cs:419/448/480`. **The
duplicate handler is the trap:** a "create one / update two" inventory misses it entirely, and because
the ctor param is trailing-defaulted, duplicating compiles unchanged even with the field never
threaded — silently nulling every duplicated row's persisted duration. Hence its explicit listing
above, its Expected-files row, and its pin in the Slice F persistence test.
The three read-mappers emit it. `InstructionDtoValidator` is untouched (a media-kind
field, nothing to validate; and per Q10 the metadata title/body stays optional — no new rule).
`AssignmentEditFormModel` maps it on load and `ToDto()` writes it back.
The questions-draft payload carries the field verbatim via `NewInstructionDto` — no draft-logic change.

### Slice C — teacher-serving route (Q2/Q9)

`MapAssignmentRoutes` gains `GET /assignments/{id:guid}/materials/{instructionId:guid}` beside the
ward media route, with a handler body copied minus `studentId`:
`assignmentRepository.GetAsync` → find `InstructionItems[id]` → require `HasMedia` + `StoragePath` →
`fileStore.OpenReadAsync` → `Results.Stream(stream, contentType, enableRangeProcessing: true,
fileDownloadName: instruction.FileName)`; 404 on missing assignment / instruction / non-media row /
missing backing file (`FileNotFoundException`); content-type falls back to `octet-stream`. **Auth
posture:** it inherits the `/assignments` group policy — authenticated bearer + resolved tenant
context; mirroring the ward media route it deliberately adds **no** reader role policy (the same
role-less-bearer reachability as the ward data it mirrors; the `FEATURE:DisableOIDCAuth` dev posture
is unchanged, as for every route on this group). Tenancy comes from the assignment row the tenant
query filter already scopes — the `IFileStore` path is never user-supplied.

*(Plan-review note, 2026-10-14, P2, report-only: the reviewer's role-less-bearer observation is
**ACCEPTED** as restating the Q9-pinned posture — no change to the plan.)*

### Slice D — transport (Q8) + client seam

- `AssignmentsApiClient.GetInstructionMediaStreamAsync(Guid assignmentId, Guid instructionId, ct) →
  Stream?`: the client's HttpClient `GetAsync` with **`HttpCompletionOption.ResponseHeadersRead`**,
  404→`null`, success → `response.Content.ReadAsStreamAsync(ct)` — the server never materializes a
  media file as a whole byte[] unless the stream is consumed.
- The **.NET→JS** half never marshals a byte array — the JS→.NET direction is already blocked at a
  32 KB single array (dotnet/aspnetcore#38941), and a .NET→JS `byte[]` in Blazor Server round-trips
  as an oversized number array. `InstructionEditorList` wraps the stream in `new
  DotNetStreamReference(stream)` and invokes `createMediaUrlFromStream`; JS receives it via
  `await streamRef.arrayBuffer()` and builds the `Blob` URL (same-origin, seeks natively without
  Range round-trips). Same ownership rules as the existing session map: entered into `_sessionMedia`,
  revoked exactly once (row remove / re-commit / the list's `DisposeAsync`), never double-owned.
- **The duration probe stays single-location in `RecorderPanel.razor.js`** — `mediaDuration(url)` at
  `:185` is already exported there and consumed by the recorder's own `StopAsync`, so it must not move.
  Import direction, explicit: the **list and the page import the recorder's module** for the probe
  (all three files are in this assembly — no shared non-collocated `wwwroot/js/` file like Families'
  `mediaPlayer.js` is needed). That makes **three consumers of one export** (recorder, list, page) —
  not a third `mediaDuration` implementation in the list's module.

### Slice E — editor wiring (C1/C2, Q6/Q7)

1. **`InstructionEditorRow.DurationSeconds`** — written by the record commit (`OnRecordedAsync`, now
   after the metadata dialog — E.4), by the upload probe (E.7), and by read-back (`FromRead`). The
   readout reads duration from the **row** (not only the session map), so uploaded and post-reload
   rows render `m:ss` from persisted data and fall back to **`—:—`** when null. Title/Text already
   live on the row (`FromRow`/`ApplyEditAsync` already round-trip them for Text/Link).
2. **The row shape (C1):** the staged-file branch for Audio/Video no longer renders a pill or a size
   text. It keeps the row's standard pieces — the leading `cq-instruction-kind` glyph span (its
   `aria-label`/`title` tooltip) and the trailing remove `×` bound to the existing `RemoveAsync`
   (`aria-label="Remove instruction"`, the continuity pin) stay exactly as for other kinds — and adds:
   the **readout** (a click target, id `{Id}-title-{index}`, showing per C2/Q10 — `2:34 [Title]` with
   CSS ellipsis truncation when titled, `2:34` untitled with known duration, `—:—` when unknown,
   a titled Image → `[Title]` (no duration); an untitled Image keeps today's size-text readout
   (Q13); `title` tooltip = FileName) and a **play icon button** in the
   actions row where Text/Link render their edit pencil — `Appearance=Stealth`,
   `IconStart=Icons.Regular.Size20.Play()`, `aria-label="Play"`, id `{Id}-play-{index}`. Text/Link
   rows keep their pencil; Image rows keep their standard glyph + `×` (Q4; readout per Q13 — an
   untitled Image's size text is unchanged, so the third-repinned size-text pin stays green for
   Image). **No `.cq-media-play`
   CSS survives** — the rounded-pill styling is deleted from `InstructionEditorList.razor.css`.
3. **The readout is the edit affordance (C2):** clicking `2:34 [Title]` invokes the list's existing
   `EditRowRequested(row)` seam — the host opens the extended `MaterialEditDialog` (Slice E.9), the
   decision to open one stays with the host and the list keeps holding no dialog. The play button is
   **purely preview**: it never opens the dialog, and the edit pencil never appears on media rows.
   **Host-mode split (verified):** the authoring page hosts the list in `RowEditingMode.Dialog` and
   opens the dialog at page level — not nested. The question host (`QuestionEditDialog.razor:47`)
   hosts the list in `RowEditingMode.Inline` and **cannot nest a dialog** (dialog-ui §4 — the reason
   recorded verbatim in `RowEditingMode.cs`); there the readout click instead expands the metadata
   INLINE on the row — an optional Title + Body/Description field pair rendered beside the readout
   (the inline pattern Text/Link rows already use), never a nested dialog. **Pinned (Q12):** on the
   Inline-mode host the recorder commit also pops nothing — the take stages as today and the freshly
   staged row's metadata is edited from its readout click; the full C2 dialog flow belongs to the
   Dialog-mode host only.
4. **Recorder commit pops the metadata dialog (C2):** `OnRecordedAsync(row, media)` — the
   "Use this recording" seam — no longer stages immediately. It stashes the pending take (row +
   `RecordedMedia`) in a new field and opens the metadata dialog seeded from the pending row. **On
   confirm:** the existing `StageMediaFileAsync` path stages the take exactly as today, the row gets
   `Title`/`Text` from the dialog result (plus the probed duration — unchanged), the review closes, and
   the host's save payload spy path is unchanged. **On dialog cancel:** the pending take is dropped but
   `_recordingRow`/`_reviewSeed` are left intact — the user stays IN the review (player + Record again
   + a fresh commit try), no staging residue. The pop opens at page level on the Dialog-mode host; on
   the Inline-mode (question) host the pop cannot nest — **pinned resolution (Q12):** the take
   commits directly as today and the freshly staged row's metadata is edited from its readout.
5. **Old rows read null (no backfill):** a pre-migration row's readout shows `—:—`; the served fetch
   path may re-probe the Blob URL with `mediaDuration` for **display only** and never persists it (no
   write route — out of scope, recorded above).
6. **Seeded review (Q6) + the metadata link (C2):** clicking the play button on a row without a session
   take behaves as planned for C1's action slot — `LoadInstructionMediaAsync(row)` (host delegate; null
   → clicking names an inline error rather than a silent dead button) → `createMediaUrlFromStream` →
   store into `_sessionMedia` → seed `RecorderPanel.ReviewSeed` → reopen the seeded review **with the
   commit button absent** (the take is already staged; the seeded branch already renders only player +
   Record again). A fetch failure → named error, best-effort revoke of any URL just created. On the
   seeded review, a **link-styled control** (“Edit title/description”) opens the same metadata dialog
   (Inline host: the Q12 inline expansion), rendered **only when a title/description exists** — a bare
   preview shows nothing extra.
7. **Upload probe (unchanged):** after `StageMediaFileAsync` stages successfully, the list invokes
   `probeDurationFromInput` with the capture slot's input selector (the slot gains an id
   `{Id}-capture-{index}`). Whether FluentInputFile (Stream mode) leaves the just-chosen `File` still
   attached to the real `<input type=file>` (readable via `input.files`) **cannot be verified from the
   repo** — it is confirmed at runtime on the preview before the probe is trusted. Best-effort either
   way: if the input is empty, or any JS failure/timeout → `null` → `—:—` fallback, staging unaffected.
   The **page dropzone** path (`OnMaterialDroppedAsync` → media rows appended) probes the same way with
   the dropzone input's selector, importing the same list module — the new import means the page's
   bUnit tests must `SetupModule` it (the `BeforeUnloadModulePath` trap).
8. **Host wiring (unchanged):** `AssignmentAuthoring` + `QuestionEditorSection`/`QuestionEditDialog`
   (via `QuestionEditModel.ForEdit`) accept and thread `LoadInstructionMediaAsync` exactly as
   `StageInstructionMediaAsync` already threads — one seam, both owners, the list stays client-free.
9. **Dialog extension (C2):** `MaterialEditDialog` gains a media branch: Audio/Video/Image render an
   optional Title `FluentTextField` + optional Body/Description, mirroring the Text branch's Title +
   rich-Body layout (for media the description is plain prose, seeded from `FromRow`'s Body mapping
   which already carries `row.Text`). The dialog's validator mirror
   requires NOTHING extra for media (title/body both optional, independent), never forbids them, and
   never touches the file fields. `MaterialEditFormModel`/`FromRow`/`MaterialEditResult` need no shape
   change — only the Kind switch and the “Media never comes through here” comments change. (Image's
   body has no render surface this round beyond the ward C3 `Text` render, but the editing surface
   exists for it anyway — C2 grants Title+Body to ALL media kinds.)

### Slice G — ward body render (C3)

`WardMaterialPlayer.razor` renders `Material.Text` (when non-empty) as a plain block under the
`ward-material-label` span / the player — one static render, one conditional. `InstructionReadDto`
(`Text`) already reaches the ward view via `GetWardAssignmentViewQueryHandler`; no DTO, route or JS
change. One bUnit pin in `WardAssignmentPlayerBunitTests.cs` (`WardPlayerBodyRender…`): material with a
body → paragraph rendered; without → nothing extra (pre-fix: never rendered).

### Slice F — tests (each must fail against pre-fix code)

`tests/SchoolCollab.Assignments.Tests.Unit/` (list, dialog) — each test is either **discriminating**
(fails against pre-fix code) or the one explicitly labelled **REGRESSION pin** below; nothing in this
table is silently assumed to fail pre-fix.

| Test | Seam + discriminator |
|---|---|
| `ServedRow_PlayButton_RendersWithoutASessionEntry_AndInvokesTheLoadSeam` | bUnit: row with `StoragePath`, no session entry → `#…-play-0` present (Stealth, IconStart=Play, `aria-label="Play"`), the `cq-instruction-kind` glyph span AND trailing `#…-remove-0` both present, the `cq-instruction-file` size text **absent** (pre-fix: size text, no play button); click → `LoadInstructionMediaAsync` spy invoked with that row + `ReviewSeed` set (player rendered, commit button absent) |
| `MediaRow_KeepsTheKindGlyphAndTheRemove_WithTheNameTooltip` — **REGRESSION pin, not discriminating** | bUnit: the staged Audio row's `cq-instruction-kind` glyph span and `#…-remove-0` (remove aria-label) are present — pinned so the retired pill shape cannot return and the round cannot regress today's glyph; a `title` tooltip carries the file name. **Cannot fail against pre-fix code** — glyph (`InstructionEditorList.razor:58-66`), remove (`:164-167`) and tooltip (`:145`/`:151`) all render today; its job is continuity, not discrimination; the Image row is unchanged (the repinned `StagedMedia_RendersTheSizeText_WithNoClearAction` stays green for Image) |
| `MediaRow_Readout_ShowsTitleDurationEllipsis_OrTheDashFallback` | bUnit: `2:34 [Title]` readout (with the ellipsis/truncation class on the readout), untitled → `2:34`, `DurationSeconds = null` → `—:—`, titled Image → `[Title]` with **no** duration, untitled Image → today's size text (Q13) — pre-fix: no duration/title readout at all (the untitled-Image size text renders both pre- and post-fix; the Image discriminator is the titled `[Title]` branch) |
| `MediaRow_Readout_ClickRequestsTheEdit` | bUnit: click the `2:34 [Title]` readout → `EditRowRequested` spy invoked with the row (pre-fix: the readout is not a click target; media rows have no pencil) — the play button never triggers it |
| `RecordingCommit_MetadataDialog_Pops_BeforeTheTakeStages` | bUnit: drive `OnRecordedAsync` (the "Use this recording" seam) → the metadata dialog renders for the media kind; **cancel** → the review is STILL open (player present, row unstaged — pre-fix: the take commits immediately); **confirm** → the row is staged carrying the dialog's Title/Text, the review closes |
| `RecordingCommit_PersistedDuration_RidesTheSavePayload` | bUnit: record commit with a probed duration (extends the existing `RecordedRow_Pill…` flow) → the host's payload spy sees `NewInstructionDto.DurationSeconds` set — pre-fix the field is absent/dropped |
| `SeededReview_TitleEditLink_RendersOnlyWhenTitleOrBodyExists` | bUnit: seeded review for a titled/body-less row → the link-styled title/description control present; untitled + body-less → absent (C2's only-when rule; pre-fix: no control at either branch) |
| `MediaKindDialog_AcceptsAMediaRow_OptionalTitleAndBody_SubmitsThem` | `MaterialEditDialogBunitTests`: dialog open for Audio → optional Title + Body/Description fields, **both blank PASSES** validation (Q10 — independent, both optional); a filled pair returns in `MaterialEditResult` (pre-fix: the dialog never seeds/yields a media kind) |
| `ServedRow_WithoutALoadSeam_ClickNamesTheFailure` | bUnit: `LoadInstructionMediaAsync` left null → click play → `#…-error` carries the named message; no exception, no dead action |
| `UpdateInstructions_LoadedRowDuration_SurvivesTheNullPreservePath` | unit (`AssignmentInstructionsTests`): a full update with null instruction payload (**preserve, `UpdateAssignmentCommandHandler:79`**) keeps `AssignmentInstruction.DurationSeconds`; the command-rows (`:90`) and **question-rows (`:192`)** projections round-trip it; and the create (`CreateAssignmentCommandHandler:107`/`:188`) and **duplicate (`DuplicateAssignmentCommandHandler:94`/`:124`)** projections round-trip it too — fails pre-fix (the field is dropped at every tuple; duplicate compiles unchanged and silently nulls) |
| `GetAuthoringChildren_AndWardView_MaterialsCarryDuration` | unit: both read-mappers emit the persisted value (and `null` for a legacy row) — pre-fix the DTO has no field to assert |
| `GetInstructionMediaStreamAsync_404_ReturnsNull_AndSuccessStreams` | scripted `HttpMessageHandler` (the `WardRoutesTests.cs` pattern): 404 → `null`, 200 → non-empty `Stream` — pins stream-not-bytearray transport (a `byte[]`-based implementation fails this pin) |

`tests/SchoolCollab.Assignments.Api.Tests.Unit/TeacherMaterialRouteTests.cs` (mirror `WardRoutesTests`)

| Test | Discriminator |
|---|---|
| `TeacherMaterial_Returns200_WithStoredContentType_AndRangeProcessing` | the new route streams with the stored content-type + range/206 — pre-fix: 404 (route does not exist) |
| `TeacherMaterial_Returns404_WhenInstructionMissing_OrNotMedia_OrFileGone` | all three miss paths |
| `TeacherMaterial_RequiresAuthentication` | bearer-absent request rejected with real auth on (testing.md flag-state rule; pre-fix: no route) |

`tests/SchoolCollab.Assignments.Tests.Integration/AddAssignmentInstructionDurationSecondsMigrationTests.cs`

| Test | Discriminator |
|---|---|
| Real-Postgres pin (`AssignmentInstructionsResponseKindsMigrationTests` shape): the pre-migration catalogue **lacks** the column; post-migration `duration_seconds` is nullable and additive-only; existing instruction rows read `null` | the script asserted against its pinned `OwnMigration` id — fails if the column were non-nullable or backfilled |

Plus one Families pin (Slice G): `WardPlayerBodyRender…` in
`tests/SchoolCollab.Families.Tests.Unit/WardAssignmentPlayerBunitTests.cs` — a material with a body
renders the body block under the player/label; without a body nothing extra renders (pre-fix: never
rendered).

Existing green pins that must stay green (**three are repinned**, not re-baselined): `MigrationGuardTests.NoUncommittedModelChanges`,
`RecordedRow_Pill_ShowsDuration_AndReopensTheReviewPlayer` (**repinned for C1** — the same flow, asserted against the
readout/play-button shape instead of the pill markup), `DialogMode_LeavesMediaRowsOnTheStagedFileFlow` (**repinned for C2** —
media rows now edit THROUGH the readout-opened dialog; the file fields still are not inline-editable and never in the dialog),
and `StagedMedia_RendersTheSizeText_WithNoClearAction` (`InstructionEditorListBunitTests.cs:103-123` — the **third repin,
Audio→Image**: it pins an Audio row, `Kind = InstructionKindDto.Audio` at `:108`, with its size text, and C1 retires the Audio
size text, so it fails post-fix as written; repinned to Image, where Q13 keeps the untitled size-text readout, it stays green).
Plus the ArchitectureTests razor-hygiene + best-practices gates.

### Verification commands

(Worker runs the scoped ones; the parent owns the authoritative re-runs. **No `dotnet build` /
`dotnet test` by this document's author.**)

1. `dotnet ef migrations add AddAssignmentInstructionDurationSeconds --project src/Assignments/SchoolCollab.Assignments.Core --context AssignmentsDbContext` — then **review the script** (ef-migrations rule 3) and implement `Down()`.
2. `dotnet build SchoolCollab.slnx` → 0 errors (watch MSB3021/3027 locks — surface, never loop).
3. `dotnet test SchoolCollab.Assignments.Tests.Unit` → 0 failures.
4. `dotnet test SchoolCollab.Assignments.Api.Tests.Unit` → 0 failures.
5. `dotnet test SchoolCollab.Families.Tests.Unit` → 0 failures (the C3 ward body pin).
6. `dotnet test SchoolCollab.ArchitectureTests.Unit` → 0 failures.
7. `dotnet test SchoolCollab.Assignments.Tests.Integration` → 0 failures (the real-Postgres migration test skips without Postgres, per the project's existing gate).

### Acceptance criteria

- **AC-1 (C1/Q3)** — A staged Audio/Video instruction row (recorded, uploaded, post-reload) renders the Text-row shape: the standard leading kind glyph and the standard trailing remove `×` are present and unchanged, and the action slot holds a PLAY icon button (Stealth, IconStart=Play, `aria-label="Play"`, id `{Id}-play-{index}`); no `.cq-media-play` pill markup or CSS survives. Text/Link rows keep their pencil; Image rows keep their standard shape (Q13: an untitled Image keeps today's size-text readout — a titled one reads `[Title]`).
- **AC-2 (C2/Q7)** — The media readout displays `2:34 [Title]` (ellipsis-truncated) when titled, `2:34` when untitled with a known duration, `—:—` when the duration is unknown, and `[Title]` (no duration) for a titled Image while an untitled Image keeps today's size-text readout (Q13); the file name rides the native `title` tooltip; pre-migration rows read `DurationSeconds = null` and render `—:—` without error.
- **AC-3 (C2)** — `MaterialEditDialog` accepts the media kinds with an OPTIONAL, independent Title + optional Body/Description for Audio/Video **and** Image, mirroring its Text branch; on the Dialog-mode host (authoring page) it opens from the row's READOUT click through the existing `EditRowRequested` seam, never from the play button; the Inline-mode host (question dialog, no nested dialog — dialog-ui §4) edits the same fields **inline on the row (Q12-pinned, not a recommendation)**; the dialog's validator mirror requires nothing and forbids nothing extra for media; `ApplyEditAsync` writes only Kind/Title/Text/Url and preserves the media file fields.
- **AC-4 (C2)** — On the Dialog-mode host, clicking the recorder review's "Use this recording" pops the metadata dialog BEFORE any staging; on confirm the take is committed carrying the title/body (plus probed duration); on cancel the review stays open with the take uncommitted and no staging residue. Inline-mode host **(Q12-pinned, not a recommendation)**: the commit pops nothing — the take stages as today and the freshly staged row's metadata is edited from its readout click.
- **AC-5 (C2/Q6)** — The play button reopens the `RecorderPanel` seeded review — playback only, never a commit button (for served/preview plays) — and the seeded review renders a link-styled title/description edit control ONLY when a title/description exists.
- **AC-6 (C2/Q10)** — The metadata itself needs **no migration and no contract widening**: `Title`/`Text` already exist on the entity, both DTOs, the ward DTO and the row; only `DurationSeconds` is new; media rows validate without either field (no new validator rule).
- **AC-7 (Q2/Q9)** — `GET /assignments/{id}/materials/{instructionId}` serves the stored content-type with `enableRangeProcessing: true`, 404s every miss path, and is bearer-authenticated with the same dev-posture behaviour as the group.
- **AC-8 (Q8)** — A play on a row without a session take fetches the staged bytes through the teacher route and converts .NET→JS via `DotNetStreamReference` (never a `byte[]` interop array).
- **AC-9 (Q7)** — A recorded take's probed duration and a client-side-probed upload duration both persist and round-trip read-back → editor row → next save, for both hosts.
- **AC-10** — A served-play fetch that 404s or fails surfaces a named inline error on the list (never a dead affordance, never an exception).
- **AC-11** — The list remains presentational: it never references an API client or hosts the dialog itself; both hosts reach playback through the supplied `LoadInstructionMediaAsync` seam, and Dialog-mode hosts open the metadata dialog through `EditRowRequested`, exactly as staging already does.
- **AC-12 (C3)** — `WardMaterialPlayer.razor` renders the media body (from the DTO's `Text`, when non-empty) under the player/label in the Families surface; one render, one bUnit pin.
- **AC-13** — All Slice F/G tests pass — each is either discriminating against pre-fix code or the one named **REGRESSION pin** (`MediaRow_KeepsTheKindGlyphAndTheRemove_WithTheNameTooltip`); the guard/migration/route/hygiene pins are all green (with `RecordedRow_Pill…`, `DialogMode_LeavesMediaRows…` **and `StagedMedia_RendersTheSizeText_WithNoClearAction` (third repin, Audio→Image)** repinned as specified in Slice F).
- **AC-14** — Gates: `dotnet build SchoolCollab.slnx` → 0 errors; Assignments.Unit / Assignments.Api.Unit / Families.Unit / ArchitectureTests / Assignments.Integration → 0 failures (parent re-verifies authoritatively).
- **AC-15** — Ward playback transport, caps, recorder capture flow and the questions-draft staging shape are otherwise unchanged (existing tests pass **unchanged except the three named repins**; the draft payload carries title/body/duration verbatim).

### Open decisions — none (OD-1 / OD-2 owner-pinned 2026-10-14)

Both decisions raised by the plan-review rework were pinned by the owner and folded into the grill
table above as **Q12** and **Q13**; the plan text (Goal, Slices E.3/E.4/E.6, AC-3/AC-4/AC-13) carries
them as pinned, not as recommendations.

- **OD-1 → Q12 (option a, pinned):** the Inline-mode host (question dialog — `RowEditingMode.Inline`,
  no nested dialogs) edits media metadata via the **inline Title/Body field pair ON the row**
  (mirroring the inline fields Text/Url rows already render there); the recorder commit **pops
  nothing** on that host — the take stages as today and the freshly staged row's metadata is edited
  from its readout click. The Dialog-mode host gets the full C2 dialog flow. C2's DTO/metadata scope
  was never affected (Title/Text already exist; only `DurationSeconds` is new).
- **OD-2 → Q13 (option a, pinned):** a titled Image row reads `[Title]`; an untitled Image row keeps
  today's size-text readout. Carried in AC-1/AC-2 and Slices E.2; this is what keeps the third-repinned
  `StagedMedia_RendersTheSizeText_WithNoClearAction` green for Image.

Everything else is settled by the grill + corrections: the row shape and readout (C1/Q3),
the metadata editing/commit/seeded-preview surfaces (C2/Q10 — Title + Body for Audio/Video/Image
alike), the ward body render (C3/Q11), and the previously pinned persistence row (Q7), transport
(Q8) and route (Q9). Secondary choices (`int` vs `double` for `DurationSeconds`; no server backfill; a
host-supplied load seam; reload-fetched durations display-only; the metadata dialog staying
host-owned via its existing `EditRowRequested` seam on the Dialog host) are decidable from the pinned
decisions, the specs and the repo facts recorded in Slices A, E and G.

## Worker Report

_(parent persists the worker's inline WORKER REPORT)_

## Review

### Plan review — pass 1 (2026-10-14, `ollama-cloud/glm-5.3`) — REWORK

```
PLAN REVIEW
Verdict: REWORK
P1: Slice B / Expected files — the DurationSeconds threading inventory is wrong and incomplete: Create has TWO
    instruction-tuple projections (:107 assignment-own, :188 question rows); Update has THREE (:79 null→preserve,
    :90 command rows, :192 question rows); DuplicateAssignmentCommandHandler.cs:94/:124 re-project every row and are
    entirely unlisted — with the trailing-defaulted ctor param, duplicate compiles unchanged and silently nulls
    every persisted duration. Only ctor sites are Assignment.cs:419/448/480.
P1: Slice F / AC-13 / AC-15 — StagedMedia_RendersTheSizeText_WithNoClearAction pins an AUDIO row
    (InstructionEditorListBunitTests.cs:108, size assert :118); C1 retires the Audio size text, so it fails
    post-fix, yet AC-13 names only two repins and AC-15 demands "existing tests pass untouched". The untitled-Image
    readout is also unspecified (Q10's Image → [Title] vs Q4 "Image keeps its readout").
P2: Slice D — testing.md §9 does not exist (testing.md has five ## sections).
P2: Slice D — mediaDuration consumer count miscounted: RecorderPanel.razor.js:185 exports its own (a third consumer).
P2: Slice E.7 — the input.files-after-FluentInputFile assertion is unverifiable from the repo.
P2: Slice C security note (report-only) — role-less bearer widens the caller class; identical to the mirrored ward
    route, Q9-pinned, no new hole.
Open decisions: OD-1 (Inline-host metadata surface); untitled-Image readout.
Gates: UI: risk · contract: none · migrations: none · secrets: none
Acceptance honesty: not ok — MediaRow_KeepsTheKindGlyphAndTheRemove_WithTheNameTooltip cannot fail against pre-fix code.
```

All six findings adjudicated by the parent against source and **confirmed**; owner pinned **OD-1 = (a)** and
**OD-2 = (a)** (now grill rows Q12/Q13). The orchestrator pass revising the plan was **interrupted mid-edit loop**
(the revision had already landed on disk; the failing edits were redundant re-attempts against an already-correct
file) — recorded as a deviation: the revision on disk is complete and was verified, not re-authored.

### Plan review — pass 2, targeted re-read of the revised sections (2026-10-14, `ollama-cloud/glm-5.3`) — ACCEPT

```
PLAN REVIEW
Verdict: ACCEPT
P1: none
P2: none
Open decisions: none
Gates: UI: risk · contract: risk · migrations: risk · secrets: none
Acceptance honesty: ok
```

Verification evidence (reviewer): all seven projection sites confirmed live at the cited lines; `mediaDuration`
confirmed at `RecorderPanel.razor.js:185` consumed by the recorder at `RecorderPanel.razor:330`;
`StagedMedia_RendersTheSizeText_WithNoClearAction` at `InstructionEditorListBunitTests.cs:103`; testing.md has no §9.
All previous findings closed.

### Diff review — pass 1 (2026-10-14, `ollama-cloud/kimi-k2.7-code`) — P1

```
REVIEW
Verdict: P1
P1: src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/RecorderPanel.razor:409 —
    OnFileChosenAsync unconditionally calls Reset() after OnRecorded returns; on Dialog-mode metadata-dialog cancel
    this resets the panel to Idle instead of leaving the review open with the take, violating AC-4.
P2: none
Best-practices: no overwrites, skills honored, readable
```

Deviations verified sound: the 8th projection site (`ConfirmQuestionsDraftCommandHandler` — the plan's "seven"
undercounted it), `InstructionEditorRow.Id` never reaches the save payload (`ToDto()` omits it), the `CopyRow`
Title copy, the `NotCoveredRoutes` addition, and the served-play re-probe (updates only the session map, never
`row.DurationSeconds`). P1 confirmed by the parent against source.

### Rework 1 (2026-10-14, `ollama-cloud/deepseek-v4.1-flash`)

AC-4 cancel path fixed in `RecorderPanel.razor` + `InstructionEditorList.razor` (+2 bUnit pins): the panel no
longer resets a held take and keeps the handed-off URL alive (ownership stays the host's); the list keeps the held
take and re-seeds the review when `_pendingRecording` survives the dialog, with a supersede-only revoke and a new
`DiscardPendingRecordingAsync` revoking exactly once on close/teardown. Build 0 errors; Assignments 1149/0;
Architecture 111/0.

### Diff review — pass 2, focused re-check (2026-10-14, `ollama-cloud/kimi-k2.7-code`) — PASS

```
REVIEW
Verdict: PASS
P1: none
P2: InstructionEditorList.razor:725-730 — the post-cancel re-seed of _reviewSeed is not discriminated by
    CancelledMetadataDialog_KeepsTheTakeAndTheReview_And_TheSecondTryStages; the panel stays mounted because
    _recordingRow is never cleared on cancel, and OnParametersSet already ignores a new seed when _state == Review,
    so removing the re-seed would not cause the test to fail. The worker's mutation-probe claim is therefore not
    plausible from the test body alone.
Best-practices: no overwrites; ownership comments and guards readable and consistent; skills honored
    (dialog-ui no-nesting, use-js-interop disposal).
```

Residual P2 **accepted** by the owner (Q17, 2026-10-14): the re-seed stays as a defensive seam; the pin remains
valid for the behaviour (take kept, review open, second commit stages) even though it does not separately
discriminate the re-seed.

## Acceptance

**Orchestrator verdict (2026-10-14) — the round CLOSES at orchestrator level: CLOSED (UI-tester pass pending).**
Both independent review gates completed (plan review pass 2 ACCEPT, no P1/P2; diff review pass 2
PASS, no P1, one accepted-residual P2). The parent's build/test numbers below are the round's
authoritative re-verification. No unaddressed P1s. Owner gate honoured: **no commit, no push, no PR**
the working tree stays uncommitted on `feat/media-pill-listing`.

### Criteria checklist (AC-1 … AC-15)

| AC | Verdict | Evidence |
|---|---|---|
| AC-1 (C1/Q3) row shape | **MET** (bUnit pins; live UI pass pending) | `ServedRow_PlayButton_…`, `MediaRow_KeepsTheKindGlyphAndTheRemove_WithTheNameTooltip` (REGRESSION pin) green; the three named repins (incl. the third-repinned `StagedMedia_RendersTheSizeText_WithNoClearAction`, Audio→Image) landed in Slice F; `.cq-media-play` pill CSS retired in `InstructionEditorList.razor.css` (diff-reviewed) |
| AC-2 (C2/Q7) readout | **MET** (bUnit pins; browser-only visual — ellipsis truncation — pending live UI pass) | readout branches (`2:34 [Title]` / `2:34` / `—:—` / Image `[Title]` / untitled-Image size text) pinned in `InstructionEditorListBunitTests`; `DurationSeconds = null` pre-migration rendering pinned; tooltip pin in the REGRESSION pin |
| AC-3 (C2) metadata edit | **MET** | `MediaKindDialog_AcceptsAMediaRow_OptionalTitleAndBody_SubmitsThem` green — both blank PASS validation, filled pair returned in `MaterialEditResult`; readout-click opens via the existing `EditRowRequested` seam (diff-reviewed); Inline-host pair is on-row (Q12 pinned) |
| AC-4 (C2) commit flow | **MET** (browser cancel flow pending live UI pass; see Q17/Q18 residuals) | **Rework 1** fixed the diff-review pass 1 P1: `RecorderPanel.razor:409` no longer unconditionally `Reset()`s after `OnRecorded` — cancel keeps the take, the review stays open, the second "Use this recording" commits; `CancelledMetadataDialog_KeepsTheTakeAndTheReview_And_TheSecondTryStages` (+ panel pins) green |
| AC-5 (C2/Q6) seeded review | **MET** | play button reopens the `RecorderPanel` seeded review via `LoadInstructionMediaAsync` → `ReviewSeed`; link-styled edit control only when title/description exists (pinned); play button carries no commit semantics |
| AC-6 (C2/Q10) no metadata migration | **MET** | the round's single schema/contract change is `DurationSeconds` (migration `AddAssignmentInstructionDurationSeconds`, additive nullable, no backfill); Title/Text pre-existing on entity, DTOs, ward DTO, row and validator (verified in plan review pass 1; no new validator rule) |
| AC-7 (Q2/Q9) serving route | **MET** | `GET /assignments/{id}/materials/{instructionId}` — range-enabled, teacher bearer, 404 on every miss path; mirrors the ward route role-for-role (pass 1 P2 security note: report-only, identical to Q9's pinned class, no new hole) |
| AC-8 (Q8) stream transport | **MET** | `GetInstructionMediaStreamAsync_404_ReturnsNull_AndSuccessStreams` — a `byte[]`-interop implementation would fail this pin; `DotNetStreamReference` → JS → Blob URL; a served play on an unsaved row surfaces the Q19 named inline error (nothing server-side to serve) |
| AC-9 (Q7) duration persistence | **MET** | all EIGHT projection sites thread `DurationSeconds` (the six plan-review findings + the 8th site `ConfirmQuestionsDraftCommandHandler` found in diff review); record-time probe reused, upload probe client-side (see Q18 residual on the upload probe) |
| AC-10 named inline error | **MET** (browser pending live UI pass) | `ServedRow_WithoutALoadSeam_ClickNamesTheFailure` green — `#…-error` carries the named message, no exception, no dead affordance |
| AC-11 list stays presentational | **MET** | diff-reviewed: the list calls no API client and hosts no dialog; playback flows only through the host-supplied `LoadInstructionMediaAsync` seam, metadata editing through `EditRowRequested |
| AC-12 (C3) ward body render | **MET** (browser body-render pending live UI pass) | `WardMaterialPlayer.razor` renders `Material.Text` under the player/label when non-empty; one render + one bUnit pin (Families.Unit 48/0) |
| AC-13 Slice F tests | **MET** | all discriminating pins distinguish pre-fix code; the one REGRESSION pin (`MediaRow_KeepsTheKindGlyphAndTheRemove_…`) and the three named repins green — Assignments.Unit **1149/0** |
| AC-14 gates | **MET** | authoritative parent numbers: see the table below — 0 build errors, 0 test failures across all five suites incl. the real-Postgres integration pin |
| AC-15 nothing else regressed | **MET** | existing tests pass unchanged except the three named repins; the draft payload carries title/body/duration verbatim; ward transport, caps and capture flow untouched — Architecture 111/0, Integration 26/0 |

**AC tally: 15 met / 0 unmet.** AC-1/2/4/10/12 carry a "live UI pass pending" qualifier — their
remaining proof is browser-level (ellipsis truncation, CSS retirement in a real stylesheet, the
browser cancel/re-commit gesture, the ward body under real data) and is exactly what the tester-scope
handover below pins. A live pass may downgrade AC-2/AC-4/AC-10 residuals without reopening the round;
a live failure reopens the round via a new rework.

### Gates — parent's authoritative numbers

| Gate | Suite / command | Result |
|---|---|---|
| Build | `dotnet build SchoolCollab.slnx` | **0 errors** |
| Unit | `SchoolCollab.Assignments.Tests.Unit` | **1149 passed / 0 failed** |
| Unit | `SchoolCollab.Assignments.Api.Tests.Unit` | **172 / 0** |
| Unit | `SchoolCollab.Families.Tests.Unit` | **48 / 0** |
| Architecture | `SchoolCollab.ArchitectureTests.Unit` | **111 / 0** |
| Integration | `SchoolCollab.Assignments.Tests.Integration` | **26 / 0** — the real-Postgres migration pin ran against the new migration |

### P1 register

- **Plan review pass 1** — six findings (5×P1-grade inventory/pin defects + report-only P2): all six
  adjudicated and fixed → pass 2 ACCEPT, no P1/P2.
- **Diff review pass 1 — P1** `RecorderPanel.razor:409` unconditional `Reset()` violating AC-4's
  cancel contract → **Rework 1** fixed it (panel no longer resets a held take; the list re-seeds the
  review on dialog cancel; `DiscardPendingRecordingAsync` revokes exactly once) → parent confirmed
  against source.
- **Diff review pass 2 — P1s: none.** Remaining P2 accepted as residual Q17 below.

### Accepted residuals (owner, 2026-10-14) — Q17/Q18/Q19, all P2 or report-only

| # | Accepted residual | Risk surface |
|---|---|---|
| Q17 | The post-cancel `_reviewSeed` re-seed in `InstructionEditorList.razor:725-730` is not separately
discriminated by `CancelledMetadataDialog_KeepsTheTakeAndTheReview_And_TheSecondTryStages` — the
removal of the re-seed would not fail the pin. Accepted: the re-seed stays as a defensive seam; the
pin remains valid for the behaviour (take kept, review open, second commit stages) | recorder cancel → re-commit |
|
| Q18 | The upload-duration probe depends on `input.files` surviving `FluentInputFile`'s completion —
unverifiable statically. If the element clears its input, the upload probe silently falls back to
`—:—` (display-only); recordings and their persisted durations are unaffected | upload staging readout |
| Q19 | A served play on a create/unsaved row (nothing server-side to serve yet) returns the named
inline error rather than silently dead-ending — accepted per AC-10's contract | authoring create mode; play before save |

### Verdict lines

**Verdict: CLOSED** — Tier 3 full round closed at orchestrator level, 2026-10-14: plan ACCEPT
(pass 2), diff PASS (pass 2), all gates green on the parent's authoritative numbers, no open P1s,
residuals owner-accepted. **UI-tester pass pending** — the branch stays uncommitted (owner gate);
the tester's report lands in `## UI Tester` below and only a live failure reopens the round.

## Tester-scope handover (UI trigger fires — `.razor` / `.razor.css` / `.js` changed)

**The UI tester's scope is EXACTLY this list** — no other page, dialog, or flow is in scope. Route
list + rationale per surface:

| Surface | Where it renders (nav entry) | What it calls | Why it's in scope |
|---|---|---|---|
| `InstructionEditorList.razor` staged media row (C1 shape: glyph + readout + play + `×`) | Content & Resources `PanelSection` on the **assignment authoring page** (`AssignmentAuthoring.razor`), in BOTH create and edit modes (Dialog-mode host) | host `LoadInstructionMediaAsync` → `AssignmentsApiClient.GetInstructionMediaStreamAsync` | the round's primary surface — row shape, readout, play affordance all live here; create-vs-edit exercises session-take vs served-post-reload playback |
| Inline-mode media rows (Title/Body pair ON the row) | same page rendered through the **question-edit dialog** (`QuestionEditDialog.razor` via `QuestionEditorSection.razor`, host threads the seame through `QuestionEditModel.ForEdit`) | same `LoadInstructionMediaAsync` seam threaded from `AssignmentAuthoring` | Q12 pinned the inline pair + no-commit-pop on this host — dialog-nesting and staging-behaviour risks live only here |
| `MaterialEditDialog.razor` media branch (optional Title + optional Body, both-blank passes) | opens (Dialog-mode host only) from the **readout click** on a staged media row | none (host-owned; `MaterialEditResult` back via `EditRowRequested`) | the C2 dialog was newly extended to media kinds — validation seeding and result carriage must be seen live |
| `RecorderPanel.razor` — review / commit | authoring page, inside an audio/video row's action flow (record from staging → "Use this recording") | `mediaDuration(url)` from `RecorderPanel.razor.js` | the commit flow — including metadata-dialog **pop before staging** — is new behaviour pinned only at bUnit level (AC-4) |
| `RecorderPanel.razor` — metadata-pop / cancel / retake | same authoring page, after "Use this recording" (dialog pops) then browser-cancel; then re-commit | no API — takes stay local until confirm | the reworked cancel path is the round's P1 fix: cancel must keep the review open, second "Use this recording" must commit; retake must not lose the held take |
| `WardMaterialPlayer.razor` media body | Families ward assignment page `/ward/{StudentId:guid}/assignments/{AssignmentId:guid}` (from `/ward`) | the ward's serving route (pre-existing) | C3 is a Families-side render (`Material.Text` under the player) — verify with a titled media row + non-empty body |

Browser-scope items the tester must exercise (all under the surfaces above):

1. **Metadata-dialog cancel → re-commit** (authoring page): record → "Use this recording" → cancel
   the popped dialog → review stays open with the take uncommitted → "Use this recording" again →
   the take commits (AC-4 / Q17's behaviour). Also verify no leaked Blob URL / double-revoke on close.
2. **Real playback of a served row**: save an uploaded/recording-backed media row, **reload**, click
   play → audio/video streams from the served route (post-reload, no session take), seeded review
   renders playback-only.
3. **Upload-duration probe** (`input.files`, Q18): upload an audio/video file → confirm the readout
   shows the probed `m:ss`; if the element cleared `input.files`, confirm the readout degrades to
   `—:—` (report as the Q18 fallback, not a bug).
4. **Ellipsis `2:34 [Title]` readout**: a long-titled media row truncates with ellipsis; `—:—` on
   unknown duration; Image `[Title]` vs untitled-Image size text (Q13).
5. **Ward body render**: log in as a family ward, open a ward assignment with a titled
   body-carrying media material → the body renders under the player/label (AC-12).

Anything outside this list is out of the tester's scope; issues found on these surfaces are
reported inline in `## UI Tester` and adjudicated by the parent before any commit/push/PR.

## UI Tester

### UI test pass (2026-10-14, `ollama-cloud/minimax-m3`) — P2-only

```
UI TEST
Scope ack: confirmed — hunted exactly the handed-over surfaces: (1) InstructionEditorList.razor staged media
    row (create + edit), (2) inline-mode media rows in QuestionEditDialog, (3) MaterialEditDialog.razor media
    branch, (4) RecorderPanel.razor review/commit, (5) RecorderPanel metadata-pop/cancel/retake,
    (6) WardMaterialPlayer.razor body render — plus the browser-scope items (AC-4 cancel → re-commit, served
    playback, upload-duration probe, 2:34 [Title] ellipsis readout, ward body render).
Verdict: P2-only
P1: none
P2: InstructionEditorList.razor:688-689 — OnReviewMetadataAsync in Inline mode delegates to
    OnReadoutClickedAsync, which toggles _inlineMediaRow. The inline Title/Description fields are not visible
    while the recorder panel is open (the panel takes over the container), so clicking the seeded review's
    "Edit title/description" link produces no visible effect; the fields only appear after the user closes the
    panel — a delayed/non-obvious affordance on the AC-5 link-styled edit control.
Out-of-round observations: none
```

P2 confirmed by the parent against source (link at `:37-41` inside the `_recordingRow is not null` takeover
branch; inline fields at `:158` inside the rows branch). Adjudication recorded in the round close below.

### Rework 2 (owner Q20 (b), 2026-10-14, `ollama-cloud/deepseek-v4.1-flash`)

`InstructionEditorList.OnReviewMetadataAsync` (`:696-706`) now closes the seeded review and expands the row's
inline Title/Body fields in one step on the Inline host, so the link takes effect immediately; the Dialog host is
unchanged. Two new pins (Inline immediate-effect + Dialog regression guard). Build 0 errors; Assignments **1151/0**;
Architecture **111/0**. Mutation probe: reverting the method fails the Inline pin.

### UI tester re-verify (2026-10-14, `ollama-cloud/minimax-m3`) — PASS

```
UI TEST
Scope ack: confirmed — hunted exactly the reworked path: OnReviewMetadataAsync (:696-706), the inline
    Title/Description field render (:158-170), CloseRecorder/DiscardPendingRecordingAsync/DropOrphanedRecorderRow
    (:614-660), the OnReadoutClickedAsync fallback (:671-684), and the two new bUnit pins.
Verdict: PASS
P1: none
P2: none
Out-of-round observations: none
```

**Round closed 2026-10-14.** Final authoritative gates (parent-run): `dotnet build SchoolCollab.slnx` 0 errors;
Assignments.Unit **1151/0**, Assignments.Api.Unit **172/0**, Families.Unit **48/0**, ArchitectureTests **111/0**,
Assignments.Integration **26/0**. Durable outcomes folded into `documents/specs/recording-materials.md` §8.
Nothing committed, pushed, or PR'd — the owner's commit gate.

---

## Post-close rework + correction (owner 2026-10-14, landed after this round as PR #350)

Two owner-directed changes followed the verdict; both are recorded in
`documents/specs/recording-materials.md` M3, which stays the durable source of truth.

1. **Media preview toggle.** The metadata dialog's description slot doubles as the media preview: an
   eye-icon toggle (rendered only when a preview can resolve) swaps the Description for the native
   `<video>/<audio>/<img>` element in the same slot. The Blob URL resolves LAZILY through a list-owned
   seam — `InstructionEditorList.GetPreviewLoader` → `ResolvePreviewUrlAsync` (a pending take or a
   session entry is instant; a persisted row streams through the teacher route and caches in the session
   map) — so opening the dialog never streams a file. The dialog never revokes; a null/failed resolve
   renders a named "Preview unavailable" state.
2. **Correction — the media Description IS the shared rich editor.** Q10's letter ("mirroring its Text
   branch's Title + rich Body") had been shipped as a plain text field; the owner re-opened it and the
   correction landed: the same `RichInstructionEditor` a Text body uses, read at submit and sanitised,
   with Quill's empty payload stored as null — both fields still optional, no validator or contract
   change. Because the preview toggle UNMOUNTS the editor, the dialog captures the editor's HTML on the
   way into the preview and sanitises the captured value when submit happens from the preview path, so
   both orders submit the edit. The ward now prints the media body through `RichInstruction` (the tree's
   one sanitiser-fed render component) instead of encoding it as text; the Inline host's pair stays plain.

**Findings owned in the follow-up pass.** Two stale preview-test assertions (the description was still
counted as a `fluent-text-field`), and one guard trip: `NoMarkupString_InSource` caught a COMMENT using
the banned token — reworded, the guard's allow-list untouched. The dialog-open site's `PreviewLoader`
wiring is pinned by the new source-scan guard `MaterialPreviewWiringArchitectureTests` (the authoring
page's bUnit suite does not open the material dialog, and a page harness for three lines was judged
disproportionate).

**Follow-up verification (parent-run, authoritative).** Build 0 errors; Assignments.Unit **1158/0**,
Families.Unit **49/0**, ArchitectureTests **112/0**.
