# Assignment notification templates — feature spec

> **Status: adopted design, NOT implemented** — grill rounds 1–6 (2026-10-15), owner confirmed all
> recommendations with amendments (tags without dots; SMS first-class; constraint-scoped templates;
> per-sendout medium restriction; grade-detail applicability card). This file is the source of truth for the feature. Companion spec:
> `documents/specs/assignment-approval-workflow.md` (separate round). **No code has been written** — the
> owner selected the **full four-agent round (Tier 3, attended)** on 2026-10-15 (Q21); round start
> deferred to 18:00 the same day, base `cec3c952`.

> **Authoring surface superseded (2026-10-20).** §3.10 (the tabbed editor) is replaced by
> `documents/specs/notification-template-authoring-redesign.md`: landing grid + per-template detail with
> right-rail `PanelSection`s, dropdowns instead of tabs, per-template audit. §3.1 (blueprint note), §3.7
> (owner precedence) and §5 (API surface) are **amended** by that spec. Everything else here — renderer,
> tags, constraints semantics, audit model, publish selection — still governs.

## 1. Current state (findings, 2026-10-15)

- Messages are rendered by the **hardcoded** static `NotificationMessageBuilder` — four kinds
  (`Publish`/`Reminder`/`Completion`/`Overdue`) × two channels (Email / SMS-WhatsApp), each a one-line
  body + `/deeplink/{token}` anchor. No template store or editing UI exists anywhere.
- Payload is **rendered and persisted at queue time** on the `NotificationLog` row (ar-16 decision (b) —
  retries re-send byte-identical); **one consolidated message per contact** (decision (e)); recipients
  without an unexpired token / address are terminal `Skipped` (decision (c)).
- Publish flow mints a contact-scoped deep-link token per recipient (expiry = policy `LinkValidityDays` ?? 7),
  applies `NotificationRecipientFilter`, broadcasts via `AssignmentNotificationBroadcaster`; the dispatch
  drain sends via MailKit SMTP (`NullEmailSender` when unconfigured) with a 5-attempt retry cap.
- **WhatsApp is a first-class channel end-to-end**: `ContactChannel` (Students) = `Email = 0, SMS = 1,
  WhatsApp = 2` (mirrored numerically by `NotificationChannel` in Core.Abstractions); contacts UI authors
  WhatsApp numbers with country-code selection; `PreferredChannelOrder` / `BlockedChannels` govern it like
  any channel; WhatsApp rows render the **SMS text body** (the shared-body precedent) and dispatch through
  `LogAndSkipSmsSender` (D-2: no SMS/WhatsApp provider in v1); `NotificationLog.Channel` keeps the actual
  channel, so WhatsApp sends stay distinguishable.
- **Sendout governance today**: a recipient's medium is their subscribed contact row's channel (a contact
  row *is* an email address or phone/WhatsApp number — a guardian with both an email and a mobile contact
  receives on both, subject to policy). Channel governance is tenant/grade notification policy
  (`PreferredChannelOrder`, `BlockedChannels`, `MaxNotifications`); counts are capped by the assignment
  policy's role caps (`MaxPrimaryContacts` for Primary guardians, `MaxCopyContacts` for others; students
  exempt) with `MaxNotifications` as the sendout-wide cap — the smaller limit wins. The publish dialog
  already lets the teacher pick a **contact subset** (per-contact checkboxes → `PublishAssignmentRequest(ContactIds)`),
  applied **before** the policy filter; the scheduled sweep publishes with `null` (all subscribed contacts).
- **Latent gaps:** the email href is **relative** (`/deeplink/{token}` — no Families public base URL is
  configured anywhere, so the link is broken in a real email client); the SMS body embeds the ~300-char
  protected token — unusable in a 160-char SMS; there is **no per-sendout medium restriction** (email-only /
  SMS-only) and **no template applicability model** (one template per kind × channel cannot fit all
  assignments); nothing records which template (if any) produced a message, and template changes would be
  unaudited.
- `TenantNotificationPolicy` carries reserved-but-unused `SendoutTimeOfDay` / `SendoutIntervalMinutes`.

## 2. Settled decisions (grill record)

