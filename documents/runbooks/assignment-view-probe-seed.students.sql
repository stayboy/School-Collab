-- Dev-only probe fixtures for the assignment VIEW page (/assignments/{id}).
-- The STUDENTS half: 28 probe students with their guardians, contacts and contact subscriptions — the five
-- contact shapes the page's Delivery / Targets & Audience column distinguishes.
--
-- WHY THIS FILE EXISTS AND IS NOT IN MigrationService: MigrationService ships to every environment, so
-- 28 probe students and their guardians and contacts would land in production. This script runs by hand
-- against a developer's local AppHost database (owner Q145).
--
-- Idempotent by FIXED GUIDS: every row this script owns carries an id in one of these four fixed series,
-- and the delete phase removes that whole series before re-inserting it. Nothing else is touched.
--
--   5c0b0000-0000-4000-8000-0000000000NN   students               (NN = roster row, 01..28)
--   9c0b0000-0000-4000-8000-00000000NN0K   guardians              (K = 1 primary link, 2 the CC guardian)
--   1c0b0000-0000-4000-8000-00000000NN0K   student_guardians
--   cc0b0000-0000-4000-8000-00000000NN0K   contacts
--   5b0b0000-0000-4000-8000-00000000NN0K   contact_subscriptions
--
-- The ASSIGNMENTS half (assignment-view-probe-seed.assignments.sql) derives the SAME student / guardian /
-- contact ids from the same roster row numbers, because the two fixtures live in different databases and
-- there is no cross-database foreign key. Changing a roster row number here means changing it there.
--
-- Run with (from the repository root; see documents/runbooks/assignment-view-probe-seed.md):
--   psql "$STUDENTS_DSN" -v tenant_id=<tenant-guid> -f documents/runbooks/assignment-view-probe-seed.students.sql
-- then the assignments half against assignments-db.

\set ON_ERROR_STOP on

BEGIN;

-- ── Owned rows, removed by id series so a re-run converges ────────────────────────────────────────────────
DELETE FROM contact_subscriptions WHERE id::text LIKE '5b0b0000-%';
DELETE FROM contacts              WHERE id::text LIKE 'cc0b0000-%';
DELETE FROM student_guardians     WHERE id::text LIKE '1c0b0000-%';
DELETE FROM guardians             WHERE id::text LIKE '9c0b0000-%';
DELETE FROM students              WHERE id::text LIKE '5c0b0000-%';

