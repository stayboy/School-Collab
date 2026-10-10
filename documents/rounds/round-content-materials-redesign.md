# Round — content & materials redesign (solo, unattended)

**Mode:** solo (owner-opted 2026-10-10) · **model:** session model — delegation is unavailable
(the Cline account layer rejects runs: `run_00001`/`run_00002`, both `Unauthorized`), so the skill's
solo default model could not have been dispatched; recorded per the solo rule.

**Owner authorization:** "go solo with redesign" — by-name authorization for this round's
self-applied execution (skill, unattended clause 1: a self-applied run is a *degraded tier run*
only when pre-authorized by name; this is solo, opted explicitly).

## Plan

Redesign the instructional-materials surface on assignment create/edit to the owner's reference
shape: the added materials render **above** a **titled dropzone section** whose header carries its
**title text** on the left and the **kebab menu** on the right; the kebab is the dropdown control,
never the title text (owner clarification, 2026-10-10).

### Settled decisions (grill round, 2026-10-10 — questions with the alternative that lost)

1. **Execution mode → solo.** Taken: solo. Rejected: light round (Tier 1–2) — cheaper to verify,
   but its worker leg is dead at the account layer, so it would have degraded to self-applied
   anyway, with a tier label claiming verification it could not get.
2. **What the titled dropzone is → infer the destination.** Taken: a dropped file is routed by
   kind — audio/video/image → an instruction row; a document → a resource (attachment); anything in
   neither set is refused by name. Rejected: keep D5's blanket document refusal (leaves the
   section's own X-6 copy — "Add PDFs, documents, or other text to reference…" — lying about what
   it accepts); and make it document-only (would un-drop instruction media that W4 just built).
3. **Header menu → `RowActionsMenu` beside the title.** Taken: `RowActionsMenu` (⋮) at the section
   header's right, actions and AC-1 order unchanged (*Add text content · Add link · Upload from
   device*); the image's third item *GitHub* is not added (no such integration exists). Title text
   and menu are distinct elements. Rejected: a `+` trigger (would change a shared Admin.Shared
   component for one surface) and adding a GitHub item (a new feature, not a redesign).
4. **Layout → notes untouched, rows above a full-width titled dropzone section.** Taken: the wide
   cell keeps Teacher notes over Student guidance (D8/D9 ids intact), the added rows move above the
   dropzone, and the dropzone becomes a full-width titled section. Rejected: collapsing everything
   to one column (silently redesigns the notes layout the owner kept in D8/D9).
5. **The image's pencil / collapsed summary → skipped.** Taken: no collapse, no pencil — these are
   edit surfaces, not a read card; recorded here as a deliberate deviation from the reference.
   Rejected: implementing collapse (its own round: state, focus behaviour, validation visibility).
6. **Sequencing → MAUI-CI change first.** Taken: the pending MAUI-CI disable was committed, pushed
   and PR'd (#332) before this branch, so one change set is one review. Rejected: folding it in
   (two change sets in one branch cannot be attributed or reviewed).

## Expected files

`AssignmentAuthoring.razor(.css)` (structure + section header + full-width dropzone),
`InstructionKindInference.cs` + a routing helper (destination inference, unit-tested),
`InstructionEditorList.razor` (unchanged seam), `instructional-materials.md` (D5/D8 revised, AC-9…11),
bUnit suites (`AssignmentAuthoringBunitTests`), the helper's unit suite.

## Acceptance

| # | Criterion |
|---|---|
| AC-9 | The dropzone section renders its title and, to its right, the kebab menu — two distinct elements, menu items unchanged (AC-1) |
| AC-10 | A dropped file is routed by kind: media → instruction row, document → resource; a file in neither set is refused by name |
| AC-11 | The added materials render above the dropzone section |

**Verification:** solution build + Assignments unit + Families unit + Architecture suites, recorded
in this doc when the round closes.

## Owner refinements (same round, 2026-10-10 — three, all applied)

