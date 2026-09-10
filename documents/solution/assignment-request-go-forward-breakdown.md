# Assignment Request (AR) Go-Forward Breakdown — Situation Analysis & Work Plan

> **Source of truth:** `documents/specs/assignment-request-feature-spec.md` (the
> AR spec). This doc situates that spec against the shipped repo features,
> inventories what is reused vs. net-new, and breaks the work into
> dependency-ordered workstreams with acceptance criteria. Per
> `documents/README.md` this is a findings/work-tracking doc — it lives in
> `solution/`, not `specs/`.
>
> **Related prior specs (subsumed by the AR spec):**
> - `specs/assignment-creation-with-ai.md` — server-side AI question generation
>   attached to the create wizard. This is the AR spec §3.4 core, already
>   fully specified with a decision log. Execute it as-is; extensions land on top.
> - `specs/notification-delivery-plan.md` — policy config + effective
>   resolution (shipped). Its deferred "§18 delivery" is the AR spec §5.
>
> **Analysis date:** 2026-09-03. Current repo state: Assignments context has
> Draft/Published/Closed lifecycle, recipients, submissions + versions, a
> guardian pre-submission gate, teacher reviews, notification *policy* (no
> delivery), and a generic AI chat engine with a CodedValues tool provider.

---

## 1. Situation analysis — spec section vs. shipped state

### Legend
- ✅ shipped & reusable · 🟡 partially exists (extend) · ❌ net-new

### §3.1 Google Classroom core

| Spec feature | State | Reusable asset / gap |
|---|---|---|
| Roster/class/group targeting | ✅ | `TargetAudienceType` (AllStudents/SelectedGrades/SelectedGroups) + `AssignmentActivityGroup` links + `GradeLevelId`/`TopicId`. AR spec adds *individual wards* — new audience value or per-ward recipient rows. |
| Due date / late policy | 🟡 | `DueDate` exists. No `available_from`, no late-submission policy field. |
| Topics/categories | ✅ | `Topic` entity (Students context), `Assignment.TopicId`. |
| Materials (files/links/video) | 🟡 | `AssignmentAttachment` entity exists but has **no aggregate methods and no storage backing**; no URL/video resources. `assignment-creation-with-ai.md` FR-210–212 already specs file uploads + `AddAttachment`. |
| Draft → Scheduled → Published | 🟡 | `Draft → Published → Closed` (+ Unpublish). No Scheduled state, no auto-publish-at date. |
| Reuse/duplicate as template | ❌ | Net-new copy command. |
| Per-ward progress + rubric grading | 🟡 | `AssignmentSubmission` (current) + `AssignmentSubmissionVersion` (immutable history) + `SubmissionReview` (score/grade/comments) exist. No rubric, no structured answers, no progress %. |
| Comments/private feedback thread | 🟡 | `SubmissionReview.Comments` is a single note, not a thread. |

### §3.2 DropSign signature workflow

| Spec feature | State | Reusable asset / gap |
|---|---|---|
| Ward completes → guardian signs → locked | ❌ | **Direction differs from shipped gate.** `GuardianSubmissionGate` is a *pre-submission* review (guardian enables student to submit, or submits on behalf). AR sign-off is *post-completion*. Reuse the per-(assignment, student) gate-row pattern + `MandatoryReview` flag pattern; do **not** mutate gate semantics — add a new signature stage. |
| Guardian e-signature (typed/drawn/click) | ❌ | Net-new `SignatureEvent` entity + sign-off UI. |
| Status chain Sent→Viewed→…→Finalized | 🟡 | `AssignmentRecipient.DeliveredAt`/`OpenedAt` cover Sent/Viewed. In-Progress/Completed/AwaitingSignature/Signed/Finalized need a per-(assignment, ward) status — extend submission/gate or new `WardProgress` entity. |
| Auto-reminders | 🟡 | Policy fields shipped (`MaxReminders`, `ReminderIntervalHours`, `SendoutTimeOfDay`) but stored-only; no worker, no channel. |
| Audit trail (IP/device/consent) | ❌ | `SignatureEvent` net-new; base audit (`IAuditableEntity`) exists. |
| Certificate PDF | ❌ | Net-new: PDF generation lib (decision), file storage (decision — none exists repo-wide). |
| Guardian delegation | 🟡 | `StudentGuardian` + `GuardianRole` (Primary/CC) + `GuardianNameHistory` exist; delegation flow net-new. |

