# Runbook — seed the student-view assignment-stats probe

**Dev-only.** Two SQL scripts that put one student, one guardian and six assignments into a developer's local
AppHost databases, so the student view's **Assignments** rail panel (issue #444, layer L2b) can be observed
against real counts.

They are **never** part of `MigrationService` (owner Q145): that runs in every environment, and a probe
student plus half a dozen fake assignments would ship to production. Nothing here is referenced by a test —
the automated evidence is the bUnit/API suites; this script exists for the one thing they cannot show, a real
browser against a real database (`.github/copilot/rules/testing.md` § "UI rounds need one live pass").

## Symptom

The student view's **Assignments** rail panel (`/students/{id}`) reads wrong or empty while the endpoint is
healthy, or a change to the panel cannot be judged at all because no student in the dev database has the
assignment states the panel distinguishes. Three shapes:

- **"Assignment stats are unavailable right now."** on a student the viewer expects to have assignments —
  either the hop failed (check `assignments-api` is running and its log for the 502) or the probe fixtures
  below were never seeded for the **current tenant**.
- **A panel of zeros** for a student who has assignments — the fixtures exist under a different tenant id, which
  is the most common cause: the ids are fixed, the tenant is a variable.
- **A funnel or review bar that cannot be checked by eye** — the seeded student has only one assignment state, so
  every segment but one is zero. Re-seed with both halves of this runbook to get one of each state.

## What it seeds, and what each row proves

| Row | State | Panel consequence |
|---|---|---|
| `PROBE — not sent (draft)` | `Status = Draft` | counted in **Assigned** only → the funnel's *not sent* segment |
| `PROBE — review gate cleared` | published, `MandatoryReview = true`, gate **reviewed** | **Sent out** + **Review required** + **Reviewed** |
| `PROBE — awaiting submission` | published, no submission | Sent out → *awaiting submission* |
| `PROBE — awaiting marking` | published, submission finalized, `ReviewState = Pending` | **Submitted** → *awaiting marking* (never *marked*) |
| `PROBE — marked` | published, submission finalized, `ReviewState = Graded` | Submitted + **Marked** |
| `PROBE — held by the review gate` | published, `MandatoryReview = true`, gate **not reviewed** | Sent out + Review required → *awaiting review* |

The expected counts are therefore **Assigned 6 · Sent out 5 · Submitted 2 · Marked 1 · Review required 2 ·
Reviewed 1**, which renders as:

- funnel: `1 not sent`, `3 awaiting submission`, `1 awaiting marking`, `1 marked` — summing to **Assigned (6)**;
- review bar: `1 of 2 reviewed · 1 awaiting — required on 2 of the 5 sent` (**not** "of 6": a draft cannot be
  gated for review).

The guardian exists so the deferred **guardian-card** probe (Q141 of the page round) runs in the same AppHost
session: one Primary guardian, one SMS contact, linked to the probe student.

## Run it

1. Start the AppHost and wait until the databases are up (the Resource page shows `students-db` and
   `assignments-db` healthy with their connection strings).
2. Pick the tenant you are signed in as. The dev tenant switcher shows the name; get the id from the settings
   database:

   ```bash
   psql "$SETTINGS_DSN" -c "select id, name from tenants order by name;"
   ```

   Seeded tenants are `Hydeson School` and `Little Legends`. Tenant ids are random GUIDs, which is why the
   scripts take one as a variable rather than hard-coding it.
3. Take the two Postgres connection strings from the Aspire dashboard (`studentsdb` / `assignmentsdb` — the
   dashboard shows them with the password redacted, so copy them from the app host's own configuration or from
   the container's `POSTGRES_*` values) and export them:

   ```bash
   export STUDENTS_DSN="host=localhost port=<p> dbname=students-db user=postgres password=<...>"
   export ASSIGNMENTS_DSN="host=localhost port=<p> dbname=assignments-db user=postgres password=<...>"
   ```

4. Run the two halves **in this order** — the assignments half points its recipient rows at the student and
   the guardian contact the students half creates:

   ```bash
   psql "$STUDENTS_DSN"    -v tenant_id=<tenant-guid> -f documents/runbooks/student-view-probe-seed.students.sql
   psql "$ASSIGNMENTS_DSN" -v tenant_id=<tenant-guid> -f documents/runbooks/student-view-probe-seed.assignments.sql
   ```

   Pass the **bare** guid: the scripts interpolate it as `:'tenant_id'`, which adds the SQL quotes itself.
   Passing `-v tenant_id="'<guid>'"` would arrive double-quoted and the cast to `uuid` would fail.
5. Open `/students/5c0a0000-0000-4000-8000-000000000001` (the first script prints that id).

### If step 4 aborts on `gender_coded_value_id`

`students.gender_coded_value_id` is **NOT NULL**, and coded values live in `settings-db`, which the students
connection cannot see — so the script cannot look one up itself. It handles that two ways:

- **By default it inherits** the value from any existing student in the same database
  (`SELECT gender_coded_value_id FROM students WHERE gender_coded_value_id IS NOT NULL LIMIT 1 \gset`).
  On a dev database that already has students — the normal case — you need to do nothing.
- **On a database with no students**, the inherit step finds nothing and the script stops with
  `seed: no gender_id available — pass -v gender_id=<guid>`. Take a real id from the tenant's coded values
  and pass it:

  ```bash
  psql "$SETTINGS_DSN" -tAc "select id from coded_values where code ilike 'male' or code ilike 'female' limit 1"
  psql "$STUDENTS_DSN" -v tenant_id=<tenant-guid> -v gender_id=<that-guid> \
       -f documents/runbooks/student-view-probe-seed.students.sql
  ```

Pass the bare GUID, like `tenant_id`: the script interpolates it as `:'gender_id'` and adds the quotes.

## Re-running

Both scripts **delete their own rows by fixed id and re-insert**. Re-running converges on the fixture state; it
never duplicates, and it never touches a row it did not create. To probe a different tenant, re-run both with
that tenant's id — the delete/re-insert carries the new `tenant_id` through.

To remove the fixture entirely:

```sql
DELETE FROM guardian_submission_gates WHERE id IN ('9a7e0000-0000-4000-8000-000000000001','9a7e0000-0000-4000-8000-000000000002');
DELETE FROM assignment_submissions    WHERE id IN ('45ba0000-0000-4000-8000-000000000001','45ba0000-0000-4000-8000-000000000002');
DELETE FROM assignment_recipients     WHERE id LIKE 'ac0a0000-%';
DELETE FROM assignments               WHERE id LIKE 'a50a0000-%';
DELETE FROM contacts                  WHERE id LIKE 'cc0a0000-%';
DELETE FROM student_guardians         WHERE id LIKE '1c0a0000-%';
DELETE FROM guardians                 WHERE id LIKE '9c0a0000-%';
DELETE FROM students                  WHERE id LIKE '5c0a0000-%';
```

(The last four run against `students-db`, the first four against `assignments-db`.)

## What the probe is looking at

1. **The counts.** The six numbers match the table above, and the funnel's four segments sum to *Assigned*.
2. **The review denominator.** The foot line says "required on 2 of the 5 sent" — the panel never divides by
   *Assigned*.
3. **Degradation.** Stop `assignments-api` in the Aspire dashboard and reload: the panel shows one muted line,
   "Assignment stats are unavailable right now.", with **no numbers at all**, and every other block still
   renders. Start it again and reload to confirm the panel comes back.
4. **Colour is not the message.** Every segment is named in the legend or the foot line.

Record what you saw (and any deviation) in the round doc — a probe that is not written down did not happen.