| # | Decision | Owner answer |
|---|---|---|
| Q1 | Templates are **tenant-scoped, user-editable**, stored in the **Settings** context, one row per tenant per kind × channel; a missing row = built-in default. **Amended by Q15**: the "no per-grade override" posture is replaced by a **constraint set** on each template (a single grade-override *row* stays rejected — multiple constraint-scoped templates are the richer model). | as recommended, amended by Q15 |
| Q2 | Publish email carries a **short preview** (title, due date, capped description excerpt) plus a prominent link; instructions/materials/questions stay behind the link. Body stays a queue-time snapshot. | as recommended |
| Q3 | New **deployment-time parameter: Families public base URL** (AppHost `Parameters:`; fail-closed default; canonical record in the AppHost `appsettings.json`; dev posture in `appsettings.Development.json`; fan-out only to the Assignments API; `documents/configuration.md` §2 mapping in the same PR). Unset → keep today's relative href (dev/MailPit posture). | as recommended |
| Q4/Q7 | **Tag vocabulary** (§3.2): dot-free PascalCase, case-insensitive matching, unknown tags left literal. | amended (no dots), then as recommended |
| Q5/Q8 | **SMS is a feature requirement**: SMS body templates editable per kind, with an **embedded repo-owned shortened link** (no external shortener). | amended (SMS in scope), mechanism as recommended |
| Q6 | **Admin editor page** with tag reference chips + live rendered preview; sanitization at save **and** re-validation at render; test-send deferred. | as recommended |
| Rich editor | **Reuse the existing Quill stack** (round `wysiwyg-instruction-editor`): `Blazored.TextEditor` over vendored Quill 2.0.3, `RichInstructionEditor` / `RichInstruction` / `RichInstructionSanitizer` in Admin.Shared. No new packages, no new interop. | resolved by repo fact, 2026-10-15 |
| Q15 | **Template applicability = constraint set** (the Targets-&-Audience analogue): audience dimensions (grade levels, streams, activity groups — never individual students) + content dimensions (subject/topic, assignment type). Unconstrained template = tenant-wide default. A template applies when **every** constrained dimension intersects the assignment. **One template per sendout** (per kind × channel); per-recipient template splitting deferred. | as recommended |
| Q16 | **Author-confirmed selection**: publish dialog shows the constraint-narrowed candidate list; most-specific match pre-selects; a tie pre-selects the default and the author picks; the confirmed choice is **stamped on the assignment** (`PublishEmailTemplateId` / `PublishSmsTemplateId`, cross-context Guids, no FK). Sweep-fired kinds (Reminder/Completion/Overdue) resolve automatically: most-specific, ties → default, editor flags identical constraint sets as a conflict warning. | as recommended |
| Q17 | **Audit**: `NotificationLog` gains nullable **`TemplateId`** (which template rendered each sent message; null = built-in default); Settings gains **`TemplateAuditEntry`** mirroring `FlagAuditEntry` (change kind + before/after subject/body) for every template mutation, surfaced on the Admin template page like `/config-flags` audit; recipient rows stay timestamp-only; **no `PublishedBy`** — deferred with the identity seam (as `ApprovedBy`). | as recommended |
| Q18 | **One round** — constraints + selection + audit fold into the `assignment-notification-templates` round, workstream-sliced (a split would re-touch the same store/resolver/editor/dialog twice). | as recommended |
| Q19 | **Per-sendout medium restriction**: publish dialog gains a medium field — "Per policy" (default) / "Email only" / "SMS only" (**SMS only = SMS + WhatsApp**) — carried on the publish request like `ContactIds`, per-publish, not persisted; enforced server-side after the contact-subset filter and inside the policy filter (**blocked channels still win** — Email-only cannot unblock a blocked channel). | as recommended |
| WhatsApp | WhatsApp **rides the SMS template slot** in v1 (the shared-body precedent: same body, same short link, same log-and-skip stub). A separate WhatsApp template slot is deferred to the provider round — it would be dead UI today (no WhatsApp provider). | resolved by repo fact, 2026-10-15 |
| Q20 | **Surfaces**: one management surface — the editor lives in the **Settings module** of the Admin host (`SchoolCollab.Settings.Application`, the `/config-flags` precedent); grade detail gains a **read-oriented "Notification templates" applicability card** (per kind × channel: templates whose constraints can apply to this grade + the default highlight + a "Manage templates" deep link into the Settings editor, pre-filtered to the grade). **No editing on the grade page.** | as recommended |

