# Runbook — seed the assignment-view probe

**Dev-only.** Two SQL scripts (plus one optional third-database step) that put 28 students and two assignments
into a developer's local AppHost databases, so the assignment view (`/assignments/{id}`) can be observed in a
browser against real counts — a page nobody has yet seen rendered.

They are **never** part of `MigrationService` (owner Q145): that runs in every environment, so 28 probe
students, two fake assignments and 27 fake delivery-log rows would ship to production. Nothing here is
referenced by a test — the automated evidence is the bUnit/API suites; this script exists for the one thing
they cannot show, a real browser against a real database (`.github/copilot/rules/testing.md` § "UI rounds need
one live pass").

## The two page states it makes observable

The page chooses its read from ONE column, `assignments.published_at`:

| Probe assignment | `published_at` | Panel title | Verdict column | Read behind it |
|---|---|---|---|---|
| `PROBE — published audience` (`a50b0000-…-000000000001`) | set | **Delivery** | `Delivery` | the notification log (`GetAssignmentAudienceHandler`) |
| `PROBE — draft with targets` (`a50b0000-…-000000000002`) | NULL | **Targets & Audience** | `At publish` | the policy-filtered preview (`GetAssignmentAudiencePreviewHandler`) |

Everything else on the page renders in both states; these two tables are the pair you cannot see without both
assignments.

## Symptom

- **The Delivery table is empty or all-`Unknown`** while the endpoint is healthy — either no recipient rows
  were seeded for the **current tenant**, or the assignment's `assignment_targets` rows are missing: the
  published read resolves the cohort from the targets and returns nothing when there are none, even though
  recipients exist. The tenant id is a variable, the ids are fixed — a fixture seeded under another tenant
  reads as an empty panel, which is the most common cause.
- **A one-row cohort with everything `Unknown`** — recipients were seeded but no `notification_logs` rows
  were (a missing publish row is `Unknown` by design, never a fabricated `Sent`).
- **A state nobody can judge by eye** — the panel distinguishes *Not reached*, *Sent*, *Opened*, *Failed*,
  *Skipped*, *Unknown* and (*draft only*) *Blocked by policy*, plus a pager that must be **absent** on an
  8-row set. One seeded shape cannot show any of that; seed both halves.

## What it seeds, and what each row proves

28 students, surnames ascending A→Z so the table's order is checkable by eye. Each carries one of the five
contact shapes the Delivery column distinguishes:

| Rows | Contact shape | Delivery column must read |
|---|---|---|
| `Amara Adeyemi`, `Zanele Zulu` (2) | no guardian, no contact at all | **Not reached** · detail `no subscribed contact`, Guardians `none` |
| 20 students, `Farah Fourie` … `Xolani Xaba` | one guardian (Primary), one email contact, subscribed | the normal **Sent** / **Opened** / **Failed** / **Unknown** cases below |
| `Busisiwe Bhengu`, `Chloe Cele`, `Dineo Dlamini` (3) | **two** guardians (Primary + CC), one subscribed email each | **two contact lines under one student row**, Guardians `2 · Primary, CC` |
| `Bongani Bello`, `Yvonne Yende` (2) | one email contact whose subscription is **Unsubscribed** | **Skipped** · detail `guardian opted out` |
| `Emeka Ebrahim` (1) | one **SMS** contact, subscribed — the only SMS row in the cohort | draft only: **Blocked by policy** · detail `blocked by notification policy` (see the optional step) |

Delivery states over the published assignment's 28 recipient rows (the last line counts cohort rows, not
recipients):

| State | Rows | Seeded as | Detail line the page prints |
|---|---|---|---|
| **Sent** | 18 | log row `delivery_status = 1`, recipient `opened_at` NULL | `sent HH:mm` |
| **Opened** | 4 | log row `delivery_status = 1` **and** recipient `opened_at` set — `Bhengu`'s first contact, `Hadebe`, `Qwabe`, `Van Wyk` | `sent HH:mm · opened HH:mm` |
| **Failed** | 3 | log row `delivery_status = 2`, `failure_reason = 'mailbox full'` — `Gama`, `Langa`, `Sithole` | `mailbox full` |
| **Skipped** | 2 | log row `delivery_status = 3`, `failure_reason = 'guardian opted out'` — `Bello`, `Yende` | `guardian opted out` |
| **Unknown** | 1 | `Ngcobo` has a **recipient row and no log row at all** | `delivery log unavailable` |
| *(no contact at all)* | 3 rows | `Adeyemi`, `Zulu` (no contact) and `Ebrahim` (its only channel was blocked at publish, so publish wrote no recipient) | `no subscribed contact` |

