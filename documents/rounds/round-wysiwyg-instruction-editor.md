# Round — WYSIWYG rich instruction (solo)

**Mode:** solo (owner-opted 2026-10-11) · **model:** session model. Delegation remains parked at the
Cline account layer (`Unauthorized` on every dispatch), so the tiered skill's Tiers 2–3 could not run and
§8's own Q18 recommendation ("then a light round") was answered honestly as solo — the owner accepted it
knowing the cost (no independent plan review).

**Follows:** `round-material-row-editing.md` (PR #335, merged `e300912d`).
**Specs:** `question-response-types.md` **§8** (in force; its §5.2/§5.6 are superseded by
`instructional-materials.md` §0) and `instructional-materials.md` **§10**, whose named follow-up says the
Text row's **body** is where the rich editor lands — the reason D6 keeps the title a separate field.

## Settled before this round (owner, 2026-10-09 — §8.4; not re-litigated here)

| # | Settled |
|---|---|
| Q11 | **Sanitised HTML**, sanitised on write **and** again on render |
| Q12 | **`Blazored.TextEditor`** (MIT) over **Quill 2.x** (BSD-3-Clause), both vendored and pinned locally (no CDN) — our sanitiser and render component own display |
| Q13 | **Both owners, one editor component** (assignment + question instructions) |
| Q14 | Rich text and the media instruction items **coexist** (no inline embeds) |
| Q15 | **One render component + one sanitiser helper**; every surface; no ad-hoc `MarkupString` |
| Q16 | Existing plain text **renders as-is, no migration** |
| Q17 | **Test the seams** (persistence, sanitiser, render component); the interop shell is excluded from bUnit |
| Q18 | **Dependencies pre-authorised** — licence, CSP and bundle size recorded in this doc |

## This round's grill round (owner: "all as recommended")

| # | Decision |
|---|---|
| Q1 | **Solo**, as above. |
| Q2 | The editor lands on the **Text material's body**, for both owners — the shared `MaterialEditDialog` is what both the page and the question dialog open, so one change serves both. The legacy **"Student guidance"** prose stays a plain textarea: a recorded non-goal, promotable later. |
| Q3 | `Blazored.TextEditor` **1.1.3** + `HtmlSanitizer` **9.2.1039** pinned in `Directory.Packages.props` (CPM); **Quill 2.0.3** `quill.js` + `quill.snow.css` vendored under the Assignments Application RCL's `wwwroot/lib/quill/` with an attribution note; no CDN; CSP and byte sizes recorded below. **Re-confirmed by the owner (2026-10-11)** after querying the wrapper's maintenance: repo not archived, last push 2025-09-21, package not deprecated, 1.1.3 targets `net9.0`. The owner had recalled §8.3's *recommendation* (Quill behind our own interop shell, no wrapper) — the §8.4 settle named the wrapper, and that divergence is now recorded here rather than left implicit. |
| Q4 | Every instruction-printing surface switches to the sanitiser render component **in this round** — a safety ordering, not a scope preference: the editor is what creates the HTML, so the sanitiser must be live everywhere before the first save can happen. |
| Q5 | #335's merge: auto-merge armed on the accepted recommendation → **MERGED**. |

**Held for round 2** (asking now would guess at an answer): **Q6** body-loss sequencing — depends on Q2,
because slice 5 replaces the very field whose body was reported lost, so the defect may vanish by
construction; **Q7** the ward card's stale "kind renders" comment — depends on Q4 (the ward surface is
touched here); **Q8** re-dispatching the parked #333 independent review — depends on Cline auth returning.

## Facts resolved before planning (evidence, never assumption)

| Fact | How it was checked |
|---|---|
| `Blazored.TextEditor` 1.1.3 targets **`net9.0`**/`netstandard2.0`, and its only dependencies are `Microsoft.AspNetCore.Components(.Web) 3.1.0` — satisfied by the framework | NuGet nuspec |
| `HtmlSanitizer` **9.2.1039** is the latest **stable** (10.x is beta) and targets `net8.0`/`netstandard2.0` | NuGet nuspec |
| The repo has **no `wwwroot/lib`** and **no** sanitiser/editor/Markdown package today — this is the first vendored asset | `src`, `Directory.Packages.props` |
| Quill 2.0.3 assets are reachable (HTTP 200) | HEAD probe |
| The ward card prints instruction prose as **plain text** (`<p>@_view.Instructions</p>`) and materials by label | `SchoolCollab.Families/…/Ward/Assignment.razor` |

