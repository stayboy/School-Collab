# Instructional materials — one add affordance for instructions, links and uploads

**Owner:** stayboy · **Opened:** 2026-10-10 · **Status:** open (spec first, implementation in a light round)

## 0. This spec supersedes

The unified area defined here replaces fragments currently owned by four other documents. Those files
keep their text; each gains a pointer to this spec:

| Superseded fragment | Where it lives |
|---|---|
| **CR-1, CR-7, CR-8** — the header `+` reveals a hidden link input; the Links group's input row | `documents/specs/assignment-authoring-content-questions-modern-ui.md` |
| **§3.2** — the Instructions compartment as a two-cell Description/Instructions grid | `documents/specs/assignment-create-edit-redesign.md` |
| **§5.2/§5.6** — the instruction list as "chips/add buttons" | `documents/specs/question-response-types.md` |
| **INS-2** — where the instructions render | `documents/specs/assignment-authoring-compartments.md` |

Still in force from those documents: **CR-2…CR-6, CR-10…CR-12** (the tile, the groups, the error bar
placement, the truncation rule, and `ResourcesSection` keeping its **type name plus the public methods
still in use** — the `RevealLinkInputAsync` reveal seam retires with CR-1/CR-8, §5 of this spec), **X-6**
(the retained strings), **INS-1** and **INS-3**, and the whole of the WYSIWYG follow-up in
`question-response-types.md` §8.

## 1. The owner's requirement (2026-10-10)

> "Current button and dropdown implementation is not UX friendly. Use menu buttons or kebab dropdown
> menu placed in [the] instruction section, that moves straight to action, and commits once action is
> taken by user. Keep a placeholder for dropping attachments. If kind can be inferred from dropzone,
> use that. However, replicate individual menus ('Upload from Device, Add Text Content') in kebab
> dropdown menu."
>
> "Can we combine both — Instructions and 'Content & Resources'… they are instructional materials…
> 'Add Text Content' must have a title, and optional [body]. Teacher notes and Student guidance Notes
> in instruction forms part of this, but specialized for attention."
>
> "Stack teacher notes and student notes, all textarea, on top of each other, to 3/4 of the section…
> and have placeholder take up 1/3 to the right, with kebab menu with it."
>
> "Other resources/materials infer their meaning from instruction notes on assignment created."

## 2. Findings (the ground this stands on)

**Three child collections exist today, not two.** The unification is a merge-shaped problem, and the
menu has to choose a destination:

| Collection | Kinds | Feeds AI generation? |
|---|---|---|
| Instruction items (`InstructionKindDto`) | **Text · Link · Audio · Video · Image** — already covers *Add Text Content* and *Add Link* | **No** (INS-3) |
| Resources (`ResourceKindDto`) | Link · **File** · Video, with `DisplayName` and `IncludedInGeneration` | **Yes** |
| Attachments (`NewAttachmentDto`) | staged files **plus** extraction status and extracted text | **Yes** — that is their purpose |

**The enums are asymmetric.** Instructions cannot represent a **Document** (yet the upload allow-list
accepts PDFs and office files); resources cannot represent **Audio or Image**.

**What each surface does today.**

- Instructions (`InstructionEditorList.razor`, shared by the page and the question dialog): one
  `+ Add instruction` button that always appends a **Text** row, then a per-row kind **dropdown** plus
  payload — the pattern the owner is replacing, and a deviation from §5.2's stated "chips/add buttons".
- Content & Resources (`ResourcesSection.razor`): the header `+` **reveals a hidden link input**
  (CR-1/CR-8); the tile **is** the drop zone (`FluentInputFile`, `DragDropZoneVisible`, "Upload files",
  CR-3); the retained strings are pinned by X-6.
- The instructions area already uses a wrapping grid — `.authoring-instructions-grid` /
  `.authoring-instructions-cell` (`flex: 1 1 320px`, stacking below the breakpoint) — so the owner's
  proposed layout is a modification of an existing pattern, not a new one.
- The ward player renders the student-facing instructions as plain text in a card
  (`SchoolCollab.Families/Components/Pages/Ward/Assignment.razor`), fed by the ward DTO via
  `GetWardAssignmentViewQueryHandler` and `WardAssignmentProjectionRepository` (INS-2).

**No title exists anywhere for instruction items.** `assignment_instructions` has no title column and
`InstructionEditorRow` has no `Title`; resources carry `DisplayName` on the Link side only.

**The repo's kebab already exists.** `RowActionsMenu` (Admin.Shared) renders one action as a plain
button and two or more as a `FluentMenu` of `FluentMenuItem`s; `ForceKebab="true"` is already in use by
`QuestionEditorSection` and the page action bar.

