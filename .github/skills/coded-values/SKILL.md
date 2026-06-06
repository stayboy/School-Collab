---
name: coded-values
description: |
  School-Collab coded values system. Use when creating, modifying, or seeding
  coded values, attribute definitions, or attribute values. Also use when
  building Admin UI pages or API endpoints for coded values.
  Triggers: "coded value", "coded values", "attribute definition", "seed csv",
  "CodedValueSeeder", "CsvSeedReader", "AI-MODELS".
---

# Coded Values

Coded values are a **hierarchical lookup table** system. Each coded value has a `Code` (unique key), `Name`, optional `Description`, and optional `ParentId` linking it to a parent. Parents define **attribute definitions** (schema/metadata); children define **attribute values** (data).

## Core Concepts

### Parent-Child Hierarchy

```
AI-MODELS (parent, no ParentId)
├── AI-MODELS_LLAMA31_8B (child, ParentId → AI-MODELS)
├── AI-MODELS_LLAMA32_3B
└── AI-MODELS_MISTRAL_7B

GENDER (parent)
├── GENDER_MALE
├── GENDER_FEMALE
└── GENDER_OTHER
```

- **Parent** = category/root node. Carries `AttributeDefinitions` that describe what metadata children should have.
- **Child** = a member of the category. Carries `Attributes` (key/value pairs) that follow the parent's schema.
- A parent can also be a child of another parent (multi-level hierarchy).
- The `Description` field on children is often repurposed for machine-readable values (e.g., Ollama model tags like `llama3.1:8b`).

### Attribute Definitions (on parents)

Attribute definitions live on **parent** coded values. They define the schema that children should populate:

| Field | Type | Purpose |
|---|---|---|
| `Key` | string | Unique identifier within the parent (e.g., `weight`) |
| `DisplayName` | string? | Human-readable label (e.g., "Weight") |
| `DataType` | enum | Text=0, Integer=1, **Decimal=2**, Boolean=3, Date=4, DateTime=5, Time=6, CodedValue=7 |
| `SourceCode` | string? | If DataType=CodedValue(7), reference to another parent's Code for dropdown values |
| `IsRequired` | bool | Whether children must set this attribute |
| `AllowMultiple` | bool | Whether children can have multiple values |
| `MinLength` / `MaxLength` | int? | String validation constraints |
| `RegexPattern` | string? | Regex validation for text values |

**Important**: A definition must exist on the parent before values can be set on children.

### Attribute Values (on children)

Attribute values are simple key/value pairs on **child** coded values:

| Field | Type | Purpose |
|---|---|---|
| `CodedValueId` | Guid | FK to the coded value |
| `Key` | string | Matches a definition Key on the parent |
| `Value` | string | The value (always stored as string, parsed by DataType) |

Example: If `AI-MODELS` parent has definition `weight` (DataType=Decimal), then child `AI-MODELS_LLAMA31_8B` has attribute `weight = "1.0"`.

## Domain Entities

- `CodedValue` — Aggregate root. Methods: `Create()`, `SetAttributeDefinition()`, `SetAttribute()`, `Disable()`, `Enable()`.
- `CodedValueAttributeDefinition` — Owned entity on `CodedValue.AttributeDefinitions`.
- `CodedValueAttribute` — Owned entity on `CodedValue.Attributes`.
- `AttributeDataType` — Enum: Text=0, Integer=1, Decimal=2, Boolean=3, Date=4, DateTime=5, Time=6, CodedValue=7.
- `CodedValueDto` — DTO with `Attributes` and `AttributeDefinitions` collections.

## Seeding

Three CSV files seed the database via `CodedValueSeeder`:

| File | Config Key | Format |
|---|---|---|
| `seed.csv` | `Seeding:FilePath` | `Code,Name,Description,ParentCode,DisplayOrder` |
| `seed-attribute-definitions.csv` | `Seeding:AttributeDefinitionsFilePath` | `ParentCode,Key,DisplayName,DataType,SourceCode,IsRequired,AllowMultiple,MinLength,MaxLength,RegexPattern` |
| `seed-attributes.csv` | `Seeding:AttributeValuesFilePath` | `Code,Key,Value` |

### Seeding Order

1. **Coded values** — Inserted in topological order (parents before children). Idempotent (skips existing codes).
2. **Attribute definitions** — Added to parent coded values. Idempotent (skips if key already exists).
3. **Attribute values** — Added to child coded values. Idempotent (skips if key already exists).

### CSV Format Notes

- Codes are normalized to **UPPER CASE**.
- `DataType` in attribute definitions is the **integer** value of the `AttributeDataType` enum.
- Boolean fields (`IsRequired`, `AllowMultiple`) accept `True`/`False` or `true`/`false`.
- All files support RFC 4180 quoted fields.
- Files are copied to output via `<Content Include="...">` in the `.csproj` with `CopyToOutputDirectory=PreserveNewest`.

