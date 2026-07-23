# Shared Constants and Enums

To maintain consistency and avoid duplication across the solution, all shared enums and constants must follow these guidelines.

## Core Principles

- **Centralization**: Shared constants, enums, and static configuration data must be stored in dedicated constant files rather than defined inline or inside component-specific files.
- **Project Placement**: 
  - For UI/Frontend shared constants (e.g., dropdown categories, theme keys), use `src/SchoolCollab.Admin.Shared/Constants/`.
  - For Core/Backend shared constants (e.g., domain types, status codes), use the `Constants` folder within the respective `.Core` project.
- **Naming Convention**: 
  - Files should be named according to the domain they serve (e.g., `CodedValueConstants.cs`, `StudentConstants.cs`).
  - Enums should use `PascalCase` and represent a closed set of options.

## Implementation Pattern

### 1. Enum Definition
Define the enum in a dedicated constant file:
```csharp
namespace SchoolCollab.Admin.Shared.Constants;

public enum CodedValueParent
{
    Gender = 0,
    Subject = 1,
    Grade = 2
}
```

### 2. Extension Methods for Mapping
When enums map to database strings or external API codes, provide a static extension class in the same file to keep the mapping logic together:
```csharp
public static class CodedValueParentExtensions
{
    public static string ToCode(this CodedValueParent parent) => parent switch
    {
        CodedValueParent.Gender => "GENDER",
        CodedValueParent.Subject => "SUBJECT",
        CodedValueParent.Grade => "GRADE",
        _ => throw new ArgumentOutOfRangeException(nameof(parent))
    };
}
```

### 3. Consumption
Always reference the constant/enum from the shared namespace rather than redefining it:
```razor
@using SchoolCollab.Admin.Shared.Constants

<CodedValueDropdown Parent="CodedValueParent.Grade" ... />
```

## Feature Flags

All runtime feature-flag keys must be declared as constants in
`SchoolCollab.Core.Features.FeatureFlagKeys`.

- **Do not** pass raw strings like `"FEATURE:EnableFoo"` to
  `IFeatureFlagService.IsEnabled`, `FeatureFlagGate.Key`, or
  `FeatureFlag.NormalizeKey`.
- **Do** use `FeatureFlagKeys.EnableFoo` everywhere in C# and
  `@FeatureFlagKeys.EnableFoo` in `.razor` markup.
- When adding a new flag, add its constant to `FeatureFlagKeys` first, then
  reference the constant in `appsettings.json` defaults, migration seeds,
  `FeatureFlagGate` usage, and API authorization gating.
- `appsettings*.json` files cannot import C# constants, so they must stay
  manually aligned. Any PR that adds or changes a flag must update both
  `FeatureFlagKeys` and the matching configuration entries.

### Example

```csharp
// SchoolCollab.Core.Features.FeatureFlagKeys
public const string EnableFoo = "FEATURE:EnableFoo";
```

```razor
@using SchoolCollab.Core.Features

<FeatureFlagGate Key="@FeatureFlagKeys.EnableFoo">
    ...
</FeatureFlagGate>
```

```csharp
if (!featureFlags.IsEnabled(FeatureFlagKeys.EnableFoo))
{
    group.RequireAuthorization();
}
```

## Prohibited Patterns
- ❌ Defining enums inside `.razor` components.
- ❌ Using "magic strings" for category keys in API calls or component parameters.
- ❌ Duplicating the same enum in both a `.Core` and an `.Admin` project (use a shared project or a contract project instead).
- ❌ Passing raw `"FEATURE:..."` strings for feature-flag keys.
