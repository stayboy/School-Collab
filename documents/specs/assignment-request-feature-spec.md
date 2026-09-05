# Feature Spec: Assignment Request (AR) System for Schools

## 1. Summary

An **Assignment Request** is a school-issued unit of work that combines:

- **Google Classroom**–style assignment management (rosters, due dates, materials, topics, grading)
- **DropSign**–style signature/completion workflow (student completes → parent/guardian signs off → submission is finalized, with full audit trail)
- **Proofpoint Security Awareness Training**–style content delivery (embedded videos, instructional guides, and quiz-style Q&A blocks that gate progress)
- **AI-assisted question generation** from linked resources (URLs, attached files) via system/user prompts, producing N questions per request

The core object is the **Assignment Request (AR)** — it moves through a lifecycle from draft → approval → publish → notify → complete → sign-off → archive.

---

## 2. Personas

| Persona | Role |
|---|---|
| **Author** (teacher/staff) | Creates and configures the AR, attaches resources, generates/edits questions |
| **Approver** (admin/dept head) | Reviews and approves ARs before they go out (optional per school policy) |
| **Ward** (student) | Completes the assignment — watches videos, reads guides, answers questions |
| **Guardian** (parent) | Reviews ward's completed work and signs off, same as a signature request |
| **System** | Generates questions, sends notifications, tracks status, enforces gating |

> **Terminology mapping (clarified 2026-09-03):** "Ward" ≡ the Students bounded
> context's `Student` entity; "Guardian" ≡ `Guardian` (via `StudentGuardian` links
> with `Contact` records). Ward/Guardian and Student/Guardian are the same concepts —
> repo code uses Student/Guardian (the Assignments context uses `WardStudentId` for
> student references on guardian-facing rows). **There is no identity/auth system for
> students/guardians yet:** in v1, deep-link tokens address existing contacts (D-4);
> no Keycloak users for students/guardians.

---

## 3. Feature Set by Source Product

### 3.1 From Google Classroom — Assignment Management Core
- Class/roster/section assignment (one AR can target a class, group, or individual wards)
- Due date, available-from date, late-submission policy
- Topics/categories for organizing ARs within a class
- Materials: attach files, links, embedded video, or existing AR templates
- Draft → Scheduled → Published states
- Reuse/duplicate a past AR as a template
- Per-ward progress and grade tracking, rubric-based scoring
- Comments/private feedback thread per ward submission

### 3.2 From DropSign — Signature & Completion Workflow
- **Multi-party completion chain**: Ward completes content → Guardian reviews & signs → submission locked
- Each AR has a **signature block** requiring guardian e-signature (typed, drawn, or click-to-sign) after ward completion
- Status tracking per recipient: `Sent → Viewed → In Progress → Completed → Awaiting Signature → Signed → Finalized`
- Auto-reminders to guardians who haven't signed (configurable cadence: e.g., 24h, 72h, day-of-due-date)
- Full **audit trail**: timestamp, IP/device, signer identity, consent language shown at signing
- Downloadable/exportable signed completion certificate (PDF) per ward
- Delegation: guardian can forward to another authorized guardian if household has multiple contacts

### 3.3 From Proofpoint Security Awareness Training — Content & Assessment Delivery
- Assignment content structured as **modules**: video → instructional guide (text/PDF/slides) → question block
- **Gating**: questions unlock only after required video/guide is viewed (min watch % or scroll completion)
- Mixed question types: multiple choice, true/false, short answer, scenario-based
- Immediate per-question feedback (optional) or held-until-submission feedback (configurable)
- Pass/fail threshold with retry logic (e.g., must score 80%+ to complete; N retries allowed)
- Completion certificate generation, tied into the DropSign sign-off step above

### 3.4 AI-Generated Question Bank
- AR author links **resources**: URLs, uploaded files (PDF, docx, video transcript, slides)
- Author selects/edits a **prompt**:
  - **System prompt** (school/org-level default, locked or editable by admin) — sets tone, difficulty, standards alignment, question format rules
  - **User (author) prompt** — task-specific instructions layered on top (e.g., "focus on chapters 3–4," "align to grade 7 reading standards")
- Author specifies **N** = number of questions to generate, question type mix, and difficulty distribution
- System generates a **draft question set** from the linked resources + prompt combination
- Author can regenerate, edit individual questions, reorder, or delete before publishing
- Generated questions are versioned — regenerating creates a new draft without destroying prior edits until confirmed