### §3.3 Proofpoint content & assessment

| Spec feature | State | Reusable asset / gap |
|---|---|---|
| Modules (video → guide → questions) | ❌ | `ContentModule` entity net-new (type, order, `min_completion_threshold`). |
| Gating (unlock questions after module complete) | ❌ | Per-(ward, module) progress tracking net-new. |
| Question types | ✅ | `QuestionType` (MultipleChoice/TrueFalse/ShortAnswer) + `AssignmentQuestion`/`QuestionOption` with `CorrectOptionId`. Wired into create flow by `assignment-creation-with-ai.md` (pending execution). |
| Pass threshold + retries | ❌ | `GradingFormat` (AutoGraded/InstantGraded) enum exists but **no scoring engine, no pass score, no attempt cap**. `AssignmentSubmissionVersion` ≈ spec's `SubmissionAttempt` — extend with structured answers + score + passed. |
| Immediate vs held feedback | 🟡 | `GradingFormat.InstantGraded` names the concept; behavior unimplemented. |

### §3.4 AI question bank

| Spec feature | State | Reusable asset / gap |
|---|---|---|
| Generation engine + prompt seam | ✅ | `AIChatEngine` (generic streaming + tool-call loop), `ISystemPromptProvider`, `IChatClientFactory` (Ollama/OpenRouter), `ChatModelResolver`. `assignment-creation-with-ai.md` decision 3–4 already define the dedicated endpoint (`POST /api/ai/assignments/questions`), `AssignmentQuestionGenerationSystemPromptProvider`, `IAssignmentQuestionGenerator` seam. |
| Author prompt override | ✅ (spec'd) | Per-assignment override is spec'd in `assignment-creation-with-ai.md` (decision 8). |
| Org-level system prompt (admin, locked/editable) | ❌ | AR addition. Precedent: `TenantNotificationPolicy` (Settings, one row per tenant) — store as a tenant-level `Settings` entity, not a coded value. |
| N questions / type mix / difficulty mix | 🟡 | Prior spec has count + type mix; **difficulty distribution** is an AR addition. |
| Versioned regeneration | ❌ | AR addition: regenerate creates a draft set without destroying prior edits (confirm-before-replace). |
| URL/file/video resources as input | 🟡 | File upload spec'd in prior spec; URL fetching + transcript extraction net-new. |

### §5 Notifications

| Spec feature | State | Reusable asset / gap |
|---|---|---|
| Valid-contacts-only rule | ✅ | `Contact.IsVerified` + `ContactSubscription` (opt-in, scope) + `StudentsContactResolver` already materialize subscribed contacts at publish. |
| Policy (blocked/preferred/cap) | ✅ | `TenantNotificationPolicy` + `GradeNotificationPolicy` + `EffectiveNotificationPolicyResolver` + `NotificationRecipientFilter` applied in `PublishAssignmentCommandHandler`. |
| Broadcast trigger on publish | ✅ | `IAssignmentNotificationBroadcaster` → outbox → `AssignmentPublishedIntegrationEvent`. |
| Actual delivery (email/SMS/WhatsApp) | ❌ | No provider infra repo-wide (no SMTP/SendGrid/Twilio). The deferred "§18" of `notification-delivery-plan.md`. |
| Deep links with secure token (no login) | ❌ | `LinkValidityDays` stored only. Net-new token auth + public routes. |
| `NotificationLog` + retries + failure surfacing | ❌ | Net-new entity + worker + admin surfacing. |
| Reminder worker | ❌ | Net-new scheduled worker (`students-worker` is the repo precedent for a worker project). |

### §3.5 Lifecycle & structural gaps

| Spec feature | State | Gap |
|---|---|---|
| Approval step (Approver persona) | ❌ | No approver role, no approval state. Needs feature flag + policy (spec open question 2/3). |
| Archive after grace period, exportable | ❌ | `Closed` is closest; read-only archive + export net-new. |
| **Ward / guardian-facing app** | ❌ | **The largest structural gap.** The repo ships one admin/teacher Blazor app. No ward or guardian experience exists anywhere. Deep-linked public flows (spec §5) imply a token-auth surface; authenticated family portal needs an OIDC decision. |

### Cross-cutting assets that carry most of the design

- **Tenancy:** `ITenantEntity`/`BaseTenantEntity` direct-tenancy pattern (operational data) — every new AR entity follows it.
- **Cross-context:** HTTP lookup interfaces in Core + implementations in Api (`IActivityGroupLookup`, `INotificationPolicyResolver`, `StudentsContactResolver`) + MassTransit integration events + shared outbox. No direct project refs.
- **Feature flags:** AppHost `Parameters:` → `IFeatureFlagService` fan-out; conditional endpoint auth (`FEATURE:DisableOIDCAuth` precedent).
- **UI:** `Admin.Shared` components, dialog shell pattern, section cards, dropdown/width ladder, FluentAutocomplete server-search.
- **EF:** `xmin` row versions, MigrationService runner, `NoUncommittedModelChanges` guard.

---

## 2. Decisions — resolved 2026-09-03

All spec §7 open questions and infra decisions are closed. The AR spec §7 now
carries this decision log verbatim; workstream impact is folded into §3 and §4.

### Spec §7 questions

| # | Question | Decision |
|---|---|---|
| 1 | Sign-off mandatory? | **Per-assignment `RequiresSignature` flag + grade-level default.** Default resolved at create time: grade-level override → tenant default → false (new `GradeAssignmentPolicy` + `TenantAssignmentPolicy` pair, mirroring the notification-policy precedent); author may override per AR. |
| 2 | Approval scope | Tenant-level feature flag (default off). When on, **every** AR requires approval before publish; no content-type-conditional approval. |
| 3 | AI review gate | Wizard Review step IS the author's explicit confirm of generated questions (they land as an editable set); approver sees an "AI-generated" provenance marker when the Q2 flag is on. No separate AI-only state. |
| 4 | Retry/appeal | `MaxAttempts` per AR (null = unlimited); exhaustion blocks the ward until a teacher raises the limit or overrides (`EnableStudentSubmission` precedent). No formal appeal workflow. |
| 5 | Multiple guardians | One Primary signer suffices; CC guardians notified; Primary may delegate. `GuardianRole` already models this. |
| 6 | Retention / SIS export | Indefinite archive + certificates retained; manual export (PDF/CSV) in Phase 5; no SIS integration. |

### Infra decisions

| # | Decision |
|---|---|
| D-1 | **`IFileStore` abstraction + local-filesystem implementation** (path via AppHost parameter). Azure Blob deferred until the deployment story needs it. v1 stored files are small (docs, certificates); videos are embedded URLs, never stored blobs. |
| D-2 | **SMTP email via MailKit** (`IEmailSender`/`ISmsSender` abstractions); SMS + WhatsApp stubbed (log-and-skip; `BlockedChannels` policy already filters). Config: AppHost `Parameters:` (smtp-host/port/user/pass secret/from-address). |
| D-3 | **QuestPDF** (CPM entry). ⚠️ License caveat recorded (verified v3.0, eff. 2026-07-06): Community tier covers individuals/small businesses <$1M, non-profit academics, OSS — it **excludes public-sector entities** regardless of revenue. If a production deployment is ever self-hosted by a public school district, swap to SkiaSharp (`SKDocument.CreatePdf`, MIT) — the certificate layout is simple enough that the swap is cheap. |
| D-4 | **New lightweight host `SchoolCollab.Families`** (SSR + InteractiveServer, DataProtection token middleware, no OIDC initially). **Clarified (2026-09-03): there is no identity/auth system for students/guardians yet.** "Ward" ≡ "student" — identity is the existing Students-context `Student`/`Guardian` entities with their verified `Contact` records (repo terms: Student/Guardian); deep-link tokens address contacts (E1 payload already carries contactId). No Keycloak users for students/guardians in v1. |
| D-5 | Execute `specs/assignment-creation-with-ai.md` unchanged as WS-B1; AR extensions layer additively as WS-B2. Both the AI spec and `notification-delivery-plan.md` now carry go-forward subsumption notes (added 2026-09-03). |
| D-6 | v1 keeps request-field attribution (`TeacherId`); dedicated identity round (Keycloak `sub` → `Teacher`, `ICurrentUser`) lands before Phase 5 compliance. Phases 1–4 unblocked (deep-link tokens carry ward/guardian identity). |

---

## 3. Workstreams

### WS-A — Assignment core & lifecycle (Assignments context)
- **A1. Resource model + content modules** *(AR §3.1 materials, §3.3 modules, §4 `Resource`/`ContentModule`)*
  - Execute `assignment-creation-with-ai.md` FR-210–212 (aggregate `AddAttachment`/`RemoveAttachment`, upload limits, storage seam).
  - Add `Resource` (url|file|video) rows distinct from student-facing `ContentModule` (video|guide, `order`, `min_completion_threshold`, required flag).
  - Migration + EF config; `NoUncommittedModelChanges` green.
- **A2. Lifecycle extensions** *(AR §3.5)*
  - `Scheduled` status + `AvailableFromUtc`; auto-publish worker or lazy transition on read.
  - `ApprovalStatus` (+ approver id/timestamp) gated by feature flag; publish blocked until approved when the tenant policy requires it.
  - `Archived` state after due-date + grace period (background sweep or lazy); read-only, exportable.
  - Keep `Unpublish` semantics; define transitions for new states on `Assignment` aggregate.
- **A3. Scoring & attempts** *(AR §3.3)*
  - Structured answers on submission versions (extend `AssignmentSubmissionVersion`: answers payload, `Score`, `Passed`, attempt number = version number).
  - Auto-score MC/TF/ShortAnswer against `CorrectOptionId`/expected text per `GradingFormat` (AutoGraded/InstantGraded vs TeacherGraded).
  - `PassScore` + `MaxAttempts` on `Assignment`; retry logic; teacher override.
  - Per-question feedback mode (immediate vs held) honoring `InstantGraded`.
- **A4. Template & duplication** *(AR §3.1)*
  - `DuplicateAssignment` command: copy fields + questions + modules + prompt config (not recipients/submissions), status → Draft, new `AssignmentNumber`.
- **A5. Ward-facing queries** *(feeds WS-F)*
  - "My assignments" list for a ward, module-progress read model, submit-answers command (reuses gate logic), feedback read.

### WS-B — AI question generation (AI.Server + Assignments)
- **B1. Execute `specs/assignment-creation-with-ai.md` unchanged** — endpoint, prompt provider, `IAssignmentQuestionGenerator` seam, wizard wiring, persistence. *(AR §3.4 core)*
- **B2. AR extensions** *(AR §3.4)*
  - `PromptConfig`: question count + type mix + **difficulty distribution** persisted per assignment.
  - Org-level system prompt: tenant entity (Settings context, `TenantNotificationPolicy` precedent) — admin-editable, lockable.
  - Versioned regeneration: draft set staged server-side; confirm-to-apply; prior edits preserved until confirmed.
  - URL resource ingestion (fetch + text extraction) feeding generation alongside uploaded files.

### WS-C — Guardian sign-off (Assignments + new portal surface)
- **C1. Signature domain** *(AR §3.2, §4 `SignatureEvent`)*
  - `RequiresSignature` on assignment (default resolved from grade-level policy — §2 Q1; the policy pair is a C1 prerequisite round) + per-(assignment, ward) sign-off status: `AwaitingSignature → Signed → Finalized`; idempotent signature (NFR §6).
  - `SignatureEvent` (signer guardian id, signed-at, ip, device, consent text shown, certificate ref) — immutable audit.
  - Consent language: per-tenant template (Settings) rendered at signing.
  - Delegation: another authorized `StudentGuardian` may sign (one-Primary-signer default, open question 5).
- **C2. Guardian sign-off UI** — review summary, e-sign (typed/click; drawn optional later), consent capture. Depends on WS-F surface.
- **C3. Certificate** — PDF on finalization, stored (D-1), linked from `SignatureEvent`; downloadable from detail + guardian view.
- **C4. Locking** — submission locked at Signed; grade recorded at Finalization (bridges to `SubmissionReview`).

### WS-D — Content delivery & gating (Assignments + portal UI)
- **D1. Module progress** *(AR §3.3)*
  - Per-(ward, module) progress: watch % (video) / scroll-complete (guide) vs `min_completion_threshold`; questions locked until required modules pass.
- **D2. Ward player UI** — module sequence, video embed + captions policy (NFR §6), guide render, question block with configured feedback mode, submit.

### WS-E — Notification delivery (deferred "§18" made real)
- **E1. Deep links** *(AR §5)* — signed token per (recipient, assignment), expiry from policy `LinkValidityDays`, public routes for ward completion + guardian sign-off pages; separate login not required.
- **E2. Channel delivery** *(AR §4 `NotificationLog`, §6)* — provider abstraction (D-2), per-recipient consolidated messages (v1.1 note in `AssignmentNotificationBroadcaster` anticipates this), `NotificationLog` entity, retry/backoff, failure surfacing on author/admin dashboard.
- **E3. Reminder + trigger worker** — new `Assignments.Worker` project (students-worker precedent): reminders for unsigned/incomplete (policy fields already stored), completion-to-guardian trigger, overdue sweep, auto-archive sweep (A2). Consumes outbox events.

### WS-F — Ward/Guardian surface (structural decision D-4)
- **F1. Host + auth**: new app (recommended: `SchoolCollab.Families` bounded-context surface, token-auth public routes for deep links + OIDC for repeat visits) or public routes on Admin. Register in AppHost; feature-flag auth modes.
- **F2. Ward experience**: assignment list, completion page (WS-D2), results/feedback.
- **F3. Guardian experience**: ward summaries, sign-off page (C2), certificates.

### WS-G — Compliance & NFR hardening (cross-cutting)
- **G1.** WCAG 2.1 AA audit of all ward/guardian pages; captions enforcement for video modules.
- **G2.** Audit immutability review (SignatureEvent + outbox), retention policy (open question 6), COPPA/FERPA consent-language legal pass, e-signature validity per locale.
- **G3.** Feature flags for: approval workflow, signature default, AI review gate (AppHost `Parameters:` pattern; map in `documents/configuration.md` §2).

---

## 4. Sequencing for go-forward acceptance

Phases are dependency-ordered; each phase is independently acceptable (demoable,
test-green, mergeable per the main-branch policy). Workstream lanes can interleave
within a phase.

### Phase 0 — Decisions & spec alignment (no code) — **COMPLETE 2026-09-03**
- [x] Open questions 1–6 decided (§2; decision log folded into the AR spec §7).
- [x] D-1…D-6 decided (§2), incl. the D-4 clarification: ward/guardian identity =
      existing Students-context entities + contacts, no new user store.
- [x] AR spec §7 converted to a decision log; `assignment-creation-with-ai.md` and
      `notification-delivery-plan.md` annotated with go-forward subsumption notes.
- **Accept:** no ambiguity blocking Phase 1.

### Phase 1 — Authoring foundations (WS-A1/A2/A3/A4, WS-B1) — *no new infra*
- [ ] B1 execute `assignment-creation-with-ai.md` (its own phases/ACs apply verbatim).
- [ ] A1 resources + modules model & aggregate methods; A2 Scheduled/Approval/Archive states (feature-flagged); A3 structured answers + auto-score + pass/retry; A4 duplicate-as-template.
- **Accept:** author builds an AR with modules + AI questions, previews pass threshold,
  duplicates it; lifecycle demo Draft→Scheduled→Published→Closed→Archived with approval
  gate on; `dotnet build`/`dotnet test` green; architecture tests green.

### Phase 2 — Ward completion experience (WS-A5, WS-D, WS-F1/F2)
- [ ] F1 host + auth modes; D1 module progress + gating; D2 ward player; A5 ward queries.
- **Accept:** a ward (authenticated in-app) opens a published AR, completes modules in
  order, questions unlock per thresholds, submits answers, gets auto-scored result with
  retry per policy; unit + bUnit coverage for gating engine and player states.

### Phase 3 — Guardian sign-off (WS-C, WS-F3 partial)
- [ ] **C1 prerequisite:** signature-default policy pair (`TenantAssignmentPolicy`
      Settings + `GradeAssignmentPolicy` Students) + effective resolver +
      grade-Detail UI + wizard pre-fill + tests.
- [ ] C1 signature domain + statuses; C2 sign-off UI; C4 locking; C3 certificate
      (D-1 local `IFileStore` + D-3 QuestPDF).
- **Accept:** guardian reviews ward's completed work, signs with consent text, submission
  locks, certificate stored + downloadable; second sign attempt is idempotent; audit row
  records identity/time; delegation to another guardian works; a new AR pre-fills
  `RequiresSignature` from the grade default and the author can override it.

### Phase 4 — Delivery & deep links (WS-E)
- [ ] E1 token deep links (expiry honored); E2 channel delivery + NotificationLog + failure surfacing; E3 reminder/overdue/archive worker.
- **Accept:** publish → notifications only to verified+subscribed valid contacts through
  policy-filtered channels; deep link opens the right ward/guardian page without login
  and expires; unsigned reminder fires per cadence; bounced/failed sends surface on the
  author dashboard; worker is idempotent under redelivery.

### Phase 5 — Polish & compliance (WS-G + remaining §3.1/3.2 niceties)
- [ ] G1 accessibility + captions; G2 audit/retention/legal; G3 flag mapping docs;
      comments/feedback threads, drawn signature, rubric scoring if still in scope.
- **Accept:** WCAG audit passes on ward/guardian pages; NFR §6 checklist signed off;
      configuration doc updated for every new flag/parameter.

---

## 5. Risk register (top items)

| Risk | Why | Mitigation |
|---|---|---|
| Portal host decision (D-4) blocks 3 phases | Ward/guardian UI is the biggest net-new surface | Decide in Phase 0; token-auth public routes are required either way — start with routes on the existing Assignments.Application host if a new app is contentious. |
| No file storage in repo (D-1) | Attachments (P1) and certificates (P3) both need it | Decide before Phase 1; minimal abstraction (`IFileStore`) keeps Blob/local swappable. |
| Gate vs. sign-off conflation | `GuardianSubmissionGate` is pre-submission; AR sign-off is post-completion | Keep both, distinct statuses; don't repurpose the gate — extend the lifecycle around it. |
| Recipient granularity | `AssignmentRecipient` is per-contact; spec's `Recipient` is per-ward with guardians[] | Keep per-contact rows (finer); aggregate per-ward views in queries. |
| AI-spec double-spec | `assignment-creation-with-ai.md` predates AR spec; overlapping-but-different prompt model | Execute prior spec unchanged (B1); AR prompt extensions (B2) versioned on top — avoids re-planning shipped decisions. |

---

## 6. Immediate next actions

1. ~~Stakeholder pass on decisions~~ — done (Phase 0 complete).
2. Execution mode chosen by the user: **full four-agent rounds (Tier 3)** for AR
   implementation; round slicing lives in
   `documents/solution/assignment-request-implementation-details.md` §3.
3. On the user's go: start round `ar-1-ai-spec-core` — orchestrator authors the round
   doc per the `orchestrator-worker-reviewer` skill; worker implements
   `assignment-creation-with-ai.md` §10 phases 1–4.