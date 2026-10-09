# Question response definitions, teacher instructions, and the teacher response loop

**Status:** spec — findings + requirements + proposed design. **Decisions open** (§7); round 1
of the grill is in flight with the owner (2026-10-09). No implementation until the frontier is
empty and the owner confirms.

Companion specs: `assignment-authoring-compartments.md` (the authoring surface),
`assignment-authoring-content-questions-modern-ui.md` (§6.2 the question list/editor),
`assignment-create-edit-redesign.md` (D15 — the type⇄grading rule this spec must respect),
`assignment-creation-with-ai.md` (FR-220 and the generation flow).

---

## 1. The owner's requirements (verbatim, 2026-10-09)

1. *"Question responses must be defined."* — a question states what response(s) it expects.
2. *"Response submission can be any of or a mix of — video, recorded audio, uploaded resource
   (doc, image), specified during question setup."*
3. *"Where teacher marked, teacher review and response is required for submitted response."*
4. *"This must refine AI question generation or as it stands, leave AI generated questions for
   further edit."*
5. *"Teacher added instruction to question as text, audio, video or attached resource
   (url, image)."*
6. *"Parent assignment must also have added instruction as text, audio, video or attached
   resource (url, image)."*

## 2. What exists today (findings)

| Area | Current shape | Gap this spec closes |
|---|---|---|
| `AssignmentQuestion` | `Id, AssignmentId, QuestionText, QuestionType (MultipleChoice · TrueFalse · ShortAnswer), DisplayOrder, CorrectOptionId?, ModelAnswer?, GenerationId?` + `QuestionOption` rows | No response definition; no instruction payload |
| `SubmissionAnswer` | `Id, TenantId, SubmissionVersionId, QuestionId, SelectedOptionId?, TextAnswer?, RowVersion, …` | Text/option only — no media answer |
| `AssignmentSubmissionVersion` | `Source, Content (string?), Score?, Passed?, VersionNumber, SubmittedByGuardianId?, …` | No per-answer teacher response |
| `AssignmentAttachment` | `FileName, ContentType, FileSize, StoragePath, ExtractionStatus, ExtractedText?` + `IFileStore` | Assignment-level only; nothing per question/answer. Its extraction pipeline is the AI-grounding precedent |
| Assignment-level `Instructions` | `AssignmentSummaryDto.Instructions: string?` — one student-facing **text** field (INS-1), edited in the authoring form's bottom **Instructions** compartment beside the internal `Description` | Text only — the assignment itself cannot carry an audio/video/url/image instruction |
| AI generation | `GeneratedQuestionDto(Type, …)` over `GeneratedQuestionType`; `QuestionPromptComposer`; `QuestionGenerationGate` (FR-220: Online/Hybrid × Auto/Instant) | No response/instruction dimension |
| D15 rule (new) | Offline → **Teacher Marked only**; auto formats only on Online/Hybrid | Media responses and auto-scoring cannot mix freely — see §5.3 |

## 3. Proposed domain shape (to be confirmed by the grill)

```text
Assignment
  + Instructions       : string?          (the existing student-facing TEXT field — unchanged)
                       + [InstructionItem]  (0..n — media/url instructions on the parent itself)

AssignmentQuestion
  + ResponseKinds      : [Video | Audio | Document | Image]   (>=1; "a mix" = a set)
  + Instructions       : [InstructionItem]  (0..n, ordered)

InstructionItem   (new — ONE shape, TWO owners: QuestionId null = the assignment)
  QuestionId? : Guid                (the nullable FK is the owner discriminator)
  Kind      : Text | Audio | Video | Url | Image
  Text?     : string                (Text)
  Url?      : string                (Url)
  Media*    : FileName, ContentType, FileSize, StoragePath   (Audio/Video/Image — IFileStore)
  DisplayOrder : int

SubmissionAnswer
  + Media      : [AnswerMedia]      (0..n — one per submitted kind)
  + Reviews    : [AnswerReview]     (0..n)

AnswerMedia   (new)   FileName, ContentType, FileSize, StoragePath, DurationMs?
AnswerReview  (new)   ReviewerTeacherId, ReviewedAt, ResponseKind(s),
                      Text? / Media*, Decision (e.g. Accepted | NeedsWork)
```