-- ── The roster: one row per probe student, carrying its contact SHAPE ─────────────────────────────────────
-- `shape` is the contract with the assignments half and with the runbook's expected read-out:
--   none   — no guardian, no contact at all            → the published row reads Not reached / no subscribed contact
--   plain  — one guardian (Primary) with one email contact, subscribed   → the normal Sent/Opened/Failed case
--   two    — two guardians (Primary + CC), one email contact each, subscribed → two contact lines under one row
--   optout — one guardian with one email contact whose subscription is Unsubscribed → Skipped / guardian opted out
--   sms    — one guardian with one SMS contact, subscribed → the ONE contact the notification filter can block
--            (Blocked by policy on the draft) — see the runbook's optional settings-db step
--
-- First names ascend with the surnames, so the reads' order-by-full-name and a human's order-by-surname are
-- the same order. That is what makes a 28-row table checkable by eye.
DROP TABLE IF EXISTS probe2c_roster;
CREATE TEMP TABLE probe2c_roster ON COMMIT DROP AS
SELECT r.n,
       r.first_name,
       r.last_name,
       r.shape,
       ('5c0b0000-0000-4000-8000-0000000000' || lpad(r.n::text, 2, '0'))::uuid AS student_id,
       ('9c0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '01')::uuid AS guardian1_id,
       ('9c0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '02')::uuid AS guardian2_id,
       ('cc0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '01')::uuid AS contact1_id,
       ('cc0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '02')::uuid AS contact2_id,
       ('1c0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '01')::uuid AS link1_id,
       ('1c0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '02')::uuid AS link2_id,
       ('5b0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '01')::uuid AS sub1_id,
       ('5b0b0000-0000-4000-8000-00000000' || lpad(r.n::text, 2, '0') || '02')::uuid AS sub2_id
FROM (VALUES
        ( 1, 'Amara',    'Adeyemi',    'none'),
        ( 2, 'Bongani',  'Bello',      'optout'),
        ( 3, 'Busisiwe', 'Bhengu',     'two'),
        ( 4, 'Chloe',    'Cele',       'two'),
        ( 5, 'Dineo',    'Dlamini',    'two'),
        ( 6, 'Emeka',    'Ebrahim',    'sms'),
        ( 7, 'Farah',    'Fourie',     'plain'),
        ( 8, 'Grace',    'Gama',       'plain'),
        ( 9, 'Hendrik',  'Hadebe',     'plain'),
        (10, 'Ivy',      'Ismail',     'plain'),
        (11, 'Jabulani', 'Jacobs',     'plain'),
        (12, 'Kabelo',   'Khumalo',    'plain'),
        (13, 'Lerato',   'Langa',      'plain'),
        (14, 'Musa',     'Mahlangu',   'plain'),
        (15, 'Naledi',   'Naidoo',     'plain'),
        (16, 'Nomsa',    'Ngcobo',     'plain'),
        (17, 'Oscar',    'Olivier',    'plain'),
        (18, 'Priya',    'Pillay',     'plain'),
        (19, 'Quinton',  'Qwabe',      'plain'),
        (20, 'Refilwe',  'Radebe',     'plain'),
        (21, 'Sipho',    'Sithole',    'plain'),
        (22, 'Thabo',    'Tshabalala', 'plain'),
        (23, 'Ulrich',   'Uys',        'plain'),
        (24, 'Vera',     'Van Wyk',    'plain'),
        (25, 'Willem',   'Williams',   'plain'),
        (26, 'Xolani',   'Xaba',       'plain'),
        (27, 'Yvonne',   'Yende',      'optout'),
        (28, 'Zanele',   'Zulu',       'none')
     ) AS r(n, first_name, last_name, shape);

-- ── 28 students ───────────────────────────────────────────────────────────────────────────────────────────
-- `gender_coded_value_id` is NOT NULL (migration 20260716183515_RequireStudentDobAndGender), and coded values
-- live in settings-db, which this connection cannot see, so the value cannot be looked up from here: inherit
-- it from any existing student in this database (the usual case), or pass -v gender_id=<guid>. Against a
-- database with no students you must pass it.
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
SELECT s.student_id, :'tenant_id'::uuid,
       'SC-PROBE-2C-' || lpad(s.n::text, 2, '0'),
       s.first_name, s.last_name,
       -- A fixed date per roster row (no now()): a re-run must produce the same row.
       (DATE '2013-01-01' + (s.n * 37)),
       :'gender_id'::uuid, false, now(), now()
FROM probe2c_roster s;

-- ── Guardians ─────────────────────────────────────────────────────────────────────────────────────────────
-- Guardian 1 (Primary) exists for every shape except `none`; guardian 2 exists only for `two`, and is the CC
-- link — two contacts under ONE student row is what the Delivery column's second line renders.
-- `date_of_birth`, `gender_coded_value_id`, `title_coded_value_id` all stay NULL: nothing on the page reads
-- a guardian's own fields (Q158 — the audience read carries no guardian name at all).
INSERT INTO guardians (id, tenant_id, first_name, last_name, display_name,
                       title_coded_value_id, address, community_id, is_deleted, created_at, updated_at)
SELECT g.guardian_id, :'tenant_id'::uuid, g.first_name, s.last_name, g.first_name || ' ' || s.last_name,
       NULL, NULL, NULL, false, now(), now()
FROM (
    SELECT student_id, guardian1_id AS guardian_id, 'Probe'::text AS first_name
    FROM probe2c_roster WHERE shape <> 'none'
    UNION ALL
    SELECT student_id, guardian2_id, 'Second'
    FROM probe2c_roster WHERE shape = 'two'
) AS g
JOIN probe2c_roster s ON s.student_id = g.student_id;

-- role 0 = GuardianRole.Primary, 1 = GuardianRole.CC. `contacts/subscribed` derives a guardian's role from
-- this link (any Primary link makes the guardian Primary), and the publish path snapshots it onto the
-- recipient row, so this column is the single source of the Guardians column's "2 · Primary, CC".
INSERT INTO student_guardians (id, tenant_id, student_id, guardian_id, relationship_coded_value_id,
                               role, is_emergency_contact, created_by_guardian_id, created_at, updated_at)
SELECT l.link_id, :'tenant_id'::uuid, l.student_id, l.guardian_id, NULL,
       l.role, l.role = 0, NULL, now(), now()
FROM (
    SELECT student_id, link1_id AS link_id, guardian1_id AS guardian_id, 0 AS role
    FROM probe2c_roster WHERE shape <> 'none'
    UNION ALL
    SELECT student_id, link2_id, guardian2_id, 1
    FROM probe2c_roster WHERE shape = 'two'
) AS l;

-- ── Contacts: one per guardian, channel 0 = Email except the one SMS contact ──────────────────────────────
-- owner_type 1 = ContactOwnerType.Guardian. channel 0 = Email, 1 = SMS. The `sms` shape exists so the
-- notification filter has exactly ONE contact it can block by channel (see the runbook's optional step);
-- every other contact is email, so blocking SMS leaves the rest of the cohort untouched.
INSERT INTO contacts (id, tenant_id, owner_type, owner_id, channel, value, label,
                      country_code, display_order, is_verified, is_deleted, created_at, updated_at)
SELECT c.contact_id, :'tenant_id'::uuid, 1, c.guardian_id, c.channel, c.value, NULL,
       c.country_code, 0, true, false, now(), now()
FROM (
    SELECT student_id, contact1_id AS contact_id, guardian1_id AS guardian_id,
           CASE WHEN shape = 'sms' THEN 1 ELSE 0 END AS channel,
           CASE WHEN shape = 'sms'
                THEN '+2782100' || lpad(n::text, 4, '0')
                ELSE 'probe.student' || lpad(n::text, 2, '0') || '@example.org'
           END AS value,
           CASE WHEN shape = 'sms' THEN '+27' ELSE NULL END AS country_code
    FROM probe2c_roster WHERE shape <> 'none'
    UNION ALL
    SELECT student_id, contact2_id, guardian2_id, 0,
           'probe.student' || lpad(n::text, 2, '0') || '.cc@example.org', NULL
    FROM probe2c_roster WHERE shape = 'two'
) AS c;

-- ── Contact subscriptions: status 1 = Subscribed, 0 = Unsubscribed ────────────────────────────────────────
-- `contacts/subscribed` INNER JOINs this table on `status = Subscribed` AND the requested scope, so a contact
-- with no row is invisible to the draft preview and to publish. The `optout` rows carry status 0 ON PURPOSE:
-- the contact still exists — which is why the published Delivery column can show it as Skipped with the
-- reason "guardian opted out" — but it is not a subscriber, so the draft's policy-filtered read does not list
-- it at all.
-- scope 0 = SubscriptionScope.AllAssignments — the scope both the preview and publish ask for.
INSERT INTO contact_subscriptions (id, tenant_id, contact_id, scope, scope_ref_id, status, created_at, updated_at)
SELECT s.sub_id, :'tenant_id'::uuid, s.contact_id, 0, NULL, s.status, now(), now()
FROM (
    SELECT student_id, sub1_id AS sub_id, contact1_id AS contact_id,
           CASE WHEN shape = 'optout' THEN 0 ELSE 1 END AS status
    FROM probe2c_roster WHERE shape <> 'none'
    UNION ALL
    SELECT student_id, sub2_id, contact2_id, 1
    FROM probe2c_roster WHERE shape = 'two'
) AS s;

COMMIT;

-- What the run should print: 28 students, 29 guardians, 29 contacts, 27 subscribed, 2 opted out, 1 SMS.
SELECT count(*)                                                            AS probe_students,
       (SELECT count(*) FROM guardians WHERE id::text LIKE '9c0b0000-%')   AS probe_guardians,
       (SELECT count(*) FROM contacts  WHERE id::text LIKE 'cc0b0000-%')   AS probe_contacts,
       (SELECT count(*) FROM contacts  WHERE id::text LIKE 'cc0b0000-%' AND channel = 0) AS email_contacts,
       (SELECT count(*) FROM contacts  WHERE id::text LIKE 'cc0b0000-%' AND channel = 1) AS sms_contacts,
       (SELECT count(*) FROM contact_subscriptions WHERE id::text LIKE '5b0b0000-%' AND status = 1) AS subscribed,
       (SELECT count(*) FROM contact_subscriptions WHERE id::text LIKE '5b0b0000-%' AND status = 0) AS opted_out
FROM students
WHERE id::text LIKE '5c0b0000-%';
