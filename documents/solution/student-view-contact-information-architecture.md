# Student view — Contact information architecture

**Status:** plan (read before implementing)
**Branch:** `feat/student-view-modernize` (continues the single-page sectioned layout work)
**Date:** 2026-07-22

## 1. Goal

The current student detail view has the contact information in the wrong
place. The domain model is:

- A **student** has their **own direct contact** (email, SMS, WhatsApp).
  The student themselves might be reachable.
- A **student** has **linked guardians** (Primary / CC, with relationship
  and emergency-contact flag).
- Each **guardian** has their **own contacts** (email, SMS, WhatsApp) with
  a primary role.

The current `Detail.razor` puts the student's own contacts in a standalone
"Contacts" section, with no contact info on the profile. That's the wrong
information architecture — the data is split awkwardly, and the user has
to read two sections to find out "how do I reach the student and their
guardians?".

The target:

- **Profile section** — show the student's own direct contact (email, SMS,
  WhatsApp) inline, with the same add/remove/verify/primary affordances
  the shared `<ContactsEditor>` already provides for the Student owner.
- **Contacts section** — replaced with a read-friendly **"Guardians &
  their primary contact"** view: for each linked guardian, show their
  primary contact (one line per channel), so the user can see at a
  glance "who do I call?". Editing of a guardian's contacts stays on
  the `GuardianDetail` page (where it already works via the same shared
  `<ContactsEditor>` with `ContactOwnerType.Guardian`).
