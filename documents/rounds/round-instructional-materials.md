# Round — instructional materials (light round, Tier 2)

**Mode:** unattended (auto-accept recommendations) — owner authorization, 2026-10-10, **this round only**.

**Status:** OPEN — **W1–W3 closed and verified** (contract · validation · persistence: entity + 3 setters + all 8 tuple sites, EF column, migration, both reads, the five-path unit gate mutation-proven, real-Postgres migration test). **W4–W8 open.** Tier corrected to Tier 3; independent review not yet performed (delegation blocked — see Execution notes).
**Mode:** **Tier 3** per the skill's eligibility table (`.pi/skills/orchestrator-worker-reviewer/SKILL.md`, line 44): this round carries **schema** (a migration), a **wire/contract** change and **multi-context** risk (the ward DTO), any one of which excludes Tier 2. The parent owns this doc and the acceptance; the plan must be independently reviewed **before** implementation; the worker implements the plan only; a reviewer verifies the diff. Attended.
**Spec:** `documents/specs/instructional-materials.md` — the durable decisions (D1–D14, AC-1…AC-8) live
there. This doc carries the plan, the worker brief and the review, and is ephemeral.

## Execution notes (environment, 2026-10-10)

Delegation was attempted and failed at the account layer; the round is paused on that, not on the plan.

| Mechanism | Observed |
|---|---|
| `spawn_agent` | tool call **interrupted, no result** — pi-hosted subagent dispatch, no backend in the Cline session |
| `team_spawn_teammate` | **succeeded** (teammate metadata created) |
| `team_run_task` | **queued** (`run_00001`) |
| `team_await_runs` | **failed**: `Unauthorized: Please make sure you're using the latest version of Cline and re-authenticate your Cline account.` |

**Conclusion:** the team runtime is wired (spawn and queue both succeed) but **executing** a teammate
requires an authenticated Cline account. Until that is restored, no worker or reviewer can run — so the
round cannot follow the tiered workflow as written.

**Paths forward (owner decision, recorded on line 1 of this doc when taken):**

1. **Re-authenticate Cline** (update the client and sign in), then run the round as Tier 3: plan review →
   worker → reviewer → acceptance. Preferred — it is the process this round's risk profile calls for.
2. **Solo with self-applied role contracts**, the precedent recorded in
   `documents/rounds/round-ar-10-ai-extensions.md` §"owner SOLO decision": the parent implements and
   then reviews its own diff against the plan, with this doc stating plainly that the independent review
   was unavailable (not that it passed). Weaker verification; acceptable only because the plan and the
   contract decisions are already owner-approved and written down.

Related: `documents/rounds/.session-state.2026-09-22.md` records a sibling dispatch failure
(*"died at launch with `Unknown subagent`"*), so this class of failure has precedent in this repo.

**Async confirmation (system-delivered, 2026-10-10):** `run_00001` (plan-reviewer) completed as
**failed — `Unauthorized: … re-authenticate your Cline account`**, matching the synchronous
`team_await_runs` result, with no runs left in progress and the teammate stopped. The delegation block
is therefore confirmed at the account layer, not a transient tool fault.

**Path taken: (2), declared.** Delegation cannot run this session, so the parent self-applies the role
contracts (the `round-ar-10-ai-extensions.md` precedent) and this doc records that the **independent
review was unavailable — it did not pass**. Two mitigations keep that from being a hole: the contract and
migration decisions are owner-approved and written down (spec §6), and the round still ships its
`diffs-instructional-materials.patch`, so an independent review can be run against the finished diff the
moment authentication returns.

## Plan

**Objective.** Replace the instruction area's button-plus-dropdown flow with one kebab-driven,
drop-capable *instructional materials* surface: the two notes fields stacked wide, a dropzone with the
kebab narrow, text/link/media items appended straight to the form model, and a required title on text
content — with the ward surface gaining the titled materials.

**Gates already cleared — do not re-litigate.** The owner approved the **wire shape and the migration**
(2026-10-10, spec §6): the nullable `title` column and the `Title` fields on the three contract rows.
AGENTS.md makes those human gates; they have been through one.

