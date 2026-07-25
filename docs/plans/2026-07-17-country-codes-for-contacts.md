# Plan: Country Codes for Contacts

**Date:** 2026-07-17
**Status:** PLAN (not yet implemented)
**Branch target:** feature branch off `main`, squash-merge via PR (repo convention: push with `SCHOOLCOLLAB_ALLOW_PUSH=1`, wait for Build & Test CI, then squash-merge).

---

## 1. Goal

Add country-code support to phone-based contacts (SMS and WhatsApp) in the shared `ContactsEditor` component. Country codes are sourced from a new coded-value category `CNCODES` (Country Calling Codes) and seeded with common dial codes. Email contacts remain unchanged.

---

## 2. Key codebase facts (research)

- **Contacts live in the Students bounded context.**
  - Domain: `src/Students/SchoolCollab.Students.Core/Domain/Contact.cs`
  - DTO: `src/Students/SchoolCollab.Students.Core/DTOs/ContactDto.cs`
  - Channel enum: `src/Students/SchoolCollab.Students.Core/Domain/ContactChannel.cs` (`Email`, `SMS`, `WhatsApp`)
  - API routes: `src/Students/SchoolCollab.Students.Api/Endpoints/ContactRoutes.cs`
  - Client contract: `src/Students/SchoolCollab.Students.Core/Contracts/IContactsClient.cs`
  - UI: `src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor`

- **Coded values are managed in the Settings bounded context.**
  - Seed file: `src/SchoolCollab.MigrationService/SeedData/seed.csv`
  - Seeder: `src/SchoolCollab.MigrationService/Seeding/CodedValueSeeder.cs`
  - Client: `src/SchoolCollab.Admin.Shared/Services/CodedValuesApiClient.cs`
  - Dropdown component: `src/SchoolCollab.Admin.Shared/Components/CodedValueDropdown.razor`
  - Parent enum/mapping: `src/SchoolCollab.Admin.Shared/Constants/CodedValueConstants.cs` (`CodedValueParent` + `ToCode()`)

- **Existing `Contact` entity stores:** `OwnerType`, `OwnerId`, `Channel`, `Value`, `Label`, `IsPrimary`, `IsVerified`.
- **`AddContactRequest`** currently carries `OwnerType`, `OwnerId`, `Channel`, `Value`, `Label`, `IsPrimary`.
- **`UpdateContactRequest`** currently carries `Value`, `Label`.

---

## 3. Design decisions

### 3.1 Store the dial code as a string on `Contact`

Add a nullable `string CountryCode` property to the `Contact` entity. It stores the actual dial code (e.g., `"+233"`), not a coded-value id.

**Why not store a coded-value id?**
- The SMS/WhatsApp subsystem ultimately needs the dial code, not a UUID.
- Country calling codes are stable; a string is sufficient and avoids a join or coded-value lookup at send time.
- The coded-value dropdown is still used as the **authoritative picker UI**; the selected item's `Name` field carries the dial code and is copied into the request.

This keeps the domain model simple while satisfying the requirement to source country codes from coded values.

### 3.2 Seed `CNCODES` as a new top-level coded-value category

Add to `seed.csv`:

```csv
CNCODES,Country Calling Codes,International dialling prefixes,,0
CNCODES_USA,+1,United States,CNCODES,1
CNCODES_GBR,+44,United Kingdom,CNCODES,2
CNCODES_GHA,+233,Ghana,CNCODES,3
CNCODES_ZAF,+27,South Africa,CNCODES,4
CNCODES_NGA,+234,Nigeria,CNCODES,5
CNCODES_KEN,+254,Kenya,CNCODES,6
CNCODES_IND,+91,India,CNCODES,7
```

> The list can be expanded; the initial seed covers the most common markets for this deployment.

### 3.3 Add `CountryCallingCodes` to `CodedValueParent`

Map `CodedValueParent.CountryCallingCodes` → `"CNCODES"` so `CodedValueDropdown` can consume it like any other category.

### 3.4 UI behaviour in `ContactsEditor`

- Add a `CodedValueDropdown` bound to `_newCountryCodeId`.
- Show the dropdown **only** when `_newChannel` is `SMS` or `WhatsApp`.
- Default selection: pre-select Ghana (`+233`) when the dropdown first appears.
- When the user changes the channel to `Email`, clear `_newCountryCodeId`.
- On add, resolve the selected coded value's `Name` and pass it as `CountryCode` in `AddContactRequest`.
- Display existing contacts as `📱 +233 20 123 4567` (combine `CountryCode` and `Value`).

### 3.5 Validation

- SMS/WhatsApp `Value` should contain only digits and an optional leading `+` (but the `+` should normally come from `CountryCode`).
- Keep validation lightweight: trim whitespace, strip non-digit characters server-side, or rely on a simple regex.
- Do **not** block saving if no country code is selected; fall back to storing the raw `Value` (backward-compatible). The UI default makes an explicit selection likely.

---

## 4. Implementation steps

### Part A — Seed data and coded-value plumbing

**Files:** `src/SchoolCollab.MigrationService/SeedData/seed.csv`, `src/SchoolCollab.Admin.Shared/Constants/CodedValueConstants.cs`

1. Add `CNCODES` parent + children to `seed.csv`.
2. Add `CountryCallingCodes = 10` to `CodedValueParent` enum.
3. Add `CodedValueParent.CountryCallingCodes => "CNCODES"` in `ToCode()`.
4. Build `SchoolCollab.MigrationService` and `SchoolCollab.Admin.Shared`.

### Part B — Domain + persistence