**Spec-time refinement (recorded, reversible):** the template body allowlist **is** the existing
`RichInstructionSanitizer` tag set — `p, br, strong, em, u, s, ol, ul, li, h1–h4, a` (href only;
http/https/mailto only; no `style`, no other attributes). The grill's "bold/italic/links/paragraphs/lists"
maps onto it with `u`/`s`/headings riding along. Rationale: the one-sanitizer doctrine from the
`wysiwyg-instruction-editor` round — a second, slightly-different sanitizer policy is surface with no job.
Tags (`{{…}}`) are substituted **after** sanitization, so a template author can never emit markup via a tag.

## 3. Design

### 3.1 Template store (Settings context)

`TenantNotificationTemplate` (Settings.Core, settings-db) — one row per `(TenantId, Kind, Channel)`
(unique index), `BaseTenantEntityWithAudit` + `IHasRowVersion` (the `TenantAssignmentPolicy` precedent):

| Column | Type | Notes |
|---|---|---|
| `Kind` | `NotificationKind` (Publish/Reminder/Completion/Overdue) | stored + travelled as the **enum name** (`HasConversion<string>` + type-level `JsonStringEnumConverter`, the assignment-policy wire precedent) |
| `Channel` | `NotificationChannel` — **two template slots: Email and Sms** | WhatsApp recipients render the **Sms** template (§ WhatsApp decision) |
| `Subject` | `string?` | email only; SMS rows ignore it |
| `Body` | `string` | sanitized HTML (email) or plain text (SMS — rendered for both SMS and WhatsApp recipients) |

A missing row ⇒ built-in default. Applicability lives in child rows (§3.1.1).

**Amended 2026-10-20 (redesign spec Q16):** a system blueprint layer now exists — `tenant_id IS NULL` rows in the same two tables, authored only from the system scope. A missing row still means the built-in default, which remains the terminal fallback; the store is no longer tenant-only.

#### 3.1.1 Constraints (applicability)

`TemplateConstraint` child rows (settings-db), one row per constrained value — the `AssignmentTarget` shape:

| Column | Type | Notes |
|---|---|---|
| `TemplateId` | Guid | parent template |
| `Kind` | `GradeLevel` / `Stream` / `ActivityGroup` / `Subject` / `AssignmentType` | the dimension |
| `RefId` | `Guid?` | entity id for entity dimensions (grade level, stream coded value, activity group, topic); null for `AssignmentType` |
| `Value` | `string?` | enum name for `AssignmentType` (the enum-name wire precedent); null otherwise |

**Matching rule:** a template matches an assignment iff **every** constrained dimension present in its
constraint set intersects the assignment — `GradeLevel`/`Stream`/`ActivityGroup` against the assignment's
`Targets` rows of the same kind, `Subject` against `TopicId`, `AssignmentType` against its type. An empty
constraint set = the tenant-wide default template. **Specificity = the count of constrained dimensions**;
two templates with equal specificity that both match = a **tie** (no further tie-break — the author breaks
it at publish; sweep-fired kinds fall back to the default).

### 3.2 Tag vocabulary

