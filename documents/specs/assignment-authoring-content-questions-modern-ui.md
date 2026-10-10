# Spec: Assignment Authoring — Content & Resources + Questions & AI (modern UI)

> **Status:** **R1 + R2 + R3 implemented 2026-10-07** on `feat/authoring-content-questions-modern-ui`
> (R1/R2 Light, **R3 Solo by explicit owner election — D28**) — see
> `documents/solution/authoring-content-questions-modern-ui-implementation.md`.
> **R4 (§6A, strand & lesson context) is specced but NOT implemented** — decisions D22–D28 / G29–G35.
> It runs as **Full four-agent (Tier 3)** because it carries an **EF migration and a contract change**;
> those reach the owner explicitly and never run unattended, and G42 gates its start on R3 being
> committed with its PR open.
> **D8/VM-1/VM-2 (read-only View chrome) is WITHDRAWN — a recorded non-goal (G48).** View mode keeps
> today's one-line hints; realising the chrome would require rewriting three invariants that assert
> the editors are *absent* in View mode, which belongs to its own spec.
>
> **Owner:** Assignments context — `AssignmentAuthoring.razor` compartments **5 Content &
> Resources** and **6 Questions & AI**, plus the section components they host.
>
> **Depends on:** `documents/specs/assignment-authoring-compartments.md` (**CLOSED** — behaviour,
> gating and mode contract; this spec refines its §3 rows 5/6 and §4 **UX-8** *presentation only*),
> `documents/specs/grade-detail-modern-ui-plan.md` (spec/anatomy precedent),
> `documents/solution/student-view-page-modernization.md` (in-page modernization precedent).
>
> **Design reference:** `C:\Users\skwar\OneDrive\Pictures\Screenshots\claude_project_question.png`
> (project page) and `…\claude_project_question_1.png` (row hover + kebab menu).
>
> **Affected:**
> - `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor` (+ `.razor.css`)
> - `…/ResourcesSection.razor` (+ `.razor.css`)
> - `…/QuestionGenerationSection.razor` (+ `.razor.css`)
> - `…/QuestionEditorSection.razor` (+ `.razor.css`)
> - `…/QuestionsDraftSection.razor` (+ `.razor.css`)
> - `…/QuestionEditDialog.razor` (+ `.razor.css`) + `QuestionEditModel` / `QuestionEditResult` — **new**
> - `…/QuestionReviewList.razor` (+ `.razor.css`) — **deleted** (its only consumer is its own test)
> - `src/SchoolCollab.Admin.Shared/Components/Chip.razor` (+ `.razor.css`) — **extended** with an opt-in selectable mode
> - `tests/SchoolCollab.Assignments.Tests.Unit/{ResourcesSection,QuestionEditorSection,QuestionEditDialog,QuestionsDraftSection,AssignmentAuthoring}BunitTests.cs`
> - `tests/SchoolCollab.Assignments.Tests.Unit/QuestionReviewListBunitTests.cs` — **deleted**
>
> **Branch (suggested):** `feat/authoring-content-questions-modern-ui`
> **Skills:** `blazor-css-isolation`, `dialog-ui`, `fluentui-dialog-shell`, `input-width-scale`,
> `flex-row-input-alignment`, `fluentui-icons-in-school-collab`, `test-dialog-opener-components`.
> **No** API, contract, domain or schema change. No migration.

---

## 0. Decisions locked in this revision