**Files:** `src/Students/SchoolCollab.Students.Core/Domain/Contact.cs`, `src/Students/SchoolCollab.Students.Core/DTOs/ContactDto.cs`, migration files

1. Add `public string? CountryCode { get; private set; }` to `Contact`.
2. Update `Contact.Create(...)` to accept `string? countryCode` and assign `CountryCode = countryCode?.Trim()`.
3. Update `Contact.Update(...)` to accept `string? countryCode` and assign it.
4. Add `string? CountryCode` to `ContactDto` positional record.
5. Update `ListContactsHandler` projection to include the new field.
6. Create an EF Core migration: `dotnet ef migrations add ContactCountryCode -p src/Students/SchoolCollab.Students.Core -s src/Students/SchoolCollab.Students.Api` (adjust project paths to match repo conventions).
7. Apply migration locally or ensure it runs via `MigrationService` at startup.

### Part C — Commands, handlers, and API

**Files:**
- `src/Students/SchoolCollab.Students.Core/CQRS/Contacts/Commands/AddContact/AddContact.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Contacts/Commands/AddContact/AddContactHandler.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Contacts/Commands/UpdateContact/UpdateContact.cs`
- `src/Students/SchoolCollab.Students.Core/CQRS/Contacts/Commands/UpdateContact/UpdateContactHandler.cs`
- `src/Students/SchoolCollab.Students.Api/Endpoints/ContactRoutes.cs`

1. Add `string? CountryCode` to `AddContact` command record.
2. Pass `command.CountryCode` into `Contact.Create(...)` in `AddContactHandler`.
3. Add `string? CountryCode` to `UpdateContact` command record.
4. Pass `command.CountryCode` into `Contact.Update(...)` in `UpdateContactHandler`.
5. Update the route handler's inline `UpdateContactRequest` to include `CountryCode`.

### Part D — Client contract

**File:** `src/Students/SchoolCollab.Students.Core/Contracts/IContactsClient.cs`

1. Add `string? CountryCode` to `AddContactRequest`.
2. Add `string? CountryCode` to `UpdateContactRequest`.

### Part E — ContactsEditor UI

**File:** `src/SchoolCollab.Admin.Shared/Components/ContactsEditor.razor`

1. Inject `CodedValuesApiClient` as `CodedValuesApi`.
2. Add `@using SchoolCollab.Admin.Shared.Services` and `@using SchoolCollab.Admin.Shared.Constants` (if not already present).
3. Add private state:
   ```csharp
   private Guid? _newCountryCodeId;
   private CodedValueDto[]? _countryCodeOptions;
   ```
4. On initialization or when `_newChannel` becomes `SMS`/`WhatsApp`, load country codes via `CodedValuesApi.GetChildrenByParentCodeAsync("CNCODES")` and default `_newCountryCodeId` to the Ghana entry.
5. Render the dropdown conditionally:
   ```razor
   @if (_newChannel is ContactChannel.SMS or ContactChannel.WhatsApp)
   {
       <CodedValueDropdown Parent="CodedValueParent.CountryCallingCodes"
                           @bind-SelectedId="_newCountryCodeId"
                           Placeholder="Country code" />
   }
   ```
6. In `AddAsync`, resolve the dial code from `_countryCodeOptions` using `_newCountryCodeId`.
7. Pass the resolved dial code as `CountryCode` in `AddContactRequest`.
8. Reset `_newCountryCodeId` to the default (Ghana) after a successful add.
9. Update display: `<span class="contact-value">@FormatPhone(c.CountryCode, c.Value)</span>`.
10. Add helper `FormatPhone(string? code, string value)` returning `$"{code} {value}"`.

### Part F — Tests

1. **Server-side handler tests**
   - `AddContactHandlerTests`: assert `CountryCode` is stored when supplied.
   - `UpdateContactHandlerTests`: assert `CountryCode` can be changed.
   - `ListContactsHandlerTests`: assert `CountryCode` is projected into `ContactDto`.

2. **bUnit / component tests**
   - `ContactsEditorTests`: assert dropdown is shown for SMS/WhatsApp, hidden for Email.
   - Assert selected country code is included in the `AddContactRequest` passed to `IContactsClient.AddContactAsync`.
   - Assert default selection is Ghana (`+233`).

3. **Seed data tests**
   - Add or update a coded-value seeder test to assert `CNCODES` and at least the Ghana child are seeded.

### Part G — Documentation

1. Update `documents/configuration.md` if country-code configuration is exposed to operators (e.g., default country code).
2. Update `documents/solution/` with a brief finding/implementation note if this becomes a notable architectural decision.

---

## 5. Out of scope / future enhancements

- **Phone-number formatting libraries** (e.g., libphonenumber). Keep the first implementation simple with string concatenation.
- **Per-tenant default country code**. For now, Ghana is the hard-coded UI default.
- **Bulk import** of contacts with country codes.
- **Validation rule** requiring a country code for SMS/WhatsApp — the UI default is sufficient for MVP.

---

## 6. Verification checklist

- [ ] `dotnet build SchoolCollab.sln` succeeds with 0 errors.
- [ ] New EF migration applies cleanly to a fresh database and to the current dev database.
- [ ] `CNCODES` parent + Ghana child appear after `MigrationService` runs.
- [ ] `ContactsEditor` shows country-code dropdown only for SMS/WhatsApp.
- [ ] Adding an SMS contact stores `"+233"` (or selected code) in `Contact.CountryCode`.
- [ ] Existing email contacts display unchanged.
- [ ] New unit tests pass.