- **Guardians section** — keeps its current role/relationship/emergency
  display (it's about *who is linked*, not *how to reach them*).

## 2. Information architecture

### Before

```
┌── Title row ─────────────────────────────────────┐
│  FirstName LastName (Gender, Age)  [Edit]         │
└───────────────────────────────────────────────────┘
┌── Profile (FluentCard) ──────────────────────────┐
│  Student #   | Full name                          │
│  DOB         | Gender                             │
│  Status      | Created/Updated                    │
└───────────────────────────────────────────────────┘
─── Enrollments ─────────────── [Enroll] ──────────
─── Guardians ─────────────────────────────────────
─── Contacts ──────────────────────────────────────
   [ <ContactsEditor OwnerType=Student /> ]
```

### After

```
┌── Title row ─────────────────────────────────────┐
│  FirstName LastName (Gender, Age)  [Edit]         │
└───────────────────────────────────────────────────┘
┌── Profile (FluentCard) ──────────────────────────┐
│  Identity (top-down stat-card)                    │
│  Student # | Full name | DOB | Gender | ...       │
│  ─────────────────────────────────────────────    │
│  Direct contact                                  │
│  [ <ContactsEditor OwnerType=Student /> ]         │
└───────────────────────────────────────────────────┘
─── Enrollments ─────────────── [Enroll] ──────────
─── Guardians ─────────────────────────────────────
─── Contacts ──────────────────────────────────────
   ┌─ Guardian 1: Jane Doe (Primary, Mother) ─────┐
   │  ✉  jane@example.com   [Primary]              │
   │  📱 +1 555 123 4567                          │
   └───────────────────────────────────────────────┘
   ┌─ Guardian 2: John Doe (CC, Father) ──────────┐
   │  ✉  john@example.com   [Primary] [Verified]   │
   └───────────────────────────────────────────────┘
```

## 3. Component changes

### 3.1 Profile card — add direct-contact sub-section

**File:** `Detail.razor`

Move the standalone `<ContactsEditor OwnerType="ContactOwnerType.Student" />`
**into** the Profile `<FluentCard class="detail-card">`, after the
`.profile-grid`, with a sub-section header ("Direct contact" or
"Student contact"). Reuse the existing `.section-header` pattern (h3 +
optional action) at a smaller scale to mark the boundary, but **inside
the same card** so the data is visually grouped as "the student".

```razor
<FluentCard class="detail-card">
    <div class="profile-grid">…</div>

    <FluentDivider class="profile-section-sep" />

    <div class="section-header section-header--sub">
        <h4>Direct contact</h4>
    </div>
    <ContactsEditor OwnerType="ContactOwnerType.Student"
                    OwnerId="@Id"
                    ShowSubscription="false" />
</FluentCard>
```

**Rationale for sub-section vs. separate card:**
- One card = "the student's data" (identity + how to reach the student)
- Separate card = same visual weight as Enrollments/Guardians/Contacts,
  but the data is **about the student** (not a parallel section of
  student-of-student relationships)
- The `FluentDivider` matches the existing design language
  (added in the previous commit, replacing the old border-top)
- `ShowSubscription="false"` — the student themselves doesn't need
  a per-channel "subscribed to announcements" toggle; that's a
  guardian-level concern

### 3.2 Contacts section — guardians & their primary contact

**File:** `Detail.razor` (rewrite the `<ContactsEditor>` invocation as a
guardian-contacts list) **or** new file `Components/Students/GuardianContactsList.razor`

**Recommended:** new file `GuardianContactsList.razor` for symmetry with
the existing `GuardiansTab.razor` and to keep `Detail.razor` focused on
orchestration.

**Shape:**

```razor
@* Lists the primary contacts of every guardian linked to a student.
   Read-only display: edits happen on GuardianDetail.razor. *@

@using SchoolCollab.Students.Core.Contracts
@using SchoolCollab.Students.Core.Domain
@using SchoolCollab.Students.Core.DTOs
@using SchoolCollab.Students.Admin.Services
@inject StudentsApiClient Api
@inject IContactsClient ContactsApi

<div class="guardian-contacts">
    @if (_loading) { <FluentProgressRing /> }
    else if (_items.Count == 0)
    {
        <FluentMessageBar Intent="MessageIntent.Info">
            No guardians linked — add a guardian in the Guardians section
            above, then come back here to see their contact info.
        </FluentMessageBar>
    }
    else
    {
        @foreach (var item in _items)
        {
            <div class="guardian-contact-card">
                <div class="guardian-contact-header">
                    <strong>@item.FirstName @item.LastName</strong>
                    <span class="muted">(@item.Role@(item.Relationship is { } r ? $", {r}" : ""))</span>
                </div>
                @if (item.PrimaryContacts.Count == 0)
                {
                    <span class="muted">No contact info on file.</span>
                }
                else
                {
                    <ul class="guardian-contact-list">
                        @foreach (var c in item.PrimaryContacts)
                        {
                            <li>
                                <span class="contact-channel">@ChannelGlyph(c.Channel) @c.Channel</span>
                                <span class="contact-value">@c.Value</span>
                                @if (c.IsPrimary) { <FluentBadge>Primary</FluentBadge> }
                                @if (c.IsVerified) { <FluentBadge>Verified</FluentBadge> }
                            </li>
                        }
                    </ul>
                }
            </div>
        }
    }
</div>
```

**Data model:** `GuardianContactsList` loads via the existing
`ListGuardiansByStudentAsync` (returns `StudentGuardianViewDto[]`) plus
`ListContactsAsync(ContactOwnerType.Guardian, guardianId)` for each. The
per-guardian contacts are typically 0-3 entries, so the N+1 query cost
is acceptable (parents typically have ≤ 4 guardians).

**Decision: per-row render or one list?** One card per guardian. Each
guardian card shows the channels they can be reached on, with the
**primary** badge prominent. Non-primary contacts (most common case: a
home phone and a work email) are also shown but de-emphasized.

### 3.3 Remove `<ContactsEditor OwnerType="ContactOwnerType.Student" />` from "Contacts" section

**File:** `Detail.razor`

Replace the standalone Contacts section's `<ContactsEditor>` with the
new `<GuardianContactsList StudentId="Id" />`. The section header stays
("Contacts" → could also rename to "Reach guardians" but "Contacts"
is the established term; the contents are now "how to contact each
guardian").

## 4. CSS additions

**File:** `Detail.razor.css`

```css
/* Sub-section divider inside Profile card. Uses the same FluentDivider
   as the section separators above, but smaller margin because it's
   inside a card. */
.profile-section-sep {
    margin: 16px 0 8px;
    opacity: 0.6;
}

/* Slightly smaller section header for in-card sub-sections. */
.section-header--sub h4 {
    margin: 0;
    font-size: 0.95rem;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: var(--neutral-foreground-hint);
    font-weight: 500;
}
```

**File:** `GuardianContactsList.razor.css` (new)

```css
.guardian-contacts {
    display: flex;
    flex-direction: column;
    gap: 12px;
}
.guardian-contact-card {
    border: 1px solid var(--neutral-stroke-rest, #e0e0e0);
    border-radius: 6px;
    padding: 12px 16px;
    background: var(--neutral-layer-1);
}
.guardian-contact-header {
    display: flex;
    align-items: baseline;
    gap: 8px;
    margin-bottom: 8px;
}
.guardian-contact-list {
    list-style: none;
    padding: 0;
    margin: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
}
.contact-channel { color: var(--neutral-foreground-hint); margin-right: 4px; }
.muted { color: var(--neutral-foreground-hint); }
```

## 5. Tests

### 5.1 Update `StudentDetailSectionsTests`

The existing test
`Detail_Embeds_Guardians_And_Contacts_Subcomponents` asserts the old
shape (`OwnerType="ContactOwnerType.Student"` in the Contacts section).
Update it to:

- The Profile section contains `<ContactsEditor OwnerType="ContactOwnerType.Student" />`
- The Contacts section embeds `<GuardianContactsList StudentId="Id" />` instead

Add new tests:

- `Profile_Includes_Student_Own_Contacts_Editor` — the `<ContactsEditor
  OwnerType="ContactOwnerType.Student" />` lives inside the `<FluentCard
  class="detail-card">` block (i.e. before `</FluentCard>`), not in a
  separate section.
- `Contacts_Section_Lists_Guardians_Not_Student_Contacts` — the Contacts
  section heading is followed by `<GuardianContactsList`, not a
  `<ContactsEditor>`.

### 5.2 New `GuardianContactsListTests.cs` (bUnit)

- Renders the empty state when no guardians are linked
- Renders one card per guardian when guardians are linked
- Each card shows the primary contact first (sort: primary first, then
  non-primary, by channel)
- Channel glyphs are correct (✉ / 📱 / 💬)
- Loading state shows a spinner

(Follow the `FakeContactsClient` pattern from `ContactsEditorTests.cs`.)

### 5.3 Regression — existing `ContactsEditorTests` unchanged

The shared `<ContactsEditor>` is unchanged in its contract; only its
*placement* moved. The existing 6+ lifecycle tests still cover the
load/error/dispose race condition behavior.

## 6. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Profile card becomes tall (identity + direct contact) | Use a `.profile-section-sep` with small margin; the card stays reasonable height |
| N+1 query for guardian contacts | Bounded by typical family size (1-4 guardians); not a performance concern at v1 |
| The `ContactDto` for a guardian might be large (5+) | Show all but de-emphasize non-primary (grey text, no badges); matches how `ContactsEditor` already does it |
| `<ContactsEditor>` re-renders when a contact is added/removed | Its existing `OnParametersSetAsync` + CTS pattern handles this; no changes needed |
| Page-section ordering | Same order: Profile → Enrollments → Guardians → Contacts. The "Contacts" rename-from-Student-contacts is what changes |

## 7. Step-by-step order

1. **Plan review** (this document)
2. **Create `GuardianContactsList.razor` + .razor.css** (new component)
3. **Move `<ContactsEditor OwnerType=Student>` into Profile card** in
   `Detail.razor`; add `.profile-section-sep` divider + sub-section
   header
4. **Replace standalone Contacts `<ContactsEditor>` with
   `<GuardianContactsList StudentId=Id />`**
5. **Add scoped CSS** (`.profile-section-sep`, `.section-header--sub`,
   `.guardian-contact-card`)
6. **Update `StudentDetailSectionsTests`** (move/rename the existing
   `OwnerType=Student` assertion, add the new subcomponent assertions)
7. **Add `GuardianContactsListTests.cs`** (bUnit, ~5 tests)
8. **Build + run all tests** (should still be 79+ Admin, 67+ Students)
9. **Commit** (one commit, or two: "feat(students): profile-internal
   contact editor" + "feat(students): guardian-contacts list in
   Contacts section")

## 8. Open questions

None — the user has stated the intent clearly:
- "Student should have a direct contact (email, sms, whatsapp) as part
  of profile" → embedding in the Profile card
- "contact list should be guardians with contacts" → Contacts section
  becomes a guardian-contacts list

## 9. Verification

- [ ] Build: 0 errors
- [ ] Unit tests: 79+ Admin, 67+ Students (matching the previous totals
      plus the new GuardianContactsListTests)
- [ ] Visual check: hard-refresh `/students/{id}` → Profile card shows
      identity stat-cards **and** the student's own contacts (add/remove
      buttons visible); Contacts section shows one card per linked
      guardian with their primary contact info
- [ ] No new `<FluentTabs` (existing test still passes)
- [ ] No new CSS rule with the wrong scope (existing
      `Detail_Preserves_Legacy_Layout_CSS_Classes` test still passes)
