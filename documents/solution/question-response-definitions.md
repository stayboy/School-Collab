# Question response definitions, instructions, and the media/auto-scored rule

**Status:** implemented (feature A, authoring side) — the durable record of *why* the shape is what it
is. The feature spec is `documents/specs/question-response-types.md` (§7 records the decisions); the
delivery round was `documents/rounds/round-question-response-types.md` (ephemeral). Read this before
changing the kinds rule, the instruction model, or the upload allow-list.

## The requirement

A question must state **what response it expects** — video, recorded audio, an uploaded document, an
image, any mix of them — and both the question and the assignment carry **instructions** in five kinds
(text · audio · video · url · image). Where the teacher marks, review + response per answer follows
(phase 2).

## The two rules, and the one that has teeth

1. **Every response kind is media**, so a question that defines any kind is valid only on a
   **Teacher Marked** assignment: an auto-scored engine has nothing to score in a video
   (`QuestionResponseKindRules`, mapped to **400** on the assignment routes beside D15's rule).
2. **A kind is mandatory exactly where a kind is permitted.** This is the non-obvious half and it is
   one invariant, asserted by a test (`RequiresResponseKinds ⟺ IsPermitted`): kinds are permitted on
   Teacher Marked alone, so on Auto Scored / Instant Feedback the set is *empty by design* and nothing
   is required — the expected answer form there is already fixed by `QuestionType`.

**Why the invariant, and not “at least one kind, always”:** §5.1 originally said every question
carries at least one kind. Taken literally that is unsatisfiable — any kind is rejected on an
auto-scored assignment — and it breaks AI generation, which is offered **only** on those two formats
(`QuestionGenerationGate`, FR-220). The generator produces kind-less questions for the author to
complete; that is what "leave generated questions for further edit" means. The owner settled the
scope on 2026-10-09 (spec §7.3 R1, round-3 Q1(ii)); the literal wording of §5.1 was amended, the
intent (the student surface can always state what is expected) was kept.

## The model

- `AssignmentQuestion.ResponseKinds` — a set, stored as `integer[]` (NOT NULL, default `{}`). A
  first-class array column, not a join table: it is a small ordered value set with no per-row
  metadata, and EF maps `IReadOnlyList<T>` natively (a hand-written converter *composes* with EF's
  element conversion and breaks the model build — do not add one).
- `AssignmentInstruction` — **one shape, two owners**: `QuestionId` null means the instruction
  belongs to the assignment, non-null ties it to that question. One owned collection on the
  `Assignment` aggregate, one table, and the read handlers group by `QuestionId`. The row carries no
  `TenantId` and no audit columns, like every other assignment child.
- Ordering: `DisplayOrder`, re-indexed per owner on every write (`InstructionsFor` orders by it).
- The assignment's existing `Instructions` **text** column is untouched and renders first; the rows
  are the media/extra half of the same block.

## The write paths, and why they surprise people

The questions ride a **full replacement**: `RemoveQuestion` → `AddQuestion` re-mints the rows on every
save, so anything not carried in the payload is silently dropped. Concretely:

- The update handler replaces the assignment's own instruction rows **per owner**
  (`SetAssignmentInstructionItems`), never the whole collection: a question-only edit must not delete
  a question's blocks, and an instructions-only edit must not delete the assignment's.
- The draft-confirm and duplicate paths carry kinds + instruction rows across their re-mints.
- The question dialog edits a **working copy** — a copy that forgets a field wipes it on confirm.

Each of those was a real bug found by a test, which is why the tests exist where they do.

## Uploads

Instruction media reuse the attachment staging path verbatim (`POST /attachments/stage`, `IFileStore`,
the sweeper, the 413/400 mapping). The one thing they could **not** reuse was the extension allow-list:
it was document/image-only while it existed for AI grounding alone, so audio and video instructions
could not be uploaded at all. `AttachmentUploadOptions.AllowedExtensions` (server) and
`AttachmentUploadPolicy.AllowedExtensions` (client mirror) now carry the media extensions too, and the
AppHost parameter default plus `documents/configuration.md` §2 record them. **Keep the two lists in
lockstep** — the client one exists only to avoid an obviously-doomed round trip; the server is
authoritative. Per-kind caps (25 MiB is about a minute of video) remain the named follow-up.

## Where the code lives

| Concern | File |
|---|---|
| The rules | `Assignments.Contracts/QuestionResponseKindRules.cs` |
| Question payload validation (kinds + options) | `Assignments.Core/…/Commands/QuestionOptionDtoValidator.cs` |
| Instruction payload validation | `Assignments.Core/…/Commands/InstructionDtoValidator.cs` |
| Domain | `Assignments.Core/Domain/{AssignmentInstruction,InstructionKind,ResponseKind}.cs`, `Assignment.cs`, `AssignmentQuestion.cs` |
| Persistence | `Assignments.Core/Data/Configurations/AssignmentConfiguration.cs` + `Migrations/20261009052346_*` |
| Editor (shared, both owners) | `Assignments.Application/…/Assignments/InstructionEditorList.razor` |
| Picker | `Assignments.Application/…/Assignments/QuestionEditDialog.razor` |
