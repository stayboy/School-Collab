# Notification-template naming, generated codes and the editor surface — implementation

**Status:** IMPLEMENTED (2026-10-23) on `feat/notification-template-naming`; spec
`documents/specs/notification-template-naming-and-editor-surface.md`, issue #401.

## Finding (why the shape is what it is)

The template store had no identity field. The UI derived a label at display time from `Subject` — which
the domain deliberately nulls for SMS and WhatsApp — so every plain-text row read as
`"{kind} · {channel}"`, and the same derivation had to be repeated in the landing grid, the detail
heading, the adopt/publish/reset prompts and the constraint dialogs. The store also had no
human-authored name, so nothing could be pointed at in a support conversation.

Three facts settled the design rather than a preference:

1. **The repo already owns code generation.** `IEntityCodeGenerator` + `EntityCodeRule` live in the
   Settings context, are seeded by the `MigrationService`, and are already used by student, staff,
   assignment and topic creation. A template code is one more rule, not new machinery — and it is an
   in-context call, so it is not a cross-module hop under `adr-cross-module-calls`.
2. **The sequence counter lives on the segment row** (`EntityCodeSegment.LastSequence`), and the
   blueprint rule is a single `TenantId = null` row — so every scope advancing `NOTIFICATION_TEMPLATE_CODE`
   shares one counter. Codes are therefore unique in practice without a global unique index, which is
   why D6 keeps the index scoped to `(tenant_id, code)` like `(tenant_id, student_number)`.
3. **The audit trail already had a required-reason dialog** for reset (`TemplateResetReasonDialog`).
   Generalising it beat writing a second one, and it let the reason input leave the action bar — where
   it sat on the create page too, asking "why" before there was anything to explain.

## Implementation steps (by workstream)

**1 — Backend (Settings.Core + MigrationService).** The aggregate gained `Title` (required, ≤120),
`Description` (≤500) and `Code` (nullable, write-once: `Create` takes it and `Update` has no path to
it). Both caps are enforced by the aggregate, not only by the column, so an over-long title is a 400
rather than a Postgres 500. The EF configuration pins the lengths and
adds `ix_tenant_notification_templates_tenant_code` on `(tenant_id, code)` filtered
`is_deleted = false AND code IS NOT NULL`. Migration
`20261022135020_AddTenantNotificationTemplateTitleDescriptionAndCode` adds the three columns, backfills
`title` (subject → first 80 characters of the HTML-stripped body → `"{Kind} · {Channel}"`), then makes
it `NOT NULL`, then creates the index. The `NOTIFICATION_TEMPLATE_CODE` rule is seeded idempotently as
`TPL` + a numeric sequence (min width 2, **no reset period**) — with `upperLimit: "999"`, because
`EntityCodeSegment` otherwise caps a min-width-2 numeric segment at 99 and throws on the hundredth
template.

**2 — Shared UI kit (Admin.Shared).** `NotificationTemplateApiClient` mirrors the three new DTO
members and the two request members. `ReasonDialog` is the generalised reset prompt: title, prompt and
submit label supplied by the caller, reason required (whitespace keeps the dialog open with an error),
exposed as `ShowReasonDialogAsync`, rendering through the house dialog shell. `RichInstructionEditor`
gained `MinHeight` (default `8rem`, so assignment authoring is untouched) implemented as a CSS custom
property consumed by the component's own scoped CSS — sizing stays in CSS.

**3 — Settings UI.** The editor's left column is a shared `PanelSection` ("Message") carrying the
Title and Description rows, one `Template Type` row with both dropdowns inline, the fill-width subject
and the body editor sized to fill the panel. The action bar lost the kind banner and the "New template"
caption and gained the template's title as its heading on edit. The reason input left the page: create
sends none; edit-save and reset both open `ReasonDialog`, and dismissing it performs no write.
`TemplateResetReasonDialog` was deleted. "View all constraints" appears only above five constraints. The
landing page gained a sortable Code column (em dash for a pre-upgrade row) and a title + description
cell, and `TemplateTitles` holds the one identity derivation both pages use.