| Tag | Renders |
|---|---|
| `{{Title}}` | assignment title |
| `{{Description}}` | assignment description (capped excerpt in the built-in preview; full value if the author places it) |
| `{{DueDate}}` | due date, empty when none |
| `{{Instructions}}` | student-facing instructions — **email only** (ignored/stripped in SMS/WhatsApp) |
| `{{StudentName}}` | the recipient's ward (student display name) |
| `{{GuardianName}}` | the guardian contact's display name (empty on student-owned recipients) |
| `{{GuardianRole}}` | `Primary` / other guardian role text |
| `{{SchoolName}}` | tenant display name (from the tenant context's `tenant_name`; empty fallback) |
| `{{Link}}` | **channel-appropriate**: absolute `/deeplink/{token}` URL (email) or absolute `/l/{code}` short URL (SMS and WhatsApp) |
| `{{LinkExpiry}}` | the link's expiry date |

Matching is **case-insensitive**; an unknown tag stays **literal** (visible in the live preview rather
than silently vanishing). Tag **values** are HTML-escaped in email rendering and inserted as plain text in
SMS/WhatsApp.

### 3.3 Renderer + built-in defaults

A pure static `NotificationTemplateRenderer` replaces `NotificationMessageBuilder` as the render path
(the builder's texts remain the built-in default source, upgraded to the Q2 preview shape — e.g. Publish
default: subject `New assignment: {{Title}}`; body = a preview paragraph (bold title, due date, one-line
description excerpt) + the link anchor). Resolve template (§3.1.1 matching, then Q16 selection) →
substitute → escape → re-sanitize (email) → persist on the `NotificationLog` row **together with the
`TemplateId`**. Queue-time render is **preserved** (ar-16 (b)): a template edit never mutates an
already-queued row; the republish upsert-in-place path re-renders with the freshest template, exactly as it
re-stamps deep-link payloads today.

Cross-context resolution is **fail-open** (the `IAssignmentPolicyResolver` posture): a Settings outage
falls back to built-in defaults — a template outage can never block a publish.

### 3.4 Name enrichment (cross-context reads)

Widen the existing subscription-resolution payload (`SubscriberInfo` in the Students API response) with
`ContactName` (`Guardian.DisplayName` for guardian-owned rows) and `StudentName` (the student's
person-demographic name) — populated where the rows are already read, **no second hop**. `GuardianRole`
rides the existing `Role` on the recipient. `SchoolName` comes from the tenant context (`tenant_name`
claim, in-process). Every name degrades to **empty string** on absence/failure — an enrichment hop never
fails the parent operation.

### 3.5 Families public base URL (absolute links)

AppHost `Parameters:` entry `families-public-base-url` — fail-closed base default, dev posture committed in
the AppHost's `appsettings.Development.json`, fanned to the **Assignments API only** (the Families app builds
relative redirects and needs nothing). Same PR updates the `Parameters:` block in the AppHost
`appsettings.json` (canonical record, `startup-flag-governance.md`) and `documents/configuration.md` §2.
Renderer: parameter set ⇒ absolute URLs; unset ⇒ today's relative href.

### 3.6 Short links (SMS/WhatsApp)

