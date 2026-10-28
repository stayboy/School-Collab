-- Dev-only probe fixtures for the student-view assignment-stats panel (issue #444, layer L2b).
--
-- WHY THIS FILE EXISTS AND IS NOT IN MigrationService: MigrationService ships to every environment, so a
-- fixture set that names a probe student and half a dozen fake assignments would land in production. This
-- script runs by hand against a developer's local AppHost database (owner Q145).
--
-- Idempotent by FIXED GUIDS and PREFIXED ROWS: every row this script owns is deleted by id and re-inserted,
-- so a re-run converges on the intended state instead of duplicating. Nothing else is touched.
--
-- Run with (from the repository root; see documents/runbooks/student-view-probe-seed.md):
--   psql "$STUDENTS_DSN"    -v tenant_id=<tenant-guid> -f documents/runbooks/student-view-probe-seed.students.sql
--   psql "$ASSIGNMENTS_DSN" -v tenant_id=<tenant-guid> -f documents/runbooks/student-view-probe-seed.assignments.sql
--
-- The two halves exist because the fixtures span two databases: the student and its guardian live in
-- students-db, the assignments and submissions in assignments-db. The probe student's id is shared between
-- them as a bare GUID (there is no cross-database foreign key).

\set ON_ERROR_STOP on

BEGIN;

-- ── Owned rows, removed by id so a re-run converges ────────────────────────────────────────────────────────
DELETE FROM contacts          WHERE id IN ('cc0a0000-0000-4000-8000-000000000001',
                                            'cc0a0000-0000-4000-8000-000000000002');
DELETE FROM student_guardians WHERE id = '1c0a0000-0000-4000-8000-000000000001';
DELETE FROM guardians         WHERE id = '9c0a0000-0000-4000-8000-000000000001';
DELETE FROM students          WHERE id = '5c0a0000-0000-4000-8000-000000000001';

-- ── The probe student ────────────────────────────────────────────────────────────────────────────────────────
-- `gender_coded_value_id` is NOT NULL (migration 20260716183515_RequireStudentDobAndGender), so a NULL
-- insert aborts the whole script. Coded values live in settings-db, which this connection cannot
-- see, so the value cannot be looked up from here: inherit it from any existing student in this database
-- (the usual case), or pass -v gender_id=<guid>. Against a database with no students you must pass it.
\if :{?gender_id}
\else
  SELECT gender_coded_value_id AS gender_id FROM students
   WHERE gender_coded_value_id IS NOT NULL LIMIT 1 \gset
\endif
\if :{?gender_id}
\else
  \echo 'seed: no gender_id available - pass -v gender_id=<guid> (see the runbook).'
  \quit 1
\endif

INSERT INTO students (id, tenant_id, student_number, first_name, last_name, date_of_birth,
                      gender_coded_value_id, is_deleted, created_at, updated_at)
VALUES ('5c0a0000-0000-4000-8000-000000000001', :'tenant_id'::uuid, 'SC-PROBE-0001',
        'Probe', 'Assignment Student', DATE '2013-04-18',
        :'gender_id'::uuid, false, now(), now());

-- ── A guardian, so the deferred guardian-card probe (Q141) runs in the same AppHost session ─────────────────
INSERT INTO guardians (id, tenant_id, first_name, last_name, display_name,
                       title_coded_value_id, address, community_id, is_deleted, created_at, updated_at)
VALUES ('9c0a0000-0000-4000-8000-000000000001', :'tenant_id'::uuid, 'Thandi', 'Probe',
        'Thandi Probe', NULL, NULL, NULL, false, now(), now());

-- role = 0 is GuardianRole.Primary; a Primary guardian is what the review gate's sign-off speaks for.
INSERT INTO student_guardians (id, tenant_id, student_id, guardian_id, relationship_coded_value_id,
                               role, is_emergency_contact, created_by_guardian_id, created_at, updated_at)
VALUES ('1c0a0000-0000-4000-8000-000000000001', :'tenant_id'::uuid,
        '5c0a0000-0000-4000-8000-000000000001', '9c0a0000-0000-4000-8000-000000000001',
        NULL, 0, true, NULL, now(), now());

-- ── Contacts: one the student owns, one the guardian owns ───────────────────────────────────────────────────
-- owner_type 0 = ContactOwnerType.Student, 1 = Guardian. channel 0 = Email, 1 = SMS.
-- The guardian's contact id is the one the assignments half uses as a recipient's contact_id.
INSERT INTO contacts (id, tenant_id, owner_type, owner_id, channel, value, label,
                      country_code, display_order, is_verified, is_deleted, created_at, updated_at)
VALUES ('cc0a0000-0000-4000-8000-000000000001', :'tenant_id'::uuid, 0,
        '5c0a0000-0000-4000-8000-000000000001', 0, 'probe.student@example.org', 'School account',
        NULL, 0, true, false, now(), now()),
       ('cc0a0000-0000-4000-8000-000000000002', :'tenant_id'::uuid, 1,
        '9c0a0000-0000-4000-8000-000000000001', 1, '0821234567', NULL,
        '+27', 0, true, false, now(), now());

COMMIT;

-- What the run should print: one row, the probe student, ready to open at /students/<id>.
SELECT s.id AS probe_student_id, s.student_number, s.tenant_id
FROM students s
WHERE s.id = '5c0a0000-0000-4000-8000-000000000001';