The published assignment also carries **8 questions** (the pager-absent set), **2 attachments** and
**1 resource link**; the draft carries **6 questions** and **no materials**.

## Run it

1. Start the AppHost and wait until `students-db`, `assignments-db` and `settings-db` are healthy.
2. Pick the tenant you are signed in as. The dev tenant switcher shows the name; get the id from the settings
   database:

   ```bash
   psql "$SETTINGS_DSN" -c "select id, name from tenants order by name;"
   ```

   Seeded tenants are `Hydeson School` and `Little Legends`. Tenant ids are random GUIDs, which is why the
   scripts take one as a variable rather than hard-coding it.
3. Take the Postgres connection strings from the Aspire dashboard and export them:

   ```bash
   export STUDENTS_DSN="host=localhost port=<p> dbname=students-db user=postgres password=<...>"
   export ASSIGNMENTS_DSN="host=localhost port=<p> dbname=assignments-db user=postgres password=<...>"
   ```

4. Run the two halves **in this order** — the assignments half only shares bare GUIDs with the students half
   (there is no cross-database foreign key), but its recipients point at the guardians and contacts the first
   script creates:

   ```bash
   psql "$STUDENTS_DSN"    -v tenant_id=<tenant-guid> -f documents/runbooks/assignment-view-probe-seed.students.sql
   psql "$ASSIGNMENTS_DSN" -v tenant_id=<tenant-guid> -f documents/runbooks/assignment-view-probe-seed.assignments.sql
   ```

   Pass the **bare** guid: the scripts interpolate it as `:'tenant_id'`, which adds the SQL quotes itself.
   Passing `-v tenant_id="'<guid>'"` would arrive double-quoted and the cast to `uuid` would fail.
5. Open the two pages (the assignment ids are fixed):

   - `/assignments/a50b0000-0000-4000-8000-000000000001` — published, log-backed Delivery.
   - `/assignments/a50b0000-0000-4000-8000-000000000002` — draft, policy-filtered Targets & Audience.

### If step 4 aborts on `gender_coded_value_id`

`students.gender_coded_value_id` is **NOT NULL**, and coded values live in `settings-db`, which the students
connection cannot see — so the script cannot look one up itself. It inherits the value from any existing
student in the same database (the normal case, nothing to do), and stops with
`seed: no gender_id available - pass -v gender_id=<guid>` on a database with no students:

```bash
psql "$SETTINGS_DSN" -tAc "select id from coded_values where code ilike 'male' or code ilike 'female' limit 1"
psql "$STUDENTS_DSN" -v tenant_id=<tenant-guid> -v gender_id=<that-guid> \
     -f documents/runbooks/assignment-view-probe-seed.students.sql
```

Pass the bare GUID, like `tenant_id`.

### Optional step — the two states these two databases cannot produce

**Optional: the page renders without this, and without it two states are simply unobservable.** Both live in
`settings-db`, because the draft's verdict column is decided by the effective notification policy and the
Approval panel by the effective assignment policy — neither has a row in students-db or assignments-db:

- `tenant_notification_policies.blocked_channels` decides *Blocked by policy*. Check for an existing row
  first; the cohort has exactly ONE SMS contact (`Ebrahim`), so blocking the SMS channel blocks exactly one
  line and nothing else:

  ```sql
  -- settings-db. Look first: if a row already exists, note its current blocked_channels before you change it.
  SELECT tenant_id, blocked_channels FROM tenant_notification_policies WHERE tenant_id = :'tenant_id'::uuid;

  UPDATE tenant_notification_policies SET blocked_channels = '[1]'::jsonb, updated_at = now()
   WHERE tenant_id = :'tenant_id'::uuid AND is_deleted = false;
  INSERT INTO tenant_notification_policies (id, tenant_id, preferred_channel_order, blocked_channels,
                                            created_at, updated_at, is_deleted)
  SELECT '7c0b0000-0000-4000-8000-000000000001', :'tenant_id'::uuid, '[]'::jsonb, '[1]'::jsonb,
         now(), now(), false
   WHERE NOT EXISTS (SELECT 1 FROM tenant_notification_policies
                      WHERE tenant_id = :'tenant_id'::uuid AND is_deleted = false);

  -- RESTORE (the inserted row), or put back the old array if you updated one:
  -- DELETE FROM tenant_notification_policies WHERE id = '7c0b0000-0000-4000-8000-000000000001';
  ```

  `[1]` is `NotificationChannel.SMS` — the column is `jsonb` holding the enum array. After this the draft's
  `Ebrahim` line reads *Blocked by policy* / `blocked by notification policy`, and the published page is
  unchanged (its read is log-backed and never re-runs the policy).