`ShortLink` (Assignments.Core, assignments-db): `Id`, `TenantId`, `AssignmentId`, `RecipientId`,
`Code` (10-char Crockford-base32 random, unique index, collision re-roll), `DeepLinkToken` (the recipient's
token), `ExpiresAt` (mirrors the recipient's `DeepLinkExpiresAt`), audit columns.

- **Minting**: at publish, for every recipient whose channel is SMS **or WhatsApp** (email keeps the full
  token); idempotent ensure-minted (re-mint when missing/expired) at render time for the E3 kinds — the
  same posture as deep-link tokens.
- **Serving**: public `/l/{code}` route on the Families app, in the same no-auth group as `/deeplink`
  (bearer code is the credential): cross-tenant lookup, tenant established from the row, unknown/expired
  code ⇒ friendly `LinkExpired` page; valid ⇒ 302 to `/deeplink/{token}` — the **existing** landing then
  validates, signs in, and stamps `OpenedAt`. No third-party shortener (tokens are contact-scoped
  credentials; they never leave the platform).

### 3.7 Template selection at publish (Q15/Q16)

- **Publish dialog** (`PublishDialog.razor`): a template picker showing the constraint-narrowed candidate
  list per channel, with the **most-specific** match pre-selected; a tie pre-selects the tenant default and
  the author must pick to send a constrained template. The selected ids ride the publish request and are
  **stamped on the assignment**: `PublishEmailTemplateId` / `PublishSmsTemplateId` (nullable Guid columns,
  cross-context ids, **no FK across contexts**). Republish reuses the stamped choice; clearing the stamps
  returns to constraint/default resolution; a **deleted stamped template degrades to constraint/default
  resolution, never blocks publish**.
- **Sweep-fired kinds** (Reminder/Completion/Overdue — no dialog exists): automatic resolution — most
  specific match, tie → tenant default. The template editor shows a **conflict warning** when two templates
  of the same kind × channel carry identical constraint sets (the practical ambiguity check).

**Owner precedence (2026-10-20 amendment).** With the system blueprint layer added by
`notification-template-authoring-redesign.md` §3.7–§3.9, the candidate set is tenant ∪ system: a tenant
row shadows a system row with an identical constraint set, equal specificity resolves tenant-first, and
the Q67 default-promotion rule applies within each scope. Stamps, the publish dialog's two slots and the
fail-open fallback are unchanged.

### 3.8 Per-sendout medium restriction (Q19)

`PublishAssignmentRequest` gains an optional `Channels` field: **Per policy (default/absent)** /
**Email only** / **SMS only** (= SMS **+ WhatsApp**). Per-publish, **not persisted** (the scheduled sweep
republishes per-policy). Enforced **server-side** in the publish handler: applied after the
`ContactIds` subset filter and inside `NotificationRecipientFilter` — **blocked channels still win**
(Email-only cannot unblock a tenant-blocked email channel; SMS-only respects `BlockedChannels` for
SMS/WhatsApp). One radio group in the publish dialog. Rationale: once a real SMS provider lands each
message costs money — a one-click "formal notice, email only" / "urgent, SMS only" prevents accidental
paid-channel blasts.

### 3.9 Audit (Q17)

- **`NotificationLog.TemplateId`** (nullable Guid): the send-time record of which template rendered each
  message — null = built-in default. One column; answers "which template did this parent actually receive".
- **`TemplateAuditEntry`** (Settings.Core, settings-db), mirroring `FlagAuditEntry`: change kind
  (create/update/delete) + before/after subject/body (and constraints summary) + timestamps; surfaced on
  the Admin template page the way `/config-flags` shows its audit.
- Recipient rows stay timestamp-only (they already persist the full cohort with channel/role/token/
  `OpenedAt`/`DeliveredAt`). **No `PublishedBy`** actor stamp — the same identity placeholder seam as
  `ApprovedBy`; deferred with it.

### 3.10 Management surface — Settings module (superseded 2026-10-20)

**Superseded by `documents/specs/notification-template-authoring-redesign.md`.** The tabbed
master–detail page described here was replaced by a landing grid + per-template detail page (right-rail
`PanelSection`s for Constraints and Preview, per-template audit, dropdowns instead of tabs). The
requirements that survive unchanged: one management surface in the Settings module (hub card + NavMenu
entry), the rich email editor, the plain-text SMS body with rendered-length guidance, tag chips, the
live preview through the exact render path, sanitize-on-save, audited save/reset. The kind/channel
**tabs** are retired, and WhatsApp is now an authorable contact mode (redesign spec §3.10).

### 3.11 Grade-detail applicability surface (Q20)

A **read-oriented "Notification templates" card** on grade detail
(`Students.Application/Components/Pages/Students/GradeLevels/Detail.razor`), sibling to the existing
Notification & Delivery and Assignment Policy cards — completing the grade's notification-configuration
trio. Per kind × channel it lists the tenant templates whose constraints **can apply to this grade**
(grade dimension absent or containing this grade; other constraints displayed), highlights the tenant
default, and carries a **"Manage templates" deep link** into the Settings editor pre-filtered to this
grade — creating a grade-specific template is one click, with the `GradeLevel` constraint pre-filled.
**No editing affordances on the grade page** — one management surface (§3.10), one sanitizer/audit/test
path. Data source: the §5 applicability query via the shared `NotificationTemplateApiClient`
(Admin.Shared — the Students module already consumes Admin.Shared services).

### 3.12 Sanitization

One sanitizer, two applications: on **write** (editor save) and on **render** (the outbound email body);
SMS/WhatsApp bodies are plain text (tag values text-only, no HTML). No second sanitizer policy exists (§2 refinement).

## 4. Data model + migrations

| Database | Migration |
|---|---|
| settings-db | `TenantNotificationTemplate` + `TemplateConstraint` + `TemplateAuditEntry` tables |
| assignments-db | `ShortLink` table; `Assignment.PublishEmailTemplateId` / `PublishSmsTemplateId` (nullable Guids); `NotificationLog.TemplateId` (nullable Guid) |

All follow `.github/copilot/rules/ef-migrations.md`.

## 5. API surface

| Endpoint | Behaviour |
|---|---|
| `GET /api/settings/notification-templates` | all tenant rows + constraints (Admin page list) |
| `GET …/{kind}/{channel}` | one row; **204 = unset** (built-in default applies) — the assignment-policy endpoint posture |
| `PUT …/{kind}/{channel}` | upsert `{ subject?, body, constraints }` (subject ignored for SMS); every mutation writes a `TemplateAuditEntry` |
| `DELETE …/{kind}/{channel}` | reset to built-in default (audited) |
| `GET /api/settings/notification-templates/applicable/{gradeLevelId}` | **new**: the grade-detail card's data — per kind × channel, the templates whose constraints can apply to that grade + the tenant-default flag |
| `GET /assignments/{id}/publish-options` | **new, Assignments side**: the constraint-narrowed template candidates per channel + the most-specific suggestion + medium-availability summary (which channels the policy leaves unblocked) — the publish dialog's picker source; the assignment's dimensions live in this context, so resolution belongs here |
| `POST /assignments/{id}/publish` | request gains optional `Channels` (§3.8) and template selection (stamped per §3.7) |

**Amended 2026-10-20 (redesign spec §5):** the store now also exposes `GET …/{id:guid}`,
`POST …/{id}/adopt`, `POST …/{id}/promote` (system scope), an owner projection on every template DTO, and
an optional `templateId=` filter on the audit route. The `{kind}/{channel}` routes above remain valid.

Client: `NotificationTemplateApiClient` (Admin.Shared, the `AssignmentPolicyApiClient` precedent).
Assignments side: `INotificationTemplateResolver` (interface in Assignments.Core, HTTP implementation in
Assignments.Api — the established cross-context resolver split; fail-open to built-in defaults). Read DTOs
gain the two template-id stamps.

## 6. Sweep integration

All four kinds render through the template resolver: the publish broadcaster (Publish — with the stamped
selection) **and** the Worker's E3 queue paths (`SweepNotificationQueuer` — Reminder/Completion/Overdue,
auto-resolved per §3.7), so an edited Reminder template actually takes effect. SMS/WhatsApp-channel rows in
every kind use the short link.

