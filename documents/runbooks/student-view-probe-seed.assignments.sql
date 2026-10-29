-- Dev-only probe fixtures for the student-view assignment-stats panel (issue #444, layer L2b).
-- The ASSIGNMENTS half: six assignments plus their recipient links, two submissions and two guardian gates,
-- so every segment of both bars is non-zero and each rule the panel claims can be checked by eye.
--
-- WHY THIS FILE EXISTS AND IS NOT IN MigrationService: MigrationService ships to every environment, so probe
-- fixtures would land in production (owner Q145).
--
-- Idempotent by FIXED GUIDS and PREFIXED ROWS: every row this script owns is deleted by id and re-inserted.
-- Run it AFTER the students half, which creates the probe student and the guardian contact the recipient rows
-- point at (ids are bare GUIDs — there is no cross-database foreign key):
--   psql "$ASSIGNMENTS_DSN" -v tenant_id=<tenant-guid> -f documents/runbooks/student-view-probe-seed.assignments.sql
-- See documents/runbooks/student-view-probe-seed.md.

\set ON_ERROR_STOP on

BEGIN;

-- ── Owned rows, removed by id so a re-run converges ────────────────────────────────────────────────────────
DELETE FROM guardian_submission_gates
 WHERE id IN ('9a7e0000-0000-4000-8000-000000000001', '9a7e0000-0000-4000-8000-000000000002');
DELETE FROM assignment_submissions
 WHERE id IN ('45ba0000-0000-4000-8000-000000000001', '45ba0000-0000-4000-8000-000000000002');
DELETE FROM assignment_recipients
 WHERE id IN ('ac0a0000-0000-4000-8000-000000000001', 'ac0a0000-0000-4000-8000-000000000002',
              'ac0a0000-0000-4000-8000-000000000003', 'ac0a0000-0000-4000-8000-000000000004',
              'ac0a0000-0000-4000-8000-000000000005', 'ac0a0000-0000-4000-8000-000000000006');
DELETE FROM assignments
 WHERE id IN ('a50a0000-0000-4000-8000-000000000001', 'a50a0000-0000-4000-8000-000000000002',
              'a50a0000-0000-4000-8000-000000000003', 'a50a0000-0000-4000-8000-000000000004',
              'a50a0000-0000-4000-8000-000000000005', 'a50a0000-0000-4000-8000-000000000006');

-- ── Six assignments: one per state the panel distinguishes ─────────────────────────────────────────────────
-- status: 0 Draft, 1 Published, 2 Closed, 3 Scheduled, 4 Archived (AssignmentStatus).
-- subject_coded_value_id is retained NOT NULL by the schema (kept for the MigrationService backfill), so it
-- carries a fixed placeholder GUID; topic_id, grade_level_id and every other optional column stay NULL.
INSERT INTO assignments (id, tenant_id, title, assignment_number, subject_coded_value_id,
                         status, published_at, mandatory_review,
                         created_by_teacher_id, created_at, updated_at)
VALUES
    -- 1. Draft: recipient-linked, never sent → Assigned only ("not sent" on the funnel).
    ('a50a0000-0000-4000-8000-000000000001', :'tenant_id'::uuid, 'PROBE — not sent (draft)',
     'PROBE-01', '50000000-0000-4000-8000-000000000001', 0, NULL, true,
     '70000000-0000-4000-8000-000000000001', now(), now()),

    -- 2. Published, mandatory review, gate REVIEWED → Sent out + Review required + Reviewed.
    ('a50a0000-0000-4000-8000-000000000002', :'tenant_id'::uuid, 'PROBE — review gate cleared',
     'PROBE-02', '50000000-0000-4000-8000-000000000001', 1, now() - interval '10 days', true,
     '70000000-0000-4000-8000-000000000001', now(), now()),

    -- 3. Published, nothing submitted → Sent out, counted in "awaiting submission".
    --    mandatory_review is FALSE on purpose: a gated assignment belongs in "awaiting review" and must
    --    never be a second fixture for it. Set true here and the review axis reads 3 required, which
    --    contradicts the expected counts this script asserts at the bottom.
    ('a50a0000-0000-4000-8000-000000000003', :'tenant_id'::uuid, 'PROBE — awaiting submission',
     'PROBE-03', '50000000-0000-4000-8000-000000000001', 1, now() - interval '9 days', false,
     '70000000-0000-4000-8000-000000000001', now(), now()),

    -- 4. Published, submission FINALIZED but still ReviewState.Pending → Submitted, "awaiting marking".
    ('a50a0000-0000-4000-8000-000000000004', :'tenant_id'::uuid, 'PROBE — awaiting marking',
     'PROBE-04', '50000000-0000-4000-8000-000000000001', 1, now() - interval '8 days', false,
     '70000000-0000-4000-8000-000000000001', now(), now()),

    -- 5. Published, submission finalized AND Graded → Submitted + Marked.
    ('a50a0000-0000-4000-8000-000000000005', :'tenant_id'::uuid, 'PROBE — marked',
     'PROBE-05', '50000000-0000-4000-8000-000000000001', 1, now() - interval '7 days', false,
     '70000000-0000-4000-8000-000000000001', now(), now()),

    -- 6. Published, mandatory review, gate NOT reviewed → Review required, "awaiting review"
    --    (and it must NOT count in awaiting submission — the gate is an overlay, not a funnel step).
    ('a50a0000-0000-4000-8000-000000000006', :'tenant_id'::uuid, 'PROBE — held by the review gate',
     'PROBE-06', '50000000-0000-4000-8000-000000000001', 1, now() - interval '6 days', true,
     '70000000-0000-4000-8000-000000000001', now(), now());

-- ── Recipient links: what makes an assignment "Assigned" (AssignmentRecipient.WardStudentId) ───────────────
-- owner_type 1 = ContactOwnerType.Guardian; owner_id is the guardian from the students half, contact_id is
-- that guardian's contact. channel 0 = Email, so short_link_code stays NULL and the global unique index on it
-- is never involved. role 0 = GuardianRole.Primary.
INSERT INTO assignment_recipients (id, tenant_id, assignment_id, owner_type, owner_id, ward_student_id,
                                   contact_id, channel, role, created_at, updated_at)
SELECT link.id, :'tenant_id'::uuid, link.assignment_id, 1,
       '9c0a0000-0000-4000-8000-000000000001', '5c0a0000-0000-4000-8000-000000000001',
       'cc0a0000-0000-4000-8000-000000000002', 0, 0, now(), now()
FROM (VALUES
        ('ac0a0000-0000-4000-8000-000000000001'::uuid, 'a50a0000-0000-4000-8000-000000000001'::uuid),
        ('ac0a0000-0000-4000-8000-000000000002'::uuid, 'a50a0000-0000-4000-8000-000000000002'::uuid),
        ('ac0a0000-0000-4000-8000-000000000003'::uuid, 'a50a0000-0000-4000-8000-000000000003'::uuid),
        ('ac0a0000-0000-4000-8000-000000000004'::uuid, 'a50a0000-0000-4000-8000-000000000004'::uuid),
        ('ac0a0000-0000-4000-8000-000000000005'::uuid, 'a50a0000-0000-4000-8000-000000000005'::uuid),
        ('ac0a0000-0000-4000-8000-000000000006'::uuid, 'a50a0000-0000-4000-8000-000000000006'::uuid)
     ) AS link(id, assignment_id);

-- ── Two submissions: one finalized-but-unmarked, one finalized-and-graded ─────────────────────────────────
-- review_state: 0 Pending, 1 Reviewed, 2 Graded (ReviewState). Marked requires finalized_at AND Graded —
-- exactly the pair the panel's "awaiting marking" segment depends on.
INSERT INTO assignment_submissions (id, tenant_id, assignment_id, student_id, last_submitted_at,
                                    finalized_at, review_state, sign_off_state, created_at, updated_at)
VALUES
    ('45ba0000-0000-4000-8000-000000000001', :'tenant_id'::uuid,
     'a50a0000-0000-4000-8000-000000000004', '5c0a0000-0000-4000-8000-000000000001',
     now() - interval '5 days', now() - interval '5 days', 0, 0, now(), now()),
    ('45ba0000-0000-4000-8000-000000000002', :'tenant_id'::uuid,
     'a50a0000-0000-4000-8000-000000000005', '5c0a0000-0000-4000-8000-000000000001',
     now() - interval '5 days', now() - interval '5 days', 2, 0, now(), now());

-- ── Two guardian gates: one reviewed (Reviewed), one open (Awaiting review) ────────────────────────────────
INSERT INTO guardian_submission_gates (id, tenant_id, assignment_id, student_id, reviewed_by_guardian_id,
                                       reviewed_at, review_comment, submission_enabled_for_student,
                                       created_at, updated_at)
VALUES
    ('9a7e0000-0000-4000-8000-000000000001', :'tenant_id'::uuid,
     'a50a0000-0000-4000-8000-000000000002', '5c0a0000-0000-4000-8000-000000000001',
     '9c0a0000-0000-4000-8000-000000000001', now() - interval '10 days', 'PROBE — cleared', true,
     now(), now()),
    ('9a7e0000-0000-4000-8000-000000000002', :'tenant_id'::uuid,
     'a50a0000-0000-4000-8000-000000000006', '5c0a0000-0000-4000-8000-000000000001',
     NULL, NULL, NULL, false, now(), now());

COMMIT;

-- What the panel must read for this fixture (the same fold GetStudentAssignmentStatsQueryHandler performs):
--   Assigned 6 · Sent out 5 · Submitted 2 · Marked 1 · Review required 2 · Reviewed 1
--   funnel: 1 not sent, 3 awaiting submission, 1 awaiting marking, 1 marked  → sums to Assigned (6)
--   review: 1 of 2 reviewed, 1 awaiting — "required on 2 of the 5 sent", never "of 6".
SELECT
    count(*)                                                                        AS assigned,
    count(*) FILTER (WHERE a.status = 1)                                            AS sent_out,
    count(*) FILTER (WHERE s.finalized_at IS NOT NULL)                              AS submitted,
    count(*) FILTER (WHERE s.finalized_at IS NOT NULL AND s.review_state = 2)       AS marked,
    count(*) FILTER (WHERE a.status = 1 AND a.mandatory_review)                     AS review_required,
    count(*) FILTER (WHERE a.status = 1 AND a.mandatory_review AND g.reviewed_at IS NOT NULL) AS reviewed
FROM assignments a
JOIN assignment_recipients r ON r.assignment_id = a.id AND r.ward_student_id = '5c0a0000-0000-4000-8000-000000000001'
LEFT JOIN assignment_submissions s ON s.assignment_id = a.id AND s.student_id = r.ward_student_id
LEFT JOIN guardian_submission_gates g ON g.assignment_id = a.id AND g.student_id = r.ward_student_id
WHERE a.id IN ('a50a0000-0000-4000-8000-000000000001', 'a50a0000-0000-4000-8000-000000000002',
               'a50a0000-0000-4000-8000-000000000003', 'a50a0000-0000-4000-8000-000000000004',
               'a50a0000-0000-4000-8000-000000000005', 'a50a0000-0000-4000-8000-000000000006');
