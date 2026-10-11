# Round — materials row editing through the dialog (solo)

**Mode:** solo (owner-opted 2026-10-10) · **model:** session model — delegation is unavailable at the
Cline account layer (`run_00001`/`run_00002` both `Unauthorized`), so the skill's solo default could not
be dispatched.

**Follows:** `round-content-materials-redesign.md`. This round carries the two items that round deferred,
by the owner's decision (Q2(a) of its round-up grill), so that a behaviour change does not ride inside the
redesign's change set.

## Plan

1. **Q4(b) — Text/Link rows are edited THROUGH the dialog.** Today the dialog only *creates* a row; the
   rows still render inline fields. This round makes the dialog the edit path: the row renders its values
   as text with an **edit affordance** that reopens `MaterialEditDialog` seeded with them, and Applies the
   result back onto the row.
2. **bUnit for AC-12/AC-13** (owed by the redesign round): the dialog's validator mirror (a Text row with
   no title, a Link with no absolute http(s) URL, refused inside the dialog with the reason shown) and the
   picker pop (`openMaterialFilePicker` invoked with `#authoring-materials-dropzone`).

## The seam this forces (decide it once, here)

`InstructionEditorList` is shared by the page and `QuestionEditDialog`, and **the repo forbids nested
dialogs** — so the question host *cannot* open the edit dialog and keeps inline fields. The component
therefore takes a **`RowEditingMode`** parameter (`Inline` default for the dialog host, `Dialog` for the
page) plus an `EditRowRequested` callback. Two behaviours exist deliberately and with a documented reason;
they are not drift.

## Grill round (owner's "go" = all three as recommended)

| # | Question | Settled |
|---|---|---|
| 1 | What the edit affordance is | An explicit pencil `FluentButton`, id `{Id}-edit-{index}`, `aria-label="Edit this material"` — the same id scheme the `×` remove button uses, so bUnit can target it. |
| 2 | Where an edit lands | Apply mutates the **staged row in place**; the page's Save still persists everything. No per-row save (out of scope). |
| 3 | Media rows | **Out of scope** — Audio/Video/Image stay file-staged (`×`-to-clear + re-upload); the file IS the value, so there is nothing a form could seed. |

## Two implementation adjustments, recorded rather than silently taken

- **The parameter is `EditingMode`, not `RowEditingMode`.** The enum keeps the name the spec's AC-16 uses
  (`RowEditingMode.Inline|Dialog`), but the *parameter* is `EditingMode`: an attribute named
  `RowEditingMode` whose value expression begins with the same identifier is legal markup and ambiguous to
  read. The spec's AC-16 wording was kept valid by naming the enum, not the parameter.
- **The write-back lives on the component** (`InstructionEditorList.ApplyEditAsync(row, result)`), not in
  the page. The page opens the dialog and hands the confirmed result back — so the list stays the single
  writer of its rows, exactly as on the create path (Q2 of the redesign round), and the write-back is
  reachable by bUnit without a dialog.

## Acceptance

| # | Criterion |
|---|---|
| AC-14 | A Text/Link row on the page renders its values as text with an edit affordance; activating it reopens the dialog seeded, and Apply updates the row |
| AC-15 | Cancelling the edit leaves the row untouched |
| AC-16 | The question dialog's host keeps INLINE fields (no nested dialog), asserted explicitly |
| AC-12/13 | Carried from the redesign round: the dialog mirrors the validator; Upload pops the picker |

## Status

**LANDED (implementation + tests) — 2026-10-10.** Spec amended: §9 gains **AC-14…AC-16** and the test
surface names the new coverage. The AC-12/AC-13 coverage the redesign round owed is delivered here too.

Evidence (local Debug — the Assignments unit project):

| Check | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | 0 errors |
| `InstructionEditorListBunitTests` (5 new of 19) | 19/19 |
| `MaterialEditDialogBunitTests` (new, 4) — AC-12's mirror both ways + the dialog's AC-14 half | 4/4 |
| Page: `Edit_RowEditAffordance_ReopensTheDialogSeeded…`, `…_CancellingLeavesTheRowUntouched`, `Edit_UploadFromDevice_PopsThePickerThroughTheCollocatedModule` (AC-13) | 3/3 |
| Full Assignments unit suite | 1099/1099 |

### The defect this round's own test caught (worth remembering)

Wiring the page with `RowEditingMode="…"` while the component's parameter was named `Mode` **compiles**
(the Razor compiler treats an unmatched attribute as HTML pass-through) and then **throws at render**
("*does not have a property matching the name*") — which the page's `ErrorBoundary` swallows, so every
page test failed as an undiagnosable 5-second `WaitForAssertion` timeout while the component's own tests
passed. What found it: the page-level test plus a baseline diff (`Create_DoesNotRenderContentOrQuestions`
passes on the stashed tree, fails on mine). Two lessons recorded rather than remembered alone:
**a page-level bUnit test is the only thing that exercises parameter binding**, and **an ErrorBoundary
turns a loud failure into a quiet one**.

### Closed with this round

- **bUnit for AC-12/AC-13** — delivered. `MaterialEditDialogBunitTests` (new) pins the validator mirror in
  both directions: an unnamed Text row and a relative Link URL are refused **with the reason shown and the
  dialog still open**, while an absolute Link closes with its payload; an edit seeds the form and its
  footer says *Save*. The page test `Edit_UploadFromDevice_PopsThePickerThroughTheCollocatedModule` pins
  AC-13 — the collocated module's `openMaterialFilePicker` call against `#authoring-materials-dropzone`,
  with no dialog shell in the path.