## 7. Out of scope (v1)

- **Real SMS/WhatsApp provider** — `LogAndSkipSmsSender` stays; templates + short links + the medium
  restriction are provider-ready; a provider round is the natural follow-up (and is where a separate
  WhatsApp template slot becomes worth having).
- **Per-recipient template splitting** (one sendout, different templates per grade) — deferred.
- Individual-student constraints on templates (a template scoped to one student is the wrong tool — that
  is what the assignment's own targets and the contact-subset picker do).
- Per-grade template *rows* (the grade-policy override shape) — replaced by constraint-scoped templates (Q15).
- Test-send; per-tenant branding (logo/colours); bounce/webhook ingestion.
- Approval-flow author/approver notification emails (identity placeholder — see the companion spec §7).
- `PublishedBy` / actor stamps (identity round, with the `ApprovedBy` seam).
- Activation of the reserved `SendoutTimeOfDay` / `SendoutIntervalMinutes` policy fields.

## 8. Verification surface

| Contract | Discriminating test |
|---|---|
| Tag substitution: case-insensitivity, unknown-literal, value escaping, `{{Instructions}}` email-only, `{{Link}}` channel-appropriate (email long URL vs SMS/WhatsApp short URL) | `NotificationTemplateRenderer` unit tests |
| Built-in defaults = upgraded preview shape | renderer default tests per kind × channel |
| Constraint matching: empty set = default; every-dimension intersect; specificity = constrained-dimension count; ties detected | constraint-resolution unit tests |
| Selection: stamp persisted on publish; cleared stamp → constraint/default; deleted stamped template degrades, never blocks | publish-handler tests |
| Medium restriction: Per policy/Email only/SMS only (= SMS+WhatsApp); applied after contact subset; **blocked channels still win**; sweep republish per-policy | filter + handler tests |
| Short link: mint idempotency, uniqueness, expiry mirror, landing redirect + expired-code page | `ShortLink` + `/l/{code}` route tests |
| Base URL: set ⇒ absolute, unset ⇒ relative; AppHost parameter fan-out recorded in `Parameters:` | renderer tests + configuration documentation checks |
| Resolver fail-open (Settings outage ⇒ defaults, publish proceeds) | resolver tests |
| Queue-time payload semantics unchanged + `TemplateId` persisted (null = default); retry idempotency; republish re-render | broadcaster/dispatcher suites extended |
| `TemplateAuditEntry` written on create/update/delete with before/after | Settings endpoint tests |
| Admin editor: tabs, chips, preview, constraints editor, conflict warning, save/reset, SMS length warning; publish dialog: picker + medium radio | bUnit page tests (MTP, `.github/copilot/rules/testing.md`) |
| Grade-detail applicability card: lists grade-applicable templates per kind × channel, default highlight, "Manage templates" deep link; read-only (no editing affordances) | bUnit tests (grade detail page) |
| Applicability query: grade-dimension filtering (absent OR contains grade), default flag, other constraints surfaced | endpoint tests |

## 9. Execution plan (slicing)

**One round** — `assignment-notification-templates` (Q18), first of the two-round train, followed by
`assignment-approval-workflow` (companion spec). Sequential on `main` — each its own branch + PR + round
doc per the merge policy; not a stacked train (no layer dependency). Order rationale: this feature is
additive config; the approval round's future author/approver notifications land on this mechanism; it is
also the conversation's origin.

Workstreams inside the round (slices for the worker, in dependency order):

1. Template store + constraints + audit entity + Settings endpoints + migrations (settings-db).
2. Renderer + `INotificationTemplateResolver` + queue-time integration (`NotificationLog.TemplateId`)
   + name enrichment (assignments-db migration: stamps + `TemplateId`).
3. Short links (`ShortLink` minting + Families `/l/{code}` route) + Families public base URL parameter.
4. Publish flow: selection stamps + `publish-options` endpoint + medium restriction (dialog + handler + filter).
5. Management surface: Settings-module editor page (Settings hub + NavMenu wiring, tabs, Quill editor
   reuse, chips, preview, constraints editor, conflict warnings, audit panel) + `NotificationTemplateApiClient`
   + the grade-detail applicability card (Students module).
6. Sweep integration (E3 kinds auto-resolution) + full verification matrix.

**Mode (owner-selected 2026-10-15, Q21): full four-agent round (Tier 3), attended** — the Admin editor
page and publish-dialog UI fire the skill's deterministic UI trigger, and the work crosses Settings,
Assignments Core/Api/Worker, Families, Admin/Admin.Shared plus two databases. Not unattended: the round
contains **migrations**, which always reach a human unless the owner pre-authorises the round by name.
**Round start deferred by the owner to 18:00 (2026-10-15)**; start from base `cec3c952` per the round
skill's step 0 (`git rev-parse HEAD` + the dirty-tree file list — the two untracked spec files ride the
round and are committed only on explicit instruction).

