# Assignment view page — modernization (publish visibility, materials preview, progress)

**Status:** spec settled (owner, 2026-10-29 — Q128–Q135 all as recommended); mock pending review
**Owner:** Assignments module · **Surface:** `/assignments/{id}` (plus a new `/assignments/{id}/progress`)
**Follows:** `documents/solution/panel-layout-pattern.md` (the skeleton), the student view redesign
(`documents/solution/student-view-redesign.md`, Q114–Q127) — the precedent that a **view** surface adopts the
panel skeleton — and `documents/specs/instructional-materials.md` (materials, grounding, ward card).
**Supersedes:** nothing. The tabbed `Detail.razor` shape is retired, not another document.
**Mock:** `.pi/mock/assignment-view-v1.html` (+ PNG states) — a review aid, never verification evidence
(`panel-layout-pattern.md` L4).
**Issue:** [#453](https://github.com/stayboy/School-Collab/issues/453) — `ready-for-agent`.

---

## 1. Problem Statement

Opening an assignment shows **a page inside a page**. The page draws its own title and five tabs, and the
Overview tab renders the whole authoring surface — which draws a second title, a second breadcrumb trail and a
second sticky action bar inside the tab strip. The students module, built in the same era, already has one
header and one scroll.

Three questions the page exists to answer cannot be answered on it:

1. **Who was this published to, and did they get it?** The *Recipients* tab lists **contact types** — "Student",
   "Guardian" — never a person; no grouping by student, no delivery state, and its subscription badge paints
   "Active" and "Inactive" with the identical appearance. *Notifications* lists failures only and resolves
   labels from guardian-owned contacts alone, so a student's own contact renders as a hex fragment. A student
   with no subscribed contact produces **no row at all**, so "meant for" and "actually reached" are never
   reconciled.
2. **What will the student actually see?** Materials attached as instructions — text, links, audio, video,
   images — cannot be previewed. The teacher opens the guardian/ward app to see what was sent.
3. **Where is this up to?** Submissions live in a tab mixing a queue, a version table and a review form. No
   surface answers *who started, submitted, was graded, had a guardian sign, and was notified*.

Editing questions or materials means scrolling a read-only authoring form, or navigating to Edit and hunting
the panel. And AI generation grounds on **URLs and attached files only** — teacher-written instructions and text
materials are excluded — while nothing of this material reaches a guardian or student surface, because the
Families host has no AI at all.

---

## 2. Solution

One page, one header, no tabs — the assignment view adopts the student view's skeleton.

- **Header** owns identity once: title, status pill, approval/signature chips, a one-line fact row, and the
  §11 lifecycle primary — **Draft → Publish**, **Scheduled → Unpublish**, **Closed → Archive**. For a **Published**
  assignment the primary is ***Open progress***, with Unpublish and Close in the kebab: the view page's job is
  running the assignment, not authoring it (Q141, settled — §11 carries the view-surface exception so the page is
  not read as drifting from the matrix).
- **Main column:** Details · Questions & AI · **the audience table** — Targets & Audience while the assignment is a
  draft (Q152), Delivery once published (Q153) · **one Status panel** holding Progress, Guardian sign-off and
  Notifications as three segments divided by coloured vertical rules (Q155).
- **Rail:** **Content & Resources** · Rules; before publish, **Approval** joins them.
- **Delivery** answers question 1 in one place: one group per **student**, each contact with channel, guardian role,
  subscription and **delivery state**, plus explicit *not reached — no subscribed contact* rows. Before publish the
  same fact is a count, which is what **Targets & Audience** already shows.
- **Progress** shows six tiles over the cohort and links to a **new progress page**: one row per student
  (contacts, submission state, score, sign-off, last activity) that opens that student's submission.
- **Materials preview** is a read-only dialog by kind: player for audio/video, viewer for images, external open
  for links, the shared sanitiser for text.
- **Content & Resources** carries the grounding readout per row: each material states whether it grounds generation
  and why not when it does not, plus one line naming the contract (five slots, 20,000 characters, instructions
  excluded). No separate "AI" panel — the material is the unit, and the settled rail already holds it.

---

## 3. User Stories

1. As a teacher, I want one page with one title, so that I am not reading two headers and two breadcrumb trails.
2. As a teacher, I want the status obvious at a glance, so that I know if I am looking at a draft, a published or a closed assignment.
3. As a teacher, I want subject, type, grading, due date and score in one line under the title, so that I do not hunt for them.
4. As a teacher, I want to publish from this page, so that publishing is one obvious action.
5. As a teacher whose assignment requires approval, I want the publish action to say approval is outstanding, so that I do not click a control that cannot succeed.
6. As an approver, I want to approve, reject or deny here, so that the decision is made where the content is.
7. As a teacher, I want to schedule, reschedule, close or archive here, so that lifecycle actions are not split across surfaces.
8. As a teacher, I want to duplicate the assignment here, so that I can reuse it as a template.
9. As a teacher, I want to see the targeting that defines the audience — grades, streams, groups, students — so that I know who it is for.
10. As a teacher, before publishing I want expected counts (students matched, primary guardians, other guardians) and an honest warning when the preview could not resolve, so that I can judge a sendout before it happens.
11. As a teacher, after publishing I want the cohort as **students**, not as a flat list of contacts, so that I can see who it reached.
12. As a teacher, I want each student's contacts with channel and guardian role, so that I can see who was contacted and how.
13. As a teacher, I want each contact's delivery state — queued, sent, failed, skipped, opened — so that "published" is not confused with "received".
14. As a teacher, I want a failure's reason on the same row as the contact, so that I need no second tab.
15. As a teacher, I want a student with no subscribed contact shown explicitly as not reached, so that silence is never read as success.
16. As a teacher, I want to see when guardian review is mandatory, so that I understand why guardians are in the audience.
17. As a teacher, I want a scheduled retry distinguished from a terminal failure, so that I know which need a human.
18. As a teacher, I want to preview a text material as the student reads it, so that I can check the wording.
19. As a teacher, I want to open a link material, so that I can confirm where it points.
20. As a teacher, I want to play an audio or video material inline, so that I can check a recording without leaving.
21. As a teacher, I want to view an image material, so that I can confirm the right file was attached.
22. As a teacher, I want to distinguish documents from media, so that I know what the ward player will do with each.
23. As a teacher, I want the read-only question list with type and option count, so that I can review the assignment's shape.
24. As a teacher, I want a direct path from Questions to the question editor, so that editing is one click.
25. As a teacher, I want a direct path from Materials to the materials editor, so that correcting a material is one click.
26. As a teacher, I want the page to refuse editing when the status forbids it and say why, so that I am never offered a control the server will reject.
27. As a teacher, I want to see which materials carry extractable text, so that I understand what the AI can ground on.
28. As a teacher, I want to see how much of the grounding budget is used, so that I know why a material was dropped.
29. As a teacher, I want to know when tenant policy has locked the AI prompt, so that I stop looking for a disabled override.
30. As a teacher, I want the guardian sign-off summary — signed, outstanding, reassigned — so that I know whether the assignment can be finalized.
31. As a teacher, I want progress tiles (cohort, reached, started, submitted, graded, signed), so that I read the funnel without arithmetic.
32. As a teacher, I want to open a full progress page, so that I can work the cohort student by student.
33. As a teacher, I want one row per student with contacts, submission state, score, sign-off and last activity, so that I can triage in one place.
34. As a teacher, I want to filter progress by state, so that "not started" or "submitted, ungraded" is findable.
35. As a teacher, I want to open a student's submission from progress, so that marking starts from the queue that produced it.
36. As a teacher, I want to review and grade from progress, so that I keep my place in the cohort.
37. As a teacher, I want to submit on behalf of a student, so that paper or offline work is not stranded.
38. As a teacher, I want to reassign a sign-off to another guardian, so that a separated family is not blocked.
39. As a teacher, I want a breadcrumb back to the list, so that navigation sits where I expect it.
40. As a teacher in dark theme, I want every status colour legible, so that state stays distinguishable.
41. As a teacher on a narrow host, I want the rail to collapse under the main column, so that fields stay first and nothing clips.
42. As a teacher, when one read fails I want that panel to say so and the page to keep working, so that one hop outage does not blank the surface.
43. As an administrator with no tenant selected, I want a tenant-required message rather than an empty assignment, so that the state is unambiguous.
44. As a teacher, I want the page to say when the cohort is capped, so that I never mistake a truncated list for the whole audience.
45. As a guardian, in a later layer, I want a helper grounded on the materials the teacher attached, so that help matches the lesson.
46. As a teacher, I want the audience table to show a channel and a count but never an address or a phone number, so that reviewing a sendout cannot expose a family's contact details.
47. As a data-protection owner, I want the assignment page to carry no contact value in its payload at all, so that masking cannot be undone by inspecting the page.

---

## 4. Implementation Decisions

### 4.1 One page, one chrome (Q128, Q129)

- The detail surface is rebuilt on the shared skeleton: one `AuthoringShell`, a `region-grid` main/rail, one
  `PanelSection` per block. **No `FluentTabs` on this surface** — a regression guard asserts zero `FluentTab`
  elements in the rendered page, mirroring the student-view guard.
- **This spec supersedes UX-10 and D5 of `assignment-authoring-compartments.md`**, which retain `Detail`'s
  operational tabs. The tabs are what produced the page-inside-a-page, and the submission queue they carried now
  lives on the progress page; the compartments document gains a pointer here in the same diff, and UX-10 is marked
  superseded rather than left contradicting this file (0813 review, finding 4).
- **The view page owns the chrome.** The authoring surface gains an **embeddable mode** that suppresses its shell
  (title, crumbs, kebab, sticky action bar) and yields its body, so the read-only content keeps **one** definition
  and `NFR-4`'s anti-drift rule survives. The mode is additive: Edit and Create are unchanged.
- **Scope honesty (0813 review, finding 3).** `panel-layout-pattern.md` scopes itself to *create / edit* surfaces.
  This view adopts the skeleton on the student-view precedent, and the pattern's scope line is **amended in this
  round** — not deferred — so the next surface does not have to argue the point again.
- **Lifecycle actions are data, not markup.** A pure decision over **`(surface, status, approval state, resolved
  policy)`** yields the allowed actions and the primary label; the authoring action bar and the view header both
  render from it, so the §11 matrix cannot fork. **`surface` is an explicit input, not an assumption** — Q141 makes
  the Published primary surface-dependent (*Open progress* on the view, *Unpublish* in authoring), and a decision
  that cannot see the surface cannot produce that answer (0813 review, pass 2, finding 1). Unit-tested directly,
  without a component.
- **The header fact row is presentation, not a second field-set definition**: at most four facts, read from the
  same summary DTO the authoring read-out uses. Any change to the field set lands in the authoring read-out first.

### 4.2 The published-to read (Q130)

- **One new read: `GET /assignments/{id}/audience`** returning the cohort per student: student id + display name,
  `reached` (any subscribed contact), and one row per contact — owner type, channel, guardian role, subscription
  active, broadcast flag, **delivery status, sent-at, opened-at, failure reason, retry-scheduled, template id**.
- Composition: the existing target resolver produces the student cohort; persisted recipient rows supply contacts;
  the notification log supplies delivery; **names come from the batched subscriber resolution the publish path
  already uses**, which returns contact and student names on the same Students response and degrades to empty
  rather than failing. **No per-student directory hop.**
- **`reached = false` rows are the point of the read** — they are what the current tabs cannot show.
- Delivery state is **read-only** here; retry/requeue is not this slice.
- **Authority (Q145).** The **notification log is authoritative for the status pill** — it knows Queued / Sent /
  Failed / Skipped, the attempt count and the failure reason, and it is the record the renderer writes.
  `AssignmentRecipient.DeliveredAt` / `OpenedAt` supply **"opened" only**: they are coarse "at least one landed"
  stamps updated by the deep-link path. Where the two disagree the log wins, and the join rule is tested rather
  than assumed.
- `GET /{id}/recipients` stays for its current callers; the failures read is superseded once the audience read
  has a call site (retirement decided then, not now).
- **Pre-publish rows (Q152).** The existing `recipient-preview` read returns **counts only**, and the authoring
  surface calls it on a **500 ms debounce** — so it must **not** learn to return rosters. The draft view calls a
  **rows variant once per load** (same target resolution, same policy filter, same 200 cap) and renders the table:
  student · guardian(s) with role · contact value and channel · `At publish` verdict. Nothing is persisted; publish
  re-resolves, and the panel says so in words.
- **Cohort cap (Q146): a fixed 200**, declared as a **shared constant** so the client and server cannot drift,
  with truncation always stated in the panel ("showing the first N of M"). Paging is a later slice, triggered by a
  real cohort rather than by speculation.

### 4.3 The progress page (Q131)

- New page at `/assignments/{id}/progress`, linked from the Progress rail panel.
- **A row's action opens a second new page: `/assignments/{id}/students/{studentId}/submission` (Q147)** — the
  version history and the review form currently trapped in the Submissions tab, moved to a route that mirrors the
  existing `/…/sign-off` page. Removing the tab would otherwise orphan submission review entirely; a drawer was
  rejected because it hides the history behind a modal and cannot be deep-linked. **This makes three new routes in
  the round, not two** — the third is a move, not new behaviour.
- Rows come from the **audience cohort ∪ submission rows**, so a student who never received the assignment still
  appears. Per row: student, contacts, submission state, **last activity**, sign-off state.
- **Score and grade (Q140, settled 2026-10-29).** The submissions projection is **widened**:
  `SubmissionForReviewDto` gains `Score` and `Grade`, so the progress list needs no per-student fetch. No schema
  change — the values already persist on the submission version. Rejected: a per-row fetch, which costs a fan-out
  the page would pay on every load.
- Existing reads only: submissions by assignment, per-student submission detail, sign-off statuses. **No schema
  change, no new write path.**

### 4.4 Editing and preview (Q133, Q134)

- Panel actions **deep-link into the edit surface** at the Questions and Materials panels. Inline structural
  editing on a view page is refused by design: the server accepts structural edits only in Draft/Scheduled, so an
  inline control would be a control that fails on save. Where editing is not possible the panel states the reason.
- **Material preview is a read-only dialog, admin-local**, using the existing authenticated material fetch: text
  through the shared sanitiser, media through the authenticated client to a blob URL, links opened externally.
- The ward player and the admin preview are the same concept in two hosts. Extraction into shared chrome is
  **priced and deferred** — the ward player owns an API client and JS interop, which makes it a feature component
  rather than presentational (component-first policy §5.3). The proposal goes to the owner with numbers; it is
  not done silently inside a page rewrite.
- **The preview dialog passes the component approval gate** (component-first policy §4) before implementation: a
  design/mock-named component needs the grill round, the mock that renders it, and the contract recorded. This spec
  supplies the design; the gate is discharged in the implementation round, not assumed here (0813 review,
  finding 6).

### 4.5 Status presentation

- Status renders with the semantic pill vocabulary the student view adopted (`--status-*` tokens, four variants
  plus info), never a bare filled badge. Lifecycle states, delivery states and sign-off states each take a
  distinct variant from **one** rule set.
- **Scope: this page only** (the Q118 precedent). The edit surface keeps its `FluentBadge`, so the same status
  reads differently on `/edit`; that divergence is recorded as a follow-up rather than half-migrated here.

### 4.6 Defects closed in the same diff

- The subscription badge passing the identical appearance for both branches.
- Failure labels resolving guardian-owned contacts only.
- The nested page chrome.

### 4.7 AI and prompts (Q132 — designed, not built)

- The page states the grounding contract truthfully: URL-derived text + attachment extracted text, five slots,
  20 000 characters each, teacher-written instructions **excluded** (`instructional-materials.md` INS-3). It lists
  each material's extraction status and whether it grounds.
- **Portal prompt seam, fixed by decision:** Assignments assembles an **entitlement-checked, titled material
  digest** and posts it to a **new AI endpoint with its own prompt provider** — never an overload of
  `/api/ai/chat`, whose single `ISystemPromptProvider` is already occupied. The guardian/student host never
  fetches a URL and never composes a prompt; it stays a thin client like the admin chat panel. Tenant org prompt
  and lock reuse the existing resolver.
- Widening the grounding contract so instructions ground too is a **separate additive layer**: it changes what the
  model sees for every generation.

### 4.8 Panel placement — the audience table in main, read-outs in the rail

The ownership test in `panel-layout-pattern.md` §5 asks whether a panel presents **other rows** or the entity's own
fields. It has no clause for a panel that presents **no rows at all** (Progress, Guardian sign-off), and none for a
panel that must be **wide** (the audience table). Both cases landed here (owner direction, 2026-10-29).

- **The rule.** **Main** carries the assignment's own content (Details, Questions) and **the audience table** — the
  panel that answers "who gets this, and did they get it". **Rail** carries one panel per relationship and per
  read-out: Content & Resources, Progress, Guardian sign-off, Notifications, Rules; **Approval** replaces
  Notifications before publish.
- **Why the audience table and not the aggregates.** It is both the widest panel (a four-column student × guardian ×
  contact grid cannot render in the 320–460 px rail track) **and** the tallest, so moving it to main is what stops
  the rail from outrunning the main column — an aggregate is compact and costs the rail little.
- **One shape in both states.** Targets & Audience (draft) and Delivery (published) are the **same panel** at
  different states of the same fact — one table, one column set, `At publish` reading `will notify` / `no contact` /
  `blocked by policy` and then `sent` / `opened` / `failed` / `skipped`. Two shapes for one fact is the drift this
  spec exists to remove (Q152, Q153).
- **The read-outs are one panel, not three (Q155).** Progress, Guardian sign-off and Notifications are three
  **segments of a single Status panel** below Delivery, divided by **coloured vertical rules** (info / success /
  warning). Three separate panels each cost their own header, border and gap — roughly 90 px of chrome apiece — and
  three short panels stacked in a rail read as one fact cut into three. As segments they cost one header and read as
  the cohort's state in a single glance.
- **Measured, not aesthetic.** With every panel in the rail the mock stood **4 991 px**, a ~1 900 px rail beside
  ~500 px of main. With the audience table and the status strip in main, and the rail down to Content & Resources
  plus Rules, the page is **4 089 px** — a full 900 px shorter than the first arrangement.
- **What did not move.** Content & Resources and Rules stay in the rail: § 5 names both. Moving either would
  re-open a settled table for a layout preference.
- **Consequence for the pattern doc.** §5's table has no clause for either case; this rule is the missing half. It
  is **proposed** for `panel-layout-pattern.md` §5, not asserted here.


### 4.9 Contact exposure — this page carries no contact value (Q154, owner 2026-10-29)

**A contact value is personal data, and this page does not need it.** A teacher answering "did this go out?" needs
channel, count, role and delivery state — not an address. The mock briefly rendered masked values
(`n.okafor@…`, `+44 7700 900…`); a masked local-part plus domain and a partial phone number still identify a
person, so masking is not a control.

- **Enforced at the contract, not the view.** The audience/delivery DTO carries **no address field at all** — channel
  kind, contact count, guardian role, delivery state, timestamps. A value that never enters the payload cannot leak
  through devtools, a screenshot, a log line or a future export. Rejected: rendering `ToAddress` masked, which is
  cosmetic and one DOM inspection away from exposure.
- **Where the values would have come from.** `NotificationLog.ToAddress` (persisted rendered address) and the
  Students contact read. Projecting either into a list read creates a **second, broader door** to data the Students
  module already guards with its own scope — the failure mode is a new read widening an old permission, not a
  missing check in the new read.
- **No guardian names either (Q158, settled).** The gate the first draft proposed — *show a name only when the
  caller could already list that student's guardians in the Students module* — has **no mechanism to hang on**:
  `TeacherScope` answers "can you see this assignment", not "may you list this student's guardians", and adding a
  per-student authorization hop would turn one read into a fan-out. So the table shows **role and count only**
  ("2 · primary, CC"). The names sit one click away in the Students module, where that exposure rule already lives
  and is already enforced.
- **Scope.** The read is scoped to the assignment's teacher scope *and* may not exceed the Students module's
  exposure for each student in the cohort. Two scopes, the narrower wins.
- **If an operator needs the address** (a bounce, a support call): a separate, permission-gated, **audited**,
  single-address action — never a column in a list.
- **Test posture.** Assert on the **serialized payload**: no rendered payload contains an `@` or a phone-shaped
  string. That is checkable in a handler test and cannot regress silently; asserting on the view would not catch a
  field that a later template starts rendering.



### 4.10 Paging — every data-driven grid, 15 by default (Q156, owner 2026-10-29)

The rule itself lives in `.github/copilot/rules/blazor-components.md` § "Grids are paged by default"; this section
is how it lands on **this** page.

- **All four tables page**: Questions, Delivery, Targets & Audience (draft), and the progress page's student list.
  The Status panel's segments and the materials list are **not** grids — they have a fixed, small shape — so they do
  not page.
- **Default 15 per page**, and the page size is a **per-grid** property: Questions 15, Delivery/audience 15 (a wide
  four-column row), progress 15. **Questions and Delivery expose a rows-per-page control (15 / 25 / 50)** — the two
  named cases, because both grow with data: 40 questions and a 300-student cohort are ordinary.
- **`EntityGrid` + `FluentPaginator`**, not a hand-rolled pager. The tables here are not `EntityGrid` layouts
  (they are panel-internal, four columns, no selection or chips), so they carry a `PaginationState` and
  `FluentPaginator` directly.
- **Paging composes with the 200 cap (Q146).** The read stays bounded; the grid pages within what arrived and says
  so: "1–15 of 200 (capped)".
- **No pager when the set fits (Q162).** `total <= page size` renders **no pager and no rows-per-page selector**;
  the Questions table (8 rows) therefore shows none, while Delivery and the progress list (28) do. The mock's pager
  helper returns nothing in that case, which is the behaviour `EntityGrid` must reproduce by deriving its pager from
  the item count instead of taking it as a caller's choice.
- **The shipped component does not do this yet (Q163).** `EntityGrid.ShowPager` defaults to `false` and **no call
  site sets it** — five grids never page. Flipping that default to `true` is a shared-chrome change (full gate) and
  lands with `LandingGridSettings.ItemsPerPage` 10 → 15, not here. Recorded so the rule and the component cannot
  disagree silently.
- **Priced consequence.** `LandingGridSettings.ItemsPerPage` defaults to **10** today. Raising it to **15** touches
  every landing page that inherits it — **shared chrome, so it carries the full gate** — and is therefore *not*
  smuggled into this docs-only PR. It belongs to the implementation round that applies the rule.
- **Two specs on `main` page at 5 and must be amended in that same round (Q159).**
  `assignment-creation-with-ai.md` §2.5 says question review uses a *"fixed page size (default 5)"*, and
  `assignment-authoring-content-questions-modern-ui.md` QA-14 mandates `FluentPaginator` over the same collection.
  Both predate the rule; each gains one line pointing at `blazor-components.md` § "Grids are paged by default" so
  the next reader does not implement whichever they hit first.



## 5. Testing Decisions

A good test here asserts **external behaviour**: what a user sees, and what the page requests — never component
internals or CSS. HTTP is mocked with real DTO shapes, so a contract change fails a test rather than slipping
through.

| # | Seam | Covers | Prior art |
|---|---|---|---|
| 1 | Page-level bUnit render of the view page over mocked HTTP | header/badge/no-tabs guard, panel presence, state-dependent primary, audience grouping, delivery pills, preview trigger, degraded reads | `AssignmentDetailBunitTests` |
| 2 | Page-level bUnit render of the **progress page** and the **submission page** | one row per student, filters, row opens the submission page, history + review render, cohort-cap note | same harness (new suites) |
| 3 | Handler unit tests for the **audience read** | cohort = targets ∪ recipients, unreached students, contact↔delivery join, fail-open on a log outage | existing recipients-query tests |
| 4 | Pure unit tests for the **lifecycle matrix** | every status × approval state → allowed actions + primary label | policy unit tests |
| 5 | Existing architecture pins | typed results on the new route, endpoint composition | `OpenApiSurfaceArchitectureTests`, `MinimalApiCompositionArchitectureTests` |
| 6 | Rendered mock (not a test) | owner's design review, light/dark/narrow | student-view mock harness |

Three new routes, and only **one** genuinely new read — the submission page is a move of behaviour that already had
tests behind the tab, so it reuses those mocks. Names degrading to short ids is asserted, not assumed: the page must
survive the directory hop being down. The delivery-authority rule (log wins over recipient stamps) and the 200 cap
are both handler-level assertions, because both are decided server-side and neither is visible in a screenshot.

---

## 6. Out of Scope

- Inline structural editing on the view page.
- Building the guardian/student AI feature (seam and pricing only).
- Widening the AI grounding contract.
- The merged material contract (instructions + resources + attachments) — its own spec.
- Notification retry / requeue / resend.
- Migrating status pills on the edit surface (this page only, per Q118). **Not** out of scope: the
  `panel-layout-pattern.md` scope-line amendment, which §4.1 commits to this round.
- The MAUI head and the Python teacher portal.
- Schema or migration change.

---

## 7. Further Notes

- **Reuse inventory:** panel skeleton, status tokens, `AuthoringShell`, `PanelSection`, `FieldDisplay`,
  `RailBand`/`RailChips`, `RichInstruction`, recipient-preview, the material fetch, the submissions and sign-off
  reads, the target resolver and the batched subscriber resolution. New: one read endpoint, one page, one preview
  dialog, one pure lifecycle matrix.
- **Model review (2026-10-29) — `deepseek-v4-pro` build `0813`, via `pi` on `ollama-cloud`.** Six findings, all
  applied; the two that changed the design are marked P1/P2 below. The `cline` pool carries the same build but its
  balance is negative (402), which is why the run moved pools — a listing is not availability.

  | # | Finding | Sev | Disposition |

---

## 8. Decisions settled (owner, 2026-10-29 — "all as recommended")

| # | Question | Ruling |
|---|---|---|
| Q128 | Tabs or one scroll? | **One scroll, no tabs.** The five tabs collapse into panels plus the progress page. |
| Q129 | Who owns the page chrome? | **The view page.** The authoring surface gains an embeddable mode that suppresses its shell. |
| Q130 | The published-to read | **One new read**, `GET /assignments/{id}/audience`, composed from targets ∪ recipients ∪ notification log, names from the batched subscriber resolution (no per-student hop). |
| Q131 | Progress page in this spec? | **Yes** — one new page on existing reads plus the audience cohort; no schema change, no new write path. |
| Q132 | How far does the AI work go? | **Research + seam only.** The portal prompt architecture is fixed here; the feature is its own layer. |
| Q133 | Editing questions/materials from here | **Deep-link into the edit surface.** Inline editing would render controls the server refuses. |
| Q134 | Material preview: local or shared | **Admin-local read-only dialog**; extraction into shared chrome is priced and deferred (component-first §5.3). |
| Q135 | Mock coverage | **Four states**, rendered light + dark + narrow. |
| Q136 | Branch | **`spec/assignment-view-modernization` off `main`.** Created. |
| Q137 | Issue timing | **Publish after the review round closes**, so `ready-for-agent` never lands on unsettled text. |
| Q138 | Execution tier | **Light round** (Tier 1–2): plan gate + static diff review, no UI tester. |
| Q139 | Pre-publish cohort names | **Counts by default, `Preview cohort` on demand.** The roster is a live query, not a record. |
| Q140 | Progress score column | **Widen `SubmissionForReviewDto`** with `Score`/`Grade` — no schema change. |
| Q141 | May the view surface promote *Open progress*? | **Yes.** §11 gains a view-surface exception; the authoring surface keeps its own primary. |
| Q142 | Delivery: own panel or folded? | **Own panel** — Targets & Audience is settled and about targets; delivery is runtime data. |
| Q143 | Spec reachability | **Commit, push, open a PR to `main`** — a `ready-for-agent` label must point at a readable path. |
| Q144 | Mock artifact location | **Commit the generator + HTML under `documents/specs/assets/assignment-view/`**, PNGs stay out of git. |
| Q145 | Delivery authority | **The notification log is authoritative** for the status pill; recipient stamps supply "opened" only. |
| Q146 | Cohort cap | **Fixed 200**, a shared constant, truncation always stated. |
| Q147 | Where submission review lives | **A new page** `/assignments/{id}/students/{studentId}/submission`, mirroring `/…/sign-off` — the tab's review UI moves there. |
| Q148 | Dispatch now or park | **Park.** A light round starts with a plan gate; it deserves a fresh start, not the tail of a long session. |
| Q149 | One round or two | **Two:** (a) the view page + audience read, (b) the progress and submission pages. Recorded on #453. |
| Q150 | Merge #454 before branching | **Merge first, then branch from `main`** — a docs-only PR, and an implementation branch without its spec is a branch that cannot be reviewed against it. |
| Q151 | Panel placement | **Aggregates and decisions in main, row collections in the rail** (§4.8). Rail: Content & Resources · Delivery · Notifications · Rules (pre-publish: Targets & Audience replaces Delivery). Main: Details · Questions · Progress · Sign-off · Approval. |
| Q152 | Draft audience as a table | **Targets & Audience moves to main on a draft and renders a per-student table** (student · guardian(s) · contacts · verdict); Approval takes its rail slot. Backed by a **rows variant of the preview read, called once per load** — never on the authoring surface's 500 ms debounce. Supersedes Q151's draft row. |
| Q153 | Published: Delivery as a table in main | **Yes** — Delivery moves rail → main as a table, and Progress + Guardian sign-off move back to the rail, so A and B show one audience surface in one shape. Supersedes Q151's published row. |
| Q154 | Contact exposure | **The page carries no contact value.** No address or phone number in the DTO, not merely masked; channel kind + count + role + delivery state only. Guardian names only where the caller could already list them in the Students module. Any address need is a separate audited action (§4.9). |
| Q155 | Status read-outs as one panel | **One Status panel in main, below Delivery**, holding Progress · Guardian sign-off · Notifications as three segments divided by coloured vertical rules. The rail drops to Content & Resources + Rules (Approval pre-publish). Page height 4 991 → 4 089 px. |
| Q156 | Paging on every grid | **Every data-driven grid pages, default 15, page size per grid.** The rule lives in `.github/copilot/rules/blazor-components.md`; Questions and Delivery/audience also expose a 15/25/50 control. `LandingGridSettings.ItemsPerPage` (10 today → 15) is shared chrome, so it moves in the implementation round under the full gate, not in this docs PR (§4.10). |
| Q157 | Dispatch round (a)? | **Stay parked.** The layout moved after the spec was first written and invalidated part of it three turns running; a plan gate should read a spec that has stopped moving. |
| Q158 | Guardian names | **Drop them.** Role and count only. The proposed gate ("show a name if the caller could list that student's guardians") has no mechanism — `TeacherScope` answers a different question, and a per-student authorization hop would fan out a read. |
| Q159 | The two specs that page at 5 | **Amend both** to point at the rule ("default 15, per grid") in the round that applies paging — not before. |
| Q160 | The two lesson candidates | **Write both** to `.github/copilot/rules/agent-lessons.md`. |
| Q161 | Merge #455 | **On green only** — one re-run for a flake (runbook line 129), a second identical failure means `main` is red and the answer becomes fix-forward. Merged as `3e6be4b9` after the re-run passed. |
| Q162 | Pager when the set fits | **Hide the pager and the size selector** when `total ≤ page size`. `1–8 of 8` with dead arrows is chrome, not information. |
| Q163 | `EntityGrid.ShowPager` | **Flip the default to `true`** — it is `false`, no call site sets it, and five grids therefore never page. Shared chrome: lands with `ItemsPerPage` 10 → 15 under the full gate. |

  |---|---|---|---|
  | 1 | §2 re-decided lifecycle primaries (Published → *Open progress*) while claiming the §11 matrix could not fork; the matrix reads Published → Unpublish, Closed → Archive | **P1** | Applied — header follows §11; Q141 later settled the view exception, so §11 now records it explicitly and the decision takes `surface` as an input (pass 2, finding 1) |
  | 2 | Materials sat in **main**; the ownership test puts Content & Resources in the **rail** | P2 | Applied — rail, with the grounding readout per row and no separate AI panel |
  | 3 | The view surface claimed the create/edit pattern without the scope line being amended | P2 | Applied — the amendment lands **in this round**, not deferred |
  | 4 | "Tabs retired" contradicted UX-10 / D5, which retain the operational tabs | P3 | Applied — this spec supersedes them explicitly, and the compartments doc gains a pointer |
  | 5 | Rail panel invented as "Publishing & audience"; the settled name is "Targets & Audience" | P3 | Applied — settled name kept; the new cohort panel is **Delivery** |
  | 6 | The preview dialog is a design-named component, so the component approval gate applies | P3 | Applied — recorded in §4.4 |

- **Second pass (same model, same pool) — 4/6 closed, 2 P1s returned.** Both were contradictions my own fixes
  introduced, and both are now closed by construction:

  | # | Second-pass finding | Sev | Fix |
  |---|---|---|---|
  | A | §4.1's pure decision had no `surface` input, so it could not yield the surface-dependent *Open progress* primary that §11 records | **P1** | Applied — `(surface, status, approval, policy)` is the signature, and `surface` is named as an input |
  | B | §6 listed amending `panel-layout-pattern.md`'s scope line as **out of scope**, while §4.1 committed it to this round | **P1** | Applied — removed from Out of Scope, and the amendment **landed** in `panel-layout-pattern.md` §1 (view surfaces wear the skeleton) |

  Findings 2, 4, 5 and 6 were confirmed resolved.

- **Third pass — clean.** `0/6` unresolved; no drift remains. Both second-pass findings were confirmed closed.

- **Self-review notes retained:** status pills land on this page only, per Q118 (§4.5); `NFR-4` is preserved by the
  embeddable mode and the header fact row is explicitly presentational; name resolution uses the **batched**
  subscriber read, not `IStudentDirectory` per student — the first draft implied N+1 and was corrected.
- **Mock is not evidence** (L4): the PNGs settle the look; only the tests settle the behaviour.

---

---

