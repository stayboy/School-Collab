-- Dev-only probe fixtures for the assignment VIEW page (/assignments/{id}).
-- The ASSIGNMENTS half: two assignments — one PUBLISHED with a 28-recipient delivery log, one DRAFT with
-- targets and no log — plus their targets, questions, options, attachments and resource link.
--
-- WHY THIS FILE EXISTS AND IS NOT IN MigrationService: MigrationService ships to every environment, so two
-- probe assignments, 28 recipients and 27 fake delivery-log rows would land in production (owner Q145).
-- Nothing here is referenced by a test — the automated evidence is the bUnit/API suites; this script exists
-- for the one thing they cannot show, a real browser against a real database.
--
-- Idempotent by FIXED GUIDS: every row this script owns carries an id in one of these fixed series, and the
-- delete phase removes the whole series before re-inserting it. Nothing else is touched.
--
--   a50b0000-0000-4000-8000-000000000001   the PUBLISHED probe assignment
--   a50b0000-0000-4000-8000-000000000002   the DRAFT probe assignment
--   7a0b0000-0000-4000-8000-00000000NN0K   assignment_targets      (NN = roster row, K = 1 published, 2 draft)
--   ac0b0000-0000-4000-8000-00000000NN0K   assignment_recipients   (K = contact index, as in the students half)
--   1a0b0000-0000-4000-8000-00000000NN0K   notification_logs       (one publish row per recipient)
--   9b0b0000-0000-4000-8000-0000000000NN   assignment_questions    (NN 01..08 published, 09..14 draft)
--   0b0b0000-0000-4000-8000-0000000000NN   question_options
--   a00b0000-0000-4000-8000-0000000000NN   assignment_attachments
--   8e0b0000-0000-4000-8000-0000000000NN   assignment_resources
--
-- Run it AFTER the students half, whose student / guardian / contact ids it re-derives from the same roster
-- row numbers (bare GUIDs — there is no cross-database foreign key):
--   psql "$ASSIGNMENTS_DSN" -v tenant_id=<tenant-guid> -f documents/runbooks/assignment-view-probe-seed.assignments.sql
-- See documents/runbooks/assignment-view-probe-seed.md.

\set ON_ERROR_STOP on

BEGIN;

-- ── Owned rows, removed by id series so a re-run converges ─────────────────────────────────────────────────
DELETE FROM notification_logs      WHERE id::text LIKE '1a0b0000-%';
DELETE FROM assignment_recipients  WHERE id::text LIKE 'ac0b0000-%';
DELETE FROM question_options       WHERE id::text LIKE '0b0b0000-%';
DELETE FROM assignment_questions   WHERE id::text LIKE '9b0b0000-%';
DELETE FROM assignment_attachments WHERE id::text LIKE 'a00b0000-%';
DELETE FROM assignment_resources   WHERE id::text LIKE '8e0b0000-%';
DELETE FROM assignment_targets     WHERE id::text LIKE '7a0b0000-%';
DELETE FROM assignments            WHERE id::text LIKE 'a50b0000-%';

