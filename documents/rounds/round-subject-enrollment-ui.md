# Round — subject enrolment requirement, layer 2 (the UI)

Mode: attended · Tier 2 light + live Playwright probe (owner Q48) · branch `feat/subject-enrollment-requirement-layer2`, cut from `main` **after #426 merges** (it carries the friendly-error seam that the new UI must use, and the `LandingPage` suppression).

Layer 1 (data + API) merged in #422 on `648b7f89`. This round is the half that never shipped: the amber band, the switch, and the enrol action.

## Settled by the owner (rounds 13/14 grill)

| # | Decision |
|---|---|
| Q39 + Q44 | The switch lives on **`GradeTopicsDialog`'s per-row kebab** (it already carries the bridge `AssignmentId`). The topic edit dialog is **wrong** — it edits the shared `Topic`, and one topic can be assigned to several grades with different flags. |
| Q42 | **Amber band = every `true` subject**: enrolled ones as normal chips, un-enrolled struck through and labelled "not chosen". Four bands total: Placement · Activity groups · **Needs explicit enrolment** · Assumed. |
| Q45 | A struck-through "not chosen" chip is **actionable** — clicking enrols that student (`AssignStudentTopicAsync`). |
| Q46 | Layer 2 = bands + kebab switch + enrol action. |
| Q48 | **Tier 2 light + a live Playwright probe** — no UI-tester seat, so the probe carries the visual burden. It must see the strike-through, the band count and the write-on-click; "the page rendered" is not a pass. |
| Q40 | **No feature flag** — `RequiresEnrollment` defaults `false`; the default is the safety. |

## What already exists (reuse before new infrastructure)

| Thing | State |
|---|---|
| `RailBand` / `RailChips` / `RailChip` | shared components from the activity-groups round |
| `RailBandVariant` | already defines `Neutral` · `Groups` · `Required` · `Assumed` — the four bands need **no new variant** |
| `GET /students/{id}/subjects` | layer 1; union returns only enrolled/implied rows |
| `GET /students/topic-enrollment` | layer 1; `TopicEnrollmentDto(Audience, Eligible)` |
| `PUT …/requires-enrollment` | layer 1; transactional grandfather on `false → true` |
| `GradeTopicsDialog` | already renders one `RowActionsMenu` kebab per row with typed action keys |

## Plan-gate amendments (`P1` → amended before any code)

The gate blocked on four findings. Each is resolved here rather than left to the worker:

**F1 — the dialog cannot label or reflect the switch: the row type carries no flag.** `GradeTopicCurriculumDto`
(Core + the client copy at `StudentsApiClient.cs:175-180`) has `AssignmentId/TopicId/Name/Code/StrandCount/LessonCount`
only, and `ListGradeTopicCurriculumByGradeHandler.cs:68` never selects `RequiresEnrollment`; the **client-side**
`TopicAssignmentDto` (`StudentsApiClient.cs:165-175`) drops it too, even though the Core DTO has it.
*Resolution:* the flag flows to the row — add it to both `GradeTopicCurriculumDto` records, select it in
`ListGradeTopicCurriculumByGradeHandler`, and add it to the client `TopicAssignmentDto`. **Grade id for the pre-write
count:** `GradeTopicsDialog` takes the grade level id as a `[Parameter]` from its host page (it does not hold one
today), so `GetTopicEnrollment(gradeLevelId, topicId)` is callable before the write.

**F2 — the enrol request's `PeriodId` was unspecified.** `AssignStudentTopicRequest` requires
`StudentId, TopicId, PeriodId, IsOverride, SourceType`, and `AssignStudentTopicHandler` rejects a `PeriodId` that is
not the active academic year or a sub-period of it (409/422) even though it stamps the row server-side.
*Resolution, pinned:* `Api.AssignStudentTopicAsync(new AssignStudentTopicRequest(Id, chip.TopicId, activeYearId,
IsOverride: false, SourceType: "IndividualAssignment"), ct)` with `activeYearId =
(await Api.GetActiveAcademicYearAsync())?.Id` (`StudentsApiClient.cs:1412`). **Do not** reuse the enrolment's
`PrimaryActiveEnrollment` period — it may be a sub-period and is not the contract the handler validates against.