Media columns are flattened (the `AssignmentAttachment` precedent) rather than a shared
`MediaRef`, so each table stays tenant-scoped and independently queryable.

## 4. Why (the workflow this enables)

- **Offline / Hybrid writing practice** is the point of the rule set: a teacher posts the
  questions, the student writes the answers, photographs or records them, and uploads — or
  hands them in physically (physical submission stays out of scope of the digital flow).
- **The instruction block** is how a teacher explains *how* to answer (a worked example read
  aloud, a diagram, a short video, a link to a source) without leaving the question.
- **The review loop** is what makes Teacher Marked meaningful for media answers: the teacher
  reads/watches/listens and answers back in kind — review **and** response per answer.

## 5. Rules that follow (proposed; §7 settles the open ones)

### 5.1 Definitions are mandatory and explicit
Every question carries at least one `ResponseKind`. No implicit default: a definition-less
question is invalid at save (the `SubmissionAnswerValidator` fail-closed precedent), so the
student surface can always state what is expected — and the AI prompt can say it too.

### 5.2 Instructions are optional, multi-modal, per question
0..n instructions of any kind, ordered; the authoring surface renders them as a compact list
(chips/add buttons — the D4 `ContextPicksSection` pattern) rather than a wall of fields.

### 5.3 Media responses cannot be auto-scored (interacts with D15 + FR-220)
A media response (video/audio/document/image) has no machine-checkable answer. Proposed:
**a question carrying a media `ResponseKind` is only valid on a Teacher Marked assignment** —
the same reasoning that makes Offline Teacher Marked only. Auto Scored / Instant Feedback
questions keep to machine-scorable kinds (MultipleChoice · TrueFalse · ShortAnswer).
(The alternative — allow the mix and let the teacher review the media answers on an auto
assignment — is a deliberate, reviewable exception; the grill decides.)

### 5.4 Teacher Marked ⇒ review + response per answer
Every submitted media answer must receive a teacher review **and** a response payload before
the answer is "complete". Whether that is a hard gate (submission stays open/incomplete) or a
queue obligation (the assignment cannot be archived while reviews are outstanding) is an open
decision (§7 Q4) — it changes the state machine, not just the UI.

### 5.5 AI generation: refine or leave-for-edit
The owner accepts either: (a) generation emits `ResponseKinds` + instructions, or (b) it keeps
emitting question text/types and the author completes the definitions. Recommended: **(b) now,
(a) as a named follow-up** — the generation contract is shared with the AI host
(`GeneratedQuestionDto`) and widening it is its own round with prompt/parser/test churn;
(b) is honest, ships the authoring model first, and the authoring editor is where definitions
are validated anyway.

### 5.6 The parent assignment carries instructions too — one model, two owners

The owner's requirement 6 (2026-10-09) extends instructions to the assignment itself. Proposed:
**one `InstructionItem` shape serves both owners** (`QuestionId` null = the assignment) — the
single-source rule the D14e/D15 work already established; the authoring surface renders the
assignment's items in the existing bottom **Instructions** compartment (beside the text field
and the internal `Description`, which stays an internal note and is NOT student-facing).
The existing `Instructions` **text** column is kept as-is for the assignment; the schema
migration it needs (and why no data backfill is needed) is recorded in **§5.7**.

### 5.7 Migration is required — and it is an owner gate (Q10 answer, 2026-10-09)
Extending instructions with media kinds needs a **schema** migration: a new `instruction_items`
table (`Id, TenantId, AssignmentId, QuestionId NULL = assignment-level, Kind, Text?, Url?, media
columns, DisplayOrder`, audit columns) with a nullable question FK and tenant/assignment indexes.
What is NOT needed is a **data backfill**: the existing `Instructions` text column stays exactly
where it is, no row is created for it, and the display order is "the text first, then the items by
`DisplayOrder`". Per AGENTS.md the EF migration reaches the owner as an explicit gate, and the
round carries a migration test (the `AssignmentContextPicksMigrationTests` precedent).

## 6. Out of scope (proposed)

- The **student-facing** submission experience (upload UI, camera/recorder, offline capture)
  and the **teacher review queue** UI — unless §7 Q1 pulls them in.
