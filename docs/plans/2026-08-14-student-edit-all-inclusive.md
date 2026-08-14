# Plan — All-inclusive student edit (draft-then-save the whole student)

> **Goal:** Make the student **edit dialog** draft and save the *whole* student —
> profile + guardians + contacts — in one atomic transaction, mirroring the existing
> create flow (`POST /students/with-linked-data`). Today the edit dialog saves only the
> profile (`UpdateStudentRequest`) while guardians/contacts persist **live** inside the
> modal, so the dialog's Save/Cancel buttons don't actually encompass guardian/contact
> changes — a real inconsistency.
>
> **Branch:** `feature/student-edit-all-inclusive` (stacked on
> `feature/dialog-binding-and-form-model-mapping`).
> **Companion docs:** `documents/solution/dto-form-model-mapping.md` (the on-model
> `From`/`LoadFrom` pattern this builds on), `documents/solution/dialog-parameter-binding.md`.
> **Mirror of:** `CreateStudentWithLinkedData` (`src/Students/SchoolCollab.Students.Core/
> CQRS/Students/Commands/CreateStudentWithLinkedData/`).

---

## 1. Goals & non-goals

### Goals

1. **One atomic edit save.** A new server endpoint `PUT /students/{id}/with-linked-data`
   updates the profile **and** reconciles guardians (link new / unlink removed / update
   link metadata) **and** reconciles contacts (add / update / delete) in a single DB
   transaction — the edit counterpart of `POST /students/with-linked-data`.
2. **All-inclusive form model.** `StudentFormModel` becomes the single source of truth
   for the whole student in the edit dialog (as it already is in create): an all-inclusive
   `LoadFrom` populates profile + guardians + contacts from the relevant DTOs; the save
   projects the model back to one `UpdateStudentWithLinkedDataRequest`.
3. **Edit dialog = draft-then-save.** The dialog loads everything into the model, the
   user edits freely, **Save** persists all changes atomically, **Cancel** reverts all
   (no partial live writes). Fixes the Save/Cancel-doesn't-cover-guardians/contacts bug.
4. **Create ↔ edit symmetry.** Both flows draft the whole `StudentFormModel` and save
   atomically (create via `CreateStudentWithLinkedData`, edit via
   `UpdateStudentWithLinkedData`). `StudentFormFields` runs in a single draft mode for
   both, dropping the edit-live branch.
5. **Real optimistic concurrency over the edit window.** The client sends back the
   `RowVersion` it loaded; the handler rejects a stale save with `ConcurrencyException`
   (→ 409). This is a genuine improvement over today's `UpdateStudent`, whose EF
   `xmin` check only catches intra-request races (the client never sends a version
   back, so a long-window edit silently overwrites a concurrent change).

### Non-goals (v1)

- **Edit *page* (`/students/{id}/edit`) stays live.** The page is a long-lived
  management surface where immediate link/unlink + live contacts editing is intentional
  UX. Migrating it to draft-then-save is a separate decision (§7, v2) — it changes the
  page's interaction model and is out of scope here. The new server endpoint can serve a
  future page migration without rework.
- **No new guardian/contact server endpoints.** Reuse the existing link/unlink and
  contact CRUD domain logic inside the new handler (the handler orchestrates the
  existing domain operations in one transaction; no new HTTP routes beyond the one
  `PUT /students/{id}/with-linked-data`).
- **No UX change to the "add existing guardian" search** (read-only API searches stay as
  they are — only the *mutating* clicks become draft operations).

---

## 2. Current state (why this is a refactor, not a small fix)

- **Create** (`Create.razor`): drafts `Model.GuardianLinks` + `Model.Contacts`, saves via
  `CreateStudentWithLinkedDataAsync` (`CreateStudentWithLinkedDataRequest` already carries
  `GuardianDraftRequest[]` + `ContactDraftRequest[]`). ✅ already all-inclusive + atomic.
- **Edit dialog** (`StudentEditDialog.razor`): `_model.LoadFrom(student)` maps **profile
  only**. `StudentFormFields` (default `Mode=Inline`, `StudentId` set) loads guardians into
  its **own** `_links` and link/unlinks **immediately**; `ContactsEditor` runs **Live**
  (persists on click). Save = `UpdateStudentRequest` (profile only). ❌ not all-inclusive.