**F3 — the probe as written would see zero struck chips.** `SetRequiresEnrollmentHandler` grandfathers **every**
current audience member on `false → true`, so the student you open immediately after the flip is already enrolled and
renders a normal chip. *Resolution:* the probe **flips first, then enrols a fresh student into that grade** (post-flip,
so not grandfathered), opens *that* student, and only then asserts the struck chips. Otherwise there is nothing to click
and the probe silently proves the opposite of what it claims.

**F4 — `RailChip` cannot render the required control.** It renders `<span role="listitem">`; its `Blocked` state is
the only strike-through (`app.css:371`) and is `aria-disabled`, i.e. non-interactive. Acceptance criterion 3 needs a
**keyboard-focusable button**. *Resolution:* **extend `RailChip` with a third state** — struck **and** actionable,
rendering a real `<button type="button">` inside the chip with the a11y contract the component-first policy §4
requires (accessible name = the subject plus its state, `aria-disabled` never used for an actionable control). **Do not**
invent a page-local button chip and do not add a new component at a first call site.

**Notes folded in:** `ListStudentSubjectsAsync` (`:1932`) and `AssignStudentTopicAsync` (`:1909`) **already exist** — P1
adds only `GetTopicEnrollment` and `SetRequiresEnrollment`. `GradeTopicsDialog` needs `@inject ILogger` for
`FriendlyError.Describe`. Kebab actions are **label + `RowAction.Callback`**, not keyed like `EditKey`/`StrandsKey` — so
P4 adds a `[Parameter]` callback plus page wiring. And **do not copy `Edit.razor`'s `_groupError = ex.Message`** pattern:
the ratchet would flag it; new sites use the seam.

## Scope

### P1 — client surface (`StudentsApiClient`)

Two **new** methods (the other two exist — `ListStudentSubjectsAsync:1932`, `AssignStudentTopicAsync:1909`):
get a topic's enrolment pair, and set the requirement flag. Mirror the layer-1 endpoints and the existing client style;
pin the enrol call exactly as F2 above specifies.

### P2 — the union must carry the "not chosen" state

`ListStudentSubjectsHandler` currently returns only rows the student is enrolled in, so `IsEnrolled` is always `true` and the amber band **cannot** be rendered. Extend it to carry un-enrolled `true` subjects for the student's grade with `IsEnrolled = false`.

- **Keep the existing `IsEnrolled == true` contract for every current caller** — this is additive.
- The grade's `true` subjects come from the same audience source layer 1 established (`Audience(g)`), not a second query shape.
- A test must assert a `false` subject is **never** returned even when a stale `StudentTopicAssignment` row exists for it (the row is ignored, per FR-ER-9).

### P3 — the rail bands (`Students/Edit.razor`)

Two new bands beside the existing Placement and Activity groups:

| Band | Variant | Content |
|---|---|---|
| Needs explicit enrolment | `Required` | every `true` subject on the student's grade: enrolled → normal chip; un-enrolled → **struck through, labelled "not chosen", clickable** |
| Assumed | `Assumed` | subjects the student gets without choosing (the implied set) |

- The struck-through chip must be **visually distinct without colour alone** (a strike-through plus the words "not chosen" — colour is not an accessible signal), and must be a **button**, not a div, since it does something. The mechanism is **F4's third `RailChip` state** — struck + actionable — not a page-local control.
- Clicking it enrols via P1's method and replaces the chip in place; **the failure path uses `FriendlyError.Describe`** (#426) and leaves the chip unchanged.
- Empty bands render nothing rather than an empty shell.

### P4 — the switch (`GradeTopicsDialog` kebab)

A new kebab action per row, keyed like the existing `EditKey`/`StrandsKey`, labelled for the state it will produce ("Require explicit enrolment" / "Stop requiring explicit enrolment" — never a bare "Toggle").

- Calls `PUT …/requires-enrollment` with the row's bridge `AssignmentId`.
- **Surfaces the grandfather consequence before the write**: flipping `false → true` enrols every current grade member, and the UI must say so in the confirm (the shared `RowActionsMenu` already supports `confirmMessage`). Flipping to `false` keeps existing rows; say that too.
- On success, the row reflects the new state without a page reload; on failure, `FriendlyError.Describe`.

### P5 — probe and tests