- Physical submission bookkeeping (the "handed in on paper" state) beyond the note that the
  work may never arrive digitally.
- Streaming/transcoding of video (store what is uploaded), virus scanning, CDN delivery.
- Any change to the scoring engine beyond refusing media kinds (§5.3).

## 7. Decision tree — round 1 frontier (open, with recommendations)

| # | Decision | Recommendation |
|---|---|---|
| Q1 | **Scope**: authoring-side only (definitions + instructions, stored and validated) vs the full round trip (student submission of media + teacher review/response UI + state machine) | **Authoring-side first**, round trip as a named phase 2 — the model must exist before either surface can be designed, and it keeps this round's blast radius to the question aggregate + the authoring editor |
| Q2 | **Response kinds**: exactly `{Video, Audio, Document, Image}`? Does `Text` remain a kind (ShortAnswer already is one), and is a URL an *answer* or only an instruction? | **`{Video, Audio, Document, Image}`** for answers; `Text` arrives via the existing ShortAnswer type; URL is instruction-only |
| Q3 | **Definition granularity**: per question only, or an assignment-level default with per-question override | **Per question only** (explicit, no inheritance to reason about); the authoring surface may offer a "copy to all questions" convenience action later |
| Q4 | **Teacher Marked enforcement**: hard gate (answer not complete until reviewed+responded) vs queue obligation (archive blocked while outstanding) | **Queue obligation, surfaced as a count** — a hard gate risks stranding submissions on a teacher's absence |
| Q5 | **Auto-scored interplay** (§5.3): forbid media kinds on auto formats, or allow the mix | **Forbid** — a media answer cannot be auto-scored, and the D15 precedent (impossible, not discouraged) applies |
| Q6 | **AI generation**: refine now vs leave-for-edit now (§5.5) | **Leave-for-edit now**, refine as a named follow-up |
| Q7 | **Media limits**: max size per kind, allowed content types, duration caps, and where they are configured (hard-coded constants · assignment policy · the Config service's runtime flags) | **Constants in the domain for v1 + a policy field later**; per-kind caps are a platform concern, not per assignment an author's |
| Q8 | **Instruction count**: exactly one instruction per question, or many | **Many, ordered** (a teacher may pair a diagram with a spoken example) |
| Q9 | **Execution mode** (AGENTS.md mandate for behavioural work): solo · light round (one worker + diff reviewer) · full four-agent round | **Light round** for the authoring-side phase; the round-trip phase likely warrants a full round |
| Q10 | **Assignment instruction model** (requirement 6): one `InstructionItem` shape shared by the assignment and its questions (nullable `QuestionId`), keeping the existing `Instructions` text column as the assignment's text instruction — vs a separate assignment-only collection (two shapes for one concept) vs migrating the text column into a `Text` row now (destructive-ish, needs a data migration) | **Shared shape + keep the text column** (§5.6): additive, no data migration, one model; the text→row unification is a named later round |

Round 2 (deferred until round 1 lands — each answer changes their shape): the review state
machine's states/transitions, the media pipeline (tables vs a media service; upload
endpoints, resumability), the authoring editor's exact UX (where the definition picker and
instruction uploader live in `QuestionEditDialog`/the draft section), the AI contract widening,
and the migration plan for existing questions (backfill of `ResponseKinds` — likely a one-off
default of the machine-scorable kinds derived from `QuestionType`) plus the eventual folding of
the assignment's `Instructions` text column into a `Text` instruction row (§5.6).

### 7.1 Settled (owner, 2026-10-09)

| # | Settled decision |
|---|---|
| Q1 | **Authoring-side first**; the student/teacher round trip is a named phase 2 |
| Q2 | Answers are `{Video, Audio, Document, Image}`; `Text` arrives via the existing ShortAnswer type; a URL is **instruction-only** |
| Q3 | Response kinds are **per question only** (a "copy to all" convenience may follow) |
| Q4 | Teacher Marked ⇒ **queue obligation with a visible count**, not a hard gate |
| Q5 | **Media kinds are forbidden on Auto Scored / Instant Feedback** questions |
| Q6 | AI generation **stays as it stands** (leave-for-edit); refining it is a named follow-up |
| Q7 | Media caps live as **domain constants for v1**, a policy field later |
| Q8 | Instructions are **many and ordered** per question |
| Q9 | **Light round** for the authoring phase |
| Q10 | One shared `InstructionItem` shape for the assignment and its questions, with the existing `Instructions` text column kept — **and the schema migration that requires is confirmed in §5.7** (a new table; no data backfill; owner gate; migration test in the round) |
| Q20 | Build order: **feature A first, then the WYSIWYG follow-up** |