- **Edit page** (`Students/Edit.razor`): `Mode=Linked`, `LinkedItems=_links` (page-owned),
  live link/unlink; separate Live `ContactsEditor`. Save = profile only.
- **No `UpdateStudentWithLinkedData`** exists (server or client). `UpdateStudentRequest`
  is profile-only.

So "guardian/contact conversions are not in the StudentDto→form-model mapping" because
`StudentDto` carries only profile + `GuardianCount`, and edit mode manages guardians/
contacts live — they're a different concern with different data sources. This refactor
unifies them under one all-inclusive model + one atomic save.

---

## 3. Design decisions

### 3.1 New server endpoint — `PUT /students/{id}/with-linked-data`

Mirror `CreateStudentWithLinkedData`:

- **Command:** `UpdateStudentWithLinkedData` (in
  `CQRS/Students/Commands/UpdateStudentWithLinkedData/`), with a handler that, in **one
  `IDbContext` transaction**:
  1. Loads the student + its current guardian links + current (non-deleted) contacts.
  2. Updates the profile fields (reuse the existing `Student.Update` domain method).
  3. **Reconciles guardians**: for each `GuardianDraftRequest` — if `ExistingGuardianId`
     is set and currently linked → update link metadata (role/relationship/emergency) via
     the existing `StudentGuardian` domain; if set and **not** linked → link it; if a
     currently-linked guardian is **absent** from the draft list → unlink it. New-guardian
     drafts (no `ExistingGuardianId`, name set) → create the guardian then link, exactly
     as `CreateStudentWithLinkedDataHandler` does.
  4. **Reconciles contacts**: diff the draft `ContactDraftRequest[]` against the current
     contacts by `Id` (new `ContactDraftRequest`s carry no id → add; matched id with
     changed fields → update; current ids missing from the draft → delete), reusing the
     existing `Contact` domain + the same channel/country-code validation as create.
- **Request:** `UpdateStudentWithLinkedDataRequest` (profile fields + `GuardianDraftRequest[]?`
  + `ContactDraftRequest[]?`) — a superset of `UpdateStudentRequest`. Reuse the same
  `GuardianDraftRequest` / `ContactDraftRequest` records the client already declares.
- **Endpoint:** `PUT /students/{id}/with-linked-data` in the students endpoint map, next
  to `POST /students/with-linked-data`. Returns `204 NoContent` (or the updated `IdResponse`
  to match create — pick for consistency with the existing update route).

**Rationale:** a client-side multi-call save (profile + N link/unlink + M contact CRUD)
is **not atomic** — a mid-save failure leaves the student half-edited and breaks the
"Cancel reverts everything" promise. The atomic server endpoint is what makes
draft-then-save honest. It also keeps the diff logic server-side (single source of truth,
testable in handler tests), exactly as create does.

### 3.2 All-inclusive `StudentFormModel` load + save projections

`StudentFormModel` already has `GuardianLinks` (`List<GuardianAssignment>`) and `Contacts`
(`List<ContactModel>`). Add:

- **`LoadFrom(StudentDto, IReadOnlyList<StudentGuardianViewDto> guardians, IReadOnlyList<ContactDto> contacts)`**
  — the all-inclusive load. Calls the profile `LoadFrom(StudentDto)` then projects:
  - `StudentGuardianViewDto` → `GuardianAssignment` (set `ExistingGuardianId = GuardianId`,
    `FirstName`/`LastName`, `RelationshipCodedValueId`, `Role`, `IsEmergencyContact` via a
    new `EmergencyContact` flag on `GuardianAssignment` if absent — see §3.3,
    `TitleCodedValueId`). Per-guardian contacts are **not** re-drafted here (the dialog
    edits the *student's* contacts, not each guardian's; guardian contacts are managed on
    the guardian surface).
  - `ContactDto` → `ContactModel` (`Channel`, `Value`, `Label`, `CountryCode`, `Order =
    DisplayOrder`; `TempId` fresh — the editor keys on it, the save matches by `Id` via a
    parallel id map, §3.4).
