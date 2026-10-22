# Spec: Notification-template naming, generated codes, and the editor surface

> **Status:** v1, 2026-10-23 — **IMPLEMENTED** on `feat/notification-template-naming` (backend,
> shared UI kit, Settings UI, tests — all gates green; code review recorded below). D1–D11 were
> ratified by the owner ("all as recommended") on 2026-10-23, unratified in the round that wrote them.
> **Owner:** Settings context (`SchoolCollab.Settings.Core` / `.Api` / `.Application`), plus the
> shared UI library `SchoolCollab.Admin.Shared` and the `MigrationService` seeders.
> **Cross-context impact:** none. The notification renderer / applicability matcher read `subject`
> and `body` only; naming metadata is never merged into render tags.
> **Amends:** `notification-template-authoring-redesign.md` — decision **Q10** and §3.3 (the left
> column "is a plain titled section, **not** a `PanelSection`"; "exactly two `PanelSection`s exist on
> this surface"), §3.5 (the audit-reason input rides the action bar), and the §3.3 field list (which
> has no naming rows). Those statements become obsolete with this spec.
> **Relates to:** `assignment-notification-templates.md` (the store and its contract),
> `documents/solution/adr-authoring-surface-pattern.md` (the authoring shell),
> `documents/solution/dialog-parameter-binding.md` (dialog plumbing), the entity-code rule pattern
> (`specs/…` seeding in `MigrationService`, generator in Settings).

---

## Problem Statement

An operator cannot tell two notification templates apart.

The only name a template carries today is its **subject line**, and the domain deliberately drops
the subject for every plain-text preferred mode of contact — so an SMS or WhatsApp template has
**no name at all**. Everywhere identity is read (the landing grid, the detail heading, the adopt and
publish-portal-wide confirmations, the reset prompt, the constraint dialogs) those rows collapse to
`"{kind} · {channel}"`, which is identical for every SMS reminder in the school. Two Email templates
with the same subject are equally indistinguishable, and a school that wants to point at "the Grade 8
Science reminder" in a support conversation has nothing to point at.

The create/edit page compounds it. It is one shared surface used by both the create route and the
per-template detail route, and it reads as an unfinished form floating in a large empty area: the
right rail's Constraints and Preview are bounded boxes, while the left half — where every actual
input lives — has no boundary of its own, and the message editor is a small fixed-height box inside
that unbounded space. Type and preferred mode of contact are two separately labelled rows although
they are one decision. The action bar repeats information the form already shows: a colour banner for
the kind being chosen a few centimetres below it, plus a "New template" label duplicating the
breadcrumb. And an audit-reason box sits on the **create** page too, where it means nothing — a first
write has no "why" — while on the edit page it is a bare text field on the action bar doing duty as
an audit control.

Finally the Constraints panel offers "View all constraints" even when there is exactly one
constraint, so the panel is noisier than the chip row it duplicates.

## Solution

Every template is **named and coded**:

- a required **Title**, the operator's own words for the template ("Grade 8 Science reminder"),
- an optional **Description**, one or two sentences saying what it is for,
- a **generated template code** (`TPL07`) minted by the existing entity-code-rule machinery, shown
  read-only, used wherever an exact reference is needed.

Title becomes the template's identity everywhere: the landing grid's template cell (`TPL07 · Grade 8
Science reminder`, with the description as a muted second line), the detail heading and breadcrumb,
and the adopt / publish / reset confirmations. The subject line goes back to being what it always
was — email content — and stops pretending to be a name.

The editor surface is tidied into the repo's own shapes:

- the left column is bounded exactly like the rail, wearing the shared panel chrome, and the message
  editor grows to fill it;
- type and preferred mode of contact sit inline under one **Template Type** label;
- the action bar stops repeating the form — no kind banner, no "New template" caption;
- the audit reason leaves the page entirely and appears as a **dialog** at the two moments it
  actually means something: saving an edit, and resetting a template;
- the constraints panel stays quiet until there are enough constraints to need the full list.

## User Stories

