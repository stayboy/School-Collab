Provider: pi, Tier 0 solo (owner Q71, 2026-10-24 — "go solo").
Mode: attended

# Round — panel rollout, Shape A first cut: Students create + edit (#411)

## Settled by the owner (round 12, 2026-10-24)

| # | Question | Settled as |
|---|---|---|
| Q70 | Approve the mock? | **Rejected v1** — the rail was missing, collapsibility was not named per panel, and the kebab affordances were unspecified. Re-cut as mock v2 |
| Q71 | Execution mode | **Solo** (Tier 0) |
| Q72 | Scope | **`/students/create` + `/students/{id}/edit` only** — grade levels and periods get their own round |
| Q73 | Edit block header | **Keep "Details"** titled; a hidden header is only for a create form the page title already names |
| Q74 | Error message home | **Stays in the block**, above where the action row used to be |
| Q75 | `StudentFormFields` API | **Untouched** — `RenderActions="false"` at the page call site only |
| Q76 | No-tenant posture | Header primary **disabled**; the message bar lives inside the block |
| Q77 | Retro | Run at the end of the round |
| Q81 | Contacts | **Main column, non-collapsible** (not rail) |
| Q82 | Guardians / Contacts collapsibility | **Contacts: no** (it is a main-column block — the main region is never collapsible). **Guardians: yes** (opt-in chevron, per §6 and the four existing rail panels) |
| Q83 | Enrollment actions | **Kebab**: `Enroll…` at panel level, `Transfer…` / `Withdraw…` at row level |
| Q84 | Create shape | **Single column, drafts inline** — draft guardians/contacts are form state, not relationships, so they fail §5's ownership test |
| Q86 | Page-level secondary actions | **None this round** — no page kebab; `DeleteStudentAsync` stays unexposed |

### Q85 — corrected during planning, not acted on as recommended

The recommendation said "build a shared `AuthoringLayout` in `Admin.Shared`, re-point
`AssignmentAuthoring` and `TemplateEditor`". **That premise was false.** The primitive already
ships in `src/SchoolCollab.Admin.Shared/wwwroot/app.css:153-214`:

| Class | Rule |
|---|---|
| `.region-grid-host` | the container (`container-name: region-grid-host` — a grid cannot query itself) |
| `.region-grid` | `minmax(0,1fr) minmax(320px,460px)`, gap 16 |
| `.region-grid__main` | flex column, gap 16 |
| `.region-grid__rail` | `position: sticky`, `align-self: start`, offset from `--app-bar-sticky-offset` |
| `.region-stack` | single-column stack, draws nothing |
| `@container (max-width:899px)` | collapses to one column |

`TemplateEditor` already consumes it; `AssignmentAuthoring.razor.css:13` names it. So the answer to
Q85 is **reuse, build nothing, re-point nothing** — the owner's intent (one pattern-level layout, not
per-page CSS) is already satisfied. Building `AuthoringLayout` would have been new infrastructure
duplicating what ships, which `AGENTS.md` § "Reuse before new infrastructure" forbids.

## Implementation

| File | Change |
|---|---|
| `Students/Create.razor` | `AuthoringShell` (crumbs `Students › New student`, title **New Student**, `Cancel` + primary `Create Student`); `.region-stack`; one `PanelSection ShowHeader="false"`; `@ref` on the form + primary calls `SubmitAsync()`; `PrimaryDisabled` when no real tenant; `TenantGate` fallback bar inside the block |
| `Students/Edit.razor` | `AuthoringShell` (crumbs `Students › {name} › Edit`, title **Edit Student**, `Cancel` + `Save Changes`); `.region-grid-host/.region-grid`; **main** = `Details` (titled, no chevron; `StudentFormFields RenderActions="false" ShowGuardians="false"`) + `Contacts` (titled, no chevron; hosts `ContactsEditor`); **rail** = `Guardians` (`Collapsible`, panel kebab `Add guardian…` / `Manage guardians…`, one row per link with a `⋮` unlink) + `Enrollment` (no chevron; panel kebab `Enroll…`, row kebab `Transfer…` / `Withdraw…`; read-only facts) |
| `Students/Edit.razor.css` | delete `.student-edit-grid*`, `.student-edit-action-stack*`, `.title-row`/`.page-title`; keep the enrollment + contact rules the panels still use |
| `Students/Create.razor.css` | delete `.title-row`/`.page-title` if unused |
| tests | retarget the surface assertions; add one structural test per page (which row owns the pair; a non-collapsible panel renders **no** chevron) |

### As built — where the code differs from the plan row above

- Crumbs render the **student number** (`Students › STUA01 › Edit`, falling back to the first 8 chars of
  the id), not `{name}`: `StudentCrumbLabel()` in `Edit.razor` picks the stable identifier. Crumbs stay
  sentence case; only the page title is Title Case (§4).