### 3.5 Assignment Request Lifecycle
1. **Draft** — author builds AR: roster, materials, videos/guides, linked resources, generates questions, sets due date & signature requirement
2. **Review/Approval** (optional, per school policy) — approver checks content before it can be published
3. **Publish** — AR is locked for structural edits; becomes visible to targeted wards
4. **Notify** — system sends notifications (email/SMS/push) to all valid contacts (ward + guardian) with a deep link to the full completion page
5. **Ward Completion** — ward views content, answers questions, submits
6. **Guardian Sign-off** — guardian is notified ward completed, reviews summary, signs
7. **Finalization** — AR marked complete for that ward; grade/score recorded; signed certificate stored
8. **Archive** — after due date + grace period, AR moves to read-only archive, still exportable

---

## 4. Core Data Model (high-level entities)

- `AssignmentRequest` (id, title, class_id, topic, status, due_date, available_from, requires_signature, approval_status)
- `Resource` (id, ar_id, type: url|file|video, source, metadata)
- `PromptConfig` (id, ar_id, system_prompt_id, user_prompt_text, question_count, type_mix, difficulty_mix)
- `Question` (id, ar_id, type, text, options, correct_answer, source_resource_id, order, gating_module_id)
- `ContentModule` (id, ar_id, type: video|guide, url/file, min_completion_threshold, order)
- `Recipient` (id, ar_id, ward_id, guardian_id[], notification_status, completion_status, signature_status)
- `SubmissionAttempt` (id, recipient_id, answers[], score, passed, timestamp)
- `SignatureEvent` (id, recipient_id, signer_id, signed_at, ip, device, consent_text_shown, certificate_url)
- `NotificationLog` (id, ar_id, recipient_id, channel, sent_at, delivery_status)

---

## 5. Notification Rules

- Notifications only sent to **contacts marked "valid"** for that ward (verified email/phone, active guardianship record)
- Triggered on: publish, reminder (unsigned/incomplete), completion (to guardian), overdue
- Each notification contains a **direct deep link** to the ward's or guardian's respective full-page view (completion page or sign-off page) — no separate login/search required, but authenticated via secure token
- Notification failures (bounced email, invalid number) surface back to author/admin dashboard for follow-up

---

## 6. Non-Functional Requirements

- **Compliance**: FERPA (student education records), COPPA (under-13 wards), state-level parental consent laws for e-signature validity in a school context
- **Accessibility**: WCAG 2.1 AA for all ward/guardian-facing pages; captions required on instructional videos
- **Security**: signed links expire; role-based access (author/approver/ward/guardian scoped strictly); audit logs immutable
- **Reliability**: notification delivery retries with backoff; idempotent submission handling to avoid duplicate signatures
- **Auditability**: every state transition (publish, notify, complete, sign) timestamped and attributable

---

## 7. Open Questions — Decision Log

Resolved 2026-09-03. Workstream impact and design details:
`documents/solution/assignment-request-go-forward-breakdown.md` §2 and
`documents/solution/assignment-request-implementation-details.md` §2.

1. **Guardian sign-off:** configurable **per assignment**. `RequiresSignature` is
   a per-AR flag whose **default is resolved from the grade-level policy** (grade
   override → tenant default → false) and snapshotted at create time; the author
   may override per AR.
2. **Approval step:** tenant-level feature flag (default off). When on, **every** AR
   requires approval before publish — no content-type-conditional approval.
3. **AI question review gate:** the create wizard's Review step IS the author's
   explicit confirmation of AI-generated questions (they land as an editable set);
   when the tenant approval flag is on, the approver additionally sees an
   "AI-generated" provenance marker. No separate AI-only approval state.
4. **Retry/appeal:** `MaxAttempts` per AR (null = unlimited); exhaustion blocks the
   ward until a teacher raises the limit or overrides (existing
   enable-submission precedent). No formal appeal workflow in v1.
5. **Multiple guardians:** one **Primary** signer suffices; CC guardians are
   notified; the Primary may delegate signing to another authorized guardian.
6. **Retention/export:** archives and signed certificates are retained
   indefinitely; manual export (PDF/CSV) only; no SIS integration.