- `tenant_assignment_policies.requires_approval_before_publish` decides the rail's **Approval** panel. It is
  `RequiresApprovalBeforePublish || FEATURE:RequireAssignmentApproval` (that AppHost parameter is `false` in
  dev), and the panel additionally requires `Status == Draft` — so one row puts the panel on the draft only:

  ```sql
  UPDATE tenant_assignment_policies SET requires_approval_before_publish = true, updated_at = now()
   WHERE tenant_id = :'tenant_id'::uuid AND is_deleted = false;
  INSERT INTO tenant_assignment_policies (id, tenant_id, requires_approval_before_publish,
                                          created_at, updated_at, is_deleted)
  SELECT '7c0b0000-0000-4000-8000-000000000002', :'tenant_id'::uuid, true, now(), now(), false
   WHERE NOT EXISTS (SELECT 1 FROM tenant_assignment_policies
                      WHERE tenant_id = :'tenant_id'::uuid AND is_deleted = false);

  -- RESTORE (the inserted row), or set the column back to false:
  -- DELETE FROM tenant_assignment_policies WHERE id = '7c0b0000-0000-4000-8000-000000000002';
  ```

  With it, the draft gains the `Approval` rail panel (`Submit for approval`), the header chip
  `Approval not submitted`, and a **disabled** `Publish` primary with the visible reason
  `Approval is required before this assignment can be published`; the published page gains the same chip and
  no panel.

After the blocked-channel step the draft table follows on the next reload (the preview's notification-policy
read is not cached). After the approval step, wait out the 5-minute cache on the assignment summary the
header and the rail are built from — or restart the AppHost — before judging them. `contacts/subscribed` is
cached for 5 minutes too, so a re-seed is not visible to the draft read until that expires.

## The expected read-out

**Published page** (`…0001`) — Delivery panel:

- header `Delivery`, count 28; the rows-per-page control **is** present (28 > the 15-row default) and the
  footer reads **`Showing 1–15 of 28 · 23 reached`**.
- first page, in order: the four **nameless** rows (`Adeyemi`, `Bello`, `Yende`, `Zulu` — see below), then
  `Bhengu` (two lines: Opened + Sent), `Cele` (Sent + Sent), `Dlamini` (Sent + Sent), `Ebrahim`
  (*Not reached*), `Fourie` (Sent), `Gama` (`mailbox full`), `Hadebe` (Opened), `Ismail`, `Jacobs`, `Khumalo`
  (Sent), `Langa` (`mailbox full`).
- Status panel: `Cohort 28` · `Submitted 0 · Graded 0` · `Reached 23`; Notifications
  **`Queued 0 · Sent 22`**, **`Failed 3 · Skipped 2`**, `Retry scheduled 0`. The strip's *Sent* counts the
  `Sent` **state**, so it includes the four rows the verdict column renders as *Opened* (22 of the 27 log
  rows; the 28th recipient is the `Unknown` one).
- Questions: 8 rows, footer `Showing 1–8 of 8`, and **no pager and no rows-per-page control** — the point of
  the 8-row set.
- Content & Resources (count 3): `probe-photosynthesis-notes.txt` → `grounds ✓ · extracted text`;
  `probe-leaf-diagram.png` → `image only · not grounded`; `Probe reading` → a link with **no grounding line**
  at all (`ResourceDto` carries no extraction outcome; nothing is invented to fill one).
- Details: the `Require guardian review before student submits` checkbox is **unchecked**.

**Draft page** (`…0002`) — Targets & Audience panel:

- header `Targets & Audience`, count 28, verdict column `At publish`, pager present, footer
  **`Showing 1–15 of 28 · 23 will notify`** (`24 will notify` if you skipped the optional blocked-channel
  step).
- 26 contact lines on 23 students read `Will notify` / `resolved at publish`; the three two-guardian students
  show `2 · Primary, CC`; the four contactless students (`Adeyemi`, `Bello`, `Yende`, `Zulu`) read
  **`No contact`** / `no subscribed contact` — the draft's wording, from the same column.
- with the optional step, `Ebrahim` reads **`Blocked by policy`** / `blocked by notification policy`.
- Status panel: `Not published yet` / `—` / `—`, `Not requested` / `Outstanding —`, `Nothing sent` / `Queued 0` /
  `Failed 0 · Skipped 0`, and the foot line `Delivery, sign-off and progress all start at publish.`
- Questions: 6 rows, footer `Showing 1–6 of 6`, no pager. Content & Resources: `No materials yet.`
- Details: the same checkbox is **checked**; the rail's Approval panel is absent unless you ran the optional
  step.

