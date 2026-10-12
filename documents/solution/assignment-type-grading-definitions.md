# Assignment type ⇄ grading format: the definitions and the rule (D15)

**Owner decisions, 2026-10-09.** This is the durable record of what the three assignment
types *mean* and how that meaning constrains the grading format. The form's hint quotes
these sentences verbatim (`AssignmentTypeGradingRules`), the pickers filter by the rule,
and the create/update command handlers reject a pair the type makes impossible.

## 1. What each assignment type means (owner's words)

| Type | `[Description]` | Definition |
|---|---|---|
| `AssignmentType.Digital` | **Online** | All assignment details are published online — students are not expected to write their answers up. |
| `AssignmentType.SemiManual` | **Hybrid** | Details are published online, but some answers are written to build writing skills; written work is uploaded or submitted physically. |
| `AssignmentType.Manual` | **Offline** | Students write the questions and answers in school to build writing skills; the work is uploaded or submitted physically. |

Before this record existed the repo carried only the three one-word labels — the semantics
lived in nobody's head but the owner's. Nothing in the codebase previously branched on the
type except the AI-generation gate (FR-220).

## 2. What each grading format does (unchanged, for the same reader)

| Format | `[Description]` | Behaviour |
|---|---|---|
| `GradingFormat.TeacherGraded` | **Teacher Marked** | The scoring engine is never called; `Score`/`Passed` stay null until the teacher marks. The Pass threshold and Max attempts fields are inapplicable. |
| `GradingFormat.AutoGraded` | **Auto Scored** | Scores automatically at submit (`Score = correctCount × MaxScore / autoScorableCount`, `Passed = Score ≥ Pass threshold`) and **holds** per-question feedback for teacher review. |
| `GradingFormat.InstantGraded` | **Instant Feedback** | Same scoring, but returns per-question feedback in the submit response — the immediate-vs-held distinction (`documents/solution/assignment-request-implementation-details.md` §3.3). |

## 3. The rule (D15)

**The assignment type defines which grading formats are possible.**

| Type | Permitted formats |
|---|---|
| Online | Teacher Marked · Auto Scored · Instant Feedback |
| Hybrid | Teacher Marked · Auto Scored · Instant Feedback |
| **Offline** | **Teacher Marked only** |

Offline work is handwritten, so there is nothing a machine can score — Auto Scored and
Instant Feedback are impossible for it rather than merely discouraged. Online and Hybrid
work is digital (Hybrid with written portions), so the engine *can* score it, but the
teacher may still mark by hand; the rule restricts only what the type makes impossible.
(Broadening it later — e.g. Online → auto formats only — is a matrix change in one place.)

## 4. Where the rule lives and who enforces it

`src/Assignments/SchoolCollab.Assignments.Contracts/AssignmentTypeGradingRules.cs` —
one static matrix plus the owner's definition constants. **Contracts** is the only project
both consumers can see:

- `Assignments.Application` (the authoring page) references `Admin.Shared`,
  `AI.Abstractions`, `Contracts`, `Students.Application` — **not** `Assignments.Core`;
- `Assignments.Core` (the command handlers) does reference `Contracts`.

The UI filters its grading picker through `PermittedFormats` and switches to
`FallbackFor(type)` when a type change invalidates the current pick; the handlers call
`EnsurePermitted`, which throws `AssignmentTypeGradingValidationException` — mapped to
**400** by the create and update routes, alongside the existing typed validators.
No architecture test forbids an `Application → Core` reference; Contracts was chosen to
keep the UI free of the whole domain assembly.

## 5. UX rules that follow (grilled 2026-10-09)

- **Legacy tolerance:** a persisted out-of-matrix pair (an older Offline + Auto Scored row)
  loads unchanged; its value stays in the picker so the label renders, and the page never
  rewrites it. Only an author edit changes it. (The load path resolves the stored format
  from the full enum, never from the filtered list — a bug the D15 tests caught.)
- **Applied side effect, never silent:** switching a type that invalidates the current
  format switches the format to the type's fallback **and** shows
  `#authoring-grading-switched` carrying `FormatSwitchedReason` (the CP-4 subject-change
  shape). The note clears the moment the author picks a format themselves.
- **The row hint states the rule** (`TypeGradingPurposeHint` + the selected type's meaning,
  four labelled tooltip lines) so an author can see *why* a format is missing.

## 6. Relationship to the AI gate (FR-220)

Unchanged and unaffected: AI generation stays `(Online|Hybrid) × (Auto|Instant)`. Offline
was already excluded from it, and the new rule removes Offline's access to the auto formats
for the same underlying reason (nothing machine-readable to score or generate against).
`QuestionGenerationGate` remains the single source for that gate.