### Adding a New Attribute

1. Add the definition row to `seed-attribute-definitions.csv` (e.g., `AI-MODELS,cost,Cost,2,,,False,False,,,`)
2. Add value rows to `seed-attributes.csv` (e.g., `AI-MODELS_LLAMA31_8B,cost,0.001`)
3. Re-run the migration service — seeder will skip existing entries.

## API Surface

All endpoints are under `/coded-values`.

| Method | Endpoint | Purpose |
|---|---|---|
| GET | `/` | List all root coded values |
| GET | `/{id}` | Get by ID |
| GET | `/code/{code}` | Get by Code (e.g., `AI-MODELS`) |
| GET | `/{id}/children` | Get children of a parent |
| POST | `/` | Create coded value |
| PUT | `/{id}` | Update coded value |
| PUT | `/{id}/disable` | Soft-delete (set IsDisabled=true) |
| PUT | `/{id}/enable` | Re-enable |
| PUT | `/{id}/attribute-definitions/{key}` | Upsert attribute definition |
| DELETE | `/{id}/attribute-definitions/{key}` | Remove attribute definition |
| PUT | `/{id}/attributes/{key}` | Upsert attribute value |
| DELETE | `/{id}/attributes/{key}` | Remove attribute value |

### Admin Client Methods

`CodedValuesApiClient` (Blazor) mirrors the API:
- `GetByCodeAsync(code, ct)` — Lookup by code
- `GetChildrenAsync(parentId, ct)` — Get children
- `SetAttributeDefinitionAsync(id, key, request, ct)` — Create/update definition
- `SetAttributeAsync(id, key, value, ct)` — Create/update attribute value
- `RemoveAttributeDefinitionAsync(id, key, ct)` — Remove definition
- `RemoveAttributeAsync(id, key, ct)` — Remove attribute value

## Admin UI Patterns

The landing page (`Components/Pages/CodedValues/Index.razor`) is the canonical example for read-only list pages. Key patterns:

1. **Items-null loading state**: `@if (_items is null) { <FluentProgressRing /> }` — not a `_loading` bool.
2. **Optimistic mutations**: Mutate in-memory DTO with `with`, call API in background, roll back on failure.
3. **IDisposable + CancellationTokenSource**: Every page with `OnInitializedAsync` data loading must own a CTS and dispose it.
4. **`_disposed` guard**: Check after every `await` to prevent setting state on unmounted components.
5. **Dynamic dropdowns**: Load from coded values API, parse `Attributes` for metadata, fall back to hardcoded list.

## EF Core Migrations

Run from repository root:
```bash
dotnet ef migrations add <MigrationName> --project src/CodedValues/SchoolCollab.CodedValues.Core --context CodedValuesDbContext
```

`IDesignTimeDbContextFactory<T>` is already implemented — no startup project or connection string needed.

## Key Files

| File | Purpose |
|---|---|
| `src/CodedValues/SchoolCollab.CodedValues.Core/Domain/CodedValue.cs` | Aggregate root with `SetAttributeDefinition()`, `SetAttribute()` |
| `src/CodedValues/SchoolCollab.CodedValues.Core/Domain/CodedValueAttributeDefinition.cs` | Owned entity for definitions |
| `src/CodedValues/SchoolCollab.CodedValues.Core/Domain/CodedValueAttribute.cs` | Owned entity for values |
| `src/CodedValues/SchoolCollab.CodedValues.Core/Domain/AttributeDataType.cs` | Enum for data types |
| `src/CodedValues/SchoolCollab.CodedValues.Core/DTOs/CodedValueDto.cs` | DTO with Attributes + AttributeDefinitions |
| `src/CodedValues/SchoolCollab.CodedValues.MigrationService/Seeding/CsvSeedReader.cs` | CSV parser for all three seed formats |
| `src/CodedValues/SchoolCollab.CodedValues.MigrationService/Seeding/CodedValueSeeder.cs` | Idempotent seeder (coded values → definitions → values) |
| `src/CodedValues/SchoolCollab.CodedValues.MigrationService/Seeding/AttributeDefinitionSeedRow.cs` | Record for definition CSV rows |
| `src/CodedValues/SchoolCollab.CodedValues.MigrationService/Seeding/AttributeSeedRow.cs` | Record for attribute CSV rows |
| `src/CodedValues/SchoolCollab.CodedValues.MigrationService/seed.csv` | Coded value seed data |
| `src/CodedValues/SchoolCollab.CodedValues.MigrationService/seed-attribute-definitions.csv` | Attribute definition seed data |
| `src/CodedValues/SchoolCollab.CodedValues.MigrationService/seed-attributes.csv` | Attribute value seed data |