### 7.2 The migration, stated plainly (Q10 follow-up)

- **Yes, a migration is required.** `instruction_items` is a new table; nothing about the
  existing rows changes.
- **No, existing data is not migrated.** The `Instructions` text column keeps its values and its
  place (rendered first); no backfill row is written.
- **Owner gate.** EF migrations are explicitly owner-gated in this repo (AGENTS.md), so the
  migration file lands only with your sign-off, alongside a migration test.


## 8. Follow-up feature (owner, 2026-10-09): WYSIWYG rich instruction

*"Assignment already have text instruction. next followup feature, is request to use a WYSIWYG
editor, for formulating multi-rich instruction."* — a distinct phase after the response-types
phase; recorded here because it authors the model §5.6 defines.

### 8.1 Findings

| Area | Today | Consequence |
|---|---|---|
| Instruction editing | `<FluentTextArea Id="authoring-basics-instructions">` bound to `_model.Instructions` (plain `string`) — and `Description` beside it as an internal note | No rich formatting, no inline media |
| Rich text / HTML in the repo | **None** — no `MarkupString`, no sanitiser (`HtmlSanitizer`/Ganss), no Markdown renderer (Markdig), no editor library | The whole stack is net-new: editor → storage format → sanitiser → renderer |
| JS interop precedent | Collocated/`_content/...` ES modules via `IJSObjectReference` (`chatInput.js` in AI Chat; `AssignmentAuthoring.BeforeUnloadModulePath`) | A JS-backed editor is architecturally precedented — but **not** bUnit-drivable (JSInterop must be Loose; the module is a thin shell) |
| Vendor JS assets | No `wwwroot/lib` anywhere in `src` | Bundling a third-party editor is a new asset/packaging decision (and a licence one) |
| AI prompt path | `QuestionPromptComposer` does not consume the instruction text today | Rich text does not have to be flattened for the prompt yet — but if it later joins the prompt, it must be **plain-text-ified** there |

### 8.2 Shape (proposed)