- bUnit: the amber band renders both chip states; the struck-through chip is a button carrying its label; clicking calls the client once and on failure leaves it unchanged; the kebab action issues the PUT with the bridge id and shows the confirm first.
- **Live Playwright probe** (the Q48 substitute for a UI-tester seat): per **F3** — flip a subject to `true`, then **enrol a fresh student into that grade** (post-flip, not grandfathered), open *that* student, and confirm the amber band appears with the un-enrolled chips struck through **and** the enrolled ones not struck; click one and confirm it becomes enrolled (a second render, not a toast); flip the flag back. Capture console errors — expected zero.

## Explicitly NOT in this round

| Deferred | Why |
|---|---|
| Bulk "enrol the remaining N" (FR-ER-8 second half) | Layer 2 ships the per-student action (Q45); bulk is a separate change |
| §7 rollover screen | Its own round; the amber band is what makes the year-boundary cliff visible, not hidden |
| `/recipient-preview` `topicId` param (#423) | Wizard surface, filed separately |
| The 298-entry friendly-error ledger (#424) | Unrelated debt; the new UI uses the seam but does not sweep the ledger |

## Acceptance criteria

1. Four bands render in the rail in the settled order; `RailBandVariant` gains **no** new member.
2. A `true` subject with an enrolled student renders one normal chip; with an un-enrolled student one struck-through chip reading "not chosen", and the strike is not the only signal.
3. The struck-through chip is a `<button>` and is keyboard-focusable.
4. Clicking it calls the client exactly once and the chip becomes enrolled on success.
5. On failure the chip is unchanged **and** a plain sentence renders (`FriendlyError.Describe`), with no exception text and no status code.
6. `GET /students/{id}/subjects` returns un-enrolled `true` subjects with `IsEnrolled = false`, and still returns nothing for a student of another tenant.
7. A `false` subject with a stale assignment row is **not** returned.
8. The kebab action issues `PUT …/requires-enrollment` with the row's bridge id and shows a confirm naming the grandfather consequence before writing.
9. The dialog reflects the new state after success without a full page reload.
10. `dotnet build` 0 errors; the affected unit suites green; `scripts/round/gates.sh` (escalated, no flags — shared chrome + a contract change) exit 0 with every skipped leg named.
11. The live probe records: band present, chip states distinguishable, click enrols, console clean.

## Not run

| Leg | Why |
|---|---|
| OpenRouter live tests (`OpenRouterClient_UsesApiKey_AuthenticatesSuccessfully`, `OpenRouterProvider_WithGoogleGemma4_RespondsToSimplePrompt`) | the integration leg's only two failures, demoted by `gates.sh`'s own allow-list: CI self-skips them for lack of a key and this machine's provider answers HTTP 400 |

Nothing else was skipped — `gates.sh` ran with **no flags**. The live probe ran, in three attempts (see below).

## The probe found the round's real defect — and it was not in this round's diff

**Clicking the struck chip did not enrol the student.** The page rendered *"'Mathematics' could not be
added to this student. Try again."* The cause, proven against the running API by sending the same body
twice: `"sourceType":"IndividualAssignment"` → **400** `The JSON value could not be converted to
…AssignStudentTopic. Path: $.sourceType`; `"sourceType":1` → **201**.

`StudentsApiClient.AssignStudentTopicRequest.SourceType` is a **string**; the command's `SourceType` is a
**`SubjectAssignmentSource` enum**; the Students API registered **no** `JsonStringEnumConverter` (the
Assignments API does, per enum type). Both records predate this round — the feature's core action was
broken end-to-end while build, 4624 unit tests, and a diff review were all green.

**Why no test caught it, and what now does.** The bUnit band test asserts the *client's* serialized body
against a stub handler — it can prove the client sends the name, never that the server binds it. The defect
lived in the seam between the two. `SubjectAssignmentSourceJsonTests` closes it by round-tripping through
the **host's own configured `JsonOptions`** (a hand-built options object would have supplied the missing
converter and masked the bug), and **teeth-checked**: remove the registration and both tests fail with the
identical `JsonException` from production.

**Fix:** `JsonStringEnumConverter<SubjectAssignmentSource>` registered **per enum type** in
`Students.Api/Program.cs`, matching the Assignments precedent so no other endpoint's JSON changes.

## Probe evidence (live, AppHost + Playwright)

| Step | Result |
|---|---|
| P4 switch, on | `PASS` — confirm *"This enrols all 0 students in this grade now, so nobody loses the subject…"*, menu label flips, `reflected=True` on reopen |
| P4 switch, off | `PASS` — confirm names the history it keeps; menu flips back |
| Fixture | `PASS` — student STUA03 created **after** the flip via `POST /students/with-linked-data` (the create form was unusable: gender combo opened 0 options — filed) |
| Union | `PASS` — `/students/{id}/subjects` returns both flipped subjects with `isEnrolled:false` |
| Band | `PASS` — `#student-edit-subjects-required`, `aria-label="Needs explicit enrolment"`, hint `0 of 2 chosen` |
| Chip | `PASS` — `BUTTON`, `text-decoration-line: line-through`, `aria-label="Mathematics — not chosen"`; **Tab** from `#student-edit-add-group` lands focus on it |
| Click | `PASS` — chip becomes plain text (`actionable=False`, `deco='none'`), hint `0 of 2` → `1 of 2`, **no reload** (marker, navigation entries and `timeOrigin` unchanged); DB row `source_type=1`, active-year period |
| Sibling untouched | `PASS` — English Home Language still struck and actionable (no over-broad re-render) |
| Console | **0** errors, 0 page errors, 0 HTTP ≥ 400 (one `_blazor/disconnect ERR_ABORTED` in the uninstrumented run; a second instrumented run that waits before closing recorded nothing, so it is teardown, not session) |

Screenshots: `$TEMP/enrol-probe/` — `3-band.png`, `3b-focused.png`, `3c-band-mixed.png`, `5a-before-click.png`, `5-enrolled-by-click.png`.

The probe also **refused** one criterion rather than claim it: "other subjects in that band are not struck" was
not observable on this fixture (Grade 4 offers exactly the two flipped subjects), so it observed the
complement instead — a mixed band with one enrolled and one struck.

## Acceptance

**Verdict: ACCEPTED.** Signed by the plan's author (parent) on the diff review (`APPROVED WITH NOTES`, zero
blocking), the probe, and the parent's own gate run.

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | Four bands, no fifth `RailBandVariant` | **met** | bands render; enum untouched (`Neutral/Groups/Required/Assumed`) |
| 2 | Enrolled chip normal, un-enrolled struck + labelled, strike not the only signal | **met** | `line-through` **and** visible "· not chosen" + `aria-label`; probe + bUnit |
| 3 | Struck chip is a focusable `<button>` | **met** | probe: `nameTag=BUTTON`, Tab focus lands |
| 4 | Click calls the client once; chip becomes enrolled | **met** | probe click → plain chip, `1 of 2 chosen` |
| 5 | On failure chip unchanged + plain sentence, no exception text | **met** | bUnit; and the real failure path was observed live *before* the fix (the sentence, not the JsonException) |
| 6 | `/subjects` returns un-enrolled `true` subjects; tenant isolation | **met** | probe JSON `isEnrolled:false`; `Union_ReturnsNothing_ForAStudentOfAnotherTenant` |
| 7 | A `false` subject with a stale row is not returned | **met** | handler test |
| 8 | Kebab issues the PUT with the bridge id, confirm names the consequence | **met** | probe: both confirms quoted above |
| 9 | Dialog reflects new state without reload | **met** | probe `reflected=True` on reopen |
| 10 | Build 0 errors; suites green; escalated gate exit 0 with skips named | **met** | build 0 errors; unit **4626/4626**; `gates.sh` **no flags, exit 0** |
| 11 | Probe records band, chip states, click, console | **met** | table above |

**Accepted notes**

1. **The confirm's audience count is read at dialog open** — a topic assigned during the same dialog session falls back to the numberless sentence. Graceful, documented, and it means the confirm is sometimes less specific than it looks.
2. **One switch test is a source-text tripwire**, not behavioural — it reads `Detail.razor` and matches substrings; the bUnit tests beside it carry the behaviour.
3. **`GradeTopicsDialog.MoveAsync` still interpolates `ex.Message`** — pre-existing, on #424's sweep list, not introduced here.
4. **A sub-period enrolment yields an empty union, silently** — a student enrolled in Grade 4 *Term 2* gets `[]` from `/subjects`, so their page renders no subject bands at all. Filed as its own issue; the union matches `PeriodId == activeAcademicYear.Id` exactly.
5. **The create-student form's gender combobox opened 0 options** in the probe environment, so the fixture had to be created by API. Not this round's code, but it is why attempt 1 could not proceed — worth its own look.