- **`ToUpdateRequest()`** that projects the
  model back to `UpdateStudentWithLinkedDataRequest` (profile + `GuardianDraftRequest[]`
  from `GuardianLinks` + `ContactDraftRequest[]` from `Contacts`). This is the
  model→request direction. The symmetric `ToCreateRequest()` (so the create flow stops
  flushing inline in `Create.razor`) is **deferred to a follow-up PR** (§7) — v1 ships
  `ToUpdateRequest()` only; create keeps its existing inline flush.

### 3.3 `GuardianAssignment` needs an `IsEmergencyContact` + stable link id

`GuardianAssignment` (the create draft record) currently has no `IsEmergencyContact` and no
link id (create doesn't need them — everything is new). For edit we need:
- `IsEmergencyContact` (to round-trip the emergency flag onto `GuardianDraftRequest`).
- A way to keep the `StudentGuardian` **link id** so a metadata-only change (role/
  relationship/emergency) is an update, not an unlink+relink. Add an optional
  `Guid? LinkId` (null for new links). Alternatively, key the reconciliation by
  `ExistingGuardianId` + student (a guardian is linked to a student at most once) and let
  the handler treat "already linked + present in draft" as an update — this avoids a new
  field. **Recommend keying by `ExistingGuardianId`** (no new link-id field); the handler
  resolves the existing `StudentGuardian` row by (student, guardian) and updates it.

### 3.4 Contact identity across the draft ↔ persisted boundary

`ContactModel.TempId` is client-only; `ContactDto.Id` is persisted. To diff on save, the
load must remember which `ContactModel` came from which `ContactDto.Id`. Options:
- (a) Add `Guid? PersistedId` to `ContactModel` (null = new). The save maps
  `PersistedId` → `ContactDraftRequest.Id` (extend `ContactDraftRequest` with an optional
  `Id` for updates) and treats null as add. **Recommend** — minimal, explicit.
- (b) Keep a side-channel `Dictionary<ContactModel, Guid>` in the dialog. More plumbing,
  leaks the mapping out of the model. Avoid.

`ContactDraftRequest` gains an optional `Guid? Id` (null ⇒ add, present ⇒ update). The
handler deletes any current contact id **not** present in the draft list.

### 3.5 `StudentFormFields` — one draft mode for create and edit-dialog

Today `StudentFormFields` branches on `IsEditMode` (`StudentId` set) to drive **live**
guardian link/unlink and the Live `ContactsEditor`. For the all-inclusive dialog the
component must run in **draft** mode (like create) with a pre-populated model:

- The edit dialog **stops passing `StudentId`** to `StudentFormFields` (it keeps the id
  itself for the save). With no `StudentId`, `IsEditMode` is false → guardians draft into
  `Model.GuardianLinks`, `ContactsEditor` runs **Buffered** into `Model.Contacts`. The
  dialog pre-populates the model before first render (gated on `_loaded`, as today).
- The **Inline edit-live branch** (`RemoveGuardianAsync(gId)` immediate unlink, the
  `IsEditMode`-driven live load in `OnInitializedAsync`) becomes dead code once the dialog
  no longer passes `StudentId`. **Remove it** — `StudentFormFields` Inline mode becomes
  create/draft-only. The "add existing" search APIs (read-only) stay.
- The edit **page** keeps `Mode=Linked` + `StudentId`-free? No — the page keeps its
  current live `Linked` mode untouched (non-goal). `Linked` mode is page-only and
  unaffected.

### 3.6 Save flow in the edit dialog

```
OnSaveAsync:
  if (!_loaded || _saving) return;
  _saving = true;
  try {
    var req = _model.ToUpdateRequest();      // profile + guardians + contacts
    await Api.UpdateStudentWithLinkedDataAsync(StudentId, req, ct);
    await Dialog.CloseAsync(StudentId);
  } catch (Exception ex) { _error = ex.Message; }
  finally { _saving = false; }
```

No per-row link/unlink/contact calls — one request. `Cancel` just closes (nothing was
written).

### 3.7 Optimistic concurrency (v1 — per decision #5)

The infrastructure already exists: `Student : IHasRowVersion` with `uint RowVersion`
(Postgres `xmin` via `ConfigurePostgresRowVersion`), and `UpdateStudentHandler` already
catches `DbUpdateConcurrencyException` → `ConcurrencyException("Student", id)`. **But
`StudentDto` doesn't expose `RowVersion` and the client never sends it back**, so today's
profile update only catches intra-request races — a long-window edit silently overwrites a
concurrent change. The all-inclusive edit does it properly:

- **DTOs carry the version.** Add `uint RowVersion` to the server `StudentDto`
  (`SchoolCollab.Students.Core/DTOs/StudentDto.cs`) + its projection, and mirror it on the
  app-level `StudentDto` in `StudentsApiClient.cs`. (`RowVersion` is the Postgres `xmin` —
  not a secret; safe to expose.)
- **Request carries the expected version.** `UpdateStudentWithLinkedDataRequest` gains an
  `ExpectedRowVersion` (uint) — the version the client loaded. `StudentFormModel` stores
  the loaded `RowVersion` (set by the all-inclusive `LoadFrom`) and `ToUpdateRequest()`
  writes it onto the request.
- **Handler validates.** Load the student; if `command.ExpectedRowVersion !=
  student.RowVersion` → `ConcurrencyException("Student", id)`. This catches concurrent
  **profile** changes since the client's load (the high-value case).
- **Child-row concurrency comes free via EF `xmin`.** The handler loads the current
  guardian-links + contacts as **tracked** entities and mutates/deletes them in the same
  `DbContext`; `SaveChanges` `xmin`-checks every touched row → a concurrent edit to a
  guardian-link or contact the client saw → `DbUpdateConcurrencyException` →
  `ConcurrencyException`.
- **Concurrent child *additions AND removals*** (another user linked/unlinked a
  guardian or added/deleted a contact the editor didn't see) are **not** caught by `xmin`
  alone (the handler loads them fresh). To detect them, the request also carries
  `LoadedGuardianIds[]` + `LoadedContactIds[]` (captured at load). The handler checks the
  loaded and current id sets are **equal** — `current.Except(loaded).Any()` (a concurrent
  addition) **or** `loaded.Except(current).Any()` (a concurrent removal) ⇒
  `ConcurrencyException`. The removal half prevents a silent re-link/resurrect of a
  row another user dropped; the addition half prevents a blind delete of a
  concurrently-added row during reconciliation (lost update). Both are tested
  (`ConcurrentGuardianAddition`/`ConcurrentGuardianRemoval`/`ConcurrentContactAddition`).
- **HTTP mapping.** Map `ConcurrencyException` → **409 Conflict** with a body the dialog
  surfaces. The existing `PUT /students/{id}` already maps `ConcurrencyException` to
  `Results.Conflict` in `StudentRoutes.cs`; the new `PUT /students/{id}/with-linked-data`
  mirrors it (the handler throws `ConcurrencyException`, the endpoint returns 409).
- **Dialog handling.** On 409, surface "This student was changed by someone else — reload
  and retry" with a **Reload** action (re-fetches + re-populates the model, preserving the
  user's in-progress edits where possible, or offering a hard reload). Do **not** lose the
  draft; do not auto-close.

---

## 4. Phased implementation

> **Progress (2026-08-14):** All four phases are **implemented and green**, plus a
> post-implementation review fixed a regression (the edit dialog's loaded guardians'
> relationship names were blank after the `StudentFormFields` cleanup) and closed test
> gaps (concurrent guardian removal + concurrent contact addition; a regression bUnit
> test). `StudentFormModel.ToUpdateRequest()` is implemented; the symmetric
> `ToCreateRequest()` is **deferred to a follow-up PR** (§7). Test suites: Admin 346,
> Settings 439, Assignments 99, Architecture 20, integration `UpdateStudentWithLinkedDataEndpointTests` 5/5 — all passing; the only solution-build error is the
> pre-existing `Students.Tests.Unit` "no Main" issue (untouched).

**Phase 0 — server: `UpdateStudentWithLinkedData`** ✅ done
- `UpdateStudentWithLinkedData` command + `UpdateStudentWithLinkedDataHandler` (mirror
  `CreateStudentWithLinkedDataHandler`; reuse `Student.Update`, `StudentGuardian`
  link/unlink/update domain, `Contact` add/update/delete domain in one transaction).
- `UpdateStudentWithLinkedDataRequest` (profile + `GuardianDraftRequest[]?` +
  `ContactDraftRequest[]?` + `ExpectedRowVersion` + `LoadedGuardianIds[]?` +
  `LoadedContactIds[]?`); extend `ContactDraftRequest` with optional `Id`.
- Add `uint RowVersion` to the server `StudentDto` + its projection (so the client can
  echo it back).
- Endpoint `PUT /students/{id}/with-linked-data`, mapping `ConcurrencyException` → **409
  Conflict** (verify the existing mapping; wire the new endpoint explicitly).
- Handler: `ExpectedRowVersion != student.RowVersion` ⇒ `ConcurrencyException`; child
  rows reconciled via tracked entities (EF `xmin`); `LoadedGuardianIds`/`LoadedContactIds`
  subset-check for concurrent additions.
- Handler unit tests: profile-only (no drafts); add/remove guardians; update link
  metadata; add/update/delete contacts; mixed; not-found; invalid-draft validation;
  **stale `ExpectedRowVersion` ⇒ ConcurrencyException**; **concurrent child addition
  (current id not in loaded set) ⇒ ConcurrencyException**. Mirror the create handler's
  test shape.

**Phase 1 — client API + model projections** ✅ done
- `StudentsApiClient.UpdateStudentWithLinkedDataAsync` + the request record (or reuse the
  Core record via the contracts path the create client uses).
- Mirror `RowVersion` onto the app-level `StudentDto`.
- `StudentFormModel.LoadFrom(StudentDto, IReadOnlyList<StudentGuardianViewDto>,
  IReadOnlyList<ContactDto>)` (all-inclusive; stores the loaded `RowVersion` + the loaded
  guardian/contact id sets) + `ToUpdateRequest()` (writes `ExpectedRowVersion` +
  `LoadedGuardianIds` + `LoadedContactIds` onto the request).
- `GuardianAssignment`: add `IsEmergencyContact`; `StudentGuardianViewDto`→`GuardianAssignment`
  converter. `ContactModel`: add `PersistedId`; `ContactDto`→`ContactModel` converter.
- Unit tests for both projections (the `*FormModelMappingsTests` style already in place),
  incl. `RowVersion` + loaded-id-set round-trip.

**Phase 2 — edit dialog wiring** ✅ done
- `StudentEditDialog.OnInitializedAsync`: load `StudentDto` + `ListGuardiansByStudentAsync`
  + `ListContactsAsync(Student)` → `_model.LoadFrom(student, guardians, contacts)`.
- Pass `StudentFormFields` with **no `StudentId`** (draft mode), `ShowGuardians=true`,
  `ShowContacts=true`, `Wide=true`.
- `OnSaveAsync` → `_model.ToUpdateRequest()` → `UpdateStudentWithLinkedDataAsync`.
- **409 handling:** on `ConcurrencyException`/409, surface "changed by someone else —
  reload and retry" with a Reload action (re-fetch + re-populate; preserve the user's
  in-progress edits where feasible). Do not lose the draft or auto-close.
- bUnit: dialog loads all three, Save issues one request (with `ExpectedRowVersion` +
  loaded-id sets), Cancel issues none; 409 → reload path.

**Phase 3 — `StudentFormFields` cleanup** ✅ done
- Remove the now-dead Inline edit-live branch (`IsEditMode`-driven live guardian load +
  immediate link/unlink in `RemoveGuardianAsync`/`AddSelectedGuardiansAsync`/
  `SaveNewGuardianAsync`). Inline mode becomes draft-only (create + edit-dialog).
- Update `StudentFormFieldsRenderActionsBunitTests` + any edit-live tests.
- Verify the edit page (`Mode=Linked`) is unaffected.

**Phase 4 — docs + arch guard** ✅ done
- Update `documents/solution/dto-form-model-mapping.md`: note the all-inclusive
  `LoadFrom` overload + the model→request `ToUpdateRequest` projection (`ToCreateRequest`
  deferred — §7).
- Arch guard added: `StudentEditDialog_Saves_Atomically_Not_Live` (scoped to the dialog —
  the page is still live, so it is not blanket-guarded).
- **Post-implementation review fixes (rolled into this phase):**
  - Fixed a regression where the edit dialog's loaded guardians' relationship names
    rendered blank (the `StudentFormFields` cleanup had removed the live load that
    populated `_relNames`); `StudentFormFields.OnInitializedAsync` now resolves
    relationship names for pre-loaded `Model.GuardianLinks`.
  - Added a regression bUnit test `StudentEditDialog_ShowsLoadedGuardianRelationship`.
  - Added integration tests `ConcurrentGuardianRemoval_ReturnsConflict` and
    `ConcurrentContactAddition_ReturnsConflict` (the both-directions subset check was
    previously tested only for guardian additions).
  - Tightened §3.7 to document the both-directions check + the 409 mapping.
- Consider an arch guard: "edit dialog saves via `UpdateStudentWithLinkedData`, not
  `UpdateStudent` + live link/unlink" (only if it holds across the codebase after the
  page is migrated — the page is still live, so scope the guard to the dialog, or defer
  until the page migrates). ✅ done (scoped to the dialog).