## Gotchas — resolved from the wrapper's own setup docs (2026-10-11)

Its README (main) and package metadata, not speculation. **No blocker**; one product trade-off and
several things that are recorded here rather than discovered later.

| # | Gotcha | Handling |
|---|---|---|
| G1 | **It is CDN-first**: the README's setup tags point at `cdn.jsdelivr.net/npm/quill@2.0.3/…` (§8.5 already caught this). The wrapper injects nothing itself. | The tags are **ours to write**, so we author them against the vendored `wwwroot/lib/quill/` files and never copy the CDN lines. Only `quill.snow.css` is needed — we use the `snow` theme, not `bubble`. |
| G2 | **Two hosts must carry those tags**: `src/SchoolCollab.Admin/Components/App.razor` and `src/SchoolCollab.Families/Components/App.razor`. (The wrapper's own two scripts come from `_content/Blazored.TextEditor/` and need no vendoring.) | Slice 2 edits both, and records that any **future** host needs the same two lines. |
| G3 | **`ToolbarContent`/`EditorContent` are `(MarkupString)`**, which collides with Q15's "no ad-hoc `MarkupString` anywhere". | A **named, narrow exception**, recorded: the toolbar is *our own constant markup*, and the seed is passed as a **string** via `LoadHTMLContent(seed)` (chosen over `EditorContent`), so the exception covers the toolbar constant only. Q15's rule keeps its teeth everywhere else. |
| G4 | **Quill's colour/background/font/size formats emit inline `style` attributes** — which would force the sanitiser to allow `style`. | Our toolbar **omits `ql-color`, `ql-background`, `ql-font`, `ql-size`** (and `ql-image`: Q14 says media coexists as items, not inline embeds), so the allow-list stays strictly tag-based. **Trade-off, recorded:** no text colour or highlight in round 1; adding either later is a deliberate allow-list change with its own tests. |
| G5 | **The repo has no CSP at all today** (checked), so nothing breaks now — but Quill's internals inject inline styles. | Recorded for Q18/AC-7: the day a CSP is added, `style-src` will need `'unsafe-inline'` (or a nonce strategy). Nothing is silently widened today because there is nothing to widen. |
| G6 | **The API is imperative**: `GetHTML()` is `async`, `LoadHTMLContent(string)` seeds it, and there is **no `OnContentChanged`** in its option list. | Read the HTML **at submit** (`await editor.GetHTML()` → sanitise → model) instead of per-keystroke binding. Better for us than a bound callback: the model can only ever receive **sanitised** HTML (AC-2), and the seed goes in via `LoadHTMLContent`. |

## Plan (slices, in order)

1. **Dependencies** — `Directory.Packages.props`: `Blazored.TextEditor` 1.1.3, `HtmlSanitizer` 9.2.1039;
   `<PackageReference>` (no `Version=`, CPM) in `SchoolCollab.Assignments.Application`.
2. **Assets** — vendor `quill.js` + `quill.snow.css` (2.0.3) under
   `src/Assignments/SchoolCollab.Assignments.Application/wwwroot/lib/quill/`, plus a `README.md` recording
   versions, licences (wrapper MIT · Quill BSD-3-Clause) and byte sizes. The interop module imports these
   local files — never a CDN.
3. **Sanitiser** — one `RichInstructionSanitizer` with its allow-list in one place; applied on **write**
   (before the value reaches the model/payload) and again on **render**. The toolbar is restricted to
   **tag-based** formats (bold, italic, lists, headers, links) so no `style` attribute has to be allowed:
   the strictest policy that still does the job, stated in the doc rather than discovered later.
4. **Render component** — one `RichInstruction` component holding the tree's only `MarkupString`, fed
   exclusively by the sanitiser (Q15's rule), with tests for both directions.
5. **Editor component** — `RichInstructionEditor` (Blazored.TextEditor over the vendored Quill; **no
   interop of our own** — the wrapper *is* the bridge, settled with the owner 2026-10-11), placed in
   `MaterialEditDialog`'s Text branch — the body field — for both owners. The value is read at submit
   (`await editor.GetHTML()` → sanitise → model, G6) and seeded with `LoadHTMLContent(string)`.
6. **Surfaces** — the ward card, the assignment detail and any other surface that prints instruction text
   render through `RichInstruction` (Q4), so the first stored HTML can never reach a student unsanitised.
## Acceptance

| # | Criterion |
|---|---|
| AC-1 | A Text material's **body** is authored in the rich editor in the shared `MaterialEditDialog` — for both owners, since that one dialog serves the page and the question editor — and the stored value is the editor's HTML. |
| AC-2 | Saving **sanitises before the value reaches the model**: `<script>`, `on*` handlers, `javascript:`/`data:` URLs and anything outside the allow-list never arrive, so nothing unsanitised can be stored. |
| AC-3 | Display sanitises **again** through the single `RichInstruction` component, and every printing surface — ward card, assignment detail, question surfaces — uses it. A stored payload carrying a script never executes anywhere. |
| AC-4 | Existing plain-text instructions render **exactly as before** (Q16): no migration, and the render component passes plain text through unchanged. |
| AC-5 | The title stays a plain field and the media kinds are untouched — the body is the only rich surface, and rich text coexists with the media items (Q14). |
| AC-6 | The seams are covered and the interop shell is excluded from bUnit (Q17); `MarkupString` appears **only** inside `RichInstruction` (a grep-able rule). |
| AC-7 | Q18's record exists in this doc: both licences, the vendored byte sizes, and any CSP carve-out the editor requires — recorded, not silently widened. |
## Defect (2) — the seed raced the editor's creation (2026-10-11, resumed session; the still-broken half after Defect 1's fix)

The first live run (`/assignments/create`, Admin host `https://localhost:7300`, dev posture OIDC-off)
did not show the editor: the page's `<ErrorBoundary>` swapped in its "Something went wrong" fallback.
**Not a width problem** — the owner ruled that out, and the `LabelPosition="Below"` + 1400 px
breakpoint work already in the tree was irrelevant to this failure. A second, earlier diagnosis in
this round (the `./`-prefixed module path — the seam test `EditorModulePath_UsesTheDotSlashForm…`)
was real but NOT the whole story: with the prefix in place the assets loaded fine and the editor
still died.

An instrumented browser repro (wrapped `QuillFunctions.*`, DOM poller, 15 ms timeline) pinned the
exact order on the create page:

| t (ms) | Event |
|---|---|
| 384 | the loading box renders (assets still loading) |
| 442–455 | `window.Quill`, `QuillBlotFormatter`, `QuillFunctions` defined — assets OK |
| 460 | **`loadQuillHTMLContent` throws `TypeError: … reading 'root'`** — `__quill` does not exist yet |
| 506 | `createQuill` completes successfully — too late |
| 518 | the ErrorBoundary fallback replaces the whole authoring form |

**Root cause:** on the batch where the `BlazoredTextEditor` wrapper first renders, the PARENT's
(`RichInstructionEditor`) `OnAfterRenderAsync` runs before the child's, so the seed
(`LoadHTMLContent`, G6) reached `QuillFunctions.loadQuillHTMLContent` before `createQuill` had set
`quillElement.__quill`. The TypeError came back as a `JSException`, unhandled, and the page's
`<ErrorBoundary>` (repo rule) replaced the entire form — the reported "the text editor is not
displaying".

**Fix (slice 5 hardened, same session):**

- the seed waits one render pass after the wrapper mounts (`_editorMounted`): the next batch is
  processed only after the wrapper's own `createQuill` has been awaited to completion — the race is
  removed by construction, not by timing luck;
- the seed is wrapped in a bounded retry (3 attempts, re-armed when `Value` changes) and a
  `JSException` catch — a broken interop logs a warning, never faults the page;
- `GetHtmlAsync` is now `Task<string?>`: `null` = "editor unavailable" (asset failure or interop
  throw), logged, and `SaveAsync` keeps the model's current value instead of wiping the guidance;
- a null-module guard after `import` completes the safe path.

Seam tests added (`RichInstructionBunitTests`, Loose-mode interop, wrapper still excluded per Q17):
asset-load failure → the loading box, no throw; unmounted read → `null`; connected read → the
wrapper's HTML; interop throw on the read → `null`.

## Defect (3) — the literal-attribute binding (2026-10-11, found by Defect (2)'s live verification)

**Found by the evidence, not by review.** Defect (2)'s fix made the editor render for the first
time — and the instrumented timeline then showed the SEED inserting the literal text
`"​_model.Instructions"` into the editor (`quill:events text-change … insert: "_model.Instructions"`).
The razor wrote `Value="_model.Instructions"` (no `@`): on a **string-typed** component parameter
Razor passes the attribute text verbatim, so the editor seeded the field NAME instead of its value —
and on save the read-back would have written that literal into the assignment's guidance.

**Fix.** `Value="@_model.Instructions"` on both branches (View: `RichInstruction`; edit:
`RichInstructionEditor`). Regression guard:
`AssignmentAuthoringBunitTests.Edit_GuidanceEditor_ReceivesTheInstructionsField_NotTheLiteralAttributeName`
— Edit mode with `instructions: "Read pages 4-6."` pins the editor's `Value` to that text.

## Extension grill round (owner, 2026-10-11 — "all as recommended" except Q2)

The owner extended the round: teacher notes takes the editor too, and the Text material's body in
Content & Resources takes it (restoring §8.4 Q2's original target). Layout: **tabs** (owner's choice,
over accordion). One grill round decided the frontier:

| # | Decision |
|---|---|
| Q1 | **Student guidance \| Teacher notes, guidance first** (recommended) — the student-facing deliverable leads |
| Q2 | **No row label; tabs stretch to fill the width** (owner's variation on the recommendation) — `FormRow` requires a label, so the tab group is a plain `.authoring-notes-tabs` container instead |
| Q3 | **View mode stacks the two sanitised readers, no tabs** (recommended) — a reader clicks nothing. Fact-finding correction: View's real surface is the **summary** (`ShowSummary` gates the form out entirely), so the readers live in the summary card beneath the facts; the form's read-only branch was defensive dead code and keeps its readers for safety |
| Q4 | **Dialog only** (recommended) — the question dialog's inline rows keep the plain body field (AC-16); a rich editor inline is a follow-up round if wanted |

Recorded consequences (settled by fact-finding, not asked): the material body is sanitised on write
in `MaterialEditDialog.SubmitAsync`; an EMPTY editor yields Quill's `<p><br></p>`, stored as **null**
via the new `RichInstructionSanitizer.ToPlainText`; the dialog's Enter-guard module + JS retired with
the textarea (its only premise); the authoring View-mode **facts** readout (Description AND
Instructions) prints `ToPlainText` values, never raw tags; the ward card's `Instructions` switched to
`RichInstruction` (Q4 safety ordering — AC-3 test added); D9's ids ride on the tab editors.

### Extension verification (2026-10-11)

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| Assignments / Admin / Families / Architecture suites | **all passed, 0 failed** (new tabs, View-readers, dialog sanitise/empty/null seams, ward AC-3, `ToPlainText`; the severed-block-guard caught a multi-line `@if` and was fixed to the single-line form) |
| Live — notes tabs | `["Student guidance","Teacher notes"]` render as tabs; **both** Quill instances mount (panels persist — hidden-tab edits survive); typing works in each; no ErrorBoundary fallback |
| Live — Add Text Content dialog | rich body editor mounts inside the dialog, typing lands as `<p>…</p>`, Apply closes the dialog and appends the titled row (`Fraction basics`) |
| Live — screenshots | `.playwright-cli/notes-tabs.png`, `material-dialog.png` |

**Tab chrome (owner refinement, same day).** Traditional folder tabs: every tab wears a 1px border
with rounded top corners; the **panel carries one continuous top border** across the full width;
the **selected** tab loses its bottom border and, filled opaque (`--neutral-layer-1`) and pulled a
pixel down, masks the panel's line exactly beneath itself — the ONLY break; unselected tabs keep
their bottom border, coinciding with the panel's line so it reads single. FluentUI's accent
underline is the tablist's shadow-DOM `.activeIndicator`, retired via its exposed
`::part(activeIndicator)`. Verified in both tab states (`.playwright-cli/tabs-v2-*.png`).

**Componentised (owner, same day): `FluentTabStrip`** — the chrome extracted into
`SchoolCollab.Admin.Shared/Components/FluentTabStrip.razor(.css)` beside `FormRow`, a thin
`ChildContent` passthrough of `FluentTab`s (FluentUI semantics untouched); the authoring notes are
its first consumer and the only tab usage in the repo today. Structure pinned by
`FluentTabStripBunitTests` (container, tabs, labels, panels, hidden-inactive behaviour).

Probe note: the `<fluent-dialog>` OUTER element measures 0×0 and never carries an `open` attribute
in FluentUI Blazor 4.14.2 (visibility is the `hidden` binding) — a Playwright `state:'visible'` wait
on it is a false negative; the dialog is visibly fine (screenshot pinned).



## Status

**Slices 1–6 landed** (uncommitted, on `feat/wysiwyg-instruction-editor`), including both "editor not
displaying" defects below. Evidence is appended as it accrues; see the two Defect sections for the
live-verification chains.

### Verification evidence (2026-10-11, resumed session)

| Gate | Result |
|---|---|
| `dotnet build SchoolCollab.slnx` | **0 errors** (15 pre-existing warnings) |
| `SchoolCollab.Admin.Tests.Unit` | **Passed, 0 failed** — incl. the 4 new editor seam tests + the `./` module-path guard |
| `SchoolCollab.Assignments.Tests.Unit` | **Passed, 0 failed** — incl. the new defect-(3) literal-binding guard |
| `SchoolCollab.ArchitectureTests.Unit` | **Passed, 0 failed** (the `MarkupString` allow-list change) |
| Live, instrumented (AppHost cold start → `https://localhost:7300/assignments/create`) | `createQuill` **then** seed on the same element, `elHasQuill=true`; **no seed at all in Create mode** (Instructions genuinely empty — defect (3) fixed); editor + toolbar render, **no ErrorBoundary fallback** |
| Live, interactive | typing lands as `<p>…</p>`; toolbar **bold** → `<p><strong>…</strong></p>` |



## Defect (1) — the bare module specifier (2026-10-11, owner-reported; fixed same day)

**Symptom.** The Student guidance rich editor rendered only its empty loading placeholder (`.rich-instruction-editor-loading`) and never mounted Quill. The owner had already ruled out width — correctly: width was a red herring (the placeholder's 2px flex width is a *consequence* of the empty box, not its cause). **This was not the whole story:** with this fix in place (and the app restarted on it) the editor still failed to display — Defect (2) below was the remaining killer.

**Root cause (measured, not guessed).** `RichInstructionEditor.AssetsModulePath` was the **bare** specifier
`"_content/SchoolCollab.Admin.Shared/js/quillAssets.js"` — no `./` prefix. `blazor.web.js`'s `"import"` interop
handler rewrites **only** specifiers starting with `./` to absolute URLs (its exact code:
`"string"==typeof e && e.startsWith("./") && (e=new URL(e.substring(2),document.baseURI).toString()), import(e)`),
then calls `import()` with whatever it was given. A bare `_content/…` specifier is neither an absolute URL nor a
`/`/`./`/`../`-rooted path, so it is treated as a **bare module specifier**: with no import-map entry it is rejected
**at resolution, before any network request**, and the resulting `JSException` was swallowed by the component's
`catch (JSException)` → warning logged → `_assetsReady` never set → placeholder for ever. Every other module import
in the repo (`dialogDrawer.js`, `chatInput.js`, collocated `*.razor.js`, `wardPlayer.js`) uses the `./_content/…`
form — this was the only bare one.

**Evidence chain** (live stack, headless Chromium probe against `https://localhost:7300/assignments/create`):

| Observation | Meaning |
|---|---|
| `window.__quillAssetsPromise === false`, `window.Quill`/`QuillFunctions`/`QuillBlotFormatter` undefined, zero injected `<script>` tags | `ensureQuillAssets()` never ran |
| **Zero network requests** for `quillAssets.js` during page load | the failure was pre-fetch — module specifier *resolution*, not loading |
| Hand-run `import('/_content/…/quillAssets.js')` + `ensureQuillAssets()` in the same page → all five assets 200, Quill functions appear | assets, module and the whole library chain are healthy — only the component's specifier was wrong |
| `blazor.web.js` source shows the `./`-only rewrite in the `import` handler | why `./` works and bare fails |

**Fix.** One-line: `AssetsModulePath = "./_content/SchoolCollab.Admin.Shared/js/quillAssets.js"` (+ the
reason recorded on the constant). Regression guard: `RichInstructionBunitTests.EditorModulePath_UsesTheDotSlashForm_BlazorOnlyRewritesDotSlashSpecifiers`
pins the `./` prefix — bUnit cannot run the interop (Q17), so this seam is a constant assertion. The width work
(`LabelPosition.Below`, the 1400px stack breakpoint) is kept: it is correct regardless, just not this bug.

**One choice taken under Q18's pre-authorisation, flagged for veto:** the sanitiser package is
**`HtmlSanitizer` (Ganss.Xss)**, the canonical .NET allow-list sanitiser on a permissive licence — §8.4
authorised "the sanitiser package" without naming one. If you would rather hand-roll a narrow allow-list
(no new package at all), say so before slice 3 and it becomes a small, test-only helper instead.

## Placement — componentised into the shared UI library (owner, 2026-10-11)

Owner: *"avoid replicating blazored files all over, with js. Can we componentized it to a shared lib"*.
Agreed, and it changed where things live — slice 2's per-host `App.razor` tags were added and then
**reverted**, because they were exactly the replication being objected to: a third host would have had
to remember them, and the second copy is where copies drift.

| Where | What | Why there |
|---|---|---|
| `SchoolCollab.Admin.Shared` | the two packages (`Blazored.TextEditor`, `HtmlSanitizer`) — **removed** from the Assignments Application, which references this library anyway | one home for the dependency; consumers inherit it |
| `Admin.Shared/wwwroot/lib/quill/` | the vendored `quill.js` + `quill.snow.css` + its provenance README (moved out of the Assignments RCL) | an asset is served wherever its **owning library** is referenced. Left in the Assignments RCL, the ward host would have needed a reference to an *application* project |
| `Admin.Shared/wwwroot/js/quillAssets.js` | `ensureQuillAssets()` — injects the CSS link and the three scripts **once, in order, each awaited**, behind a page-level promise guard | replaces per-host tags; component-level `<script>` cannot promise the order the wrapper's JS needs (`window.Quill` first) |
| `Admin.Shared` | `RichInstructionEditor`, `RichInstruction`, `RichInstructionSanitizer` (slices 3–5) | Q15's "one component, every surface" made structural rather than aspirational |
| Both hosts | **nothing** | the point of the change |

**Q1 (this change's one question) — answered (a):** the Families host gains a `ProjectReference` to
`SchoolCollab.Admin.Shared`, so the ward surface can render authored HTML through the same sanitiser-fed
component as every other surface (Q4's safety ordering). Cost: one project reference; nothing new
transitively, since Families already depends on `Core`, `Students.Core` and `ServiceDefaults` — which is
all `Admin.Shared` brings. The rejected alternatives were a new `SchoolCollab.RichText` library (tidier
by name, a project + solution-items + guards for ~3 files) and leaving the ward on plain text (which Q4
rules out the moment authored HTML can be stored).