| # | Decision |
|---|---|
| **D1** | The two compartments are composed as a **two-column row** mirroring the reference: **Questions & AI (composer + question list) in the main column**, **Content & Resources (Context-style card) in the rail**. |
| **D2** | Compartments 1–4 keep their layout; only their **chrome tokens** (radius / hairline / spacing) are aligned, so the page does not read as two designs. *(Amended for R4: compartment 1 Basics gains one **additive** `Strands & lessons` row (CP-1) — no other compartment-1 restructuring.)* |
| **D3** | **Content & Resources** adopts the reference's *Context* card: header (title + count + `+`) → **drop tile** (illustration + helper text, doubling as the `FluentInputFile` drop zone) → two labelled groups, **Files** and **Links**, as hairline rows. |
| **D4** | **Questions & AI** is fully restructured: a **composer hero** for generation, then a **single-line row list** (`#N · text · type badge · ⋮`) whose text opens the question in an **edit dialog** and whose kebab (`RowActionsMenu`) offers **Edit** / **Remove**. **No accordion, no inline expansion.** |
| **D5** | The tile illustration is an **icon composition** (stacked document icons + add badge) — no SVG asset, no hardcoded colours. |
| **D6** | Card chrome is **feature-local**. The shared `Chip` is **extended** (opt-in selectable mode) because it is a real second-consumer need; `SectionCard` is untouched. |
| **D7** | **Presentation + small affordances only.** No change to data flow, gating, save semantics or the fail-closed children load. |
| **D8** | *(**Withdrawn 2026-10-07 — G48**, recorded as a non-goal.)* View mode gets the same chrome, read-only, rendered by the **shared** question-list component. `QuestionReviewList` is deleted regardless (dead code). |
| **D9** | The new row shares row 1's **`320px–460px`** rail track, so the page has one column grid. |
| **D10** | Rail item rows are **two-line** (name / `size · badge`) with actions on the right. |
| **D11** | Count / difficulty stay **inline compact number fields** in the composer. *(Superseded by D14 for R3 — the knobs move into the dialog.)* |
| **D12** | The draft surface is a **collapsed "Draft preview" disclosure**, auto-expanded when a draft is staged. |
| **D13** | The row kebab lives in the DOM and is revealed on **hover + `focus-within`** — never hover-only. |
| **D14** | The generation knobs (count · difficulty · type mix) move **out of the composer into a `QuestionPromptDialog`**, which composes a coherent, line-shaped prompt narrative into the top textarea. The composer keeps the textarea, a read-only summary line and a **Configure** button. |
| **D15** | **Authority unchanged:** the structured request fields (`QuestionCount`, `Types`, difficulty counts, `ResourceTexts`, `TopicId`/`TopicName`) keep driving generation; the composed text rides `PromptOverride` as *additional teacher guidance* (the server's own framing). **No AI-host/contract change.** |
| **D16** | Difficulty stays the **three counts** (easy / medium / hard) — no level preset. |
| **D17** | **Overwrite discipline — replace, not merge:** the composed narrative **replaces** the textarea only when it is empty or still matches the **deterministic template** (PB-6's complete structural match); otherwise the dialog asks (`ShowConfirmDialogAsync`, destructive) before replacing. The author's existing wording is **not merged** — on confirm it is replaced. Cancel changes nothing. No transient "last composed" value is stored or consulted: the template match *is* the marker. |
| **D18** | **Context composition:** the narrative carries the assignment's **subject** — always, with no omission rule (D17's replace semantics make duplication impossible) — its **grade target(s)** from `GradeLevel.Level` (sorted, `Grade 1, Grade 2`; `Grades 1–4` when contiguous and >2), and the **names** of the attached files/links when any exist. It is refreshed at Generate time when those inputs changed **and** the text still matches the template (QA-23). |
| **D19** | **`PromptLocked`:** the knobs stay editable; the prompt-composition/replace behaviour is disabled with the existing lock reason, because the server strips `PromptOverride` (`AssignmentQuestionGenerationService:82`). A locked org therefore gets **no** teacher grade context — correct per the lock's intent. |
| **D20** | `QuestionsDraftSection` opens the **same** dialog and uses the **same** composed prompt. |
| **D21** | **No AI parse.** Re-hydration is **two-sourced and never transient**: the **difficulty** defaults come from the **persisted** `Model.Difficulty*Count` (S13 — difficulty *is* on the contract); the **question count + type mix** come from a narrow deterministic parse applied **only on a complete structural match of the template**, otherwise the composer's current values. Hand-written prose is never reverse-engineered, and no "last composed" field exists anywhere. |
| **D22** | **Strands & lessons are an assignment property, picked in Basics** (W1 = C) — beneath the Subject row, not in the prompt dialog. |
| **D23** | **They are persisted** (X1 = B): link rows on the assignment, surviving reload and round-tripping through create/update/duplicate. Carries an **EF migration + contract change** ⇒ R4 runs Tier 3 and the migration reaches the owner. |
| **D24** | **Wiring:** picked **strands** ride `QuestionGenerationRequest.ContextStrands` (structured — survives a tenant lock); picked **lesson names** ride the composed narrative (and are therefore dropped for a locked org, per D19). |
| **D25** | **Selection shape:** multi-select strands; the lessons list follows the subject and is narrowed to the picked strands when any are picked. Both optional. |
| **D26** | **Optionality is visible:** explicit empty states (no subject → disabled with reason; no strands/lessons → muted note; nothing picked → "the AI uses the whole subject"), picks **reloaded and cleared on a subject change**, and the picked names shown in the composer's config summary (QA-3). |
| **D27** | The composed narrative names the picks, so the prompt text remains the durable record of what steered a given generation even after the picks change. |
| **D28** | **Mode rule:** a round carrying a migration is **Full four-agent (Tier 3)**. **R3 = Solo by explicit owner election (2026-10-07)** — recorded as a deliberate deviation from the "Solo = trivial/non-behavioural only" guidance, because the owner picks the mode; R4 = Tier 3. |

---

## 1. Goal and design reference

The authoring page today stacks five full-width card sections; compartments 5 and 6 are unstyled
toolbars and paginated editor cards with no visual relationship to each other. This spec gives
them the **Claude project page** treatment: a composer + list against a context rail, in a light,
low-contrast, whitespace-led aesthetic.

```
┌───────────────────────────────────────────────┬──────────────────────────────┐
│  Questions & AI  (main column)                │  Content & Resources (rail)  │
│  ┌─────────────────────────────────────────┐  │  ┌────────────────────────┐  │
│  │ composer: prompt textarea               │  │  │ Content & Resources  + │  │
│  │ ●MCQ  ○T/F  ●Short    10 ▸Generate      │  │  │  ┌──────────────────┐  │  │
│  └─────────────────────────────────────────┘  │  │  │  ▤▤▤  +   (tile) │  │  │
│  5 question(s)                          + Add │  │  │  Add files…      │  │  │
│  ── #1 What is 3/4 of 20?       [MC]      ⋮   │  │  └──────────────────┘  │  │
│  ── #2 Name the process…        [SA]      ⋮   │  │  Files  2 file(s) attached│
│  ── #3 True or false…           [TF]      ⋮   │  │  ── ▤ syllabus.pdf       │  │
│  ‹ 1 2 ›                                       │  │       1.2 MB  ▤ badge   │  │
│  ▸ Draft preview                               │  │  Links  1 link(s)        │  │
└───────────────────────────────────────────────┴──────────────────────────────┘
```

Aesthetic: **card radius ~14px, 1px hairlines, a barely-filled inner tile, hairline-separated
rows with a full-bleed hover band, pill chips, and exactly one accent colour (the primary
action).**

---

## 2. Current state (what changes)

| Area | Today | Source |
|---|---|---|
| Layout | Compartments 5 and 6 are full-width `section.authoring-compartment`s stacked after the Basics/Targets grid | `AssignmentAuthoring.razor` L350–431 |
| Content & Resources | `FluentInputFile` toolbar + URL input + attachment list; empty state is a `FluentMessageBar Intent="Info"` | `ResourcesSection.razor`, 330 L |
| Questions & AI | `QuestionGenerationSection` (prompt + count/difficulty/type + Generate) → `QuestionEditorSection` (5-per-page editor cards) → `QuestionsDraftSection` | 316 + 210 + 348 L |
| Read-only View | Both compartments render a single hint line | `AssignmentAuthoring.razor` |
| Shared pill | `Chip.razor` — a `FluentBadge`-based chip for *selected/added* items: `Label` + `Subtitle` + optional `OnDismiss`. **No toggle mode, no `aria-pressed`** | `src/SchoolCollab.Admin.Shared/Components/Chip.razor` |
| Dead code | `QuestionReviewList.razor` + its suite exist, but nothing in `src` renders it | repo-wide grep |

**Guarded contracts** (see §8/§10): the per-compartment `h3.authoring-compartment-title`, the
`#authoring-content` / `#authoring-questions` anchors, `#authoring-content-reason` /
`#authoring-questions-reason`, the component **types** `ResourcesSection`, `QuestionEditorSection`,
`QuestionGenerationSection` (asserted via `FindComponents<T>()`), and three user-facing strings.

---

## 3. Design language (DL)

Declared as **component-local custom properties on the new grid wrapper** (§4.1) in
`AssignmentAuthoring.razor.css`, and referenced as `var(--cq-…, <fallback>)` by each child's
isolated stylesheet — one place to retune the composition.

| # | Contract |
|---|---|
| **DL-1** | **Tokens only.** Colours/fills/borders come from FluentUI design tokens (`--neutral-layer-1/2`, `--neutral-stroke-rest`, `--neutral-stroke-divider-rest`, `--neutral-stroke-strong-rest`, `--neutral-foreground-rest`, `--neutral-foreground-hint`, `--neutral-fill-rest`, `--accent-fill-rest`, `--foreground-on-accent`). **No hex literals.** Dark theme follows automatically. |
| **DL-2** | **Radii.** `--cq-radius-card: 14px`, `--cq-radius-tile: 10px`, chips `999px`. |
| **DL-3** | **Hairlines, not boxes.** Card border `1px solid var(--neutral-stroke-divider-rest)`; list rows are separated by a 1px hairline on the row's bottom edge — never individual bordered cards. |
| **DL-4** | **Row rhythm.** `--cq-row-pad: 14px 4px`; full-bleed hover band (`var(--neutral-fill-secondary-hover)`); `transition: background-color 120ms ease`; no elevation/shadow anywhere. |
| **DL-5** | **Type.** Group labels `0.8125rem` muted; row titles `0.9375rem` **normal weight** (the reference's rows are regular, not bold); numbers `font-variant-numeric: tabular-nums`. |
| **DL-6** | **Inner tile.** Fill `var(--neutral-layer-2)`, radius `--cq-radius-tile`, centred content, helper text `max-width: 320px`, muted. The tile is the drop zone and carries the primary "Upload files" affordance. |
| **DL-7** | **Spacing.** `--cq-gap: 12px` inside a card; `16px` card padding; the existing `.authoring-layout` `gap` between columns. |
| **DL-8** | **Motion guard.** Every transition is disabled under `@media (prefers-reduced-motion: reduce)`. |
| **DL-9** | **Isolation.** Styles live in the owning `.razor.css`; no `<style>` blocks, no inline `style=` (except a computed custom property). FluentUI internals are reached with `::deep`. |
| **DL-10** | **Pill two-state look** (the reference's Chat/Cowork): **selected = filled `--neutral-layer-2`, no border; unselected = hairline border `--neutral-stroke-divider-rest`, transparent fill.** This is the `.chip--toggle` variant (§5 CH-1). |

---

## 4. Layout (LA)

### 4.1 The composition row

```razor
<div class="authoring-layout authoring-layout--cq">
    <section id="authoring-content" class="authoring-compartment authoring-compartment--flush">…</section>
    <section id="authoring-questions" class="authoring-compartment authoring-compartment--flush">…</section>
</div>
```

**LA-1** — Grid areas place **Questions & AI left (main)** and **Content & Resources right
(rail)**, while **DOM order stays `content` then `questions`**, so the existing
`section.authoring-compartment`-order assertion and the jump-nav order both survive unchanged:

```css
.authoring-layout--cq {
    grid-template-columns: minmax(0, 1fr) minmax(320px, 460px);   /* D9: same track as row 1 */
    grid-template-areas: "questions content";
}
.authoring-layout--cq > #authoring-content   { grid-area: content; }
.authoring-layout--cq > #authoring-questions { grid-area: questions; }
```

**LA-2** — `authoring-compartment--flush` resets the legacy frame (padding/background/border →
none), exactly as the existing `--right` modifier does, because the new card chrome is the single
visible frame.

**LA-3** — Below `1100px` the row collapses to one column, stacked **Questions then Content**
(`grid-template-areas: "questions" "content"`).

**LA-4** — The compartment `<h3 class="authoring-compartment-title">` renders in **every** branch.
For Content & Resources the **live** header belongs to `ResourcesSection` (see CR-1); for the
disabled / read-only / fail-closed branches the page renders its own header, so X-1 holds and no
`@ref` delegation is needed.

### 4.2 Card chrome

`authoring-compartment--flush` sections gain a `cq-card` modifier (radius `--cq-radius-card`,
hairline border, `--neutral-layer-1` fill, `16px` padding) plus a header band (`.cq-header`) that
is a flexible row — `h3` left, count chip and trailing actions right.

**LA-5** — Compartments 1–4 are **not** restructured; only their `border-radius` and the
compartment gap are aligned to `--cq-radius-card`.

---

## 5. Content & Resources (CR)

Header · drop tile · **Files** · **Links**.

| # | Contract |
|---|---|
| **CR-1** | **Header (owned by `ResourcesSection`)** — `h3.authoring-compartment-title` "Content & Resources" + a muted item count + a trailing `FluentButton` (`Appearance.Stealth`, `Size.Small`, `IconStart="@FluentIcons.Add"`, `aria-label="Add resource"`) that **toggles the link-input row** (revealed + focused, hidden by default). The page renders an equivalent static header on the disabled / read-only / fail-closed branches. |
| **CR-2** | **Drop tile** (`#cq-context-tile`) — `--neutral-layer-2` fill, `--cq-radius-tile`, centred: an `aria-hidden` icon composition (two overlapping `Document` icons + a small `Add` badge); the muted helper line **"Add PDFs, documents, or other text to reference in this assignment."**; and a `FluentButton` labelled **"Upload files"**. |
| **CR-3** | The tile **is** the drop zone: `FluentInputFile` keeps `DragDropZoneVisible="true"`, `Mode="InputFileMode.Stream"`, `Multiple="true"`, and the existing `MaximumFileCount` / `MaximumFileSize` / `Accept` / callbacks from `AttachmentUploadPolicy`. The tile's button is the input trigger via **`FluentInputFile.AnchorId`** (verified present in the pinned 4.14.2) — no JS, no hidden-element click. |
| **CR-4** | The helper line **"No resources attached yet."** is retained as the tile's secondary muted line when both groups are empty (replacing the `FluentMessageBar Intent="Info"`), so the empty state reads as an invitation. |
| **CR-5** | **Files group** (`#cq-files`) — sub-label "Files" + the **existing string** `N file(s) attached`; one row per `Model.Attachments`. |
| **CR-6** | **File row** (two-line, D10): line 1 = `Document` icon + filename (ellipsis + `title`); line 2 = `AttachmentUploadPolicy.FormatFileSize` (tabular, muted) + the extraction `FluentBadge` **only when the status is not `Succeeded`** (unchanged `ExtractionStatusText` wording). Trailing actions right: **Re-read** (persisted rows only) + `Stealth` remove. |
| **CR-7** | **Links group** (`#cq-links`) — sub-label "Links" + the **existing string** `N link(s)`; one row per `Model.ResourceUrls` (`Link` icon · display name or URL, ellipsis + `title` · remove). |
| **CR-8** | The link **input row** (`#cq-add-link-input`) sits at the foot of the Links group and is **hidden until CR-1's `+` reveals it**; it then keeps today's behaviour verbatim — `FluentTextField Class="w-4"`, an accent **Add link** button disabled on blank, and the duplicate warning. |
| **CR-9** | Behaviour and seams unchanged: `StageFileAsync`, `AddUrlAsync`, `RemoveUrlAtAsync`, `RegenerateExtractionAsync`, `OnFileCountExceededAsync`, the staging guard, the typed-error mapping, and the `Changed` callback. |
| **CR-10** | Error/warning `FluentMessageBar`s render **inside the card**, beneath the tile, keeping `MessageIntent.Error` / `Warning` (they are not cosmetic hints). |
| **CR-11** | The rail is narrow: every truncating cell carries a `title` with the full value; nothing wraps a control wider than `min-width: 0`. |
| **CR-12** | `ResourcesSection` **keeps its type name and public method names**; only markup and `.razor.css` change. |

### Chip extension (CH)

| # | Contract |
|---|---|
| **CH-1** | `Chip` gains an **opt-in selectable mode**: `Selected` + `OnToggle`. When `OnToggle` is set the chip renders as a real `<button type="button" class="chip-toggle" aria-pressed="@(Selected ? "true" : "false")">` carrying the label; the existing read-only and dismissible (`FluentBadge`) modes are untouched and remain the default. **Never bind `aria-pressed` to the `bool` directly** — Blazor renders a bool attribute as a *bare* attribute when true and omits it when false, which made the control announce "not pressed" in both states (fixed 2026-10-07; pinned by `ChipBunitTests`). |
| **CH-2** | `chip--toggle` uses the **DL-10** two-state look; transition guarded by DL-8. Keyboard activation, focus ring and contrast come from the native button. |
| **CH-3** | No existing `Chip` call site changes behaviour — the new mode is parameter-gated, and the `EntityGrid` / picker consumers keep the badge rendering. |

---

## 6. Questions & AI (QA)

**Composer hero** → **question list** → **Draft preview** panel.

### 6.1 Composer (`QuestionGenerationSection`)

Composer hero (prompt + Configure + Generate) → question list → Draft preview.

| # | Contract |
|---|---|
| **QA-1** | **Composer card** (`#cq-composer`) — `--cq-radius-card`, hairline border, `16px` padding, no visible field label, but the prompt keeps an **accessible name** (visually-hidden `<label for="cq-composer-prompt">`). |
| **QA-2** | Prompt textarea (`#cq-composer-prompt`, `@bind-Value="Model.AiPromptOverride"`): `Rows="3"`, `MaxLength="4000"`, placeholder "Describe the questions you want — e.g. '10 mixed questions on fractions, 40% hard'", existing `PromptLocked` disable + tooltip. It is the **single prompt string**: the composition writes the narrative into it and (per D17) **replaces** the author's wording after the confirm path — the two do **not** coexist. |
| **QA-3** | **Config summary** (`#cq-config-summary`) — a read-only line so the config is visible without opening the dialog, e.g. `10 questions · 4 easy / 4 medium / 2 hard · Multiple choice, Short answer` (each part dashed when unset). It is the visible guard against **a composed narrative** and the request disagreeing (D15) — it does **not** constrain hand-written prose, which was already free to differ from the structured knobs before R3, because the structured fields are authoritative (G36). The summary and the narrative are **two renderings of the same parts/helper**, not one literal string — see PB-4. |
| **QA-4** | **Configure** (`#cq-configure`) — `Appearance.Outline` + `IconStart="@FluentIcons.Settings"`, opens `QuestionPromptDialog` (§6.1a). Disabled **with reason** (`#cq-configure-reason`) when no subject is selected, mirroring the composer's own "Select a subject before generating questions." |
| **QA-5** | **Generate** stays the composer's primary action (`Appearance.Accent`) on the trailing edge of a wrap-capable footer row that follows the repo's multi-input top-alignment rule (`flex-row-input-alignment`). |
| **QA-6** | Generate / `Cancel` / progress and the three warning bars (generation error, URL-budget, attachment-budget) keep their current strings and intents, inside the composer card under the footer. |
| **QA-7** | Gating untouched (UX-19): when the gate is off the page still renders `#authoring-questions-reason` with `QuestionGenerationGate.DisabledHint`, and the composer renders disabled with its tooltip — never hidden. |
| **QA-22** | The count / difficulty / type controls **no longer render inline** (D14) — they live in the dialog; the composer shows only the summary (QA-3) and Configure (QA-4). *(Id renumbered 2026-10-07: §6.2 already owns QA-8.)* |
| **QA-23** | **Composition timing (D18):** the composed narrative is written (i) when the dialog confirms, and (ii) again immediately before Generate, when the subject / grade targets / resource set changed since the text was composed **and** the text still matches the deterministic template (PB-6). "Unedited" means **template-matched**, not "equal to a remembered value", so the rule holds across a page reload where no transient state exists. A non-template text is never overwritten. *(Id renumbered 2026-10-07: §6.2 already owns QA-9.)* |

### 6.1a Prompt builder dialog (`QuestionPromptDialog`)

| # | Contract |
|---|---|
| **PB-1** | New `QuestionPromptDialog.razor` + `.razor.css` + `QuestionPromptModel` / `QuestionPromptResult` (Assignments.Application, siblings of `QuestionEditDialog`), `@inherits DialogShellBase<QuestionPromptModel, QuestionPromptResult>`, **`DialogSize.Medium`**, opened from the composer's Configure (QA-4) and from the draft panel (D20). |
| **PB-2** | **Editable fields:** Number of questions (1–30); Easy / Medium / Hard counts (0–30, the existing three — D16); the **type-mix chips** (shared `Chip`, selectable mode — CH-1). The "all off" state keeps today's meaning (`types = null` → the server's balanced default) and shows the existing helper line. |
| **PB-3** | **Read-only context block:** `Subject: {TopicName}` (or the disabled reason when none), `Grade level: Grade 1, Grade 2` from `GradeLevel.Level` sorted ascending (`Grade level: Grades 1–4` when contiguous and more than two; `Grade level: —` when the assignment targets no grade), and `Grounding: {file/link names} (N)` when resources exist (D18). The label spelling **matches the narrative line** (PB-4). The block is a preview of what the composition will use — it is never editable here. |
| **PB-4** | **Composition — normative skeleton (the predicate's source of truth).** A deterministic, line-shaped narrative built from the knobs + context:
```
Generate 10 questions for the subject "Photosynthesis".
Grade level: Grade 3.
Difficulty mix: 4 easy / 4 medium / 2 hard.
Include: Multiple choice, Short answer.
Grounded on: syllabus.pdf, example.com/article.
```
The summary line (QA-3) and this narrative are **two renderings of the same parts and the same helper** — not one literal string — so they cannot disagree in *content*; only their punctuation differs. Label spellings are shared (`Grade level:`). **The skeleton is normative — the single predicate source for PB-5, PB-6 and QA-23:**

- **Order is fixed** as above, every line is `Label: value.`, and each label is spelled exactly as shown.
- **Mandatory lines:** (1) `Generate {N} questions for the subject "{TopicName}".` — `N` is the count knob — and (3) the difficulty line, which always renders, as `0 easy / 0 medium / 0 hard` when all three counts are zero.
- **Conditional lines, never omitted:** (2) renders `Grade level: —` when the assignment targets no grade; (4) renders `Include: balanced mix.` when every type chip is off (`types = null`, the server's balanced default) and otherwise lists the selected names; (5) renders only when resources exist.
- **Type names** use one canonical spelling — `Multiple choice`, `True / false`, `Short answer` — produced by `EnumHelper.GetDescription` after R3 adds `[Description]` attributes to **`QuestionTypeDto`**, the enum every UI label already renders through (the domain `QuestionType` is never used by the Application layer). `ContractTypes.cs` already carries **31** such attributes and every sibling DTO enum has them, so this is the consistent repair. **Attributes only — no field, type or serialization change** (enum wire shape is name/number and is unaffected by metadata). **R3 sub-task:** add the three attributes and re-point tests that assert the raw member names.
- **R4 extends this skeleton** by appending `Strands: {names}.` and `Lessons: {names}.` when picks exist (CP-9). Each line renders **independently** when its own collection is non-empty, always in that order, **immediately after the resource line's position** — i.e. directly after `Include:` when no resources exist. The same parser must accept all four combinations (strands ± lessons × resources ±), or an R4-composed prompt would fail PB-6/QA-23. |
| **PB-5** | **Overwrite discipline (D17) — replace, not merge:** on confirm, if `AiPromptOverride` is empty **or still matches the deterministic template** (PB-6), it is replaced with the new narrative; otherwise a destructive confirm is shown before replacing, so hand-written guidance is never silently lost. **The confirm runs CALLER-SIDE** — immediately after the dialog closes, via `ShowConfirmDialogAsync` — because `dialog-ui` forbids nested dialogs and a `ConfirmDialog` inside a shell dialog is exactly that. A declined confirm still keeps the author's **knob** edits (only the text write is skipped). **The author's wording is not merged into the result** — on confirm it is replaced; cancel changes nothing. No transient "last composed" value is stored or consulted. |
| **PB-6** | **Re-hydration (D21) — reload-proof, no transient marker.** (i) **Difficulty** opens from the **persisted** `Model.DifficultyEasy/Medium/HardCount` (S13 — difficulty *is* on the assignment contract), so a reload cannot desync it. (ii) **Question count + type mix** (transient, not on the contract) are recovered by running the narrow deterministic parse (`N questions`, the type names, the grade line) against `AiPromptOverride` and applying it **only on a complete structural match of the template**; on any partial match the dialog keeps its current/default values (count 5, balanced types). Hand-written prose never matches the template, so it is never reverse-engineered. Because the trigger is a **template match rather than equality with a remembered value**, behaviour is identical before and after a reload — and PB-5/QA-23 use the same predicate. |
| **PB-7** | **No AI call** anywhere in the dialog or the composition path (D21) — the narrative is a template, not a model output. |
| **PB-8** | **`PromptLocked` (D19):** the knob fields stay enabled (they still reach the model); the prompt-composition and replace behaviour is disabled with the existing lock reason ("Your organization has locked the AI prompt."), because the server discards `PromptOverride` for a locked org. |
| **PB-9** | **Authority (D15):** the request keeps sending `QuestionCount` / `Types` / difficulty counts / `ResourceTexts` / `TopicId`·`TopicName`; the narrative rides `PromptOverride` as *additional teacher guidance*. No AI-host, contract or schema change. |
| **PB-10** | **Persistence (A4):** the written prompt goes through the existing `AiPromptOverride` field — 4000-char cap (the dialog enforces it visibly) — so the form goes dirty and the text persists/round-trips on save. The cap is enforced **visibly on both surfaces**, including the draft panel's transient prompt (PB-11), even though only `AiPromptOverride` is persisted. |
| **PB-11** | **Draft panel post-R3 shape (D20).** `QuestionsDraftSection` keeps its own inline generation controls (QA-20) **and** gains a Configure trigger opening the **same** `QuestionPromptDialog`, plus its own summary line. Its prompt is its own **transient `_prompt`**, not `Model.AiPromptOverride`: composing or generating there **never dirties the form** (nothing is persisted), the cap is still enforced visibly (PB-10), and PB-5's replace-vs-confirm and QA-23's Generate-time refresh apply to **that surface's own** prompt. Only the composer's Configure writes the persisted `AiPromptOverride`, and only that write dirties the form (G28). On the **draft** surface the dialog opens from **that surface's own** transient knob values; PB-6(i)'s persisted-count source applies **only** to the composer. |

### 6.2 Question list (`QuestionEditorSection`)

| # | Contract |
|---|---|
| **QA-8** | **List header** — the **existing string** `N question(s)` + the always-available **Add question** button (`#cq-add-question`, outline + `IconStart="@FluentIcons.Add"`) in every gate state. |
| **QA-9** | **Rows, not cards** (`#cq-question-list`, `#cq-qrow-{i}`) — a single-line, hairline-separated list: `#N` (muted, tabular) · question text as the **open target** (`#cq-qrow-{i}-open`, a `FluentAnchor Appearance="Hypertext" Href="#" OnClick` — the `SectionCard` item pattern) with a one-line clamp and `title` · trailing `FluentBadge` with `EnumHelper.GetDescription(row.Type)` · a `RowActionsMenu` kebab (`#cq-qrow-{i}-actions`, `UseMenuService="false"`). No accordion, no inline fields (D4). |
| **QA-10** | **Kebab actions** — **Edit** (opens the dialog, QA-11) and **Remove** (`RowAction.Callback(..., destructive: true)`, so the repo's shared confirmation prompt fires). |
| **QA-11** | **Editing is a dialog** (D4 / grill 1a). `QuestionEditDialog` is opened with `ShowShellDialogAsync<QuestionEditDialog, QuestionEditModel, QuestionEditResult>` at **`DialogSize.Medium` (560px)**, and holds the existing per-type fields **verbatim** — MC option rows with the correct-option radio group and add/remove option, T/F radios, the Short-answer model-answer textarea, and the Type dropdown whose change still runs `QuestionEditorRow.ApplyTypeChange`. The dialog edits a **working copy**; on Save the result is applied to the row, on Cancel nothing changes. Only one dialog at a time; no nested dialogs (`dialog-ui`). |
| **QA-12** | **Row text / kebab Edit / double-click-free**: both the text anchor and the kebab's Edit open the same dialog with that row's values. (The row itself is not a button — a nested button inside a clickable row is invalid.) |
| **QA-13** | **Add** appends a blank row and opens the dialog for it; Cancel leaves the blank row, which is exactly the state Add produced before this change (the submit gate already rejects it). |
| **QA-14** | **Pagination retained** (`FluentPaginator`, `AssignmentEditFormModel.QuestionPageSize = 5`) using `GetQuestionPage` / `QuestionPageCount` unchanged. |
| **QA-15** | The empty state keeps the **existing string** "No questions yet. Add a question to get started." as a muted block inside the list region (not a `FluentMessageBar`), with the Add affordance present. |
| **QA-16** | **Kebab visibility (D13)** — always in the DOM, visually revealed on row hover **and** `:focus-within`; never the only way to reach information. |
| **QA-17** | `QuestionEditorSection` **keeps its type name** and its public seams (`OnAddQuestionAsync` / `OnRemoveQuestionAsync`); `QuestionGenerationSection` likewise. Section-internal state that is no longer needed (pagination `_currentPage` stays; expansion state does not exist) is removed. |

### 6.3 Draft preview (`QuestionsDraftSection`)

| # | Contract |
|---|---|
| **QA-18** | The staged-draft surface is a **collapsed disclosure** (`#cq-draft-panel`, `<button aria-expanded>` + labelled region) titled **"Draft preview"**, **auto-expanded when a draft is staged** (D12). Header row carries `Confirm & replace` / `Discard draft`. |
| **QA-19** | Draft rows reuse the QA-9 row chrome (index · text · type badge), read-only. |
| **QA-20** | **Ownership does not move.** `QuestionsDraftSection` keeps its own generation controls and its `StageQuestionsDraft` / `ConfirmQuestionsDraft` / `DiscardQuestionsDraft` calls; the duplicated prompt/count/type controls are **not** deduplicated this round (§9). |
| **QA-21** | The informational line ("Regenerate questions as a draft. Confirming replaces every current question.") is retained as the panel's descriptive text. |

---

## 6A. Context picks — strands & lessons (R4)

> **Round R4 — Full four-agent (Tier 3)** because it carries an **EF migration** and a
> create/update/duplicate **contract change**. Per `AGENTS.md` the migration reaches the owner
> explicitly and must never run unattended; the exact storage shape (CP-5) is settled by R4's plan
> and independently reviewed before implementation.

**Placement (D22 / W1 = C):** the pickers live in **compartment 1 Basics**, directly beneath the
**Subject** row — the author picks the subject first, then narrows it. They are *not* in
`QuestionPromptDialog`.

| # | Contract |
|---|---|
| **CP-1** | **Placement** — a `FormRow Label="Strands & lessons"` beneath Subject in Basics, rendered in Create/Edit; View mode renders the resolved names read-only (blocked by the D8 deferral, so read-only picks follow that decision). Wrapped in a feature-local `ContextPicksSection.razor` (+ `.razor.css`) so the Basics spine stays title-less. |
| **CP-2** | **Pickers** — multi-select **strands** for the selected subject via `ListTopicStrandsAsync(topicId)`, **filtered to `IsLesson == false` / `ParentStrandId == null`** (lessons are `TopicStrand` rows too — `ListTopicStrands` returns them, so an unfiltered picker would duplicate the lessons list), and multi-select **lessons** via `ListTopicLessonsAsync(topicId, strandId?)` narrowed to the picked strands when any are picked (D25/W3). Both optional. |
| **CP-3** | **Empty / absent states** (D26) — no subject → the pickers render **disabled with reason**; a subject with no strands/lessons → a muted note ("This subject has no strands yet."); nothing picked → "No strand selected — the AI uses the whole subject." |
| **CP-4** | **Subject change** reloads the lists and **clears** the picks (a strand belongs to a subject), and marks the form dirty. |
| **CP-5** | **Persistence (D23/X1 = B)** — the picks are an assignment property: persisted link rows that survive reload and round-trip through create/update/duplicate. **[R4 plan decision — PINNED 2026-10-07]** the storage shape is **PostgreSQL `uuid[]` columns** (`ContextStrandIds` / `ContextLessonIds` on `assignments`; no join table, no new entity — OD-1, §13.5); the EF migration and the contract fields (`CreateAssignmentRequest`, `UpdateAssignmentRequest`, **`AssignmentAuthoringChildrenDto`** — the `/authoring` children read, **not** `AssignmentSummaryDto`, which would force dead correlated projections into the list/ward/sweep reads for no consumer — OD-2, §13.5, plus the duplicate handler) land together. **[R4 plan checklist]** the picks must also join the **unsaved-changes fingerprint** — `CaptureSaveSnapshot`'s completeness rule makes a payload field without its mix read as *clean* — and the guard test `SaveSnapshot_MatchesTheSerializedPayload_OnEveryDirtyRelevantField` must be updated. |
| **CP-6** | **Generation wiring (D24)** — the picked strands ride `QuestionGenerationRequest.ContextStrands` **structurally** — its elements are the picked strands' **display names** (the field is `IReadOnlyList<string>?`), matched 1:1 to the picked ids; the picked **lesson names** are written into the composed narrative (and are therefore stripped for a locked org, per D19). |
| **CP-7** | **Summary (D26)** — the picks appear in the composer's config summary (QA-3), e.g. `… · Strands: Fractions · Lessons: Equivalent fractions`. |
| **CP-8** | **Draft parity** — `QuestionsDraftSection`'s generation uses the same picks and the same composed narrative (D20). |
| **CP-9** | The composed narrative names the picks (`Strands: …` / `Lessons: …`) immediately after the resource line's position, extending PB-4's skeleton (same parser), so the prompt text stays the durable record of what steered a generation even after the picks change (D27). |
| **CP-10** | **Contract discipline** — the change is additive: an existing assignment round-trips unchanged and the migration is additive (no backfill beyond the new nullable columns / empty join rows). **Update wire semantics are pinned to the repo's real collection precedent** — the questions/attachments full-replacement rule in `UpdateAssignmentCommandHandler` ("When the inbound collection is null we preserve the current children; a non-null but empty collection clears the children"): **null = preserve** the persisted set, **empty list = clear** all picks, **non-empty = replace**. *(`Targets` is NOT the precedent — `SetTargets` throws on an empty list, `Assignment.cs:314`.)* Without this, "remove every pick" is inexpressible. |
| **CP-11** | **Dangling picks.** A strand/lesson deleted in the Students context leaves an id the client list no longer resolves. Unresolved picks render as a `(removed)` chip in Basics, are **excluded** from the composed narrative, the summary and `ContextStrands`, and are **filtered out of the save payload** on the next save — a payload filter, *not* a model mutation, so merely resolving a deleted id never dirties the form nor enters the fingerprint (CP-5) — they never block a generation. |

---

## 7. Read-only View mode (VM) — **WITHDRAWN (recorded non-goal, G48)**

> **Withdrawn 2026-10-07 (G48).** Not implemented, and no longer planned inside this spec. View mode
> keeps today's one-line hints. The two existing invariants
> (`Edit_LaterStatus_DegradesToReadOnlyView`, `View_LaterStatus_RendersAllCompartmentsReadOnly`)
> assert `QuestionEditorSection` / `ResourcesSection` are **absent** in View mode; rendering them
> read-only is a deliberate contract change that belongs to its own spec (rewrite those assertions to
> read *read-only* rather than *absent*). `QuestionReviewList` was deleted regardless (dead code).
>
> The contracts below are kept only as the design sketch such a decision would start from.

| # | Contract |
|---|---|
| **VM-1** | In View mode (UX-9/UX-12) both compartments render the **same card chrome** without interactive affordances: Content shows the header + counts + read-only file/link rows (no tile, no upload, no link input, no remove/re-read); Questions shows the composer's read-only summary (the prompt text, if any) and the question rows **without** the kebab. |
| **VM-2** | The **shared** question-list component renders the read-only rows (a `ReadOnly` parameter), so edit and read-only chrome cannot drift. `QuestionReviewList.razor`, `QuestionReviewList.razor.css` and `QuestionReviewListBunitTests.cs` are **deleted** (D8); its valuable assertions — type badges, the correct-option marker, the model answer — are re-homed onto the shared list's read-only tests. |

---

## 8. Invariants (X)

| # | Invariant — must survive the redesign |
|---|---|
| **X-1** | Exactly one `h3.authoring-compartment-title` per compartment in **every** mode/branch (Create, Edit, View, offline gate, fail-closed, read-only hints) — for Content & Resources that means the child renders it live and the page renders it in the non-live branches. |
| **X-2** | `#authoring-content`, `#authoring-questions`, `#authoring-content-reason`, `#authoring-questions-reason` keep their ids and branches. |
| **X-3** | `FindComponents<ResourcesSection>()`, `<QuestionEditorSection>()`, `<QuestionGenerationSection>()` still resolve; they render **nowhere** on the fail-closed children-read path, and `QuestionEditorSection` renders in Create. |
| **X-4** | Jump-nav text and href order unchanged: `Basics · Targets & audience · Rules · Content & Resources · Questions & AI` → `#authoring-basics · #authoring-targets · #authoring-rules · #authoring-content · #authoring-questions`. |
| **X-5** | Save semantics, `Changed` callbacks, the children full-replacement / fail-closed rule (UX-21), and every API call are untouched. *(Scoped to R1–R3: R4's §6A deliberately adds contract fields and a `CaptureSaveSnapshot` mix.)* |
| **X-6** | Retained user-facing strings: "Upload files", "No resources attached yet.", "N file(s) attached", "N link(s)", "No questions yet. Add a question to get started.", "Add question", "N question(s)", the extraction-status labels, and `ChildrenUnavailableReason`. |
| **X-7** | Accessibility: the drop tile and every icon-only button have an accessible name; chips are real toggle buttons with `aria-pressed`; the row kebab is reachable by keyboard (D13) and the row's open target is a real anchor; the draft disclosure exposes `aria-expanded`/`aria-controls`; the dialog traps focus and returns it; contrast meets WCAG 2.1 AA in both themes; nothing depends on hover alone. |
| **X-8** | No `<style>` blocks, no inline `style=` (except computed custom properties), no hardcoded colours; each changed component keeps a colocated `.razor.css`. |
| **X-9** | **No accordion / no inline question expansion** — the row + kebab + dialog is the settled shape (D4); reintroducing inline fields in the row contradicts this spec. |
| **X-10** | `Chip`'s existing modes are unchanged (CH-3); the toggle mode is opt-in. |

---

## 9. Non-goals

| Item | Why |
|---|---|
| A page-level **side drawer** for editing (grill 1a chose the dialog) | No precedent here; the repo's drawer pattern is SideDrawer-inside-a-dialog |
| Accordion / expand-in-place question editing | Off-reference (D4); settled against |
| A **native checkbox** pill instead of the shared `Chip` | Settled: extend `Chip` (CH-1) |
| Deduplicating the composer's and the draft panel's generation controls | Ownership move = data-flow change (QA-20) |
| Per-question difficulty, tags, reorder/duplicate | No model/field support; not presentation |
| Interleaving files and links into one list | D3 — different affordances |
| Restructuring compartments 1–4 | D2 — token alignment only *(except R4's single additive `Strands & lessons` row, CP-1)* |
| Modifying `SectionCard` | D6 |
| **Read-only View-mode chrome (D8/VM-1/VM-2)** | **Withdrawn** (G48) — it requires rewriting three invariants that assert the editors are *absent* in View mode; that is its own spec's decision |
| Backend, contracts, EF, feature flags | No change *(R3 adds `[Description]` attributes to `QuestionTypeDto` — metadata only, no wire/shape change)* |
| A **new structured context field** on `QuestionGenerationRequest` (grade/subject) | Settled against (D15/D22): it would need an AI-host contract change *and* a lock-policy decision, since teacher context must not bypass a locked org prompt |
| An **AI call to parse** the prompt text back into the knobs | Settled against (D21): adds latency, cost and non-determinism to a dialog for a lossy transform |
| Raising the ≤5 / ≤20 000-char `ResourceTexts` budget | An AI-host constant change with cost implications — needs its own decision |
| A **difficulty level** preset in place of the three counts | Settled against (D16) |
| **Placement-only** or **name-only** strand/lesson picks | Settled against (D23/X1 = B): the owner chose real persisted links |
| Putting the strand/lesson pickers in `QuestionPromptDialog` | Settled against (D22/W1 = C): they are an assignment property in Basics |

---

## 10. Tests

| Contract cluster | Test |
|---|---|
| DL-1/DL-8 (tokens, motion, no literals) | Source check: changed `.razor.css` files contain no `#rrggbb` literal; no `<style>` in `.razor`. |
| LA-1/LA-3 (composition) | `AssignmentAuthoringBunitTests`: the `.authoring-layout--cq` row exists, contains both anchors, DOM order is `content` then `questions`; existing compartment-order test **unchanged**. |
| CR-1…CR-8 | `ResourcesSectionBunitTests`: tile + `AnchorId` wiring; "Upload files" and "No resources attached yet." present; Files/Links counts; `+` reveals and focuses the link input; extraction badge only for non-`Succeeded`; two-line row renders size + badge. |
| CR-9 | Existing `StageFileAsync` / `AddUrl` / `RemoveAt` / re-extract tests pass **unmodified**. |
| CH-1…CH-3 | `ChipBunitTests` (**new**, Admin.Tests.Unit) — the attribute really is the literal `"true"`/`"false"` in both states (never bare, never absent), a click fires `OnToggle` with the requested state, the two-state class is applied, and the badge / dismissible modes are untouched. |
| **PB-2** *(was mislabelled QA-3)* | Dialog chips: three toggle buttons bound to the bools; the "all off" helper line appears. |
| QA-9/QA-10/QA-16 | List rows: `#cq-qrow-{i}` renders index · text · badge · kebab; kebab has Edit + Remove; Remove fires the shared confirmation. |
| QA-11/QA-12/QA-13 | `QuestionEditDialogBunitTests` (new, per `test-dialog-opener-components`): opening with a row's values renders the per-type fields; Save applies, Cancel discards; Add opens it for a new row. |
| QA-14/QA-15 | Existing pagination and zero-state tests pass, with the zero-state string preserved. |
| QA-18…QA-21 | `QuestionsDraftSectionBunitTests`: disclosure collapsed by default, auto-expanded when a draft exists; confirm/discard calls unchanged. |
| VM-1/VM-2 | New: View mode renders read-only rows in both compartments; `QuestionReviewList` no longer exists (file + suite deleted). |
| X-1…X-4 | Existing `AssignmentAuthoringBunitTests` invariants pass unmodified (`CompartmentTitles`, `JumpNav_StillResolvesAllFiveAnchors`, the fail-closed and Create children tests). |

| PB-1…PB-11 (dialog) | `QuestionPromptDialogBunitTests` (new, dialog-shell harness): fields bound to the saved knobs; read-only subject/grade/grounding block; composition template; no subject → Configure disabled-with-reason; `PromptLocked` keeps the knobs live but disables composition. |
| PB-5/PB-6/QA-23 (overwrite · re-hydration · timing) | Unit tests on the composer/parser helper: empty or template-matched → replace; hand-edited (non-template) → confirm path; a composed prompt re-hydrates **after a simulated reload** (no transient state) with difficulty from the persisted counts; a partially-matching text falls back to defaults; hand-written prose is never parsed. |
| QA-3/QA-23 (summary + timing) | bUnit: the summary line reflects the model; changing the subject/grade target with an unedited text rewrites the narrative at Generate; an edited text is left alone. |

**Test updates allowed (chrome-necessary replacements only):**
- `QuestionEditorSectionBunitTests` moves from inline-field assertions to **list + dialog** assertions:
  the per-type field tests relocate to `QuestionEditDialogBunitTests`; the positional
  `FindAll("fluent-button")[n]` lookups become stable ids (`#cq-add-question`, `#cq-qrow-{i}-actions`,
  `#cq-qrow-{i}-open`).
- `QuestionReviewListBunitTests.cs` is deleted; its assertions are re-homed (§VM-2).
- `ResourcesSectionBunitTests.EmptyModel_RendersUploadControlAndNoResourcesInfo` keeps all three
  string assertions (X-6); only the element around the empty line changes.
- `QuestionGenerationSectionBunitTests` loses its inline count/difficulty/type-control assertions (they move to `QuestionPromptDialogBunitTests`), keeping its Generate/request-threading and gate tests; the `Difficulty_Fields_ThreadIntoRequest` assertions are re-pointed at the dialog.
- No other assertion may be weakened. `AssignmentAuthoringBunitTests` model-level tests
  (`section.Instance.Model…`) and markup-presence tests ("Loaded question?", "syllabus.pdf") survive
  unchanged, because the row still renders the question text.

---

## 11. Round slicing and execution mode

| Round | Content | Mode |
|---|---|---|
| **R1** | §3 tokens + §4 row + §5 Content & Resources card (tile, groups, `+`, View read-only) | **Light round (Tier 2)** — presentation only, no schema, no contract |
| **R2** | §5 CH (shared `Chip` toggle mode) + §6 Questions & AI (composer, row list + kebab + `QuestionEditDialog`, Draft preview) + the `QuestionReviewList` deletion (§7's read-only chrome was **not** implemented — withdrawn, G48) | **Light round (Tier 2)** |
| **R3** | §6.1/§6.1a prompt builder: `QuestionPromptDialog`, the composition/re-parse helper, the composer summary + Configure, the grade/subject/resource context, the draft panel sharing it, and the `[Description]` attributes on `QuestionTypeDto` (PB-4) | **Solo — explicit owner election 2026-10-07 (D28)**, despite being behavioural (dialog + deterministic composition/parse + control relocation + test churn); a migration would force Tier 3 |
| **R4** | §6A strand & lesson context: Basics pickers, subject-scoped multi-selects, persistence (EF migration + create/update/duplicate contract), `ContextStrands` wiring, summary + draft parity | **Full four-agent (Tier 3)** — carries a migration + contract change; the migration and the wire shape reach the owner explicitly and never run unattended (D28) |

Each round is independently demoable, build-green and mergeable. R2 depends on R1's tokens and row
and is delivered as a stack layer on top of R1 if R1's PR is still open.

---

## 12. Verification

1. `dotnet build SchoolCollab.slnx` — 0 errors.
2. `dotnet test` — `SchoolCollab.Assignments.Tests.Unit`, `SchoolCollab.Admin.Tests.Unit` (the shared
   `Chip` lives in Admin.Shared) and `SchoolCollab.ArchitectureTests.Unit` green.
3. Manual pass on `/assignments/{id}/edit` (Draft), `/assignments/create`, and a Published assignment
   in View mode, in **light and dark**: composition, hairline rows, hover bands, chip two-state look.
4. Keyboard-only pass: tab to a question row → its kebab is visible via `focus-within` → open Edit via
   Enter → the dialog traps focus → Escape/Cancel returns focus to the invoking control; Space toggles
   a type chip; the `+` reveals the link input with focus; the draft disclosure toggles.
5. With a draft staged, reload and confirm the draft panel auto-expands.
6. No horizontal scrollbar at 1280px (two columns) or 900px (stacked).

---

## 13. Settled decision log (2026-10-07)

### 13.1 Settled by fact (no owner decision needed)

| # | Statement | Evidence |
|---|---|---|
| **S1** | The kebab's "Generate questions" anchors to `#authoring-questions` and is disabled when the gate is closed. *(Corrected 2026-10-07: the section's first child is now the R1/R2 header band, not the composer — the anchor still lands at the section top with the composer immediately below.)* | `AssignmentAuthoring.razor:2220` (`RowAction.Navigate("Generate questions", "#authoring-questions", …)`); header `:388-391`, composer `:403` |
| **S2** | Page size stays **5**. | `AssignmentFormModelMappingsTests.QuestionPageSize_DefaultIsFive` ("FR-240: page size is fixed at 5") |
| **S3** | The four section components are used **only** by `AssignmentAuthoring.razor`; churn is contained. | repo-wide grep |
| **S4** | View mode uses the **same** two-column row. | consistency; no variant layout |
| **S5** | The rail is **not sticky** — the page already stacks two sticky layers (action bar, jump-nav). | `AssignmentAuthoring.razor.css` L16, L49 |
| **S6** | The AI host **already** composes the difficulty line and serializes the whole request as JSON, then sends `PromptOverride` as a separate third user message framed as *additional teacher guidance (does not change the output format)*. | `AssignmentQuestionGenerationSystemPromptProvider.cs` `BuildMessages` L124–154 |
| **S7** | A tenant lock nulls **only** `PromptOverride`; `TopicId`/`TopicName`, `QuestionCount`, `Types`, the difficulty counts and `ResourceTexts` all survive. | `AssignmentQuestionGenerationService.cs:82` |
| **S8** | `GradeLevelDto` carries `Level`, `Name` and `DisplayOrder` as three separate fields (fields at `:6-8`); `Level` **defaults** from the GRADE coded value's `DisplayOrder` when a grade is picked on the create page (a page default, not an automatic server-side seed) — the canonical community grade number. | `Students.Core/DTOs/GradeLevelDto.cs:4-17`, `GradeLevel.cs:23-25`, `Students.Application/Components/Pages/Students/GradeLevels/Create.razor` `OnGradePickedAsync` ~:131 |
| **S9** | The grade targets are already on the page (`KnownGradeTargetIds` + `_gradeLevels`, no new API), and resources already ride `ResourceTexts` (≤5 texts × ≤20 000 chars). | `AssignmentAuthoring.razor:994,1588`; `ResourceTextBudget.cs:22-25` |
| **S10** | `AiPromptOverride` is a persisted 4000-char column and part of the unsaved-changes fingerprint. | `AssignmentEditFormModel.cs:68,803`; `AssignmentConfiguration.cs:76` |
| **S11** | `QuestionGenerationRequest.ContextStrands` (`IReadOnlyList<string>?`) **already exists** and is passed `null` at **both** call sites — i.e. a designed-but-unused seam that already reaches the model and is not stripped by a tenant lock. There is **no lessons field**. | `AssignmentQuestionGenerationTypes.cs:36`; `QuestionGenerationSection.razor:248`; `QuestionsDraftSection.razor:263` |
| **S12** | Both pick sources are subject-scoped and already client-side: `ListTopicStrandsAsync(topicId, parentStrandId?)`, `ListTopicLessonsAsync(topicId, strandId?)`; `TopicStrandDto` has `IsLesson`/`ParentStrandId`, `TopicLessonDto` has `StrandId`. **Nothing in `src/Assignments` references strands or lessons today**, and `Assignment` carries only `TopicId`. | `StudentsApiClient.cs:196-224,1276,1315`; repo-wide grep of `src/Assignments` |
| **S13** | **Corrected 2026-10-07 (the original claim was refuted by audit).** The **difficulty counts ARE persisted** on the assignment: they ride `CreateAssignmentRequest`/`UpdateAssignmentRequest`, are entity columns and round-trip on load. **Only the question count and the type mix are transient** (absent from the contract). `AiPromptOverride` is persisted as well. | `AssignmentEditFormModel.cs:86-92,257-259,467-469,806-808`; `Assignment.cs:66-70`; snapshot `difficulty_easy_count`/`_medium_`/`_hard_` |

### 13.2 Settled by the owner (grill rounds 1–2)

| # | Question | Answer | Cost accepted |
|---|---|---|---|
| **G1** | Layout composition | Two-column row, Questions main / Content rail | narrow rail needs ellipsis + `title` |
| **G2** | Content & Resources shape | Context card with drop tile + Files/Links groups | empty state becomes the tile |
| **G3** | Questions & AI shape | **Full restructure** | bigger test churn than a restyle |
| **G4** | Tile illustration | Icon composition | diverges slightly from the reference's 3-card art |
| **G5** | Card primitives | Feature-local; `SectionCard` untouched | none |
| **G6** | Scope fence | Presentation + small affordances only | draft-control dedup deferred |
| **G7** | Contracts & View parity | Preserve ids, strings, component types; View gets read-only chrome | detail tests gain a dialog/read-only harness |
| **G8** | Spec home + slicing | `documents/specs/assignment-authoring-content-questions-modern-ui.md`, R1/R2 light | none |
| **G9** | Row interaction (re-asked) | Single-line rows + kebab; **no accordion** | `QuestionEditorSectionBunitTests` restructured |
| **G10** | Editing surface (1a) | **Dialog** via `DialogShellBase` / `ShowShellDialogAsync`, `DialogSize.Medium` | modal interrupts list scanning |
| **G11** | Kebab visibility (1b) | Always in DOM; revealed on hover **+** `focus-within` | none |
| **G12** | Type-mix chip (re-asked) | **Extend the shared `Chip`** with an opt-in selectable mode (`<button aria-pressed>`) | `Chip` gains a second rendering mode |
| **G13** | Rail width | Share row 1's `320px–460px` track | rail 40px wider than first drafted |
| **G14** | Rail row layout | Two-line (name / `size · badge`) + actions | taller rows, slightly off the reference's one-liners |
| **G15** | Rail header + `+` | `ResourcesSection` owns the live header; `+` toggles the link input | `h3` rendered in two places (X-1 tested) |
| **G16** | Count / difficulty | Inline compact number fields | composer footer busier than the reference |
| **G17** | Draft panel | Collapsed disclosure, auto-expanded when staged | one extra click for draft-first authors |
| **G18** | Read-only list | Fold into the shared list component; **delete** `QuestionReviewList` | its suite is deleted, assertions re-homed |
| **G19** | Difficulty input shape | **Three counts** (A) — no level preset | none |
| **G20** | Overwrite + re-hydration | Replace only when empty/**template-matched**; otherwise confirm; re-parse only a template-matched prompt; **no merge** — the author's content survives only on cancel; confirming replaces it | no stored marker — a template-match predicate + a narrow parse helper |
| **G21** | Subject context | Read-only row; the subject is **always** included *(superseded by D18 on 2026-10-07 — the "omitted when the author's text already names it" clause was removed)*; Configure disabled-with-reason when none | refresh-on-change guard |
| **G22** | Context channel / authority | Composed into `PromptOverride`; structured fields stay authoritative; **no contract change** | a locked org receives no teacher grade context (by design, S7) |
| **G23** | Parse mechanism | Deterministic template + narrow re-parse; **no AI call** | parse scope deliberately limited |
| **G24** | Grade field | `GradeLevel.Level` → "Grade 1" | `Name` / `DisplayOrder` not used |
| **G25** | Multiple grades | All targeted grades, sorted by `Level`; `Grades 1–4` when contiguous and >2 | none |
| **G26** | Resources in context | Already grounded via `ResourceTexts`; additionally **name** them in the narrative; budget unchanged | none |
| **G27** | Composition timing | On dialog confirm **and** at Generate when the inputs changed and the text is unedited | one comparison, shared with G20 |
| **G28** | Lock · draft · residue · wording | Knobs stay live under a lock; `QuestionsDraftSection` shares the dialog and the composed prompt; composer keeps a summary + Configure; line-shaped narrative; writing dirties the form | the draft component and its 9 tests gain the dialog |
| **G29** | Where the strand/lesson pickers live (W1) | **Assignment-level, in Basics next to Subject** (C) | compartment 1 gains a section |
| **G30** | Structured vs narrative (W2) | Strands → `ContextStrands` (lock-proof); lessons → the composed narrative | asymmetric behaviour under a tenant lock (documented) |
| **G31** | Selection shape (W3) | Multi-select strands; lessons narrowed to the picked strands | one lessons fetch per picked strand |
| **G32** | Optionality & visibility (W4) | Optional with explicit empty states; cleared on subject change; named in the config summary | reload/clear path + two empty states to test |
| **G33** | Slicing (W5) | A **new R4** after R3 | R3 stays contained |
| **G34** | Persistence (X1) | **Persisted** (B): link rows on the assignment | EF migration + create/update/duplicate contract + DTO fields |
| **G35** | Mode rule (X2) | A migration-bearing round is **Full four-agent (Tier 3)**; R3 = Solo, R4 = Tier 3 | R4's migration/contract reach the owner explicitly; heavier process |

### 13.3 Settled by the owner (grill round 3 — R3 points of note, 2026-10-07)

| # | Question | Answer | Cost accepted |
|---|---|---|---|
| **G36** | Decline semantics + QA-3's claim (Q2) | A declined replace still applies the **knob** edits; QA-3's "cannot disagree" is scoped to machine-composed narratives | hand-written prose may omit the new knobs (it always could) |
| **G37** | PB-5's confirm location (Q1) | Stays **caller-side**, immediately after the dialog closes | one extra modal on the rare hand-written path; no nested-dialog exception |
| **G38** | The all-off hint (Q3) | Dialog-only; the composer summary carries `Balanced mix` | no composer-level restatement of the state |
| **G39** | EC-10 residual guard (Q4) | `OnGenerateAsync`'s `_questionCount < 1` guard stays as defence-in-depth, untested | EC-10 is proven by the dialog suite, not the composer suite |
| **G40** | R4's lessons channel (Q5) | Keep D24/G30 — lessons ride the narrative, **no** AI-host field | a locked org keeps strands but loses lesson names (re-confirm at R4) |
| **G41** | R4's storage shape (Q6) | Stays a Tier-3 plan decision (CP-5) — **settled 2026-10-07 as `uuid[]` columns** (OD-1, §13.5) | the decision moved into R4's plan as intended |
| **G42** | R4 timing (Q7) | R4 starts only after R3 is committed and its PR is open | R4's migration diff stays isolated from R3's UI churn |
| **G43** | Shipping R1–R3 (Q8) | **One PR** from `feat/authoring-content-questions-modern-ui` — no related PR is open, so no stack | a larger single review; each round is documented separately |

### 13.4 Settled by the owner (grill round 4 — open decisions, 2026-10-07)

| # | Question | Answer | Cost accepted |
|---|---|---|---|
| **G44** | Commit granularity (Q1) | **One commit** for R1–R3 — the default squash-merge makes a multi-commit history moot | a bisect-hostile single commit |
| **G45** | The keyless live-test failures (Q2) | **Out of this PR** — and after diagnosis, **no fix was needed**: the self-skip guard already exists and works (`CodedValueAIServiceLiveTests.cs:85-91`). *(Corrected 2026-10-07 — the original rationale, "fix the self-skip guard", was wrong.)* The real findings were a **dead CI secret** (`ci.yml` injects `OpenRouter__ApiKey`, the name the AI host reads, while the tests read `Parameters:openrouter-api-key` ⇒ CI always self-skips) and a live **HTTP 400** for the pinned model once a real key is present. | no code change; the live tests keep self-skipping in CI and failing locally when a stale key is configured |
| **G46** | `Chip` coverage (Q3) | Add **`ChipBunitTests`**, and correct both CH-1 (which *prescribed* the defect) and §10's claim | a new test file in an already-large PR |
| **G47** | The load-flaky draft-preload test (Q4) | Raise its `WaitForAssertion` budget to an explicit 5s | a longer timeout could mask a genuinely slow path (bounded, still fails loudly) |
| **G48** | D8/VM read-only chrome (Q5) | **Withdrawn — a recorded non-goal** of this spec | View mode stays visually thinner than Edit |
| **G49** | The manual light/dark + keyboard pass (Q6) | Run it **after the PR opens**; findings become a follow-up commit — **waived 2026-10-07**: the owner skipped the live pass, so R1–R3 were accepted on CI's green run alone | a possible second commit on the PR; the §12 visual/keyboard checklist stays unrun for R1–R3 |

### 13.5 R4 plan decisions (owner, 2026-10-07)

Declared as `Open decisions` by R4's orchestrator run and answered by the owner **before** the plan gate.
The round doc's `## Pinned decisions` carries the orchestrator's original rationale of record.

| # | Question | Owner answer |
|---|---|---|
| **OD-1** | CP-5 storage shape (G41-deferred) | **`uuid[]` columns** — `ContextStrandIds` / `ContextLessonIds`, two nullable arrays on `assignments`; no join table, no entity |
| **OD-2** | Which read DTO carries the picks | **`AssignmentAuthoringChildrenDto`** (the `/authoring` children read) — not `AssignmentSummaryDto`, which would add dead correlated projections to the list/ward/sweep reads for no consumer |
| **OD-3** | View-mode display of the picks | **None this round** — Create/Edit only; View-mode picks are a follow-up alongside the withdrawn D8/VM chrome (G48) |

---

## 14. References

- Behaviour contract: `documents/specs/assignment-authoring-compartments.md` (§3 rows 5/6, §4 UX-8, §5 UX-21, §6 UX-12, §12 UX-17/UX-19).
- Spec/anatomy precedent: `documents/specs/grade-detail-modern-ui-plan.md`.
- In-page modernization precedent: `documents/solution/student-view-page-modernization.md`.
- Dialog pattern: `src/SchoolCollab.Admin.Shared/Components/Dialogs/DialogShellBase.cs`, `DialogServiceExtensions.ShowShellDialogAsync`, `DialogSize`.
- Shared pill: `src/SchoolCollab.Admin.Shared/Components/Chip.razor`.
- Rules: `.github/copilot/rules/blazor-components.md` ("Blazor CSS isolation and styling"), `.github/copilot/rules/section-card.md`.
- Skills: `blazor-css-isolation`, `dialog-ui`, `fluentui-dialog-shell`, `input-width-scale`, `flex-row-input-alignment`, `fluentui-icons-in-school-collab`, `test-dialog-opener-components`.
- Screenshots: `C:\Users\skwar\OneDrive\Pictures\Screenshots\claude_project_question.png`, `…\claude_project_question_1.png`.

---

> **Superseded (2026-10-10):** **CR-1, CR-7 and CR-8** — the card header's `+` that revealed the hidden link input, and that input row — are replaced by the unified instructional-materials area: see `documents/specs/instructional-materials.md` §0. **CR-2…CR-6 and CR-10…CR-12 remain in force**, with CR-12 narrowed to `ResourcesSection` keeping its type name and the public methods still in use.