| # | Work | Files |
|---|---|---|
| **W1** | Contract: `Title` on `NewInstructionDto`, `InstructionReadDto`, `InstructionEditorRow` | `Assignments.Contracts/ContractTypes.cs`, `AssignmentQuestionEditorRows.cs` |
| **W2** | Validator: Text requires a non-blank title, Link optional, media ignored; the client mirrors it so a bad row blocks save | `InstructionDtoValidator`, `AssignmentEditFormModel` / its projections |
| **W3** | Migration: additive nullable `title` on `assignment_instructions`; EF pending-model guard stays green; a real-Postgres migration test in the `AssignmentContextPicksMigrationTests` shape. **Wider than it reads** (found 2026-10-10): the title must thread through the aggregate's tuple-shaped setters — `SetInstructionItems` and `SetAssignmentInstructionItems` both take `(Guid? QuestionId, InstructionKind Kind, string? Text, string? Url, string? FileName, string? ContentType, long FileSize, string? StoragePath)` and must gain `Title` — plus their handler callers, the entity's ctor, the EF column, the children-read projection, **and the two re-mint paths that carry instruction rows across a save (questions-draft *confirm* and *duplicate*)**, where a missing `Title` would silently drop it. | `AssignmentInstruction.cs`, `Assignment.cs`, `AssignmentConfiguration.cs`, `AssignmentEditFormModel`/handlers, `GetAssignmentAuthoringChildrenQuery`, `SchoolCollab.Assignments.Core/Migrations/*`, `tests/…/Tests.Integration` |
| **W4** | The section: `RowActionsMenu` kebab (text/link/upload items), section-level dropzone placeholder that infers the kind, the flex-2 / flex-1 layout, the relabelled notes fields | `AssignmentAuthoring.razor(.css)`, `InstructionEditorList.razor(.css)`, `ResourcesSection.razor` |
| **W5** | Retire the Content & Resources add-flow into the section (supersedes CR-1/CR-7/CR-8); keep CR-2…CR-6/CR-10…CR-12 and every X-6 string | `ResourcesSection.razor(.css)` |
| **W6** | Ids: keep the existing ones (bUnit contract), add ids for the menu, its items and the dropzone | the components above |
| **W7** | Ward surface: the titled materials on the ward DTO and the Families card, teacher notes excluded | `GetWardAssignmentViewQueryHandler`, `WardAssignmentProjectionRepository`, `Families/…/Ward/Assignment.razor` |
| **W8** | Tests: section bUnit (menu, append+focus, inference, refusal, empty state, read-only), the two existing bUnit classes updated without losing assertions, ward test, validator tests, migration test | `tests/…Assignments.Tests.Unit`, `tests/…Tests.Integration`, `tests/…Families.Tests.Unit` |
**W3 map correction (2026-10-10, after the first execution pass).** The row above understated the thread twice. The setters are **three**, not two — `AddInstructionItems` carries the same tuple and is the *confirm-questions-draft* path, one of the two silent-drop sites. And the tuple type is written in **five more places the map missed**: the callers' **`var`-inferred origin projections** (`CreateAssignmentCommandHandler:107` and `:187`; `DuplicateAssignmentCommandHandler:94` and `:123`; `UpdateAssignmentCommandHandler:79` — both branches — and `:189`; `ConfirmQuestionsDraftCommandHandler:92`) and **two explicit local declarations** (`UpdateAssignmentCommandHandler:162`, `ConfirmQuestionsDraftCommandHandler:67`). A signature change therefore lands as one atomic batch **with its origin projections and its local declarations**, or the compiler fails at the caller's *own local* rather than at the call — which is precisely how batch 1 taught this (12 errors, 4 of them this class, 0 after the locals were widened). Landed as three verified batches — (1) `SetInstructionItems` + all six origin projections/declarations, (2) `AddInstructionItems` + the confirm path, (3) `SetAssignmentInstructionItems` — with the entity carrying a **trailing defaulted** `title = null` appended after `displayOrder`. Solution build 0 errors; suite 1068/1070 with the only two failures being `NoUncommittedModelChanges`, i.e. the guard naming the EF column + migration as the next hop.