## 10. As-built status (recorded 2026-10-18, after Round B1 acceptance)

**Round A** — PR #362, merged as `d3328320`: template store (`TenantNotificationTemplate`),
`TemplateConstraint` + dimension enum, `TemplateAuditEntry` + recorder, Settings endpoints, the
settings-db migration, 9 wire tests.

**Round B1** — accepted 2026-10-18 (Tier 3 lean), branch `feat/notification-templates-b1`, base
`4fb36f28`, round doc `documents/rounds/round-notification-templates-b1.md`:

- **Delivered:** the ten-tag §3.2 vocabulary (`All` in spec order, Round-A aliases honoured);
  pure renderer with channel-appropriate escaping (`{{Instructions}}`/`{{Description}}` stay authored
  markup, body re-sanitized); `{{Description}}` renders the full value (the 120-char cap now lives only
  in the built-in preview body); `{{GuardianName}}`, `{{GuardianRole}}`, `{{StudentName}}`,
  `{{SchoolName}}`, `{{LinkExpiry}}` all resolve with degrade-to-empty semantics; name enrichment rides
  the existing subscription read (no second hop); `NotificationLog.TemplateId` (nullable; null =
  built-in default); **one** built-in default source (`NotificationMessageBuilder`, 4 kinds × 2
  channels) that the renderer delegates to; `INotificationTemplateResolver` in `Assignments.Core` with a
  Core-local wire mirror (no `Settings.Core` reference) registered on **both** hosts after
  `AddAssignmentsCore`; fail-open covers 204/500/timeout/non-JSON-200 while genuine cancellation still
  propagates; the sanitizer was relocated to `SchoolCollab.Core.Abstractions` (one copy, no new project
  reference).
- **Deferred, with owner workstreams:** SMS/WhatsApp short links (`/l/{code}`) → **B2 (WS3)**; publish
  stamps + `GET /assignments/{id}/publish-options` + the medium restriction → **B2 (WS4)**; E3 sweep
  resolver wiring → **B2 (WS6)**; `families-public-base-url` fan-out + the `documents/configuration.md`
  §2/§11 rows → **B2**; editor page, publish dialog, grade-detail card, `NotificationTemplateApiClient`
  → **B3 (WS5)**.