- The Guardians panel kebab carries **one** action, `Add guardian…` (it opens the *Manage guardians*
  dialog — that dialog's own title is "Manage guardians"); the plan row's second item does not exist.
  The per-row `⋮` carries `Unlink guardian…`.

## Verification

| Gate | Command |
|---|---|
| Build | `dotnet build SchoolCollab.slnx` |
| Gate (full, no flags — shared chrome) | `scripts/round/gates.sh` |
| Rendered proof | AppHost + Playwright: title case, pair flush right, one pinned row, chevron only on Guardians, no chevron on Enrollment |

### Rendered proof — measured (2026-10-24, Chromium 1440×950 @DPR2, dev AppHost)

Probed against a real created student (`01234282-11af-4d56-87db-ffd5813282de`, number `STUA01`),
not bUnit markup.

| Assertion | Create (`/students/create`) | Edit (`/students/…/edit`) |
|---|---|---|
| Title case | `New Student` (bar heading + only `h1`) | `Edit Student` (bar heading + only `h1`) |
| Pair flush right | `controlsRight 1416 == barRight 1416` | `controlsRight 1416 == barRight 1416` |
| One pinned row | bar `position: sticky`, `top: 0px` | bar `position: sticky`, `top: 0px` |
| Primary / secondary | `Create Student` / `Cancel` | `Save Changes` / `Cancel` |
| Regions | one block `student-create-details`: no title, no chevron, `borderTopColor rgba(0,0,0,0)` (borderless main) | grid `692px 460px`; main `student-edit-details` + `student-edit-contacts` (no chevron, borderless); rail `student-edit-guardians` (**chevron**) + `student-edit-enrollment` (no chevron), both `rgb(225,225,225)` |
| Crumbs | `Students › New student` | `Students › STUA01 › Edit` |
| Narrow (820px) | — | `.region-grid` collapses to one track (`548px`), rail stacks below main via the shared container query |
| Console | no errors | no errors |

Two observations, neither caused by this round:

- The crumb shows the **student number** (`STUA01`), not the name the plan wrote (`{name}`). The
  number is also what the API stored after a typed `S-2026-777` — the entity-code rule overrides the
  form value (pre-existing create behaviour, outside these two pages).
- **Found defect (pre-existing):** in `ContactsEditor`'s add row the channel dropdown paints over the
  neighbouring `Label (optional)` field — hit-testing at the label's left edge returns
  `fluent-dropdown`, and hiding the dropdown reveals the full placeholder. `ContactsEditor` and
  `DropdownForEnum` are untouched here (last change 2026-10-17) and the pin set is unchanged, so the
  round neither introduced nor is expected to fix it; reported rather than widened into this change
  set (filing it is an owner decision, see the reply).

## Owner decisions after the rendered probe (round 3, 2026-10-24)

| Q | Decision |
|---|---|
| Q3 — dropdown overlap | **Bump the 3 `ContactChannel` sites from W2 (120px) to W3 (160px)** — `ContactsEditor.razor:152`, `:278`, `ContactChangeDialog.razor:37` — plus a "a dropdown is never sized below W3" note in the width guidance and a deterministic `EditContactEditorTests` assertion on the emitted `width:160px`. **No** shared `overflow: hidden` clip rule: it would hide the next too-narrow dropdown instead of failing loudly. |
| Q4 — Student Number discarded on create | **Filed as issue #413**, fixed in a dedicated pass (shared `StudentFormFields`, 4 call sites). Not widened into this change set. |

Root cause of Q3: Fluent v5's `fluent-dropdown` shadow `.control` hard-codes `min-width: 160px`; the
W-ladder sizes only the host (`app.css` `fluent-field > fluent-dropdown { width:100%; min-width:0 }`)
and the host keeps `overflow: visible`, so a 120px host paints 160px and covers its neighbour. Exactly
three W2 sites exist, all `ContactChannel`; every other dropdown in `src` is W3+.

Still open (next round): the commit scope/timing question, then the execution mode for the W3 bump.

### Q3(a) as built — one substitution from the accepted answer

Accepted: *bump the 3 `ContactChannel` sites to W3, pin the floor in the width guidance, assert the
emitted `width:160px` in `EditContactEditorTests`.* Built as:

| Step | Where |
|---|---|
| W2 → W3 | `ContactsEditor.razor` (add row + buffered inline-edit row), `ContactChangeDialog.razor:37` |
| Floor pinned | `FieldWidth.cs` — new `<para>` in the enum's `<remarks>` (root cause, the measurement, why CSS cannot enforce it, why the `overflow: hidden` clip was rejected); W1/W2 summaries now say "single-line text fields, never a dropdown" |
| Assertion | **`tests/SchoolCollab.ArchitectureTests.Unit/DropdownWidthFloorArchitectureTests.cs`** — a repo-wide `.razor` scan proving no `DropdownForEnum` / `CodedValueDropdown` / `DropdownComponent` is sized at W1/W2, instead of a single-file `EditContactEditorTests` assertion |

Why the substitution: `EditContactEditorTests` reads `Edit.razor` only, so it could not see the two
`ContactsEditor` sites or `ContactChangeDialog`; the repo-wide scan lives where razor source scans
live (`RazorMarkupHygieneArchitectureTests`, `DialogChromeArchitectureTests`) and catches the next
too-narrow dropdown anywhere. Scanner proven on real data: 188 `.razor` files, **64** dropdown tags
matched (W3 ×10, W4 ×3, W5 ×3, W6 ×3, W9 ×5, 40 with no `Width`), 0 violations; a mutated W3→W2 is
flagged.

### Gates run for this change set (2026-10-24)

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors**, 48 pre-existing warnings |
| `ArchitectureTests.Unit` (whole project) | **145/145** passed |
| `Admin.Tests.Unit` (whole project) | **820/820** passed |
| `Students.Tests.Unit` (whole project) | **637/637** passed |
| Rendered re-probe (Chromium 1440×950 @DPR2) | channel field `160px` = control `160px` (was 120 vs 160), label starts 8px after the field, hit-testing at the label's left edge returns `fluent-text-input` (was `fluent-dropdown`), full "Label (optional)" visible, no console errors |
| Full gate `scripts/round/gates.sh` | **not yet run** — belongs to the PR pre-flight; the diff touches `SchoolCollab.Admin.Shared` (`FieldWidth.cs`), so it must run with **no flags** before a PR |

Commit shape (owner, Q5 → b): two commits on one branch — the panel rollout, then the
`ContactChannel` W2→W3 fix. Not committed; awaiting the explicit instruction.

## Round 4 (2026-10-24): pattern-conformance check against the two reference creates

The owner's Q7 answer: *the pattern is well established by assignment create and notification template create — no
doc change if the students surface uses the same pattern; follow those two.* So the check was rendered, not read:
all three create routes probed side by side (Chromium 1440×950 @DPR2, dev AppHost).

| Axis | `/students/create` | `/assignments/create` | `/notification-templates/create` |
|---|---|---|---|
| Page-title carrier | bar heading `New Student`, no `h1` | bar heading `New Assignment`, no `h1` | bar heading `New Notification Template`, no `h1` |
| Crumb case | `Students › New student` | `Assignments › New assignment` | `Notification templates › New template` |
| Action bar | `sticky`, `top:0`, controls flush right, `Cancel` + `Create Student` | same, `Cancel` + `Save as Draft` | same (no primary in the bar) |
| Main block | `#student-create-details` — **no head, no title, no chevron, border not painted** | `#authoring-basics` — identical | `#template-editor-message` — identical |
| Region | `.region-stack` (single column) | `.region-grid` + 3 rail panels | `.region-grid` + 2 rail panels |
| Rail content | **none** | Targets & Audience (count 0, no chevron, kebab) · Content & Resources (chevron, count 0, kebab) · Rules (chevron) | Constraints (chevron, kebab) · Preview (chevron) |

**Conclusion: the create surface already follows the pattern in both reference pages**, on every axis the pattern
names. `ShowHeader="false"` + `ShowBorder="false"` on create is not a students invention — it is the established
idiom, verbatim:

- `AssignmentAuthoring.razor:186-187` — `<PanelSection Id="authoring-basics" Title="Details" ShowBorder="false" ShowHeader="@(Mode != AssignmentAuthoringMode.Create)">`
- `TemplateEditor.razor:77-81` — `<PanelSection Title="Message" … ShowBorder="false" ShowHeader="@(Mode != TemplateEditorMode.Create)">`
- `Create.razor` — `<PanelSection … ShowHeader="false" ShowBorder="false">`

The **only** difference is the region mode, and it is content-driven, not a deviation: students has no relationship to
put in a rail at create time (draft guardians are form state, Q84), and `panel-layout-pattern.md` §6 already rules
*"If the rail is empty, the page is single-column."* No doc change made, per the owner.

Residual doc inaccuracy, left untouched by owner decision: §5's rail-membership table still reads
`Student (planned) | Details | Guardians · Contacts`, while the built (and owner-approved, Q81) layout puts **Contacts
in the main region** and adds an **Enrollment** rail panel that the table does not name.

## Round 4 — Q8(a) and Q9(a) as built

| Item | Result |
|---|---|
| Q8(a) guardians dropdown | `GuardianSection.razor` `Style="width: 150px"` → `Style="@FieldWidth.W3.ToCssStyle()"` (160px, no literal); the stale `150px` note in `GuardianSection.razor.css` replaced. Re-measured: host **160** = control **160**, control right 617 vs next input left 625 → **8px clear** (was a 2px overlap) |
| Q8(a) guard | `DropdownWidthFloorArchitectureTests` gained `NoRawDropdownControl_SizedBelow_FieldWidth_W3` — scans raw `<FluentSelect>` / `<FluentDropdown>` / `<FluentAutocomplete>` for a literal `width: <160px` or a `w-1`/`w-2` class (53 tags scanned, 0 violations). **Negative control run:** restoring `Style="width: 150px"` makes it fail; restored file passes |
| Q9(a) | Filed [#414](https://github.com/stayboy/School-Collab/issues/414) — `FormRow`'s W9 "fill" step fills only a content-sized cell (name field 378px inside a 1134px form), with the measurement and the reason it is not riding this round |

Gates after these edits: `dotnet build SchoolCollab.slnx` **0 errors**; `ArchitectureTests.Unit` **146/146**;
`Students.Tests.Unit` **637/637**.

## Round 4 — Q11 / Q12 settled

| Q | Settled as |
|---|---|
| Q11 — §5's student row (`Student (planned) · Details · Guardians · Contacts`) | **Left as-is** (owner Q11(a)): the pattern rules themselves are correct and proven by assignment + template; the factual inaccuracy is recorded here rather than amended in the doc. The next surface that copies that row will read Contacts as a rail panel — if that needs correcting later, it is a one-row index fix. |
| Q12 — commit shape | **Three commits** on `feat/students-panel-rollout`: (1) students panel rollout, (2) `ContactChannel` W2→W3, (3) guardians 150→160 + the raw-dropdown guard. |

No push, no PR: both still need their own instruction.

## Round 5 (2026-10-24): the two surfaces unified — model create on edit

Owner instruction: *"model new student create to edit student. guardians and contacts on both must be the same. edit
doesn't have guardians"*, then *"hide details title on edit. same on create too. contacts should not be borderless on
both create and edit page for students"*.

Both pages now present the **same three blocks in the same order**:

| # | Block | Frame | Header | Create | Edit |
|---|---|---|---|---|---|
| 1 | Details | borderless (`ShowBorder="false"`) | **none** (`ShowHeader="false"`) | `StudentFormFields` | `StudentFormFields` (read-only number) |
| 2 | Contacts | **bordered** | titled `Contacts`, no chevron | `ContactsEditor` **Buffered** → `Model.Contacts` | `ContactsEditor` Live (`OwnerId=Id`) |
| 3 | Guardians | **bordered** | titled `Guardians`, no chevron | `GuardianSection` **Inline** (drafts) | `GuardianSection` **Linked** (manage/remove) |
| — | Enrollment | bordered, rail | titled, no chevron | absent (nothing enrolled yet) | rail panel, unchanged |

| File | Change |
|---|---|
| `Students/Create.razor` | `ShowGuardians="true"` dropped from `StudentFormFields`; two new blocks `student-create-contacts` + `student-create-guardians`; page renders the shared editors directly; **new contact flush** — `Model.Contacts` is POSTed via `Api.AddContactAsync` once the student has an id (mirrors the existing guardian flush) |
| `Students/Edit.razor` | Details `ShowHeader="false"`; Contacts lost `ShowBorder="false"` (now boxed); Guardians **moved from the rail into the main column** as a bordered block hosting `GuardianSection` Linked; page-local `rail-list`/count-chip markup and its `GuardianPanelActions`/`GuardianRowActions`/`GuardianRowAriaLabel` helpers deleted (the shared component owns those affordances now); rail keeps **Enrollment** only |
| `Students/Edit.razor.css` | `.panel-kebab`, `.panel-count`, `.rail-list*` deleted; `.rail-empty` / `.enrollment-display*` kept |
| `EditSidebarLayoutTests.cs` | retargeted: asserts the CSS carries the enrollment rules and **no** dead rail-list/count-chip rules (comments stripped before asserting) |
| `StudentsPanelLayoutTests.cs` | retargeted: all three blocks in `.region-grid__main`, only Enrollment in the rail, and **no** chevron on any edit block |

### Verification

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| `Students.Tests.Unit` | 637/637 |
| `Admin.Tests.Unit` | 820/820 |
| `ArchitectureTests.Unit` | 146/146 |
| Rendered, create (1440×1100 @DPR2) | Details headerless/borderless · Contacts bordered+titled · Guardians bordered+titled · stack · no console errors |
| Rendered, edit (same viewport) | same three blocks, same order, same frames · Enrollment bordered in the rail · no console errors |
| **End-to-end (live)** | filled the create form + added one contact → submit → `/students`; DB row `STUA02` (`Contacty McFlush`) **and** `contacts.value = flush2@example.com` with `owner_id` = that student — the new Buffered→API flush works |

Not run: MAUI head — no `SchoolCollab.Admin.Shared` or package change in this round (the earlier `FieldWidth.cs` edit was
already verified green there). Full `scripts/round/gates.sh` still belongs to the PR pre-flight.

## Round 6 (2026-10-24): mock v3 approved — guardians rail on both pages, kebab-only add, one-line contact row

Owner: *"screenshot matches my expectation. all as recommended"* — Q13(a) muted Enrollment on create, Q14(a) dialog for
adding, Q15(a) one line down to ~520px. Mock: `.pi/mock/panel-rollout-students-v3.png` (rendered from
`panel-rollout-students-v3.html`, 1780×1200 @DPR2).

| Change | File |
|---|---|
| Contacts add row is **one line** — channel · label · [value ＋]; the value+Add pair is one nowrap group and the row wraps only when it cannot hold them (fallback = the old two-row form) | `Contacts/ContactsEditor.razor` + `.razor.css` |
| Rail-panel internals promoted to **shared chrome** (`.panel-count`, `.panel-kebab`, `.rail-list*`, `.rail-empty`) — two pages render them now | `SchoolCollab.Admin.Shared/wwwroot/app.css`; removed from `Edit.razor.css` |
| Create page grows the rail: **Enrollment** (muted, no action) above **Guardians** (chevron, count, kebab, draft rows with per-row *Remove guardian…*) | `Pages/Students/Create.razor` |
| Edit page: **Guardians moved back to the rail**, beneath Enrollment, same markup/order as create | `Pages/Students/Edit.razor` |
| Tests: rail order on both surfaces, chevron only on Guardians, no inline guardian add row, CSS ownership moved to `app.css`, one-line add row | `StudentsPanelLayoutTests` (+1 create test), `EditSidebarLayoutTests` |

**As built, one simplification against the mock:** the create page's kebab opens the **same `GuardianPickerDialog`**
as edit (title *Add guardians*), not a bespoke new-guardian dialog — `GuardianPickerModel` carries only an optional
exclusion id, so the picker works on a student that does not exist yet and already contains the *New guardian* panel.
Results append straight to `Model.GuardianLinks` (`GuardianAssignment` is the draft shape), and the existing save flush
links them. No new dialog, no remapping (`AGENTS.md` § "Reuse before new infrastructure").

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| `Students.Tests.Unit` | **638/638** (incl. the new create-surface test) |
| `Admin.Tests.Unit` | **821/821** |
| `ArchitectureTests.Unit` | **146/146** |
| MAUI head (shared `app.css` changed) | 0 errors |
| Rendered, create (1440×1100 @DPR2) | grid `692 + 16 + 460`; main = Details + Contacts; rail = Enrollment `y176` then Guardians `y299` (chevron, count 0, kebab); add row **1 line**, value+Add same line; 980px → 2 lines; no console errors |
| Rendered, edit (same viewport) | identical block set, identical rail order and y positions; same add row; no console errors |
| Kebab → dialog (live) | create page's Guardians `⋮` → *Add guardian…* → **Add guardians** dialog opens (search, grid, *New guardian*, Add); no console errors |

### Round 6a — the add-row stretch defect (owner report)

Owner: *"contacts email text input doesn't fill the whole stretch"*. Two independent causes, both measured live:

| Cause | Evidence | Fix |
|---|---|---|
| `.contacts-value` / `.contacts-label` never matched at all — those classes land on the **`fluent-field` host of a child `FluentTextField`**, which carries the CHILD's CSS-isolation scope attribute, so a bare selector in `ContactsEditor.razor.css` was dead CSS | computed `flex: 0 1 auto`, `min-width: auto` on the value field despite `flex: 1 1 0; min-width: 180px` in the file | `.contacts-editor-add__row ::deep .contacts-value` / `::deep .contacts-label` |
| The add row was **content-sized**: as a flex item of the wrapping `.contacts-editor-add` container it had no width, so its flexible child had nothing to grow into | row `w 586` inside a container `w 658` | `.contacts-editor-add__row { width: 100% }` |

Re-measured after the fix (create, 1440×1100): container `658`, row **`658`** (was 586), value field **`292`** (was 185,
`flex: 1 1 0`), Add button `x891–923` = the row's right edge, **0px** trailing gap (was ~72px). Label settled at its
declared 150px. Same on edit. Regression pinned in `EditSidebarLayoutTests.Contacts_Add_Row_Is_One_Line_With_Value_And_AddGrouped`
(`width: 100%`, `::deep .contacts-value`, `::deep .contacts-label`), because a bare-selector regression is silent:
the page still renders, just with a dead gutter.

## Round 7 (2026-10-25): count chip placement, contact add-row order, and the enrolment mock

| Item | Decision / result |
|---|---|
| Count chip (owner: *"count display is far from title text"*) | **Fixed.** Root cause: `.panel-section__title` carried `flex: 1 1 auto`, so the whole right-aligned `.panel-section__actions` wrapper (which hosted the count) was pushed to the row's far edge. `PanelSection` now owns the count (`Count` / `CountId`, rendered between title and actions), the title is `flex: 0 1 auto`, and the actions wrapper takes `margin-left: auto`. Measured: header order `chevron → title → count → actions`, title→chip **8px** (was ~195px). Dead `.panel-count` / `.panel-kebab` removed from `app.css`. |
| Contact add row order (owner: *"option input for label takes precedence over required value … swap"*) | **Fixed.** Row reads channel · **value** · label · ＋; value · label · ＋ travel as one nowrap group so the required input leads and the trio never splits. Measured: channel 160 · value 292 · label 150 · Add 32, right edge flush (0px). Source-order assertion added to `EditSidebarLayoutTests`. |
| Enrolment with activity groups + enrolment-required subjects | **Mocked only** — `.pi/mock/panel-rollout-students-v4.png` (the rail summary, four colour bands) and `.pi/mock/enrol-dialog-v5.png` (dialog: placement as its own banner, no left label column). No implementation yet. |

**Owner rounds settled for the enrolment work:** Q16(a) two rounds — *round A* activity groups only (no schema change),
*round B* the `RequiresEnrollment` flag + enrolled/assumed bands (migration on the `TopicAssignment` TPH base, grade-card
toggle, grandfathering); Q17(a) create keeps the muted Enrollment panel; Q18(a) invalid activity groups stay visible,
struck through with the reason; Q19(a) Enrollment carries no count chip (per-band totals instead).

**Facts behind the mock:** `ActivityGroup` + `activity_group_grade_levels` eligibility + `ActivityGroupMembership` +
`AddMembershipHandler` all ship (so “only valid groups” is a read + filter). `RequiresEnrollment` is spec-only
(`documents/specs/subject-enrollment-requirement-flag.md`, “nothing implemented”); `StudentTopicAssignment` + its
commands and routes exist, but it has no seeder, no reader and no UI. FR-ER-24 already names the student create/edit
page as the primary enrolment surface.

Gates after the two fixes: build 0 errors; `Admin.Tests.Unit` **821/821**; `Students.Tests.Unit` **638/638**;
`ArchitectureTests.Unit` **146/146**.

---

## Round 8 (2026-10-25): enrolment round A — activity groups on the edit rail

Round A of the Q16(a) split. No schema change, no flag: activity groups are additive UI over a domain that
already ships (`ActivityGroup`, `activity_group_grade_levels`, `ActivityGroupMembership`, `AddMembershipHandler`).

### As built

| Piece | File | What it does |
|---|---|---|
| Eligibility mirror | `src/Students/SchoolCollab.Students.Application/Services/ActivityGroupEligibility.cs` | `ActivityGroupOption` + `Evaluate`/`RejectionReason`. Mirrors the server's admission rules — inactive group, grade eligibility (an *empty* grade set means any grade), enrolment window, capacity, already-joined — so the picker can strike a row through with the same reason the write would fail with. |
| Eligibility tests | `tests/SchoolCollab.Students.Tests.Unit/ActivityGroupEligibilityTests.cs` | 14 tests, including the correction below. |
| Picker | `.../Components/Students/ActivityGroupPickerDialog.razor` + `.razor.css` + `ActivityGroupPickerModel.cs` | Lists every group, badges joined ones, strikes blocked ones through with the reason, allows selecting only addable ones, returns the selected ids. The **page** performs the writes, so the dialog owns no API client. |
| Rail bands | `src/SchoolCollab.Admin.Shared/wwwroot/app.css` (`— .rail-band*`, `.rail-chip*`, `.rail-add-link`) | Shared band/chip vocabulary; `Edit.razor` renders a *Placement* band and an *Activity groups* band inside `#student-edit-enrollment`. |
| Page wiring | `.../Pages/Students/Edit.razor` | `_activityGroups`, `_savingGroup`, `_groupError`, `LoadActivityGroupsAsync`, `AddActivityGroupsAsync`, `RemoveActivityGroupAsync`. |
| Layout test | `tests/SchoolCollab.Students.Tests.Unit/StudentsPanelLayoutTests.cs` | `EnrollmentPanel_PresentsPlacementAndActivityGroupBands` pins round A to exactly two bands. |

### Corrections found while implementing (not by review)

- **The eligibility mirror was wrong about a student with no active enrolment.** `AddMembershipHandler.EnsureGradeEligible`
  blocks a *grade-restricted* group when the student has no active enrolment, but allows an *unrestricted* one. The
  first draft blocked both. The test caught it; the mirror now matches the handler exactly.
- **Capacity is a group-wide approximation.** `ActivityGroupDto` carries only `ActiveMemberCount`, not a per-grade
  count, so the picker's capacity check is honest-approximate and documented as such rather than pretending to
  per-subtype precision.
- **The picker initially rendered no `DialogShellBody`** — caught by `DialogChromeArchitectureTests`, exactly the
  failure that test exists for (no title, no close X, and the dialog says nothing about it).
- **Two source-reading tests had to move with the surface.** `EditSidebarLayoutTests.Edit_Primary_Active_Enrollment_Is_Displayed_Read_Only`
  asserted the old single `class="enrollment-display"` wrapper; the panel is now read-only *bands of chips*, so the
  test asserts the band seam and then asserts **negatively** that the placement block contains no `FluentTextInput`
  / `FluentSelect` — the regression it was really guarding.
- **The bUnit fixture had no active enrolment**, so the panel rendered its empty state and the bands never appeared.
  `RegisterBackend(bool withActiveEnrollment = false)` + an `ActiveEnrollment()` fixture fixed the harness rather than
  the assertion.

### The reuse observation that produced the component-first policy

The band markup was written **inline** in `Edit.razor` (~50 lines of `.rail-band` / `.rail-chip` structure) because a
single call site has no evidence a shape is stable. Round B adds two more bands and the Enrol dialog's placement
banner repeats the shape — i.e. the second and third call sites are already committed work. That is the exact trigger
the proposed `documents/solution/component-first-policy.md` names (issue #415), and `RailBand` is its first candidate
component (Q22 in the pending grill round).

### Defect surfaced by the live probe — issue #416

The populated picker path could not be reached, and the reason is **not** "the flag is off by default":

1. `FEATURE:ENABLEACTIVITYGROUPS` was flipped to `true` in `settings-db`; the Settings API resolved it `true`
   (`GET /api/features/global` → `FEATURE:ENABLEACTIVITYGROUPS True GlobalDefault`); the AppHost was restarted
   **after** the flip. `SchoolCollab.Students.Api` still answered `404` on `/activity-groups` and
   `/students/{id}/activity-groups`.
2. Root cause: `StudentEndpoints.cs:34` (and `AssignmentEndpoints.cs:72`) gate **route mapping** on
   `IFeatureFlagService`, but only `SchoolCollab.Admin` and `SchoolCollab.Families` call
   `AddConfigFeatureFlagClient`. The two API hosts fall through to
   `ConfigurationFeatureFlagService` (`AuthTenancyExtensions.cs:107`) — **IConfiguration only** — and their
   `appsettings.json` has no `EnableActivityGroups` key, so the flag is false forever. Route mapping also runs
   once at startup with no tenant in scope, so the pilot `TenantFeatureFlagOverride` cannot unmask it either.
3. `documents/configuration.md` §2 lists `SchoolCollab.Students.Api` as a consumer of the runtime flag. The code
   cannot honour that claim — a documentation defect in the same change's blast radius.

Two UI defects ride along, independent of the flag: a **failed** group load renders the picker's info bar
"No activity groups exist yet…" (a load failure presented as emptiness) *and* surfaces the raw exception string
"Response status code does not indicate success: 404 (Not Found)" in a message bar.

Filed as **#416** with three candidate fixes (register the config client; move the gate to request time so tenant
overrides work; or both) — an owner decision, not taken here. The populated-path screenshots therefore required a
**forced dev posture** (documented with the run), never a default-configuration result.

---

## Decisions settled after round A (2026-10-25 → 26)

**RATIFIED.** Q21–Q25 were settled by the owner as a set ("all as recommended") and **were acted on**: the policy
doc flipped from `PROPOSED` to `ADOPTED`, and `RailBand` was built from them. Q20 was settled the same way
(label above the control) and stays with the Enrol-dialog banner, which is round B work. The table below is kept as
the decision record — read the *Recommended* column as *what was decided*, not as an open question.

| # | Decision | Recommended |
|---|---|---|
| Q20 | Label placement in the Enrol dialog placement banner (mock `enrol-dialog-v5.png`) | **(b) label above the control** — `label-position="above"`, which is what every repo dropdown already does and what assistive tech reads in order. Round A as built does **not** touch the Enrol dialog; the banner and Q20 stay with the component-first extraction. |
| Q21 | Extraction trigger | **(a)** second call site today, **or** a second committed this/next round, **or** a design/spec names the surface |
| Q22 | First component to extract | **(a)** `RailBand` (+ `RailChip`) in `Admin.Shared` |
| Q23 | Cost guardrail | **(a)** presentational-only rule + "advise against with a measurement" |
| Q24 | Approval gate for design/spec-derived components | **(a)** grill round + rendered states mock + recorded contract, before building |
| Q25 | Where the policy lives | **(a)** `documents/solution/component-first-policy.md` + one `AGENTS.md` row; CI checks deferred until it fires twice |

---

## `RailBand` / `RailChip` / `RailChips` — the contract (Q22a, Q24a)

Design-derived, so the policy's approval gate applied: settled in the Q22/Q24 round, approved from the rendered
states mock `.pi/mock/rail-band-states.png` (rest · empty · dense · blocked · round-B variants), and recorded here.

| Parameter | Type | Contract |
|---|---|---|
| `RailBand.Label` | `string` (`EditorRequired`) | Always rendered as visible text + `aria-label`. A band with no label is a review rejection: colour never carries the meaning alone. |
| `RailBand.Hint` | `string?` | Right-aligned secondary text — per-band counts ("0 joined") and markers ("read-only"). Bands never carry a count CHIP (Q28a). |
| `RailBand.Variant` | `RailBandVariant` | `Neutral` (default, emits no class) · `Groups` · `Required` · `Assumed`. The last two exist for round B so it adds call sites, not CSS. |
| `RailBand.Id` | `string?` | Probe/test seam. Every production call site passes one. |
| `RailBand.ChildContent` | `RenderFragment` | Normally a `RailChips` set. The band owns layout and tint, never the meaning of its content. |
| `RailChip.Text` | `string` (`EditorRequired`) | Also the accessible name of its remove button (`Remove {Text}`). |
| `RailChip.Blocked` | `bool` | Struck through + `aria-disabled`. |
| `RailChip.Reason` | `string?` | **Required by convention when `Blocked`** — enforced by review and by a test that pins the gap, not by the compiler. Rendered in **three** channels: visible `· reason`, `title`, `aria-description`. |
| `RailChip.OnRemove` | `EventCallback` | Supplying it renders the ✕; absent ⇒ no affordance (facts are not removable). |
| `RailChip.Disabled` | `bool` | Disables the ✕ during a write, never the chip's text. |
| Accessibility | — | `role="group"` + `aria-label` (band); `role="list"` (chips set) / `role="listitem"` (chip); blocked ⇒ `aria-disabled="true"`. |
| Sizing | — | No fixed width; fills and wraps. **No parent needs `::deep` to size it** — the contacts add-row defect (2026-10-25) is why this is written down. |

**Call sites today:** `Students/Edit.razor` ×2 (`student-edit-enrollment-facts`, `student-edit-activity-groups`).
**Committed next:** the Enrol dialog's placement banner, and round B's required/assumed bands.

### Where the review's findings landed (2026-10-26)

The pre-flight code review found one blocking defect **introduced by the flag retirement** and three test gaps;
all were fixed before the PR:

| Finding | Fix |
|---|---|
| `Subjects.razor` kept `if (false)` and never set `_activityGroupsEnabled = true`, so the *Activity Group* owner filter was permanently gone — a **lost capability**, exactly what the owner forbade | Flag probe deleted; the filter is offered when the group load **succeeds**. The happy-path test now asserts the rendered option, not just the HTTP call |
| `ActivityGroupEligibility` was **stricter than the server**: it window-checked every span and refused a future start date, while `AddMembershipHandler` checks only `DateRange`'s end date | Mirror matches the server exactly; two new tests pin both asymmetries; the two remaining permissive divergences (grade source, span resolvability) are documented on the type |
| `JoinGroupsDialog` put `ex.Message` in a message bar — the rule the deleted flag-off test also guarded | Logs the exception, shows a plain sentence; the isolation doc's rule survives the flag |
| `AssignmentAuthoring` left the *SelectedGroups* category offered with an empty picker after a failed load | Fail-closed: enabled only after a successful load |
| `Create.razor`'s contact flush (`AddContactAsync` per contact) had **no test** | Added (create → one POST per contact; a failure surfaces a sentence and leaves the student created) |
| The picker dialog had no bUnit test | **Not fixed — filed (#419).** Its behaviour *is* verified end-to-end on a live AppHost (5 rows, Choir + Switched off struck through with their reasons, add/remove round-trip), but no unit test pins it. Recorded honestly rather than claimed. |

---

## Round 9 (2026-10-26): `FEATURE:EnableActivityGroups` retired — the flag, not the feature

Owner instruction: *"do not remove the feature. remove the flag… stop blocking with flag is what I meant."*
Activity groups are established, so nothing is gated any more. **The feature is untouched**; only the gating went.

### Removed

| Site | What went |
|---|---|
| `SchoolCollab.Core.Abstractions/FeatureFlagKeys.cs` | The `EnableActivityGroups` constant, replaced by a retirement note |
| `Students.Api/StudentEndpoints.cs` | The `if (featureFlags.IsEnabled(...))` gate — the group routes are **always mapped** |
| `Assignments.Api/AssignmentEndpoints.cs` | The same gate around the assignment↔group link routes |
| `Admin/Components/Layout/NavMenu.razor` | `<FeatureFlagGate>` around the Activity Groups nav item |
| `ActivityGroups.razor`, `ActivityGroupDetails.razor` | `<FeatureFlagGate>` wrappers |
| `Students/Detail.razor` | `<FeatureFlagGate>` around the Activity Groups section |
| `Subjects.razor`, `TopicCreateDialog.razor`, `JoinGroupsDialog.razor`, `TeacherEditDialog.razor`, `AssignmentAuthoring.razor` | The `IsEnabledAsync` probes |
| `MigrationService/Program.cs` | `SeedEnableActivityGroupsAsync` + the pilot-override call; `PilotActivityGroupFlagOverrideSeeder.cs` **deleted** with its test |
| `Admin/appsettings.json` | The cold-start fallback key |
| `documents/configuration.md` | §1 row → RETIRED, §2 consumer row struck, §6.1 pilot-override section → a *Retired flags* record |

### Deliberately kept

- **The isolation on every client call site.** A failed group load still cannot take down a page, a dialog or the
  grade-level owner path. That lesson never depended on the flag — issue #416 reproduced the failure with the flag
  **ON**, and the two UI defects it recorded (failure rendered as "none exist", raw exception text in a message
  bar) are still open on their own merits.
- **The `feature_flags` row and the hydeson-school `TenantFeatureFlagOverride`** in an existing Settings database.
  Nothing reads them; deleting seeded data is a migration, i.e. a separate owner-approved change.
- The feature-flag *infrastructure* — five other flags still use it.

### Tests re-framed rather than deleted where the behaviour survived

| Test | Fate |
|---|---|
| `StudentDetailSectionsTests` (student-detail Activity Groups section) | **Inverted** — now asserts the section is NOT wrapped in a `FeatureFlagGate` and that *Join Group* survives |
| `SubjectsLandingPageTests` ×3 | Renamed `ActivityGroupsFlagOff_*` → `ActivityGroupsUnavailable_*`; the 404 case still must not cost the page its topics, must not spin, must not report an error, and must not retry — only the *reason* the route 404s changed (`asks once` replaces `never asks`) |
| `TopicCreateDialogTests` | `…FlagOff_StillOpensWithGradeOwner` → `…ActivityGroupsLoadFails_StillOpensWithGradeOwner`; the dialog now always tries the route, so the assertion flipped from "never calls it" to "survives the answer" |
| `TeacherEditDialogBunitTests` | `…FlagOff_HidesActivityButton` → `…NoActivityGroups_HidesActivityButton`; the dialog now offers *+ Activity* when `_allActivities.Length > 0`, so the dead-end guard survives without a flag |
| `JoinGroupsDialogTests`, `AssignmentAuthoringBunitTests` (R2) | **Deleted** — both asserted a "flag is off" behaviour that no longer exists |

### Why the routes were the real prize

Route *mapping* is a startup decision with no tenant in scope, so a tenant-scoped runtime flag could never have
unmasked the group routes — and neither API host registers `AddConfigFeatureFlagClient`, so both resolved
`IFeatureFlagService` from `IConfiguration` only. Under the flag the group API was **unreachable in development at
any flag state** (`#416`). Retiring the flag removes that entire class of failure rather than documenting it.

### Gates after the retirement

`dotnet build SchoolCollab.slnx` **0 errors** (17 pre-existing warnings) · `Admin` **830** · `Students` **653** ·
`Architecture` **146** · `Assignments` **1390** · `Assignments.Api` **202** · `Settings` **678** ·
`Settings.Api` **58** — all **0 failed**. MAUI head **0 errors**.

### Live proof that the retirement worked (2026-10-26, default posture)

The decisive evidence is that **nothing was enabled**: the settings-db row `FEATURE:ENABLEACTIVITYGROUPS` stayed
`is_enabled = false` and the Hydeson tenant override stayed in place, yet every surface worked.

| Check | Before the retirement | After |
|---|---|---|
| `GET /activity-groups` (Students API :7450) | **404 at every flag state** | **200**, 5 groups |
| `GET /students/{id}/activity-groups` | 404 | **200**, `[]` |
| Edit rail group band | "No groups yet" **+ a 404 error bar** | "ACTIVITY GROUPS · 0 joined · No groups yet", **no error bar** |
| Add-group picker | 5 rows only with a forced env override | **5 rows on a default posture**, Choir + Switched off struck through with reasons |
| Join / remove | — | "1 joined" + `Chess Club ✕` → back to the empty word (not a struck chip) |
| `/activity-groups` landing | gated by the flag | 5 names in the grid, **0 message bars** |
| Student detail | section hidden behind the gate | **Activity Groups** header + *Join Group* present |

Cleanup verified: membership rows **0**, both flag rows untouched (`is_enabled=f`, audit count 10), app stopped,
repo unchanged. Console/page errors: **0**.

Incidental finding for future probes: Aspire 13.5.4 does **not** print per-service URLs to the AppHost log (only
the dashboard, `https://localhost:17160`); the Students API address has to come from
`src/Students/SchoolCollab.Students.Api/Properties/launchSettings.json` → `applicationUrl` (`https://localhost:7450`).