**Nothing owed from this round.** Next per the spec's §10: `question-response-types.md` §8 — the Text
row's **body** as a rich editor (the reason D6 keeps the title a separate field).

## Owner refinements (2026-10-10, second pass) — the kind reads as an icon, the row identifies

Owner: *"Instead of the text 'Text' to indicate instruction kind in display, use Icon … also the space
between the kind and the displayed text should be collapsed to reasonable padding"* and *"for adding 'Text
Content', display in list as 'Icon'+'Title'"*. Settled by a `grill-me` round (the skill's frontier +
recommendation format; the skill tool is not registered for Cline, so its documented procedure was
followed from `~/.pi/…/skills/grill-me/SKILL.md`), all six answers accepted as recommended:

| # | Decision |
|---|---|
| Q1 | Glyphs: Text → `TextDescription` and Link → `Link` (the **kebab's own two**, so the menu and the row cannot drift); Audio/Video/Image → `MusicNote2`/`Video`/`Image`, added to the shared `FluentIcons` (Size16). |
| Q2 | The word survives as the wrapper's `aria-label` **and** `title` — an icon-only indicator must not be nameless. |
| Q3 | Both modes carry the icon (one indicator, no second fork). |
| Q4 | The 120px text column becomes a fixed 16px inline-flex box; the row's own `gap` is the icon→value space. |
| Q5 | Neutral — no new colour rule. |
| Q6 | The **ward card stays out of scope** (it renders no visible kind, only `data-kind`); its comment claiming otherwise is a separate small doc fix. |
| Q7 | Mapping lives as shared `FluentIcons` constants + a component-local `KindIcon` switch (no second consumer yet). |

Then the row's read-out was narrowed by the same owner decision: **a row identifies, it does not read.**
Text → icon + **title** (the body belongs to the dialog); Link → icon + **label, or its URL when the label
is blank** (never a blank row — the ward card's own `title ?? url` rule). **AC-14 amended** to match.

### Coverage added, and the bug report behind it

Owner: *"editing item in panelSection, loses the body content, for 'Text Content' in the edit dialog"* plus
*"how did we miss this — are there tests for PanelSection on edit, delete, add and open-dialog?"*

Coverage audit at the time: the page suite drove add (`AddRowAsync`), edit (through a **mocked**
`IDialogService`) and the dialog-open seed, but **the real dialog never rendered in the page path**, the
dialog tests asserted the **result payload** rather than the **rendered field**, the one page→dialog edit
test used a **Url** row, and **nothing at all** covered delete at the page level. `PanelSection` itself has
no test class — it is a presentational shell (title + `HeaderActions` slot + the rule), so its edit/delete/
add behaviour lives entirely in its consumers, which is where the gap was.

Added: `Edit_ShowsTheSeededBodyInTheField_NotJustInTheModel` (the seed is *rendered*, not merely bound — the
assertion whose absence let this through), `Edit_ChangingTheBody_ReturnsTheEditedBody` (the author edits the
body → the result carries it), `Edit_RowRemoveAffordance_DropsThatRow` (page-level ×, with re-indexing),
`DialogMode_IdentifiesTheRowByItsName_WithNoFields`, `DialogMode_LinkWithNoLabel_IsNamedByItsUrl`,
`Kind_IsAnIcon_WithTheWordAsItsAccessibleName`.

**Status of the body-loss: not reproduced server-side.** Every bUnit-exercisable path preserves the body —
the seed reaches the rendered field (`value="…"` on `<fluent-text-area>`), a change driven through the
control returns in the result, and the apply writes it back. The remaining unexercised step is
**client-side**: whether the `fluent-text-area` web component populates its DOM from that `value` attribute
(bUnit sees rendered attributes; a web component's property sync is invisible to it). If it does not, the
author sees an empty box and a save pushes the empty box back — exactly the reported symptom. Discriminator
available in seconds: the **notes** textarea (`authoring-basics-instructions`) is a seeded `FluentTextArea`
too, so if loaded notes also appear blank when editing an existing assignment, the hypothesis holds and both
are fixed together; if they do not, the repro needs narrowing (a scripted browser check, or the owner's
exact steps).

## Owner requirement (2026-10-10) — Enter in the body must not submit the dialog

*"TextArea for 'Text Content' in dialog must disable enter key from submitting dialog."* The body is
multi-line prose, so Enter inside it belongs to the text, not to the dialog.

Solved with the repo's own precedent rather than a new idea: `FluentTextArea` exposes no `OnKeyDown`
parameter and Blazor's static `@onkeydown:preventDefault` cannot be made conditional, so `AiChat` already
solves this class of problem with a **native keydown listener** (`chatInput.js`,
`SchoolCollab.AI.Chat`). The dialog gains a collocated module of its own,
`MaterialEditDialog.razor.js#guardEnterKey(id)`, attached on first render to the body field's host element
(`BodyFieldId = "material-edit-body"`) and disposed with the module.

**One deliberate difference from `chatInput.js`, recorded in the module header:** there plain Enter is a
*SEND*, so it is `preventDefault`-ed; here Enter is a *NEWLINE*, so the guard only **stops propagation** —
the textarea's own default still inserts the line break and Blazor's binding is never touched. What it
cuts off is every ancestor that could read Enter as a submit (the `EditForm`, FluentUI's dialog host).

Coverage: `Text_BodyField_IsGuardedAgainstEnterSubmitting` (the module is imported and asked to guard the
body field's real id) and `Link_HasNoBodyField_SoNothingIsGuarded` (a Link has no textarea, so the guard
must not be attached to nothing). The *behaviour* is browser-side and therefore needs one manual pass in
the running app — bUnit sees the wiring, not the key event.