---

## 5. Decisions (locked)

1. **Scope = edit dialog only** (page stays live). ✅
2. **Atomic server endpoint** `PUT /students/{id}/with-linked-data` (not client-side
   multi-call). ✅
3. **Contact identity:** add `PersistedId` to `ContactModel` + optional `Id` to
   `ContactDraftRequest`. ✅
4. **Guardian reconciliation keyed by `ExistingGuardianId`** (no new link-id field). ✅
5. **Optimistic concurrency IN v1** (per your call) — client echoes `RowVersion` as
   `ExpectedRowVersion`; handler rejects stale saves with `ConcurrencyException` (→ 409);
   child rows covered by EF `xmin` + loaded-id-set subset-check for concurrent additions.
   ✅
6. **Endpoint response:** `204 NoContent`. ✅

---

## 6. Test plan

- **Server handler unit tests** (Phase 0): the reconciliation matrix above. This is the
  core correctness surface — the diff logic lives in the handler. Includes the
  **concurrency** cases: stale `ExpectedRowVersion` ⇒ `ConcurrencyException`; concurrent
  child addition (current guardian/contact id not in the loaded set) ⇒
  `ConcurrencyException`; concurrent child modification ⇒ `DbUpdateConcurrencyException`
  ⇒ `ConcurrencyException` (via EF `xmin`).