1. As an administrator, I want to give a template a title of my own, so that I can recognise it without decoding its body text.
2. As an administrator, I want the title to be required, so that no template can be created nameless.
3. As an administrator, I want an optional short description on a template, so that a colleague can tell what it is for without opening it.
4. As an administrator, I want an SMS template to have a name, so that the row I see in the landing grid is not just "Reminder · SMS".
5. As an administrator, I want a WhatsApp template to have a name, so that it is distinguishable from every other WhatsApp template.
6. As an administrator, I want two Email templates with the same subject to be distinguishable, so that I can pick the right one to edit.
7. As an administrator, I want the title shown as the detail page's heading and breadcrumb, so that I always know which template I have open.
8. As an administrator, I want the title to appear in adopt, publish-portal-wide and reset dialogs, so that the confirmation names the row I am about to change.
9. As an administrator, I want the description to appear under the title in the landing grid, so that I can scan meaning rather than guess.
10. As an administrator, I want a generated template code shown read-only, so that support conversations and audit notes have an exact reference.
11. As an administrator, I want the code generated automatically and never typed, so that I cannot create a duplicate or a malformed one.
12. As an administrator creating a template, I want to be told the code will be minted on save, so that the empty field is not mistaken for a required input.
13. As an administrator, I want the code to appear next to the title in the landing grid, so that I can match a code from an audit entry to a row.
14. As an administrator, I want adopting a system blueprint to give my copy its own code, so that two rows never share one identifier.
15. As an administrator, I want publishing a template portal-wide to give the blueprint copy its own code, so that the source and the copy stay separately traceable.
16. As an administrator, I want the title editable on an existing template, so that I can fix a naming mistake.
17. As an administrator, I want the description editable on an existing template, so that I can keep it accurate as usage changes.
18. As an administrator, I want a title edit to be audited (who, when, why), so that a rename is attributable.
19. As an administrator, I want existing templates to have a sensible title after the upgrade, so that nothing is nameless on day one.
20. As an administrator, I want existing templates to show a dash where no code was minted, so that I can tell a historical row from a fresh one.
21. As an administrator, I want the left column of the create/edit page bounded like the right rail, so that the page reads as two deliberate panels rather than one floating form.
22. As an administrator, I want the message editor to fill the left panel vertically, so that the largest input on the page is not the smallest box on it.
23. As an administrator, I want the subject line to have the full width of its panel, so that I can read a long subject without horizontal scrolling.
24. As an administrator, I want type and preferred mode of contact on one row under a single "Template Type" label, so that I treat them as the one decision they are.
25. As an administrator, I want a "fixed per template" explanation available on that row rather than occupying a paragraph, so that the page stays compact while the rule stays discoverable.
26. As an administrator, I want no kind banner in the action bar, so that the page does not tell me the kind twice.
27. As an administrator, I want no "New template" caption in the action bar on create, so that the breadcrumb is the single place that says where I am.
28. As an administrator editing a template, I want my template's title as the action-bar heading, so that the page names itself.
29. As an administrator, I want the owner badge (System, or the tenant's name) still shown when editing, so that I know whose row this is.
30. As an administrator creating a template, I want no audit-reason field on the page, so that I am not asked "why" before there is anything to explain.
31. As an administrator editing a template, I want a reason dialog when I save, so that the audit entry carries my own words.
32. As an administrator editing a template, I want the reason dialog to be the only interruption, so that I am not also asked to confirm the same save.
33. As an administrator, I want an empty reason rejected in that dialog, so that the trail never receives a blank justification.
34. As an administrator, I want cancelling the reason dialog to abandon the save, so that nothing is written without my reason.
35. As an administrator resetting a template, I want the same reason dialog I see when saving, so that resets and edits are audited the same way.
36. As an administrator, I want the reset dialog to be its own confirmation, so that I am not asked twice for one destructive act.
37. As an administrator, I want "View all constraints" hidden when a template has five constraints or fewer, so that the panel is not noisier than the chips it duplicates.
38. As an administrator, I want "View all constraints" available once a template has more than five, so that a long constraint list stays readable in one place.
39. As an administrator, I want to dismiss any constraint chip in place regardless of count, so that staging an edit never needs a dialog.
40. As a tenant-scope administrator, I want the same naming and layout improvements on the system blueprint rows I am allowed to write, so that the two scopes do not diverge.
41. As a tenant-scope administrator opening a system blueprint row, I want it read-only exactly as it is today, so that adopting remains the only path into it.
42. As a system-scope administrator opening a tenant's row, I want it read-only exactly as it is today, so that publishing portal-wide remains the only path into it.
43. As an administrator, I want the conflict warning to name templates by title, so that the message points at something I can find.
44. As an administrator, I want the duplicate-constraint warning, the SMS length guidance and the degraded-preview note unchanged, so that the polish does not cost me the diagnostics.
45. As an administrator on a narrow window, I want the two panels to stack as they do today, so that the new boundary does not break the layout.
46. As a keyboard user, I want the Template Type dropdowns and the reason dialog reachable and dismissible, so that none of the reorganisation locks me out.
47. As an administrator, I want a failed save surfaced as an error on the page after the reason dialog closes, so that I do not lose my reason or my draft.
48. As an administrator, I want the create page to keep its grade-level deep-link prefill, so that arriving from a grade card still starts the draft constrained.
49. As a developer, I want the title to be the one identity field the codebase reads, so that no surface has to re-derive a label from body text.
50. As a developer, I want the code to come from the existing entity-code-rule generator, so that template codes behave like student, staff, assignment and topic codes.

## Implementation Decisions

### Naming fields (D1–D4)

1. **D1 — `Title` and `Description` are new template columns.** A template gains a required
   `Title` (max 120 characters) and an optional `Description` (max 500 characters). Both are shown
   as form rows directly above the Template Type row: `Title` a single-line input, `Description` a
   two-line text area. `Title` is required in the create/edit form — the domain rejects a blank or
   whitespace-only title, mirroring how the body is rejected today.
2. **D2 — No uniqueness invariant on `Title`.** Two templates may share a title; the generated code
   is the unique identifier, and the existing identical-constraint-set warning is the ambiguity
   signal in the applicability matcher. Adding a "unique title per scope × kind × channel" rule
   would invent an invariant nothing else in the repo carries and would make renames fail in ways
   operators do not expect.
3. **D3 — `Title` replaces `Subject` as identity everywhere; `Subject` returns to being content.**
   The landing grid's template cell, the detail heading and breadcrumb, the adopt / publish-portal-
   wide / reset dialog copy, the constraint dialogs' label, and the identical-constraint warning all
   read `Title`. `Subject` stays a content field rendered only for Email and is never used to
   identify a row again. The audit stream's key (`{kind}:{channel}`) is unchanged, so history stays
   addressable.
4. **D4 — Description is display-only metadata.** It is never merged into render tags, never
   delivered, and never read by the applicability matcher. It exists to be read by humans in the
   landing grid and in the editor.

### Generated code (D5–D7)

5. **D5 — A new entity-code rule, `NOTIFICATION_TEMPLATE_CODE`, minted in the create handler.** The
   rule follows the `STUDENT_CODE` shape: a fixed stamp segment (`TPL`) followed by an
   alphanumeric sequence, producing `TPL01`, `TPL02`, … The generator is the existing
   `IEntityCodeGenerator` in the Settings context, called from the template create handler — an
   in-context call, not a cross-context HTTP hop, because Settings owns both the rule and the row.
   The rule is seeded idempotently by the MigrationService's entity-code-rule seeder alongside the
   existing rules, so it ships with the same mechanism as student/staff/assignment/topic codes.
6. **D6 — `Code` is persisted, unique per scope, and never writable.** A nullable `Code` column on
   the template with a unique index over `(TenantId, Code)` filtered to live rows — the precedent set
   by `(TenantId, StudentNumber)` on students. The create handler mints it; update never touches it;
   it is not part of any request body.
7. **D7 — Adopt and publish-portal-wide mint a fresh code for the copy.** Both actions copy a row
   into another scope, so each copy gets its own code from the same generator, and the audit reason
   already recorded mentions the source. This keeps one code per live row, which is what makes the
   code usable as a reference. Historical rows get **no** backfill code: a null code renders as a
   dash, so an operator can tell a pre-upgrade row from a minted one.

### Schema, migration and backfill (D8)

8. **D8 — One migration adds `title`, `description` and `code`, backfills titles, and adds the
   unique index.** The migration (Settings database, via the documented EF-migration process) adds
   three columns and the filtered unique index. `Title` is backfilled from the label the codebase
   currently derives at display time: the subject when present, otherwise the first ~80 characters of
   the body's plain text, otherwise `"{kind} · "{channel}"`. Description stays null and code stays
   null for existing rows. The column is added as required only after the backfill runs in the same
   migration.

### Template Type row and action bar (D9)

9. **D9 — One `FormRow` labelled "Template Type" holds both dropdowns inline, and the action bar
   stops repeating the form.** The kind and preferred-mode-of-contact dropdowns become children of a
   single form row — the shared row primitive's input cell already lays children out inline with a
   gap, so no new container and no new CSS. Each dropdown is sized on the existing field-width
   ladder (a mid step that fits the longest option text), and the "type and contact mode are fixed
   per template" sentence becomes the row label's help tooltip rather than an inline paragraph. On
   edit the dropdowns stay disabled exactly as today and the owner badge (System, or the tenant's
   name) stays in the action bar. The kind banner and the "New template" caption are removed from the
   editor's action bar in both modes; on edit the heading becomes the template's title. The
   colour-coded kind banner remains in the landing grid, which is where at-a-glance kind scanning
   actually happens; the template code prefix in the landing cell also names the kind via the title.

### Left panel and editor sizing (D10)

10. **D10 — The left column wears the shared panel chrome and the message editor fills it.** The left
    column becomes a titled panel rendered by the shared panel component already used by Constraints
    and Preview, so border, radius and the full-width header rule come from one implementation
    instead of a second hand-rolled box. Consequently the earlier "exactly two panels on this
    surface" rule is retired — the surface now has three bounded panels, and the audit trail section
    below the grid keeps its plain heading (it is not a panel). The message editor gains a height
    parameter on the shared rich-text editor component (default unchanged, so the assignment
    authoring surface is untouched), and the template editor passes a taller value so the body fills
    the panel it now sits in. The subject input takes the field-width ladder's fill step. Because the
    audit section currently borrows the left column's heading style, the heading style must survive
    the panel conversion (a shared panel title supersedes it for the panel, and the audit section
    keeps its own heading treatment).

### Audit reason leaves the page (D11)

11. **D11 — The reason input is removed from the action bar; a reusable reason dialog takes over.**
    - Create sends **no** reason at all. A first write has no "why", and the audit entry records the
      actor, the timestamp and the created values.
    - Edit-save opens a **reason dialog**; the reason is **required** (whitespace rejected in the
      dialog, which keeps itself open with an error rather than submitting). Cancelling the dialog
      abandons the save and leaves the draft untouched. The dialog is the only interruption — there
      is no second confirmation for a save.
    - Reset, both from the landing row and from the editor action bar, opens the **same dialog**. It
      remains the destructive confirmation (no additional confirm), preserving the decision already
      made for reset.
    - The existing single-purpose reset-reason dialog is generalised into one shared reason dialog
      whose title, prompt and submit label are supplied by the caller, so save and reset cannot drift
      into two different reason experiences. It keeps the shared dialog shell, so footer, saving state
      and error rendering are the ones every other dialog uses.
    - On success the page returns to its normal post-save state; on failure the error surfaces on the
      page as it does today, after the dialog has closed.

### Constraints panel (D12)

12. **D12 — "View all constraints" appears only when the constraint count exceeds five.** The
    threshold is a named constant. Chips stay unbounded and each stays dismissible in place, so
    staging an edit never needs a dialog regardless of count. The add-constraint menu, the chips and
    the conflict warning are unchanged.

### Modules touched

- Settings domain: the template aggregate gains title/description/code, the create path accepts a
  title/description and mints the code, the update path replaces title/description, and the audit
  recorder receives the title on create and update.
- Settings DTOs and CQRS: create/update requests gain title and description; the template DTO gains
  title, description and code; the create request keeps its optional reason field for other callers
  but the editor stops sending one.
- Settings API routes: unchanged paths and verbs; the create/update bodies carry the new fields; the
  scope rules (403 for a foreign row, 404 for an invisible row) are unchanged.
- MigrationService: the entity-code-rule seeder gains the template rule.
- Settings database: one migration (three columns, filtered unique index, title backfill).
- Shared UI library: the reason dialog (generalised), the rich-text editor's height parameter.
- Settings admin application: the shared template editor (naming rows, Template Type row, panel
  conversion, editor height, subject width, reason dialog wiring, view-all threshold), the landing
  page (template cell, dialog copy), and no change to the thin create/detail routes.

## Testing Decisions

**What makes a good test here.** Assert observable behaviour through a seam that already exists — the
Settings notification-template HTTP contract, and the rendered DOM of the shared editor addressed by
its published element ids. Do not assert that a handler was called, that a private method ran, or
that a specific CSS class exists for its own sake. A test must fail if the operator-visible outcome
regresses (a nameless row, an unreasoned edit, a code shared by two rows, a dialog that submits a
blank reason, a "view all" button on a one-constraint template) and stay green through refactors of
the internals.

**Seams — no new seam is introduced.**

1. **Server seam (existing): the Settings notification-template handlers**, exercised against an
   in-memory Settings database with the real handlers constructed directly. Prior art:
   `NotificationTemplateHandlerTests` (create/update/delete/audit through the handlers),
   `EntityCodeGeneratorTests` (rule + generator), `NotificationTemplateOwnershipTests`. This seam
   covers: title required, code minted and unique per scope, code untouched by update, adopt/promote
   copies minting their own code, audit entry carrying title/reason, migration backfill shape.
2. **UI seam (existing): the fake Settings API over an `HttpMessageHandler`, with the real shared
   editor rendered by bUnit.** Prior art: `NotificationTemplateTestHarness`,
   `NotificationTemplateCreateTests`, `NotificationTemplateDetailTests`,
   `TemplateEditorPreviewTests`, `NotificationTemplatesLandingTests`. This seam covers: the naming
   rows present and required, the Template Type row holding both dropdowns, no kind banner and no
   "New template" caption in the action bar, no reason input on the page, the reason dialog opening
   on save and cancelling the save when dismissed, reset opening the same dialog, the "view all"
   button absent at ≤5 and present at 6 constraints, the landing cell rendering code + title +
   description, and dialog copy naming the title.
3. **Browser pass (verification, not a gate): a UI-tester run** over the create and detail routes —
   the panel boundary at wide and narrow widths, the editor filling its panel, the subject width, the
   inline Template Type row's tops aligning. The repo already runs Playwright against Settings; this
   is a one-off visual confirmation of a layout change, not a new permanent suite.

**Modules tested:** Settings template handlers (create/update/adopt/promote), the entity-code rule
seeding shape, and the shared template editor plus landing page via bUnit. The migration itself is
verified by running the MigrationService against the dev database, per the standing runbook, rather
than by a new automated migration test.

## Out of Scope

- **An enable/disable lifecycle state on a template.** Reset (soft delete, built-in default applies)
  remains the only off switch. Adding an `IsEnabled` flag changes the applicability matcher, the
  default invariant and the precedence rules and deserves its own spec.
- **Extending the audit schema** with previous/new title and description columns. The audit entry
  keeps its current before/after fields (subject, body, constraints) and records actor, timestamp,
  change kind and reason; title changes are attributable through that entry but not diffed.
- **Enforcing title uniqueness**, per D2.
- **Backfilling codes for historical rows**, per D7.
- **Renaming the kind×channel audit key**, the store's route shape, or the scope rules.
- **Any change to the render path, render tags, the applicability matcher, the preview contract, the
  SMS length guidance, or the constraint vocabulary.**
- **The mobile head** — the notification-template surface does not exist there.
- **A second colour-coded kind banner anywhere on the editor surface**; the banner stays in the
  landing grid only.

## Further Notes

- **Ratification is outstanding.** The eleven decisions above are the round-1 recommendations the
  owner has not yet answered. Treat them as the plan of record for now, but each one is cheap to
  reverse before implementation; the expensive ones to unwind later are D5–D8 (schema + generator)
  and D11 (dialog contract), so those are the ones worth an explicit yes.
- **Spec amendments land in the same change.** Because this spec retires three pinned statements in
  `notification-template-authoring-redesign.md` (Q10's "left is not a panel", the "exactly two
  panels" rule, and the action-bar reason input), that spec is amended in the same work — per the
  repo rule that a losing document is amended rather than silently overridden.
- **Ordering constraint.** The migration and the seeder rule must land before the create handler calls
  the generator, or a fresh database will fail its first template create with "rule not found". The
  seeder is idempotent, so an existing database picks the rule up on the next MigrationService run.
- **A null code is a feature, not a defect.** Any surface rendering the code must handle null with a
  dash and must not sort on it as though it were always present.
- **Delivery shape.** The work is one round: schema + generator + contract, then the editor surface,
  then the tests. It touches a migration, a wire contract and a UI surface, so it is not a Tier 0
  job; the tier is the owner's call at implementation time.