- One shared **`RichInstructionEditor`** component (the §5.6 model's editing vehicle) used by the
  assignment's Instructions compartment and the question editor alike.
- Storage: the instruction text remains a string column; its **format** is the open decision (Q11).
- Rendering: one shared read-only render component (Detail/student surfaces) fed by one sanitiser.
- Coexistence: the rich text and the §5.6 media items share the instruction block — the text
  carries prose/links, the items carry audio/video/url/image.

### 8.3 Decision tree — round for the WYSIWYG follow-up (open, with recommendations)

| # | Decision | Recommendation |
|---|---|---|
| Q11 | **Storage format**: sanitised HTML (allow-list) · Markdown rendered by us · structured JSON blocks | **Sanitised HTML**, sanitised on write **and** again on render (defence in depth), with the sanitiser policy in one place. Markdown is the runner-up (smaller XSS surface, poorer WYSIWYG fidelity); JSON blocks are the most control for the most work |
| Q12 | **Editor**: bundled third-party JS editor (Quill · TipTap) · minimal custom toolbar over `contenteditable` · no WYSIWYG (Markdown textarea + live preview) | **Quill** (single-purpose, MIT, one file) behind the interop shell; custom `contenteditable` is a maintenance trap; the Markdown-textarea fallback is the zero-dependency option if the owner declines a new vendor asset |
| Q13 | **Scope of the editor**: the assignment instruction only, or question instructions too | **Both — one component** (§5.6 already shares the model; the editor is the same control) |
| Q14 | **Coexistence**: rich text + the media items, or WYSIWYG subsumes the media (inline embeds) | **Coexist.** Inline embeds would make the editor responsible for audio/video trust and playback; the items already model that |
| Q15 | **Rendering + sanitisation policy**: which surfaces render it, and where the single sanitiser lives | **One `RichInstruction` render component + one sanitiser helper**, used by every surface; sanitise on write and on render; no ad-hoc `MarkupString` anywhere (a rule to add to `blazor-components.md`) |
| Q16 | **Compatibility**: what happens to the existing plain-text instructions | **Render as plain text, no migration** — the editor loads the stored string as-is; the column stays; a later round may convert |
| Q17 | **Testability without JS**: the editor cannot be driven in bUnit | **Test the seams** — persistence (string in/out), the sanitiser's allow-list, the render component, and the model's round-trip; the interop shell is excluded from bUnit (the `chatInput.js` precedent) |
| Q18 | **Dependency approval + mode**: adding a JS vendor asset and/or a sanitiser package is a human decision (AGENTS.md), then the execution mode | **Explicit owner sign-off on the dependency**, then a **light round**; the asset's CSP/bundle size recorded in the round doc |

### 8.4 Settled (owner, 2026-10-09)

| # | Settled decision |
|---|---|
| Q11 | **Sanitised HTML**, sanitised on write and again on render |
| Q12 | A **community Blazor component** — see §8.5; **Quill is BSD-3-Clause** (this corrects the earlier "MIT" claim) and the licence hazards to avoid are named there |
| Q13 | **Both owners, one editor component** (assignment + question instructions) |
| Q14 | Rich text and the media instruction items **coexist** |
| Q15 | **One render component + one sanitiser helper**, used by every surface; no ad-hoc `MarkupString` |
| Q16 | Existing plain text **renders as plain text, no migration** |
| Q17 | **Test the seams** (persistence, sanitiser allow-list, render component); the interop shell is excluded from bUnit |
| Q18 | **Dependencies pre-authorised** — the editor asset and the sanitiser package may be added, with licence, CSP and bundle size recorded in the round doc |

### 8.5 Editor licence research (the Q12 answer)

Researched when the owner flagged IP rights. **Correction: Quill is BSD-3-Clause, not MIT**
(`slab/quill` LICENSE — © 2017-2024 Slab, © 2014 Jason Chen, © 2013 salesforce.com). BSD-3 is
permissive: attribution + no-endorsement clauses, **no copyleft, no commercial-use restriction** —
so Quill itself is not an IP obstacle.

**Hazards to avoid (verified):**

| Component | Licence | Verdict |
|---|---|---|
| **CKEditor 5** | GPL-2.0-or-later **or** commercial | Copyleft — unsafe here |
| **BlockNote** | AGPL-3.0 | Network copyleft — unsafe |
| **Blazorise** (incl. `Blazorise.RichTextEdit`) | "Source-available", **dual-licensed** (LICENSE.md **or** a commercial licence "depending on your scenario") | Commercial-scenario ambiguity — avoid |
| TipTap core | MIT (Pro extensions commercial) | Core fine; no Pro extensions |

**Community Blazor components on a clean licence:**

| Component | Licence / base | Notes |
|---|---|---|
| **`Blazored.TextEditor`** | **MIT**, wraps **Quill** (`quill@2.0.3`) | Smallest fit; community-maintained; its setup pulls Quill from a **CDN**, so we **vendor and pin** the assets locally (the repo ships no CDN assets) and keep the render path ours (§Q15) |
| `Spillgebees.Blazor.RichTextEditor` | community, Quill-based | Same base, less adoption |
| `Radzen.Blazor` `RadzenHtmlEditor` (+ its `MarkdownEditor`) | **MIT source** (the subscription covers themes/blocks/support, not the components) | Capable, maintained — but adopting it adds a **second component library** beside FluentUI (styling/theming divergence to own) |
| Syncfusion / Telerik / DevExpress | commercial | Rejected on cost |

**Recommendation:** `Blazored.TextEditor` (MIT) over **Quill 2.x (BSD-3-Clause)**, both vendored and
pinned locally, with our sanitiser and render component owning display. Alternatives, in order:
(a) the Q11 Markdown route with our own renderer (zero third-party UI); (b) `RadzenHtmlEditor` if a
maintained first-party component is preferred over a small community wrapper, accepting the second
UI library.