7. **Content & Resources occupies the RIGHT THIRD** of the Instructions section (owner: "must occupy the
   right 1/3 of Instructions section on create assignment form"). Taken: the notes keep the wide cell
   (flex 2) and the C&R card is the right cell (flex 1), with the materials still listed **above** both —
   so AC-11 ("listed above dropzone") holds literally. Rejected: leaving the section full-width below the
   notes (my first pass) — the owner's correction, recorded here rather than quietly re-laid-out.
8. **Icons on the kebab items.** Taken: `TextDescription` (Add Text Content), `Link` (Add Link),
   `ArrowUpload` (Upload From Device). Note for the next author: in this FluentUI version
   `Icons.Regular.Size16.X` is a **class you instantiate** (`new Icons.Regular.Size16.Link()`), exactly as
   `SchoolCollab.Admin.Shared.Constants.FluentIcons` builds its constants — a bare reference is CS0119, and
   `Icons` needs the alias `@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons` (the
   `ResourcesSection` precedent). Rejected: leaving `Icon = null`, which is what made the menu read flat.
9. **Title Case for headers and menu items** ("Content & Resources", "Add Text Content", "Add Link",
   "Upload From Device"). Applied to the markup, the spec (D2, AC-1, the §5 menu rule), and the two
   refusal strings that name a menu item — the bUnit assertions moved with them. Rejected: sentence case
   as the reference image shows it — the owner asked for capitalization.

**Beautify (item 8's other half):** the section is a quiet card — hairline border, soft fill, 8px radius,
8px internal gap — with the header row holding the title text left and the kebab right.

10. **Headers on one line; the list lives inside Content & Resources** (owner, 2026-10-10: "push
    'Content & Resources' up to align with Instructions header. List of Instructions must be inside
    'Content & Resources', just beneath header text"). Taken: the compartment `<h3>Instructions</h3>`
    now **opens the wide cell** and the `h4 Content & Resources` opens the right cell, so the two
    headers sit on one line — the C&R card lost its top padding and its title was matched to the h3's
    type (1.17em / 600 / `0 0 4px` margin). The `InstructionEditorList` moved **inside** the C&R
    section, directly beneath the header row and above the dropzone, so AC-11 now reads
    *header → list → dropzone* instead of *list → section*. Rejected: keeping the list at compartment
    top level (the owner's correction), and leaving the card's 12px top padding (it was what pushed the
    title out of line).



| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| `SchoolCollab.Assignments.Tests.Unit` | **1087 / 1087** (was 1079: +7 routing cases, +1 reshaped D8 test) |
| `SchoolCollab.ArchitectureTests.Unit` | **105 / 105** (razor markup-hygiene guard included) |
| `SchoolCollab.Families.Tests.Unit` | **45 / 45** |

**One failure found and fixed during the round:** `Instructions_OwnBottomCompartment_SideBySide_WithLabelsBelow`
asserted `HaveCount(2)` instruction cells — the superseded D8. It was **updated, not weakened**: the
cell assertion now records one wide cell (the narrow one is gone), and the test gained the new AC-9
(title + menu are distinct elements, menu keeps AC-1's actions) and AC-11 (materials precede the
dropzone in markup) assertions. Every other assertion in it is untouched.

**Cleanup:** the orphaned `.authoring-instructions-cell-narrow` rule was deleted with the narrow cell.

Change set: 4 modified (`instructional-materials.md`, `AssignmentAuthoring.razor(.css)`,
`AssignmentAuthoringBunitTests.cs`) + 3 new (`MaterialDropRouting.cs`, its unit suite, this doc).

## Dialog round (owner, 2026-10-10 — Q1/Q2/Q3/Q5 landed; Q4(b) outstanding)

D3 was superseded: *Add Text Content* and *Add Link* are now captured in **`MaterialEditDialog`** (ONE
dialog, fields switching on the kind, built on `DialogShellBase` per the dialog-ui skill — never
hand-rolled chrome, footer `Error` bound), and the row is appended **from its result** by the page, so
the list stays the single writer of its rows. *Upload From Device* pops the picker **directly**: the
collocated `AssignmentAuthoring.razor.js` gained `openMaterialFilePicker(selector)`, which clicks the
hidden `input[type=file]` FluentInputFile renders inside `#authoring-materials-dropzone`. That is a
deliberate departure from the "no JS, no hidden-element click" note in `ResourcesSection`, recorded
there and here rather than absorbed silently.

The dialog mirrors the validator (AC-12): a Text row without a title, a Link without an absolute
http(s) URL, are refused inside the dialog with the reason shown. Spec amended: **D3**, **AC-2**,
**AC-12**, **AC-13**. Evidence: build 0 errors; Assignments unit **1087/1087**.

### Still to land (explicit, not forgotten)

- **Q4(b) — dialog-only row editing.** The owner chose (b): Text/Link rows are edited **through the
  dialog**, the row rendering its values as text with an edit affordance. NOT implemented yet — rows
  still render inline fields. One fact forces a seam: `InstructionEditorList` is shared with
  `QuestionEditDialog` and the repo forbids nested dialogs, so the **question host keeps inline fields**
  and the page takes a dialog mode — a parameter (`RowEditingMode`), not two behaviours by accident.
  `InstructionEditorListBunitTests` moves with it.
- **bUnit for AC-12/AC-13** — the dialog's validator mirror and the picker-pop interop call. The dialog
  is wired and builds; its coverage is the next step.

## Owner refinements (third pass, 2026-10-10) — LANDED, with the reversals recorded

15. **The notes moved into Basics; the Instructions compartment is collapsed; Content & Resources tops the
    right pane.** Applied: `Teacher notes` and `Student guidance` render as ONE `FormRow` in
    `#authoring-basics`, directly beneath the Guardian review row, the two textareas inline to each other
    (`w-6` each — the `Assignment type & grading format` shape); the whole materials block (header +
    kebab, `InstructionEditorList`, dropzone, refusal bar) moved **complete** into `#authoring-targets`
    ahead of the `Targets & audience` `SectionCard`; the `<section id="authoring-instructions">` wrapper is
    gone, and so are its orphaned `.authoring-instructions-grid` / `-cell` / `-cell-wide` /
    `.authoring-materials-cell` CSS rules. The ids are untouched, which is why the bUnit contract survived
    the move.
16. **Dialog width** — `DialogSize.Medium`; the `Small` default is 420px and could not hold the two-field
    Text form.

**Reversals this pass records** (and the spec followed): the notes were deliberately taken OUT of Basics
into their own compartment in an earlier round — asserted by
`Instructions_OwnBottomCompartment_SideBySide_WithLabelsBelow` — and this pass puts them **back into
Basics**. That test was **rewritten, not deleted**: it is now
`Notes_LiveInBasicsInOneInlineRow_AndMaterialsTopTheRightPane` and asserts the compartment's absence, the
single inline row (both fields `w-6`, sharing one `FormRow`), and the materials block topping the pane
ahead of the SectionCard. `ExpectedFormSections`, `ExpectedCreateSections` and the Rules test's
expected-section list dropped `authoring-instructions` for the same reason — five stale structural
assertions in total, each updated rather than removed. Spec updated: **D1**, **D8**, **AC-1**.

**Evidence:** build 0 errors; Assignments unit **1087/1087**.

**Still open, unchanged:** Q4(b) dialog-only row editing, and the bUnit coverage for AC-12/AC-13.

## Owner refinements (fourth pass, 2026-10-10) — the notes row and the shared PanelSection

17. **The notes row** — the label is now just **"Notes"**, each textarea carries its **specific caption
    beneath it** (`Teacher notes` / `Student guidance`, `.authoring-note-label`), and both fields **fill
    the row** (`.authoring-note-cell { flex: 1 1 0 }`, `.authoring-note-input { width: 100%; resize:
    vertical }`) — the owner found the `w-6` tenth-fields too narrow.
18. **`PanelSection` — the shared titled-panel component.** Created at
    `Components/Pages/Assignments/PanelSection.razor(.css)`: header text on the left, a `HeaderActions`
    slot on the right, and **a thin line underneath the header TEXT** (the title's own bottom border, so
    the line is inset by the text box, with its own margin). The rule is documented **inside the
    component** — that is where a refactor will look for it. Content & Resources now wears it (its kebab
    rides the `HeaderActions` slot).
    - **Naming, and why it cost two renames:** written as `TitledSection`, then `Panel`, and settled as
      **`PanelSection`** at the owner's direction — `Panel` is exactly the name a future FluentUI
      component would claim, and a bare `Panel` in this namespace would collide or shadow. The three
      dead files were deleted, not left behind.
    - **RZ9996 lesson:** once a component declares a named fragment, Razor turns OFF implicit child
      content — the body must be wrapped in an explicit `<ChildContent>`. That is what the first build of
      this wiring failed on (14 errors), and the component's doc comment now says so.
19. **`PanelSection` reused for the Rules card — LANDED (owner chased it: "did you fix the tests to pass
    for the rules redo?").** `#authoring-rules` keeps its wrapper (still a direct child of
    `#authoring-targets`, still last) but its `SectionCard` is gone: the five readouts render inside
    `<PanelSection Title="Rules" TitleId="authoring-rules-title">` via a `@foreach (var readout in
    PolicyReadouts)`, so the title, the thin rule and the action slot are the shared ones. SectionCard's
    chrome retired with it — its title, its **count chip** (PanelSection has no count slot: the badge is
    deliberately dropped, not forgotten) and its Add (already off).
    - **Five assertions moved, not four.** The four enumerated here earlier
      (`#authoring-rules .section-card__title` ×2 → `.panel-section__title`; `.section-card__body
      .authoring-policy-row` ×2 → `.panel-section .authoring-policy-row`) **plus one I only found by
      running the suite**: `TargetsCompartment_CardHeaderIsTheOnlyTitle_…` asserted the pane's titles were
      all `.section-card__title` — it now reads `.section-card__title, .panel-section__title` and expects
      `["Content & Resources", "Targets & audience", "Rules"]`, which is the pane's real top-to-bottom
      order. That is the lesson worth keeping: the enumerated delta was right about the Rules card and
      blind to the test that counts titles across the whole pane.
    - Evidence: build 0 errors; Assignments unit **1087/1087**.



**Evidence for 17–18:** build 0 errors (after fixing the RZ9996 pair); Assignments unit **1087/1087**,
with the notes-row test updated again to the new shape — filling fields, per-field captions in document
order, the absence of the old combined label, and a `>\s*Notes\s*<` match for the row label.



## Owner refinements (second pass, 2026-10-10) — one landed, three pending with anchors

11. **Dialog width — LANDED.** `ShowShellDialogAsync` now passes `DialogSize.Medium`; the `Small` default
    (420px) could not hold the two-field Text form comfortably (owner's report). One size for both
    kinds, so the dialog does not jump width as the kind switches.
12. **PENDING — move Content & Resources to the top of the Targets & Audience pane.** Anchors: the C&R
    block (`#authoring-materials-section` — header + kebab + the instructions list + dropzone + refusal)
    currently sits inside the Instructions compartment (starts line ~685); the destination is
    `#authoring-targets` (`class="authoring-compartment authoring-compartment--right"`, line 362), whose
    first child is the `SectionCard Title="Targets & audience"` (line 388). Move the whole
    `#authoring-materials-section` to the top of that pane, before the SectionCard.
    **Open question for the owner — there are now TWO blocks named "Content & Resources":** this one, and
    the pre-existing compartment `#authoring-content` (line 510: the resources tile + file list + its own
    `<h3>Content &amp; Resources</h3>`). Either **merge** them (that is D1's "one area", and the bigger
    job — it also decides where documents land now that the dropzone routes them), or **rename** mine so
    the two do not collide. Not guessed on purpose: whichever way it goes, one of the two blocks changes
    identity, and that is the owner's call.
13. **PENDING — move Teacher notes + Student guidance into Basics**, as `FormRow`s aligned like
    `Label="Assignment type &amp; grading format"` (line 280) — same `FormRow Label=… For=…` shape,
    keeping the ids `authoring-basics-description` and `authoring-basics-instructions` (D9's bUnit
    contract). Must land **atomically with 14**: leaving the old copy in place would duplicate both ids.
14. **PENDING — collapse the Instructions compartment totally.** Once 12 and 13 have emptied it, the
    `<section id="authoring-instructions">` wrapper (line 685) goes, and so do the now-unused
    `.authoring-instructions-grid` / `.authoring-instructions-cell*` CSS rules.

### What these reversals mean for the spec (must be edited with 12–14)

- The notes were deliberately moved **out** of Basics into their own bottom compartment —
  `Instructions_OwnBottomCompartment_SideBySide_WithLabelsBelow` asserts exactly that today. This pass
  **reverses** it, so that assertion flips back to "they ARE part of Basics".
- D1/D8's "the Instructions compartment hosts the materials" becomes "the Content & Resources block
  tops the right pane"; AC-1's "the Instructions compartment offers one kebab" becomes "the Content &
  Resources block offers one kebab"; AC-8's View-mode assertion loses the Instructions wrapper.
- D9's ids survive the move untouched — that is why they are pinned in the test rather than the layout.