## 3. Settled decisions (owner, 2026-10-10)

| # | Decision |
|---|---|
| **D1** | **One area, one affordance.** The Instructions compartment hosts the instructional materials; text/link/media items are instruction items, uploaded files are resources, and attachments stay the collection reserved for AI-reference files. The separate "Content & Resources" add-flow is retired into it. |
| **D2** | **The add affordance is a kebab menu** — `RowActionsMenu`, so a lone action still renders as a plain button and the rest as menu items: **Add text content · Add link · Upload from device**. |
| **D3** | **Straight to action, committed on the spot.** An action appends its row to the form model immediately, with no confirmation dialog. Files stage to storage at selection through the existing seam; the assignment itself still persists on *Save as Draft*. |
| **D4** | **One section-level dropzone placeholder** that infers the kind and appends the matching row. Each media/file row keeps its own replace control. |
| **D5** | **Infer where a kind exists; refuse with a named reason where it does not** (a document dropped on the instructions side points the author at the file material path). Never accept silently and drop. |
| **D6** | **Text content is titled: `Title` required, body optional.** |
| **D7** | **The title is stored** — a nullable `title` column plus `Title` on `NewInstructionDto`, `InstructionReadDto` and `InstructionEditorRow`. Required by the client and the validator for **Text**; optional for **Link**; nullable for rows that predate it (fail-closed: never invent a title, never block on old data). |
| **D8** | **Layout:** the left cell (flex 2) stacks **Teacher notes** over **Student guidance**, both textareas; the right cell (flex 1) holds the dropzone placeholder with the kebab attached. The existing grid stacks the cells below its breakpoint. |
| **D9** | **Labels:** "Teacher notes" (internal) and "Student guidance" (student-facing). The ids `authoring-basics-description` and `authoring-basics-instructions` are **preserved** — they are the bUnit contract. |
| **D10** | **Materials infer their meaning from the instruction notes.** A non-text material carries no prose of its own; only an optional label (the Link's title). This is the rule that keeps the model small. |
| **D11** | **The ward surface renders the materials**: the ward DTO and the Families ward card show the titled items (teacher notes excluded). The Python portal follows in its own round. |
| **D12** | **Consistency vehicle:** `RowActionsMenu`; the X-6 retained strings and existing element ids survive; new ids are added for the menu, its items and the dropzone. |
| **D13** | **The enum holes are not widened in this round** (Document on the instructions side; Audio/Image on the resources side). D5's refusal covers them; widening is a named follow-up. |
| **D14** | **No true model merge in this round.** One surface over the existing collections; collapsing them into a single material contract is a named follow-up that needs its own spec. |

## 4. The target surface

```
┌─ Instructions ────────────────────────────────────────────────────────────┐
│ ┌─ Teacher notes (textarea) ─────────────────────┐ ┌─ dropzone ──────────┐ │
│ │  internal only                                 │ │  Drop files here    │ │
│ └────────────────────────────────────────────────┘ │  (infers the kind)  │ │
│ ┌─ Student guidance (textarea) ──────────────────┐ │                     │ │
│ │  shown to students                             │ │  [ ⋮ ]  ← kebab     │ │
│ └────────────────────────────────────────────────┘ └─────────────────────┘ │
│ ── materials ───────────────────────────────────────────────────────────── │
│  ▸ Text · "Read the worked example"      (title, body)                     │
│  ▸ Link · "Revision guide"               (label, url)                      │
│  ▸ File · worked-example.pdf             (label optional)                  │
└───────────────────────────────────────────────────────────────────────────┘
```

- Left cell: `flex: 2`, the two textareas stacked, each keeping its existing row markup and ids.
- Right cell: `flex: 1`, the dropzone placeholder plus the kebab trigger.
- The rows list keeps the per-row affordances it has: kind (implied by the row's own shape), payload,
  remove, and a replace control for media/file rows.

## 5. Behaviour rules

1. **Menu → straight to the action.** *Add text content* appends a Text row with an empty title and
   focuses the title field; *Add link* appends a Link row and focuses its field; *Upload from device*
   opens the file picker with the shared allow-list.
2. **Drop → infer.** A dropped file's kind is inferred from its content type/extension: audio → Audio,
   video → Video, image → Image, document → refused with the D5 reason. Dropping onto the resources
   side appends a resource of the matching kind.
3. **No confirm step, no modal per item** — the item appears and is edited in place.
4. **Validation matches D6/D7**: a Text row without a title is invalid at save, reported the same way
   the other child validators report (the `InstructionDtoValidator` fails the write, the page names it).
5. **Read-only (View) posture is unchanged**: no menu, no dropzone, no row affordances — the materials
   render as text, exactly as the instructions do today.
6. **Empty state**: the materials list keeps a one-line invitation, and the dropzone placeholder is
   always visible (it is the affordance, not a state).

## 6. Contract and migration (D7)

| Surface | Change |
|---|---|
| `NewInstructionDto` | `string? Title = null` |
| `InstructionReadDto` | `string? Title = null` |
| `InstructionEditorRow` | `string? Title { get; set; }` |
| `InstructionDtoValidator` | **Text** requires a non-blank `Title`; **Link**'s title stays optional; media kinds ignore it |
| `assignment_instructions` | one **additive, nullable** `title` column — no backfill, no data migration, safe on populated tables |

Rules: the client mirrors the validator (a Text row without a title is marked and blocks save, the way
the other child rows already do); legacy rows stay nullable and render without a label rather than
having one invented for them; the migration lands with the standard guard test (no pending model
changes) and a real-Postgres migration test in the `AssignmentContextPicksMigrationTests` shape.

## 7. The meaning rule (D10)

**A material's meaning comes from the instruction notes.** Exactly two authored text fields exist —
Teacher notes and Student guidance — and they are the frame every material sits inside. Consequently:

- No per-material description or caption field is added (the Link's optional title is a *label*, not
  prose).
- Uploaded files are described by their filename plus, where present, their extraction status — nothing
  new.
- **INS-3 stands**: instruction items do not join the AI generation reference set; resources and
  attachments continue to.

## 8. Ward / student surface (D11)

The ward DTO gains the titled materials as a read-only list — kind, title/label, and the url or file
name — and `SchoolCollab.Families/Components/Pages/Ward/Assignment.razor` renders them in the same card
the instructions already use. **Teacher notes never reach this surface**, which is the whole point of
keeping the two fields distinct. The Python portal is explicitly out of scope here.

## 9. Acceptance criteria

| # | Criterion |
|---|---|
| AC-1 | The Instructions compartment offers one kebab (`RowActionsMenu`) whose items are *Add text content*, *Add link*, *Upload from device* — and it renders as a plain button when only one action is available (the shared component's own rule). |
| AC-2 | Each action appends its row to the form model immediately, with no confirmation step, and focuses that row's first field. |
| AC-3 | The section-level dropzone infers audio/video/image kinds from the dropped file and appends the matching row; a document is refused with a named reason naming the file-material path. |
| AC-4 | A Text row cannot be saved without a title; a Link row saves with or without one; a row that predates the column still loads and saves. |
| AC-5 | The layout matches D8: Teacher notes stacked over Student guidance in the wide cell, dropzone + kebab in the narrow cell, stacking below the existing breakpoint. |
| AC-6 | The retained strings (X-6) and the existing element ids still resolve; the menu, its items and the dropzone carry new ids. |
| AC-7 | The ward card renders the titled materials and never the teacher notes. |
| AC-8 | View mode renders the materials with no menu, no dropzone and no row affordances. |

**Test surface:** bUnit for the section (menu items, append-and-focus, inference, refusal, empty state,
read-only), the existing `InstructionEditorListBunitTests` and `ResourcesSectionBunitTests` updated for
the new ids without losing their assertions, ward tests for AC-7, the validator unit tests for AC-4, and
the migration test for the column.

## 10. Non-goals and named follow-ups

| Follow-up | Why it is not here |
|---|---|
| **One merged material contract** replacing instruction items + resources (with a migration) | D14 — a data-model change that redefines the ward DTO, the publish payload and the AI reference set; needs its own spec. |
| **Widening the enums** (Document on instructions; Audio/Image on resources) | D13 — a wire-contract widening with editor, validation and surface consequences; D5's refusal covers the gap honestly. |
| **The Python portal surface** | D11 — follows the Families card in its own round. |
| **`question-response-types.md` §8 WYSIWYG** | Unchanged and unblocked: the Text row's **body** is where the rich editor lands, which is why the title is a separate field (D6). |
| **Restoring a UI path for reference-URL resources** | D1 retires the Content & Resources add-flow into this area, so `ResourcesSection.AddUrlAsync` is left with **no UI caller** — links are authored as instruction Link rows now. This is an **accepted consequence** of the round (owner-accented 2026-10-10), recorded rather than silently regressed; a future affordance must first decide *which* collection a reference URL belongs to, which is D14's question. |
