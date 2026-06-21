# Copilot Instructions Split

## Findings

The repository-level Copilot instructions in `.github/copilot-instructions.md` had grown
beyond a concise global manifest. Several stable topic areas were large enough to live
as specialty guidance:

- Blazor component conventions, Fluent UI usage, landing-page patterns, and CSS styling
- Entity Framework Core migration rules
- Serilog and Aspire observability rules
- Bug-fix regression tests and unit test conventions
- Fluent UI icon usage, already represented by `.github/skills/fluentui-icons/SKILL.md`

The existing skill files under `.github/skills/` were also not consistently linked from
the main instructions file.

## Decision

Keep `.github/copilot-instructions.md` as the global instruction manifest and move
large topic-specific guidance into specialty rule files under
`.github/copilot/rules/`.

This keeps repository-wide rules easy to find while making targeted guidance more
discoverable for agents working in a specific area.

## Implementation

### Created specialty rule files

- `.github/copilot/rules/README.md`
  - Index for rule files and related skills.
- `.github/copilot/rules/blazor-components.md`
  - Blazor render mode, loading states, error boundaries, `@key`, component parameters,
    landing-page performance pattern, Fluent UI usage, CSS isolation, inline style
    rules, `::deep`, global styles, and edit form layout.
- `.github/copilot/rules/ef-migrations.md`
  - EF migration triggers, commands, naming conventions, rules, pending model-changes
    guard, and seeding vs schema migration guidance.
- `.github/copilot/rules/logging-aspire.md`
  - Serilog/Aspire logging rules, backend API logging, Blazor frontend logging,
    domain/core logging, and Aspire dashboard visibility.
- `.github/copilot/rules/testing.md`
  - Bug-fix regression test rules and unit test conventions for feature additions.

### Updated main instructions

`.github/copilot-instructions.md` now keeps only repository-wide guidance and links to
specialty files from:

- `## Specialty instructions`
- `## Topic links`

The following topic sections were removed from the main file and moved to specialty
files:

| Moved topic | New location |
|---|---|
| Logging and Aspire observability | `.github/copilot/rules/logging-aspire.md` |
| Blazor component best practices | `.github/copilot/rules/blazor-components.md` |
| Fluent UI icon usage | `.github/skills/fluentui-icons/SKILL.md` |
| EF Core migrations | `.github/copilot/rules/ef-migrations.md` |
| CSS and styling | `.github/copilot/rules/blazor-components.md` |
| Bug-fix regression tests | `.github/copilot/rules/testing.md` |
| Unit tests for feature additions | `.github/copilot/rules/testing.md` |

### Updated related skill

`.github/skills/fluentui-icons/SKILL.md` was updated to match the repository rule:

- Standalone `<FluentIcon>` uses the generic `Icon` parameter.
- Fluent component icon parameters use shared `FluentIcons` constants.
- `Icon.FromType<T>()` is not used in Razor markup.
- Missing reusable icons are added to
  `src/SchoolCollab.Admin.Shared/Constants/FluentIcons.cs`.

## Verification

Ran a targeted search to confirm the moved sections now live in specialty files and are
not duplicated as top-level sections in the main instructions file:

```bash
rg -n "^## (Logging|Blazor component best practices|Entity Framework Core migrations|CSS and styling|Bug-fix regression tests|Unit tests for feature additions)" .github/copilot-instructions.md .github/copilot/rules/*.md
```

The main file no longer contains those top-level topic sections. The moved topics are
available under `.github/copilot/rules/` or `.github/skills/`.

## Follow-up guidance

Future instruction additions should follow this split:

- Add repository-wide rules to `.github/copilot-instructions.md`.
- Add large topic-specific rules to `.github/copilot/rules/*.md`.
- Add agent-triggered, package-specific, or domain-specific guidance to
  `.github/skills/<topic>/SKILL.md`.
- Avoid duplicating detailed rules in both the main file and specialty files.