- **Carried residual:** the older `AssignmentPolicyResolver` still catches only `HttpRequestException`
  (the same hardening gap closed here for templates) — later round.
- **Slicing correction:** §9's original "one round" recommendation was superseded by the adopted
  **A → B1 → B2 → B3** cut. The round docs are the record; this spec keeps the contract only.

**Round B2a** — accepted 2026-10-19 (Tier 3 lean), PR #372, merged as `58b8d173`, round doc
`documents/rounds/round-notification-templates-b2a.md`:

- **Delivered:** short links end-to-end — `AssignmentRecipient.ShortLinkCode` (nullable, **global**
  filtered unique index, 16-char CSPRNG codes with collision re-roll), minted only for SMS/WhatsApp
  recipients atomically with the token, `NotificationLinkOptions.BuildDeepLink` channel-driven so email
  always keeps `/deeplink/{token}`, anonymous **cross-tenant** resolution via a single-row
  `IgnoreQueryFilters` lookup returning only the token, Families `GET /l/{code}` → 302 →
  `/deeplink/{token}` with unknown/expired landing on the existing LinkExpired page, the code never
  logged; the **medium restriction** enforced at both previously-inert seams (route → command →
  handler), inside `NotificationRecipientFilter` after the `ContactIds` subset and before the caps,
  blocked channels still winning; E3 sweep kinds rendered through the template path with `TemplateId`
  recorded both ways, **ensure-mint** giving pre-existing SMS recipients a code at render time;
  `Assignment.PublishEmailTemplateId`/`PublishSmsTemplateId` + migration with the full lifecycle
  (request-null = reuse; cleared → live template → built-in default; deleted/foreign degrades and never
  blocks publish); `GET /assignments/{id}/publish-options` (`TypedResults`, tenant-scoped, 404 foreign);
  both `AssignmentPolicyResolver` and `NotificationPolicyResolver` widened to the fail-open contract
  (Q49) with genuine cancellation propagating.
- **Discriminated slot read (spec §3.3 + §3.8/§5 both hold):** `NotificationTemplateSlotState`
  {`NotSet`, `Unreadable`, `Found`} — a Settings outage accepts an explicit stamp with a warning
  (fail-open, §3.3) while a genuine `NotSet` or `Found` mismatch returns the typed 400.
- **Deviations from §3.6, recorded (Q61):** short links live on the recipient row (not a separate
  `ShortLink` table) with 16-char URL-safe codes (not 10-char Crockford-base32); the deviation exists
  because the code mirrors the token row's expiry with no join on the public resolution path.
- **Corrections of B1's own residual list:** `families-public-base-url` was **already complete** (Round
  A: `AddParameter`, fan-out, both appsettings rows, both `configuration.md` rows); the publish request
  fields (`Channels`, both stamp ids) **existed on the wire but were inert** at two seams — the route
  dropped them and the handler ignored them — so "medium restriction" and "stamps" were enforcement
  work, not contract work.
- **Q16 amendment (2026-10-19, Q67):** for the equal-specificity **non-default** tie the spec was
  silent; the deterministic rule is **default first, then the oldest `CreatedAt`** — `UpdatedAt` moves
  on every edit, so it would silently change the winner whenever an unrelated field is edited.
- **Slicing correction (Q63):** B2 split into **B2a → B2c → B2b** — Round A's unique index on
  `(TenantId, Kind, Channel)` filtered `is_deleted = false` permits one live template per slot, so the
  §3.1.1 matcher has no substrate; relaxing the index **requires** converting the Upsert writer to
  create/update-by-id in the same round (`SingleOrDefaultAsync` on the natural key throws the moment a
  second candidate exists), and the matcher moves to **B2c** with the settings-db index, the
  candidates-with-constraints read and the publish-time evaluation. The publish dialog is **B2b**;
  the editor page, the grade-detail applicability card and `NotificationTemplateApiClient` stay **B3**.
- **Deferred, with owner workstreams:** the §3.1.1 evaluation + settings-db index relaxation +
  candidates read + writer identity → **B2c**; the publish dialog → **B2b**; editor page,
  grade-detail card, `NotificationTemplateApiClient` → **B3**.