-- ── The roster, as this database must re-declare it ───────────────────────────────────────────────────────
-- `shape` is copied from the students half (keep the two lists in step). `delivery` is this half's own column:
-- what the PUBLISHED read must show for that student's FIRST contact —
--   sent | opened | failed | skipped | unknown (no log row at all) | none (no recipient row either).
DROP TABLE IF EXISTS probe2c_roster;
CREATE TEMP TABLE probe2c_roster ON COMMIT DROP AS
SELECT r.n,
       r.shape,
       r.delivery,
       ('5c0b0000-0000-4000-8000-0000000000' || lpad(r.n::text, 2, '0'))::uuid AS student_id,
       ('9c0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '01')::uuid AS guardian1_id,
       ('9c0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '02')::uuid AS guardian2_id,
       ('cc0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '01')::uuid AS contact1_id,
       ('cc0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '02')::uuid AS contact2_id
FROM (VALUES
        ( 1, 'none',   'none'),
        ( 2, 'optout', 'skipped'),
        ( 3, 'two',    'opened'),
        ( 4, 'two',    'sent'),
        ( 5, 'two',    'sent'),
        ( 6, 'sms',    'none'),
        ( 7, 'plain',  'sent'),
        ( 8, 'plain',  'failed'),
        ( 9, 'plain',  'opened'),
        (10, 'plain',  'sent'),
        (11, 'plain',  'sent'),
        (12, 'plain',  'sent'),
        (13, 'plain',  'failed'),
        (14, 'plain',  'sent'),
        (15, 'plain',  'sent'),
        (16, 'plain',  'unknown'),
        (17, 'plain',  'sent'),
        (18, 'plain',  'sent'),
        (19, 'plain',  'opened'),
        (20, 'plain',  'sent'),
        (21, 'plain',  'failed'),
        (22, 'plain',  'sent'),
        (23, 'plain',  'sent'),
        (24, 'plain',  'opened'),
        (25, 'plain',  'sent'),
        (26, 'plain',  'sent'),
        (27, 'optout', 'skipped'),
        (28, 'none',   'none')
     ) AS r(n, shape, delivery);

-- ── The two assignments ────────────────────────────────────────────────────────────────────────────────────
-- status: 0 Draft, 1 Published. published_at is what the page uses to choose the read: null ⇒ the DRAFT
-- read (targets re-resolved through the policy filter, verdict column "At publish"), set ⇒ the PUBLISHED read
-- (log-backed, verdict column "Delivery"). That single column is the difference between the two page states
-- this probe makes observable.
--
-- mandatory_review is the GUARDIAN-REVIEW flag, not the approval workflow: it drives the Details block's
-- read-only "Require guardian review before student submits" checkbox (#authoring-basics-mandatory-review).
-- The rail's Approval panel is `RequiresApproval && Status == Draft`, where RequiresApproval is
-- `policy.RequiresApprovalBeforePublish || FEATURE:RequireAssignmentApproval` — neither is this column. See
-- the runbook's optional settings-db step for the Approval panel.
--
-- `topic_id` is required by the model (non-nullable Guid) and carries a fixed placeholder: a probe has no
-- Topic row, and nothing on this page reads one — the summary projection maps TopicName as null, and the
-- preview's subject-enrolment hop only runs when a GradeLevel target exists, which neither probe has.
-- `subject_coded_value_id` is the legacy NOT NULL column migration 20260707091652 kept only so the
-- MigrationService backfill can read it; it carries a fixed placeholder for the same reason.
-- `target_audience_type` 3 = TargetAudienceType.Mixed — the value Assignment.SyncDerivedTargeting derives
-- for a set of Student targets.
INSERT INTO assignments (id, tenant_id, title, description, instructions, assignment_type, grading_format,
                         target_audience_type, topic_id, assignment_number, due_date, max_score, pass_score,
                         max_attempts, requires_signature, status, created_by_teacher_id, mandatory_review,
                         published_at, archive_grace_days, context_strand_ids, context_lesson_ids,
                         subject_coded_value_id, created_at, updated_at)
VALUES
    -- 1. PUBLISHED — the log-backed Delivery column: Sent / Opened / Failed / Skipped / Unknown across 28 rows.
    ('a50b0000-0000-4000-8000-000000000001', :'tenant_id'::uuid,
     'PROBE — published audience', 'Dev probe fixture — published state.',
     'Read the panel, not the assignment.', 0, 0, 3,
     '70000000-0000-4000-8000-0000000000c1', 'PROBE-2C-PUB',
     now() + interval '7 days', 20.00, NULL, NULL, false, 1,
     '00000000-0000-0000-0000-000000000003', false,
     now() - interval '2 days', 30, '{}', '{}',
     '50000000-0000-4000-8000-000000000001', now(), now()),

    -- 2. DRAFT — never published: recipients 0, logs 0, and the Targets & Audience panel answers from the
    --    policy-filtered preview read instead. mandatory_review true (the one column that differs from the
    --    published row) so the Details checkbox is observable in both states.
    ('a50b0000-0000-4000-8000-000000000002', :'tenant_id'::uuid,
     'PROBE — draft with targets', 'Dev probe fixture — draft state.',
     'Six questions, no materials, no recipients.', 0, 0, 3,
     '70000000-0000-4000-8000-0000000000c2', 'PROBE-2C-DRAFT',
     now() + interval '14 days', 15.00, NULL, NULL, false, 0,
     '00000000-0000-0000-0000-000000000003', true,
     NULL, 30, '{}', '{}',
     '50000000-0000-4000-8000-000000000001', now(), now());

-- ── Targets: one Student target per roster row on BOTH assignments ───────────────────────────────────────
-- kind is stored as its NAME ('Student' = TargetKind.Student, TGT-6), and it is the authored source of the
-- cohort: the published read AND the draft preview both resolve the cohort through it. Student targets keep
-- the cohort exactly 28 regardless of what else lives in the tenant — an AllStudents target would pull in
-- every other dev student and make the counts unreadable.
INSERT INTO assignment_targets (id, tenant_id, assignment_id, kind, ref_id, display_order, created_at, updated_at)
SELECT ('7a0b0000-0000-4000-8000-00000000' || lpad(s.n::text, 2, '0') || t.k)::uuid,
       :'tenant_id'::uuid, t.assignment_id, 'Student', s.student_id, s.n - 1, now(), now()
FROM probe2c_roster s
CROSS JOIN (VALUES ('a50b0000-0000-4000-8000-000000000001'::uuid, '01'),
                   ('a50b0000-0000-4000-8000-000000000002'::uuid, '02')) AS t(assignment_id, k);

-- ── Recipients: what publish wrote on the PUBLISHED assignment ────────────────────────────────────────────
-- owner_type 1 = ContactOwnerType.Guardian; channel 0 = Email; role 0 = GuardianRole.Primary,
-- 1 = GuardianRole.CC. Every recipient here is an email contact: the one `sms` student has NO recipient row
-- because the publish-time filter blocked the only channel that contact has, so publish wrote nothing (the
-- draft's Targets & Audience read is where that block shows). `subscription_active` is false for the two
-- opted-out contacts — that is what keeps their students out of the footer's "reached" count while their
-- contact line still says Skipped.
-- No recipient rows for `none` (no contact exists) either — 28 cohort rows, 25 with contacts.
-- short_link_code stays NULL (email only), which keeps the global unique index out of play.
INSERT INTO assignment_recipients (id, tenant_id, assignment_id, owner_type, owner_id, ward_student_id,
                                   contact_id, channel, role, notify_on_broadcast, subscription_active,
                                   opened_at, created_at, updated_at)
SELECT ('ac0b0000-0000-4000-8000-00000000' || lpad(s.n::text, 2, '0') || '01')::uuid,
       :'tenant_id'::uuid, 'a50b0000-0000-4000-8000-000000000001', 1, s.guardian1_id, s.student_id,
       s.contact1_id, 0, 0, true,
       s.shape <> 'optout',
       -- "Opened" is SENT plus the recipient's own opened stamp; the log never overwrites it (Q145).
       CASE WHEN s.delivery = 'opened' THEN now() - interval '1 day 3 hours' END,
       now(), now()
FROM probe2c_roster s
WHERE s.shape IN ('plain', 'two', 'optout');

INSERT INTO assignment_recipients (id, tenant_id, assignment_id, owner_type, owner_id, ward_student_id,
                                   contact_id, channel, role, notify_on_broadcast, subscription_active,
                                   created_at, updated_at)
SELECT ('ac0b0000-0000-4000-8000-00000000' || lpad(s.n::text, 2, '0') || '02')::uuid,
       :'tenant_id'::uuid, 'a50b0000-0000-4000-8000-000000000001', 1, s.guardian2_id, s.student_id,
       s.contact2_id, 0, 1, true, true, now(), now()
FROM probe2c_roster s
WHERE s.shape = 'two';

-- ── Publish log rows: the Delivery column's five states ───────────────────────────────────────────────────
-- delivery_status: 0 Queued, 1 Sent, 2 Failed, 3 Skipped. `unknown` students get NO row on purpose — the read
-- answers a missing publish row with Unknown rather than a fabricated Sent. Skipped rows carry no rendered
-- body (the broadcaster's own contract) and their reason is the `failure_reason` the Detail column prints.
-- One publish row per recipient: the unique index ux_notification_logs_publish_uniqueness (tenant,
-- assignment, recipient, kind = 0) allows no more.
INSERT INTO notification_logs (id, tenant_id, assignment_id, recipient_id, contact_id, channel, kind, attempt,
                               to_address, subject, body_html, template_id, delivery_status, sent_at,
                               failure_reason, next_retry_at, is_deleted, created_at, updated_at)
SELECT ('1a0b0000-0000-4000-8000-00000000' || lpad(s.n::text, 2, '0') || '01')::uuid,
       :'tenant_id'::uuid, 'a50b0000-0000-4000-8000-000000000001',
       ('ac0b0000-0000-4000-8000-00000000' || lpad(s.n::text, 2, '0') || '01')::uuid,
       s.contact1_id, 0, 0, 0,
       'probe.student' || lpad(s.n::text, 2, '0') || '@example.org',
       'PROBE — published audience',
       CASE WHEN s.delivery = 'skipped' THEN '' ELSE '<p>PROBE — seeded delivery row.</p>' END,
       NULL,
       CASE s.delivery WHEN 'sent' THEN 1 WHEN 'opened' THEN 1 WHEN 'failed' THEN 2 ELSE 3 END,
       CASE WHEN s.delivery IN ('sent', 'opened') THEN now() - interval '2 days' END,
       CASE s.delivery WHEN 'failed' THEN 'mailbox full'
                       WHEN 'skipped' THEN 'guardian opted out' END,
       NULL, false, now(), now()
FROM probe2c_roster s
WHERE s.delivery IN ('sent', 'opened', 'failed', 'skipped');

-- The three `two` students' SECOND contact: one log row each, all Sent, so each of those rows carries two
-- lines with two different delivery states under one student (contact 1 Opened, contact 2 Sent).
INSERT INTO notification_logs (id, tenant_id, assignment_id, recipient_id, contact_id, channel, kind, attempt,
                               to_address, subject, body_html, template_id, delivery_status, sent_at,
                               failure_reason, next_retry_at, is_deleted, created_at, updated_at)
SELECT ('1a0b0000-0000-4000-8000-00000000' || lpad(s.n::text, 2, '0') || '02')::uuid,
       :'tenant_id'::uuid, 'a50b0000-0000-4000-8000-000000000001',
       ('ac0b0000-0000-4000-8000-00000000' || lpad(s.n::text, 2, '0') || '02')::uuid,
       s.contact2_id, 0, 0, 0,
       'probe.student' || lpad(s.n::text, 2, '0') || '.cc@example.org',
       'PROBE — published audience', '<p>PROBE — seeded delivery row.</p>', NULL,
       1, now() - interval '2 days', NULL, NULL, false, now(), now()
FROM probe2c_roster s
WHERE s.shape = 'two';

-- ── Questions: 8 on the published assignment, 6 on the draft ───────────────────────────────────────────────
-- QuestionType has THREE members — 0 MultipleChoice, 1 TrueFalse, 2 ShortAnswer — so the mock's "Diagram",
-- "Long answer" and "Calculation" rows cannot be seeded as types (they are mock inventions; recorded as a
-- mock correction in the runbook). They land as ShortAnswer, the typed-answer type, and the mix below keeps
-- the mock's shape: option counts only where the type HAS options.
-- 8 rows is the point of this block: 8 ≤ the tables' default 15 rows per page, so the Questions panel renders
-- NO pager and no rows-per-page control.
INSERT INTO assignment_questions (id, assignment_id, question_text, question_type, display_order,
                                  correct_option_id, model_answer, response_kinds)
VALUES
    -- Published: 8 rows.
    ('9b0b0000-0000-4000-8000-000000000001', 'a50b0000-0000-4000-8000-000000000001',
     'Explain in one sentence why water boils at a lower temperature at altitude.', 2, 0, NULL,
     'Lower air pressure means less energy is needed to overcome it.', '{}'),
    ('9b0b0000-0000-4000-8000-000000000002', 'a50b0000-0000-4000-8000-000000000001',
     'Which of these is a renewable energy source?', 0, 1, NULL, NULL, '{}'),
    ('9b0b0000-0000-4000-8000-000000000003', 'a50b0000-0000-4000-8000-000000000001',
     'A plant cell has a cell wall.', 1, 2, NULL, NULL, '{}'),
    ('9b0b0000-0000-4000-8000-000000000004', 'a50b0000-0000-4000-8000-000000000001',
     'Describe the two products of photosynthesis.', 2, 3, NULL,
     'Glucose and oxygen.', '{}'),
    ('9b0b0000-0000-4000-8000-000000000005', 'a50b0000-0000-4000-8000-000000000001',
     'Which instrument measures atmospheric pressure?', 0, 4, NULL, NULL, '{}'),
    ('9b0b0000-0000-4000-8000-000000000006', 'a50b0000-0000-4000-8000-000000000001',
     'Sound travels faster in water than in air.', 1, 5, NULL, NULL, '{}'),
    ('9b0b0000-0000-4000-8000-000000000007', 'a50b0000-0000-4000-8000-000000000001',
     'Which of these is a greenhouse gas?', 0, 6, NULL, NULL, '{}'),
    ('9b0b0000-0000-4000-8000-000000000008', 'a50b0000-0000-4000-8000-000000000001',
     'Show your working: a car travels 150 km in 2 hours. What is its average speed?', 2, 7, NULL,
     '75 km/h.', '{}'),

    -- Draft: 6 rows.
    ('9b0b0000-0000-4000-8000-000000000009', 'a50b0000-0000-4000-8000-000000000002',
     'State the difference between weather and climate.', 2, 0, NULL, NULL, '{}'),
    ('9b0b0000-0000-4000-8000-00000000000a', 'a50b0000-0000-4000-8000-000000000002',
     'Which planet is closest to the sun?', 0, 1, NULL, NULL, '{}'),
    ('9b0b0000-0000-4000-8000-00000000000b', 'a50b0000-0000-4000-8000-000000000002',
     'The moon produces its own light.', 1, 2, NULL, NULL, '{}'),
    ('9b0b0000-0000-4000-8000-00000000000c', 'a50b0000-0000-4000-8000-000000000002',
     'Name one renewable and one non-renewable energy source.', 2, 3, NULL, NULL, '{}'),
    ('9b0b0000-0000-4000-8000-00000000000d', 'a50b0000-0000-4000-8000-000000000002',
     'Which unit measures force?', 0, 4, NULL, NULL, '{}'),
    ('9b0b0000-0000-4000-8000-00000000000e', 'a50b0000-0000-4000-8000-000000000002',
     'Ice is less dense than liquid water.', 1, 5, NULL, NULL, '{}');

-- ── Options: only where the type has options (15 published + 11 draft = 26) ────────────────────────────────
INSERT INTO question_options (id, question_id, option_text, is_correct)
VALUES
    -- Q2 — multiple choice, 4 options.
    ('0b0b0000-0000-4000-8000-000000000001', '9b0b0000-0000-4000-8000-000000000002', 'Solar', true),
    ('0b0b0000-0000-4000-8000-000000000002', '9b0b0000-0000-4000-8000-000000000002', 'Coal', false),
    ('0b0b0000-0000-4000-8000-000000000003', '9b0b0000-0000-4000-8000-000000000002', 'Diesel', false),
    ('0b0b0000-0000-4000-8000-000000000004', '9b0b0000-0000-4000-8000-000000000002', 'Natural gas', false),
    -- Q3 — true / false, 2 options.
    ('0b0b0000-0000-4000-8000-000000000005', '9b0b0000-0000-4000-8000-000000000003', 'True', true),
    ('0b0b0000-0000-4000-8000-000000000006', '9b0b0000-0000-4000-8000-000000000003', 'False', false),
    -- Q5 — multiple choice, 3 options.
    ('0b0b0000-0000-4000-8000-000000000007', '9b0b0000-0000-4000-8000-000000000005', 'Barometer', true),
    ('0b0b0000-0000-4000-8000-000000000008', '9b0b0000-0000-4000-8000-000000000005', 'Thermometer', false),
    ('0b0b0000-0000-4000-8000-000000000009', '9b0b0000-0000-4000-8000-000000000005', 'Hygrometer', false),
    -- Q6 — true / false, 2 options.
    ('0b0b0000-0000-4000-8000-00000000000a', '9b0b0000-0000-4000-8000-000000000006', 'True', true),
    ('0b0b0000-0000-4000-8000-00000000000b', '9b0b0000-0000-4000-8000-000000000006', 'False', false),
    -- Q7 — multiple choice, 4 options.
    ('0b0b0000-0000-4000-8000-00000000000c', '9b0b0000-0000-4000-8000-000000000007', 'Carbon dioxide', true),
    ('0b0b0000-0000-4000-8000-00000000000d', '9b0b0000-0000-4000-8000-000000000007', 'Nitrogen', false),
    ('0b0b0000-0000-4000-8000-00000000000e', '9b0b0000-0000-4000-8000-000000000007', 'Oxygen', false),
    ('0b0b0000-0000-4000-8000-00000000000f', '9b0b0000-0000-4000-8000-000000000007', 'Argon', false),
    -- Draft Q2 — multiple choice, 4 options.
    ('0b0b0000-0000-4000-8000-000000000010', '9b0b0000-0000-4000-8000-00000000000a', 'Mercury', true),
    ('0b0b0000-0000-4000-8000-000000000011', '9b0b0000-0000-4000-8000-00000000000a', 'Venus', false),
    ('0b0b0000-0000-4000-8000-000000000012', '9b0b0000-0000-4000-8000-00000000000a', 'Earth', false),
    ('0b0b0000-0000-4000-8000-000000000013', '9b0b0000-0000-4000-8000-00000000000a', 'Mars', false),
    -- Draft Q3 — true / false, 2 options.
    ('0b0b0000-0000-4000-8000-000000000014', '9b0b0000-0000-4000-8000-00000000000b', 'True', false),
    ('0b0b0000-0000-4000-8000-000000000015', '9b0b0000-0000-4000-8000-00000000000b', 'False', true),
    -- Draft Q5 — multiple choice, 3 options.
    ('0b0b0000-0000-4000-8000-000000000016', '9b0b0000-0000-4000-8000-00000000000d', 'Newton', true),
    ('0b0b0000-0000-4000-8000-000000000017', '9b0b0000-0000-4000-8000-00000000000d', 'Joule', false),
    ('0b0b0000-0000-4000-8000-000000000018', '9b0b0000-0000-4000-8000-00000000000d', 'Watt', false),
    -- Draft Q6 — true / false, 2 options.
    ('0b0b0000-0000-4000-8000-000000000019', '9b0b0000-0000-4000-8000-00000000000e', 'True', true),
    ('0b0b0000-0000-4000-8000-00000000001a', '9b0b0000-0000-4000-8000-00000000000e', 'False', false);

-- `correct_option_id` is what the Edit surface round-trips; the view page reads the per-option flag instead.
-- Set it to the single is_correct option so the two readings cannot disagree.
UPDATE assignment_questions q
SET correct_option_id = o.id
FROM question_options o
WHERE o.question_id = q.id
  AND o.is_correct
  AND q.id::text LIKE '9b0b0000-%';

-- ── Materials: two attachments (one grounded, one image-only) and one resource link ───────────────────────
-- extraction_status: 0 NotAttempted, 1 Succeeded, 2 Failed. The rail's grounding line is derived from exactly
-- two facts — the persisted status and the content type — so:
--   * a Succeeded text attachment reads  "grounds ✓ · extracted text"
--   * an image with no attempt reads     "image only · not grounded"
-- The resource link has NO grounding line at all: ResourceDto carries no extraction outcome, and nothing is
-- invented to fill one.
INSERT INTO assignment_attachments (id, assignment_id, file_name, content_type, file_size, storage_path,
                                    extraction_status, extracted_text, extracted_at, extraction_error)
VALUES
    ('a00b0000-0000-4000-8000-000000000001', 'a50b0000-0000-4000-8000-000000000001',
     'probe-photosynthesis-notes.txt', 'text/plain', 2048,
     'probe/assignment-view/probe-photosynthesis-notes.txt', 1,
     'Photosynthesis: carbon dioxide + water + light → glucose + oxygen.', now() - interval '2 days', NULL),
    ('a00b0000-0000-4000-8000-000000000002', 'a50b0000-0000-4000-8000-000000000001',
     'probe-leaf-diagram.png', 'image/png', 51200,
     'probe/assignment-view/probe-leaf-diagram.png', 0,
     NULL, NULL, NULL);

INSERT INTO assignment_resources (id, tenant_id, assignment_id, resource_kind, url, storage_path,
                                  display_name, included_in_generation, created_at, updated_at)
VALUES
    ('8e0b0000-0000-4000-8000-000000000001', :'tenant_id'::uuid, 'a50b0000-0000-4000-8000-000000000001',
     0, 'https://example.org/probe-reading', NULL, 'Probe reading', true, now(), now());

COMMIT;

-- What the run should print — every count the page must show. `opened` is SENT plus the recipient's own
-- opened stamp, so it is a rendering of four of the sent rows, not a sixth log state.
SELECT line, value FROM (
    SELECT 1 AS ord, 'published · cohort rows (targets)' AS line,
           (SELECT count(*)::text FROM assignment_targets
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000001') AS value
    UNION ALL SELECT 2, 'published · rows with a contact',
           (SELECT count(DISTINCT r.ward_student_id)::text FROM assignment_recipients r
             WHERE r.assignment_id = 'a50b0000-0000-4000-8000-000000000001')
    UNION ALL SELECT 3, 'published · Not reached / no subscribed contact',
           ((SELECT count(*) FROM assignment_targets
              WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000001')
          - (SELECT count(DISTINCT r.ward_student_id) FROM assignment_recipients r
              WHERE r.assignment_id = 'a50b0000-0000-4000-8000-000000000001'))::text
    UNION ALL SELECT 4, 'published · recipient rows',
           (SELECT count(*)::text FROM assignment_recipients
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000001')
    UNION ALL SELECT 5, 'published · verdict Sent (log Sent, not opened)',
           (SELECT count(*)::text FROM notification_logs l
              JOIN assignment_recipients r ON r.id = l.recipient_id
             WHERE l.assignment_id = 'a50b0000-0000-4000-8000-000000000001'
               AND l.delivery_status = 1 AND r.opened_at IS NULL)
    UNION ALL SELECT 6, 'published · verdict Opened (log Sent + opened_at)',
           (SELECT count(*)::text FROM notification_logs l
              JOIN assignment_recipients r ON r.id = l.recipient_id
             WHERE l.assignment_id = 'a50b0000-0000-4000-8000-000000000001'
               AND l.delivery_status = 1 AND r.opened_at IS NOT NULL)
    UNION ALL SELECT 7, 'published · Status panel Sent (the Sent STATE, incl. opened)',
           (SELECT count(*)::text FROM notification_logs
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000001'
               AND delivery_status = 1)
    UNION ALL SELECT 8, 'published · Failed (mailbox full)',
           (SELECT count(*)::text FROM notification_logs
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000001' AND delivery_status = 2)
    UNION ALL SELECT 9, 'published · Skipped (guardian opted out)',
           (SELECT count(*)::text FROM notification_logs
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000001' AND delivery_status = 3)
    UNION ALL SELECT 10, 'published · Unknown (no log row)',
           (SELECT count(*)::text FROM assignment_recipients r
             WHERE r.assignment_id = 'a50b0000-0000-4000-8000-000000000001'
               AND NOT EXISTS (SELECT 1 FROM notification_logs l WHERE l.recipient_id = r.id))
    UNION ALL SELECT 11, 'published · reached (footer + Status panel)',
           (SELECT count(DISTINCT r.ward_student_id)::text FROM assignment_recipients r
             WHERE r.assignment_id = 'a50b0000-0000-4000-8000-000000000001'
               AND r.subscription_active)
    UNION ALL SELECT 12, 'published · questions (8 ⇒ no pager)',
           (SELECT count(*)::text FROM assignment_questions
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000001')
    UNION ALL SELECT 13, 'published · question options',
           (SELECT count(*)::text FROM question_options WHERE id::text LIKE '0b0b0000-%'
                 AND question_id IN (SELECT id FROM assignment_questions
                                      WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000001'))
    UNION ALL SELECT 14, 'published · attachments',
           (SELECT count(*)::text FROM assignment_attachments
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000001')
    UNION ALL SELECT 15, 'published · resource links',
           (SELECT count(*)::text FROM assignment_resources
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000001')
    UNION ALL SELECT 16, 'draft · cohort rows (targets)',
           (SELECT count(*)::text FROM assignment_targets
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000002')
    UNION ALL SELECT 17, 'draft · recipient rows (must be 0)',
           (SELECT count(*)::text FROM assignment_recipients
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000002')
    UNION ALL SELECT 18, 'draft · notification rows (must be 0)',
           (SELECT count(*)::text FROM notification_logs
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000002')
    UNION ALL SELECT 19, 'draft · questions',
           (SELECT count(*)::text FROM assignment_questions
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000002')
    UNION ALL SELECT 20, 'draft · attachments (must be 0)',
           (SELECT count(*)::text FROM assignment_attachments
             WHERE assignment_id = 'a50b0000-0000-4000-8000-000000000002')
    UNION ALL SELECT 21, 'draft · mandatory_review (Details checkbox)',
           (SELECT mandatory_review::text FROM assignments
             WHERE id = 'a50b0000-0000-4000-8000-000000000002')
) AS report
ORDER BY ord;
