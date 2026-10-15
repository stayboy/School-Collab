# Round: recorder → metadata-dialog stream wedge

**Status:** CLOSED (2026-10-15 — delivered on `fix/recorder-dialog-stream-wedge`).
**Mode:** Solo (owner Q16, 2026-10-15 — supersedes the Q14 light-round recommendation) · **attended**.
**Base:** `7425984c68c27eca0c20f3109d850e1d4c742f8f` (main) · **Branch:** `fix/recorder-dialog-stream-wedge`.
**Review caveat:** the Tier-2 static review was performed by the **parent, not an independent reviewer** —
the reviewer delegate could not be dispatched (Cline auth error), so this diff has had no independent
verification. Flagged here so the PR is not read as having had one.
Delivered as a plain PR from `main` (no related PR was open).
Started from: owner live-testing report, 2026-10-15 — "dialog for audio recorded on a new
assignment not yet saved … buttons not working and rich text editor not showing".

## 1. Defect (live-reproduced before any change)

On `/assignments/create` (unsaved new assignment): material kebab → **Record Audio** →
*Start recording audio* → *Stop* → *Use this recording*. The **Edit Material** metadata dialog
opened **inert** — Cancel / Close / Preview / Save all dead — and the Description rich-text editor
never mounted (stuck on `rich-instruction-editor-loading`, no `.ql-toolbar` in the dialog). ~10 s
later the circuit died with `System.TimeoutException: Did not receive any data in the allotted
time.`; a later Save then performed a native form POST → 400 *"The POST request does not specify
which form is being submitted."*

**Root cause.** `RecorderPanel.OnFileChosenAsync` opens the recorded take's LIVE `BrowserFileStream`
and hands it to `OnRecorded`. In Dialog mode `InstructionEditorList.OnRecordedAsync` did not consume
it: it stashed the live stream in `_pendingRecording` and then awaited the metadata dialog
(`EditRowRequested`). The stream was therefore left unconsumed across the dialog round-trip — the
pending JS data stream timed out, the dialog's editor interop never completed, no further circuit
event was dispatched, and the take's bytes were unreadable when the confirm finally staged them.

## 2. Decisions (grill round; owner settled 2026-10-15)

| Q | Decision |
|---|---|
| Q1 | Buffer the take into a list-owned `MemoryStream` **before** the dialog pops; release it through the existing pending-take lifecycle (stage / supersede / discard / teardown). Accepted cost: one take in memory, bounded by `AttachmentUploadPolicy.MaxFileSizeBytes`. |
| Q2 | **Withdrawn by the parent as not a legitimate question** — decidable from the repo, and answered: no deferral of the dialog open is needed. Both shipped working shapes (`OnFileUploadedAsync`, the Inline-host recorder path) already consume in-callback, and the kebab path already awaits a dialog inside an event handler. Q1 is the whole fix. |
| Q3 | Add `TimeoutException` to `RecorderPanel.OnFileChosenAsync`'s catch filter so a stalled stream surfaces on the panel's error line instead of terminating the circuit. |
| Q4 | Discriminating bUnit pin + one runbook failure-mode line. |
| Q5 | Plain PR from `main`; round doc in `documents/rounds/`; light round. |

## 3. Change set