**W3 landed (2026-10-10).** Entity `Title` + trailing defaulted `title`; all three setters and all eight tuple-writing sites threaded in three verified batches; EF column `title` (`character varying(200)`, nullable) via `AssignmentConfiguration`; migration `20261010114247_AddAssignmentInstructionTitle` (additive `AddColumn` + matching `Down`); both read projections (`GetAssignmentAuthoringChildrenQueryHandler.MapInstruction`, `GetAssignmentByIdQueryHandler`) and `AssignmentEditFormModel.FromRead` carry it, so the name survives a load-edit-save round-trip instead of being nulled on the author's next save. Evidence: solution build **0 errors**; `SchoolCollab.Assignments.Tests.Unit` **1070/1070, 0 failed**, the two `NoUncommittedModelChanges` failures gone. **Trap worth remembering:** the first `dotnet ef migrations add` ran with `--no-build` against an assembly predating the `HasMaxLength(200)` config, so it emitted `type: "text"` with no `maxLength` and left the guard red — the fix was `migrations remove --force`, a build, then `add` *without* `--no-build`. Never generate a migration against a stale binary.

**W4–W8 grill round (2026-10-10, unattended — all eight auto-accepted as recommended).** Facts settled first: `RevealLinkInputAsync` is referenced *only* inside `ResourcesSection.razor` (line 23 the `+`, 196 the drain field, 352 the method) — no test touches it, so Q5's removal is safe; the shared allow-list already exists (`AttachmentUploadPolicy.AllowedExtensions`/`AcceptExtensions`) with `FluentInputFile` as the existing drop seam. **(Q1)** The kebab lives in the *section's* narrow cell with the dropzone (D8/§4) and drives the list through a new `InstructionEditorList.AddRowAsync(kind)` via `@ref`; the dialog keeps its current add affordance (the list's own button stays, suppressed by a default-true visibility parameter so the dialog's rendering is untouched). **(Q2)** The per-row kind dropdown is removed in **both** hosts (§4: kind implied by the row's shape; it *is* the rejected pattern), replaced by a non-interactive kind label — reclassifying becomes remove + re-add, an accepted behavior change for the dialog. **(Q3)** The dropzone reuses `FluentInputFile` (Drag, single file), infers audio→Audio · video→Video · image→Image by content type then extension, and refuses a document with a new named string — `"«name» is a document — use Upload from device, or drop it on the resources tile."` — pinned by a bUnit assertion. **(Q4)** Inference lives in a pure `InstructionKindInference` helper under `Application/Helpers`. **(Q5)** W5 removes the header `+`, `_revealLinkInput`, its drain and the public method, retiring only reveal-specific bUnit assertions; CR-12 narrows in §0 to "type name + methods still in use". **(Q6)** The ward materials list reuses `InstructionReadDto` over the assignment's **own** rows only, labelled `Title ?? FileName ?? Url` — no new DTO; teacher notes are excluded structurally (they are the scalar `Description`, never an instruction row). **(Q7)** Focus is `ElementReference.FocusAsync()` behind a pending-focus flag in `InstructionEditorList.OnAfterRenderAsync` — no JS interop. **(Q8)** W8 adds `InstructionalMaterialsSectionBunitTests` and updates the two existing suites' ids without dropping assertions.

**W4–W8 landed (2026-10-10, unattended run to W8).** W4/W6: the Instructions compartment now renders one `RowActionsMenu` kebab (*Add text content · Add link · Upload from device*, ids on the menu/dropzone), a section-level dropzone that infers the kind (`InstructionKindInference`) and refuses a document by name (`#authoring-materials-refusal`), the flex-2 notes / flex-1 dropzone+kebab layout with **Teacher notes** over **Student guidance** (both ids preserved), a Title field on Text rows in the shared editor, the per-row kind dropdown replaced by a label, and `ElementReference`-style focus via `FluentTextField.FocusAsync()` behind a pending flag. W5: the header `+`, `_revealLinkInput`/`_focusLinkPending`/`_linkField`, the drain and `RevealLinkInputAsync` are retired; `AddUrlAsync(url)` took an optional parameter so its row/duplicate/payload coverage survives, and CR-12 is narrowed in §0. W7: `WardAssignmentViewDto.Materials` (reusing `InstructionReadDto`, own rows only) is projected and the Families ward card renders it in the Instructions card, labelled `Title ?? FileName ?? Url`. W8: three section tests added (AC-2 append-with-no-confirm, AC-3/D5 named refusal, AC-8 View suppresses every affordance) and the compartment test now asserts AC-1's three kebab items. **Evidence:** solution build 0 errors; `SchoolCollab.Assignments.Tests.Unit` **1079/1079, 0 failed**; `SchoolCollab.Families.Tests.Unit` **44/44**; §0 pointers added to the four superseded specs; `diffs-instructional-materials.patch` written. **AC-7 landed (2026-10-10):** `Materials_Render_WithTheirTitles_AndNeverTheTeacherNotes` in `WardAssignmentPlayerBunitTests` asserts the titled materials render (title, and a titleless Link falling back to its url), that the list is present, and that the internal note's label never reaches the card — `SchoolCollab.Families.Tests.Unit` **45/45, 0 failed**. **Still open:** the PR-time solution-wide `dotnet test` gate, and independent review (this run was self-applied per Q4(c); `diffs-instructional-materials.patch` is the artifact for it).

**Q2 settled — accepted consequence, recorded (owner-accented 2026-10-10).** W5 retires the Content & Resources add-flow (D1), which leaves `ResourcesSection.AddUrlAsync` with **no UI caller**: reference links are authored as instruction **Link** rows now, and the resources tile is upload-only. This is an accepted consequence, not an oversight — it is recorded here and as a follow-up row in the spec's §10, so the next reader sees the capability change explicitly and knows a future affordance must first decide which collection a reference URL belongs to (D14's question).