- **Client projection unit tests** (Phase 1): `LoadFrom` (all-inclusive) + `ToUpdateRequest`
  round-trip; `ContactDto`↔`ContactModel` id preservation; `StudentGuardianViewDto`→
  `GuardianAssignment` emergency/role mapping; `RowVersion` + loaded-id-set round-trip
  onto the request.
- **Dialog bUnit** (Phase 2): loads profile+guardians+contacts; Save → exactly one
  `UpdateStudentWithLinkedDataAsync` call with the expected payload; Cancel → zero calls.
- **Regression:** existing `StudentFormFieldsRenderActionsBunitTests`,
  `StudentDetailSectionsTests`, create-flow tests, and the `CreateStudentWithLinkedData`
  integration tests must stay green (create is untouched). Run Admin/Settings/Assignments/
  Architecture + the Students integration suite.

---

## 7. Future / v2 (explicitly deferred)

- **Follow-up PR: `StudentFormModel.ToCreateRequest()`.** v1 ships only
  `ToUpdateRequest()`; the create flow still flushes its `CreateStudentWithLinkedDataRequest`
  inline in `Create.razor`. A small follow-up PR should extract that into an on-model
  `ToCreateRequest()` (symmetric with `ToUpdateRequest()`), unit-test it, and update the
  `dto-form-model-mapping.md` “All-inclusive load + model→request projection” section to
  cover both directions. Out of scope for this PR because the create flow is untouched and
  working; this is a consistency/testability refinement, not a behaviour change.
- **Edit page migration** to draft-then-save (using the same endpoint) — separate UX
  decision; the page's live management is intentional today.
- **Guardian contacts editing** from the student edit dialog (today and in v1, a
  guardian's own contacts are managed on the guardian surface, not the student dialog).