| File | Change |
|---|---|
| `src/Assignments/…/InstructionEditorList.razor` | Dialog branch of `OnRecordedAsync` materialises the take into a list-owned `MemoryStream` before the dialog; the buffered take is stashed; the buffer is released after staging (`finally`), when superseded, in `DiscardPendingRecordingAsync`, and via `DisposeAsync`. `_pendingRecording = null` moved out of `ApplyEditAsync` into `StageRecordedTakeAsync` so the release can follow the staging call. |
| `src/Assignments/…/RecorderPanel.razor` | `TimeoutException` added to the `OnFileChosenAsync` catch filter. |
| `tests/SchoolCollab.Assignments.Tests.Unit/InstructionEditorListBunitTests.cs` | `DialogHandOff_BuffersTheTake_BeforeTheMetadataDialogOpens`. |
| `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentsApiClientInstructionMediaTests.cs` | `StageAttachmentAsync_ParameterisedMimeType_StagesAsBareMediaType` + `StageAttachmentAsync_UnparseableMimeType_DegradesToOctetStream` (the second defect's pins, §6). |
| `src/Assignments/…/Services/AssignmentsApiClient.cs` | The staged part's `Content-Type` is normalised to the bare media type (§6). |
| `documents/runbooks/dev-loop-hot-reload.md` | Failure-modes bullet for this symptom class. |

Parent review applied two hardening fixes the worker's report did not surface: the supersede path
now disposes the replaced buffer only **after** the new copy succeeds (a failed retake can no longer
leave the pending take disposed), and the staging release sits in a `finally` (staging has
non-throwing `Fail`-and-return paths that would otherwise strand the buffer).

Note: the independent static reviewer could not be dispatched (Cline auth error), so the parent
performed the static review itself — it is **not** independent.


## 4. Gates

| Gate | Command | Result |
|---|---|---|
| Build | `dotnet build src/Assignments/…/SchoolCollab.Assignments.Application.csproj` | 0 errors |
| Tests | `dotnet test tests/SchoolCollab.Assignments.Tests.Unit/…csproj` | **1159 passed / 0 failed** |
| Discrimination | temporary revert of the buffer (worker) | **1158 passed / 1 failed** — `ObjectDisposedException` at the staging delegate, i.e. pre-fix the bytes were unreadable |

## 5. Live acceptance (the round's UI-tester leg)

Re-run on `dotnet watch` at `https://localhost:7300/assignments/create`:

| Symptom | Before | After |
|---|---|---|
| Description rich-text editor in the dialog | `LOADING`, 0 toolbars | **MOUNTED, 1 toolbar** |
| Preview toggle | dead | **works** — resolves the take's `blob:` URL into a real player |
| Cancel | inert (dialog stays open) | **closes the dialog** |
| Save | native POST → 400, circuit dead | **closes the dialog, no error page** |
| `TimeoutException` in the server log | yes | **none** |
| **The take lands** | row stayed unstaged (`Record` capture slot, no readout) | **staged row — readout `0:15` + Play + ×** |
| Play the staged take | n/a | **works** — session Blob URL plays |

Screenshots: `.playwright-cli/shots/30-FIXED-dialog-editor-mounts.png`,
`31-FIXED-preview-toggle-works.png`, `40-FIXED-take-staged-with-duration.png`;
pre-fix `10-dialog-open.png`, `11-after-cancel-click-still-open.png`,
`12-after-save-click-circuit-dead.png`; control `20-control-dialog-editor-works.png`.

## 6. Second defect — the take did not land (found by this round's live pass; FIXED here too)

The take still did **not** land: after Save the row stayed unstaged (capture slot, no readout), with no
error line and no server exception. An instrumented repro produced:

```
StageRecordedTakeAsync THREW System.FormatException: The format of value 'audio/mp4;codecs=opus' is invalid.
```

Site: `AssignmentsApiClient.StageAttachmentAsync` —
`fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType)`. A MediaRecorder MIME type
carries a `codecs` parameter, which is legal in a MIME type but rejected by that **constructor**
(it accepts a bare type only), so the exception was thrown before the request was sent.
`FormatException` is not in `RecorderPanel.OnFileChosenAsync`'s catch filter, so it escaped the JS
`NotifyChange` invocation (the console's *"There was an exception invoking 'NotifyChange'"*) and was
swallowed — the author saw nothing.

**Pre-existing and independent of the circuit fix** — unreachable before only because the circuit died
at the dialog. Not recorder-specific: any producer whose MIME type carries parameters reaches the same
line. `AssignmentsApiClient.cs:637` was the only production `MediaTypeHeaderValue` site in the repo.

Fix (grill round 2, owner settled 2026-10-15 — Q6–Q8 accepted as recommended, Q9 folded into this
round):

- **Q6 — normalise at the boundary.** `TryParse` the value and use its bare `MediaType`, dropping
  parameters; unparseable input degrades to `application/octet-stream` rather than throwing. Chosen
  over preserving the parameters because the stored value is a bare media type *by design* — the
  served-media route hands it straight to `Results.Stream` (`AssignmentRoutes.cs:1545`), and
  `InstructionKindInference.FromContentType` prefix-matches it.
- **Q7 — the recorder's catch filter is deliberately NOT broadened.** Fixing the boundary makes it
  unreachable, and widening filters is what hid this defect for a round.
- **Q8 — two pins** in `AssignmentsApiClientInstructionMediaTests`: a parameterised MIME type stages
  as the bare type, and an unparseable one degrades to octet-stream. Both **verified discriminating**:
  with the fix reverted they fail with the production exception
  (`FormatException: The format of value 'audio/mp4;codecs=opus' is invalid.`).

| Gate | Result |
|---|---|
| Build | 0 errors |

## 7. Third slice — the seeded review auto-plays (owner request 2026-10-15)

**Request.** "for clicking 'play' icon in list, auto play media once is switched, than to make user
click play again on the media preview page."

**Decision (grill Q15, owner agreed; Q16 = Solo).** Scope is the list's play click **only** — the
post-record review and the retake keep their manual player, because there the author is checking
their own fresh take.

**Change set.**

| File | Change |
|---|---|
| `RecorderPanel.razor.js` | New `playPreview(elementId)` export — `play().catch(() => {})`, the module's own idiom (`startCapture`/`bindPreview` use it verbatim). |
| `RecorderPanel.razor` | `_needAutoPlay` armed in `OnParametersSet` where the SEED is applied (the seed *is* the list's play click), spent once in `OnAfterRenderAsync`. |

`InstructionEditorList` needed no change: `SeedReview` is reached only from the play paths.

**Two facts that shaped the design.**

1. The panel is `@key`ed on the row, so a same-row re-entry REUSES the instance — a plain
   `bool AutoPlay` parameter would fire once and never again. Arming the flag where the seed is
   *applied* avoids that entirely.
2. The flag is cleared only when the call actually runs: the first render lands **before** the module
   import, so the flag must survive it — the same rule `_needPreviewBind` already follows.

**Deliberately fail-soft.** The gesture is one circuit round-trip old when `play()` runs, and the
browser's autoplay policy decides on its own (Chrome/Edge allow unmuted playback after any
interaction with the page; Firefox/Safari are stricter). A refusal is swallowed: the player renders
with its own controls, so a blocked autoplay costs the author one click and never an unhandled
rejection. The harness's `--autoplay-policy=no-user-gesture-required` flag was **removed** so a
future live pass is meaningful rather than self-confirming.

**Gates.** Build 0 errors; `1163 passed / 0 failed` (+2 pins). Both pins **verified discriminating**:
disarming `_needAutoPlay` fails the positive pin with *"playPreview" was not called the expected
number of times*.

**Live pass — NOT DONE (blocked, reported honestly).** The Playwright harness stopped launching
sessions after the AppHost restarts: `open` times out and the session does not persist, and the one
session that did launch landed on `chrome-error://chromewebdata/` (a cert/config problem in the
fresh browser — the earlier sessions had inherited `ignoreHTTPSErrors` from the session created with
`--config`). The app itself is healthy (all services up; port 7300 owned by `dcp.exe`). So the
browser-side half — that the media actually starts playing — remains unverified and is owed.

| Tests (full suite) | **1161 passed / 0 failed** |
| Discrimination (fix reverted, filtered run) | **failed: 2 / succeeded: 0**, both with `FormatException` |

## 8. Fourth slice — the metadata link moves into the recorder header (owner request 2026-10-15)

**Request.** "'Edit Title/Description' is wrongfully placed and not a native blue looking anchor(link).
Add it next to 'Recording audio' as 'Recorded Audio (edit note)'. apply same to other media (video,
image)."

**Decisions (grill round 3, owner: "all as recommended"; mode = Solo — the owner's explicit Q16
answer, which supersedes Q14's light-round recommendation).**

| Q | Decision |
|---|---|
| Q10 | A `RenderFragment? HeaderAction` slot on `RecorderPanel`; the LIST supplies the anchor. The panel owns the header's layout, the host owns the action. |
| Q11 | The link renders on **every** seeded review — the `HasMetadata` gate is **removed**. Rationale: the takeover suppresses the rows, so the readout is unreachable and a bare take had no route to a note at all. |
| Q12 | Label the link per medium — ~~`Recorded Video (edit note)` / `Image (edit note)` / `Recorded Audio (edit note)`~~ → **amended by the owner the same day: a single `Edit Note`**. The medium was already carried by the readout beside the anchor, so repeating it in the label was noise. One `const string` now serves both call sites. |
| Q13a | An IMAGE row gets its own hypertext anchor beside its readout — an image has no recorder and no review (play is Audio/Video only). Audio/Video rows keep the readout as their affordance (round media-pill-listing C2), so they get no row-level link. |

**Change set.**

| File | Change |
|---|---|
| `RecorderPanel.razor` | `HeaderAction` parameter + a `.recorder-header-leading` group so the action sits BESIDE the title (not floating in the header's `space-between` gap). |
| `RecorderPanel.razor.css` | `.recorder-header-leading`. |
| `InstructionEditorList.razor` | The stealth button below the panel is replaced by a hypertext `FluentAnchor` in the header slot; `HasMetadata` deleted; `EditNoteLabel` added; the image row's anchor added. |
| `InstructionEditorList.razor.css` | `.cq-instruction-edit-note` / `.cq-review-edit` (`::part(control)` — FluentAnchor is a shadow-DOM custom element). |
| `InstructionEditorListBunitTests.cs` | Repin of `…RendersOnlyWhenTitleOrBodyExists` → `SeededReview_MetadataLink_AlwaysRendersAsAHypertextAnchor`; + `StagedImageRow_CarriesTheEditNoteHypertextAnchor_AndOpensTheMetadataDialog`; + `StagedAudioVideoRows_DoNotCarryTheRowLevelEditNoteAnchor`. |

**Razor gotcha (cost a build).** A named child element cannot be wrapped in `@if` — RZ9996
("Unrecognized child content"). The condition must live INSIDE `<HeaderAction>`.

**Live pass — DONE, and it is the acceptance for this slice.**

| Check | Observed |
|---|---|
| Header layout | `"Recording audio \| Edit Note \| Cancel"` — the link sits immediately after the title (read from `.recorder-header-leading`: `"Recording audioEdit Note"`) |
| Native link look | a real `<a>` inside the FluentAnchor's shadow root: `color: rgb(58,58,120)`, `text-decoration: underline` |
| Bare-take review (Q11) | the link renders |
| Image row (Q13a) | row reads `70 B · Edit Note · ×`; the anchor is the same blue underlined link; clicking it opens the **Edit Material** dialog (`authoring-instruction-items-edit-note-1 = "Edit Note"` → `FLUENT-DIALOG` "Edit Material") |
| Audio/Video rows | no row-level link (their readout stays the affordance) |
| **Autoplay (slice 3) re-verified** | `audio.paused === false`, `currentTime 4.91` with `--autoplay-policy=no-user-gesture-required` REMOVED — i.e. it plays under the browser's real autoplay policy |

Screenshots: `50-header-edit-note-link.png` + `51-image-row-edit-note-link.png` (the per-medium wording), then
`52-header-edit-note-simplified.png` + `53-image-row-edit-note-simplified.png` after the owner's label amendment.

**Environment note (cost significant time).** The Aspire proxy (`dcp.exe`) wedged: port 7300 stayed in
LISTEN while TCP connects hung (curl `000`, browser `chrome-error://chromewebdata/`), which looked
exactly like a cert/config fault. Recovery: kill the watcher tree + any `dcp`/AppHost leftovers,
`dotnet build-server shutdown`, restart the watcher. Two MSB3501/CS2012 build locks were also hit and
cleared the same way — both surfaced rather than retried in a loop.