**4 — Tests.** Two existing seams, no new one: the Settings handlers against an in-memory database, and
the fake Settings API over an `HttpMessageHandler` with the real editor/landing rendered by bUnit. A new
`TemplateDialogs` seam records dialog opens and scripts the reason/confirm answers, so "dismissed the
dialog ⇒ no write" is asserted through the same surface the operator uses. The migration's data
rewrite is pinned by a regex-read of the shipped migration (the `AssignmentPolicyFieldsMigrationTests`
precedent), and the behaviour InMemory cannot model — the hybrid tenant filter and the filtered unique
index — is pinned by a Testcontainers Postgres suite.

## Verification results (2026-10-23)

| Gate | Result |
|---|---|
| `dotnet build` — Settings API | 0 errors, 0 warnings |
| `dotnet build` — MigrationService | 0 errors |
| `dotnet build` — Admin.Shared | 0 errors |
| `dotnet build` — Settings.Application + Admin host | 0 errors |
| `SchoolCollab.Settings.Tests.Unit` | 677 total, 0 failed |
| `SchoolCollab.Admin.Tests.Unit` | 763 total, 0 failed |
| `SchoolCollab.Settings.Tests.Integration` — notification-template suite | 3 total, 0 failed (Testcontainers Postgres) |
| `dotnet build SchoolCollab.slnx` | 0 errors (17 pre-existing advisory warnings) |
| Migration SQL | reviewed via `dotnet ef migrations script`; backfill + `SET NOT NULL` + index in one migration |
| Mobile head (`src/Mobile/SchoolCollab.Mobile.slnx`) | **0 errors** (`-m:1 -p:BuildInParallel=false`; 2 pre-existing CS8603 warnings in unrelated clients) |

The full integration assembly still has two environment-dependent failures
(`OpenRouterClient_UsesApiKey_…`, `OpenRouterProvider_WithGoogleGemma4_…`) that return **402 Payment
Required** from OpenRouter — no account credit. They are unrelated to this change and were failing
before it.

## Code review (two axes, 2026-10-23)

The standards axis blocked on three findings, all fixed in one pass:

1. `PromoteTenantNotificationTemplateHandler` wrapped its **read** in `SuppressTenantGuard()`; the guard
   is consulted only at save time, so it now wraps only the `TenantId = null` insert, and the XML doc
   says so — the exact shape `dotnet-best-practices.md` forbids.
2. The shipped title backfill had no test; it is now pinned by
   `TenantNotificationTemplateTitleBackfillMigrationTests` (one `Sql` block, the `COALESCE` chain, and
   the `AlterColumn … nullable: false` ordering).
3. No handler had an integration test although InMemory cannot enforce the hybrid tenant filter or a
   filtered unique index; `NotificationTemplateStoreApiTests` now runs against Testcontainers Postgres.

The same pass also removed the dead audit channel (`Update` no longer returns unused
`PreviousTitle`/`PreviousDescription`), gave the editor a read-only code row (stories 10/12), extended the
landing search to title + description, added the domain length guards, deduplicated the channel-label
switch into `Admin.Shared.Constants.EnumLabels`, collapsed the seeder's three near-identical methods,
named the arguments at every `Create`/`Update` call site, fixed a v4 CSS token that had no fallback, gave
an orphan markup class its rule, made four no-op `async Task` handlers `void`, reverse-ordered the
migration's `Down()`, and added arrange/act/assert markers to the new tests.

The spec axis returned PARKED on three gaps — the missing code surface, the unamended redesign spec, and
the landing search — all now closed. Both axes reviewed the tree at `HEAD` rather than the commit delta
(the reviewers had no shell access), so the commit boundaries were checked by the parent instead.

## Follow-ups deliberately not taken

- `ApplicableTemplateDto` (the grade-detail applicability payload) still carries no title; the grade
  card lists templates by its own label. Extending that wire shape is a separate decision.
- The MAUI head build, deferred to the pre-merge gate.