## Known read behaviour this probe will confirm (recorded, not "expected-correct")

- **The four contactless students have a blank name cell today.** Both audience reads take the display name
  from the same `contacts/subscribed` response the contacts come from, so a student with no *subscribed*
  contact contributes no name. They therefore sort first (empty name, then by id) and render `—`. In the
  published state that blank name also trips the panel's degraded note (`Some rows could not be fully
  resolved …`), because the read reports *any* unnamed cohort member as an outage — including ones who
  legitimately have no contact. Confirm it here; it is a read behaviour, not a seed defect.
- **`Unknown` prints `delivery log unavailable`**, which reads like an outage for `Ngcobo`'s deliberately
  absent log row. Same sentence for both meanings; confirm it as it stands.
- **The Guardians column shows role and count only** (`2 · Primary, CC`) — never a guardian name (Q158).

## Deviations from the approved mock (recorded)

- **Question types.** `QuestionType` has exactly three members — `MultipleChoice`, `TrueFalse`,
  `ShortAnswer`, rendered `Multiple choice` / `True / false` / `Short answer`. The mock's `Diagram`,
  `Long answer` and `Calculation` **do not exist** and are not invented here: those rows are seeded as
  `ShortAnswer` (the typed answer), keeping the mock's shape of option counts only where the type has
  options (15 options on the published set, 11 on the draft). The point of the 8-row set — no pager — is
  unaffected.
- **`mandatory_review` is not the approval workflow.** It drives the Details block's read-only
  `Require guardian review before student submits` checkbox (`#authoring-basics-mandatory-review`), and is
  seeded `true` on the draft and `false` on the published assignment. The checkbox shows the ROW's value only
  while the tenant assignment policy leaves guardian review unset — a policy-set value locks it and overrides
  what is displayed, and the Rules panel then says so. The Approval rail panel comes from
  `RequiresApprovalBeforePublish` and the draft status — see the optional step.

## Re-running

Both scripts **delete their own rows by fixed id series and re-insert**. Re-running converges on the fixture
state; it never duplicates, and it never touches a row it did not create. To probe a different tenant, re-run
both with that tenant's id — the delete/re-insert carries the new `tenant_id` through.

To remove the fixture entirely:

```sql
-- assignments-db
DELETE FROM notification_logs      WHERE id::text LIKE '1a0b0000-%';
DELETE FROM assignment_recipients  WHERE id::text LIKE 'ac0b0000-%';
DELETE FROM question_options       WHERE id::text LIKE '0b0b0000-%';
DELETE FROM assignment_questions   WHERE id::text LIKE '9b0b0000-%';
DELETE FROM assignment_attachments WHERE id::text LIKE 'a00b0000-%';
DELETE FROM assignment_resources   WHERE id::text LIKE '8e0b0000-%';
DELETE FROM assignment_targets     WHERE id::text LIKE '7a0b0000-%';
DELETE FROM assignments            WHERE id::text LIKE 'a50b0000-%';

-- students-db
DELETE FROM contact_subscriptions WHERE id::text LIKE '5b0b0000-%';
DELETE FROM contacts              WHERE id::text LIKE 'cc0b0000-%';
DELETE FROM student_guardians     WHERE id::text LIKE '1c0b0000-%';
DELETE FROM guardians             WHERE id::text LIKE '9c0b0000-%';
DELETE FROM students              WHERE id::text LIKE '5c0b0000-%';
```

`contacts/subscribed` is cached for 5 minutes, so a re-seed is not visible to the draft read until the cache
expires (or the AppHost restarts).

## What the probe is looking at

1. **The two tables are different reads, not one table twice.** Published says `Delivery`; draft says
   `At publish` and explains `resolved at publish`.
2. **The counts.** Published: 28 rows, `23 reached`, `Sent 22 · Failed 3 · Skipped 2`. Draft: 28 rows,
   `23 will notify`, no materials, 6 questions.
3. **The pager rule.** 28 audience rows → pager and rows-per-page control; 8 and 6 question rows → neither.
4. **No invented line.** The resource row carries no grounding line; the two attachments carry exactly the
   two sentences their persisted status and content type imply.
5. **Degradation, split by outage.** Stop `students-api` and reload: the **published** page falls back to
   its persisted recipients (25 of the 28 cohort rows survive) with every name blank and the panel's
   degraded note shown; the **draft** page's panel says
   `The audience could not be resolved. Reload the page to try again.` — and every other block still renders.
   Stopping `assignments-api` is a different outage: the whole page fails its summary read and shows
   `Assignment not found.`, not the panel's sentence.

Record what you saw (and any deviation) in the round doc — a probe that is not written down did not happen.