**Q3 — independent review: attempted, PARKED at the account layer (2026-10-10).** Dispatched twice through the async team path — `run_00001` (plan review) and `run_00002` (static diff review, this round's patch) — and both died within ~1.6 s with `Unauthorized: Please make sure you're using the latest version of Cline and re-authenticate your Cline account.` The queue accepts and the account layer rejects, so the failure is **not** the delegation mechanism. A parent self-review is deliberately **not** substituted: the parent authored the work, and calling that independent would be the exact self-review-wearing-a-review-costume the skill's seat rule forbids. `diffs-instructional-materials.patch` is frozen as the review vehicle; the trigger to proceed is the owner re-authenticating, after which the same brief re-dispatches unchanged and its findings land as bounded rework on this branch.

**Q6 settled — the next-round sequence (owner-accented 2026-10-10).** §8 WYSIWYG first (the Text row's **body**, vendored Quill 2.x behind the round's own interop shell — unblocked and exactly where W4 left a seam), then D13 (the enum widening: Document on instructions, Audio/Image on resources — which retires the D5 refusal's reason to exist), then the Python portal surface (D11's stated follower), then D14 (the true model merge, which needs its own spec). Each is its own round; none starts before this one clears its commit gate.

**Q1 settled — skill patch text (owner-accented 2026-10-10, "all recommended as-is").** Applied verbatim to `.pi/skills/orchestrator-worker-reviewer/SKILL.md` §Unattended rounds on a follow-up branch once this round lands — one file, all three clauses inside the Unattended section (Q1-a), the pre-handover trigger conditional (Q1-b), the evidence sentence as requirement-not-recipe (Q1-c), this block as the approval artifact (Q1-d), every clause carrying the section's own evidence tag (Q1-e). The text, exactly as accented:

**Clause 1 — degraded dispatch.** A tiered round that degrades to self-applied execution — the dispatch mechanism itself unavailable (runtime auth, agent backend), so the parent runs the worker's scope — is not a solo round and never becomes one. It is a *degraded tier run*: legal only on the owner's by-name pre-authorization for that round, recorded as degraded on the round doc, and it **runs to completion** — it does not park on a decision the inline grill can settle; parking stays the time-box rule's outcome, never a default. Its verdict never reads CLOSED-with-review ("independent review unavailable — it did not pass"), and the round doc must record what evidence substitutes for the missing independent pass — the requirement, not the recipe; the recipe is each round's own lessons. *Verified 2026-10-10 (round `instructional-materials`).*

**Clause 2 — the execution-stage grill (solo · worker · degraded runs; Tiers light, 3 lean, 3 full).** An executor that meets a decision the plan does not settle does not stop and does not guess silently: it holds a `grill-me` round inline — the whole frontier at that stage, numbered, each with a recommendation — and **accepts the recommendations so the work runs to completion**. Every auto-acceptance is recorded with three things: the question, the recommendation taken, and **the rejected alternative's strongest case** — a one-sided record is a rubber stamp waiting to be stamped. The WORKER REPORT is the vehicle in tiered rounds; the **round doc is the home in every tier**, because Tier light has no reviewer to read anything else. The seat rule is scoped to the **plan and acceptance stages** (2b and 5), where self-acceptance is self-review and stays human-gated; at the execution stage the round's downstream passes are the correction mechanism, and they correct only what the record shows them. **Boundary:** the inline grill covers decisions the plan leaves open; a repeated build failure or a defect is a *block*, the time-box/park rule applies unchanged, and reframing a block as a decision is the failure mode this boundary exists to catch. **Hard exclusions do not ride the inline grill:** migrations, contract/wire shape, security and auth, tenant isolation, and deleting a public route park mid-flight unless the owner pre-authorized that round by name. *Verified 2026-10-10 (round `instructional-materials`).*

**Clause 3 — completion and handover.** Step 5's residual grill is held **with the `grill-me` skill** — named, not merely "in grill format" — and its **first block re-surfaces every execution-stage auto-acceptance for re-ratification**: question, recommendation taken, rejected alternative, grouped at the top so the owner re-ratifies against the case that lost — never a mid-list entry an "all as recommended" can skim past. Before a round — CLOSED **or** PARKED — is handed on via its round doc and frozen patch, any open next-round decision in that handover goes through **one `grill-me` round with recommendations**: the next model receives decisions with recommended answers, never a bare "Still open" list. A handover with nothing left to decide needs no grill, mirroring "a clean CLOSED needs no grill". *Verified 2026-10-10 (round `instructional-materials`).*

**W3 gate landed and mutation-proven (2026-10-10).** All five per-path round-trip assertions exist: create (`CreateAssignmentCommandHandlerQuestionsTests` — both owners, plus a null-title control on the unlabelled Link/Audio rows), update-**preserve** and update-**supplied** (the two branches of `UpdateAssignmentCommandHandlerQuestionsTests`' seeds), duplicate (`Duplicate_CarriesInstructionTitles_ForBothOwners`, both owners), and questions-draft **confirm** (`Confirm_CarriesInstructionTitles_FromTheDraft`). Suite **1072/1072, 0 failed** (was 1070 — one new test per re-mint path, the rest assertions inside existing tests). **The gate is proven, not assumed:** mutating the confirm path to `Title: (string?)null` fails exactly one test with *"Expected …Title to be \"Speak it\" because D7: the drafted material's name must survive the confirm re-mint, not be nulled, but found `<null>`"*, and the restore returns the suite to 1072/1072. (A bare `Title: null` in that `Select` will not even compile — CS0411, no type information — so the mutation had to name the type.) **Still open:** the real-Postgres migration test (extend `AssignmentInstructionsResponseKindsMigrationTests`, Q3(a)), then W4/W6 → W5 (+CR-12 narrowing) → W7 → W8 → §0 pointers → acceptance → `diffs-instructional-materials.patch`.



**Worker constraints (repo rules that bite here).**

1. `InstructionEditorList` is **shared with the question dialog** — the page-level changes must not
   alter the dialog's rendering. Use a parameter seam rather than changing the default.
2. **Ids are the bUnit contract** (`.github/copilot/rules/testing.md`): preserve
   `authoring-basics-description`, `authoring-basics-instructions`, `#cq-*`, `instr-*`, and the X-6
   strings; add new ids rather than renaming.
3. Directly-invoked component methods need an explicit `StateHasChanged()` (the rule landed with the
   A6 work) — the new append/focus paths are exactly that shape.
4. Build with `dotnet build SchoolCollab.slnx` after every change; no new NuGet packages; migrations
   are additive only and never edited after creation.

**Risk register.** (i) The layout change touches shared CSS — the question dialog's rendering is the
regression to watch; (ii) the dropzone refusal path must not leave a half-staged file; (iii) the ward
DTO change crosses a context boundary — the projection repository is the only place the ward shape is
built.

## Worker Report

_(filled by the parent from the worker's run.)_

## Review

_(static, diff-only, from `diffs-instructional-materials.patch`.)_

## Acceptance

_(against spec §9, AC-1…AC-8.)_
