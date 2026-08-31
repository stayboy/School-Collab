# Copilot Rule Files

This directory holds specialty instruction files for SchoolCollab. Keep
`AGENTS.md` (repo root) as the global manifest and move large topic-specific
guidance into these files.

## Current specialty rules

| Rule | File |
|---|---|
| Agent / AI communication style | `communication-style.md` |
| Blazor components, Fluent UI, and styling | `blazor-components.md` |
| Card-level create / edit dialogs and sections | `section-card.md` |
| Entity Framework Core migrations | `ef-migrations.md` |
| Logging and Aspire observability | `logging-aspire.md` |
| Testing | `testing.md` |
| Configuration documentation | `configuration-documentation.md` |
| Shared constants and enums | `shared-constants.md` |
| .NET/C# best practices | `dotnet-best-practices.md` |

## How to use

Before working in a topic area, read the matching rule file. Do not duplicate large
topic-specific sections in `AGENTS.md`; link to them instead.

## Related skills

Skills live under `.github/skills/` and include trigger metadata for agent selection.

| Skill | File |
|---|---|
| Dialogs (shared form-fields, no nesting) | `../skills/dialog-ui/SKILL.md` |
| Fluent UI icons | `../skills/fluentui-icons/SKILL.md` |
| Fluent UI component props | `../skills/fluentui-component-props/SKILL.md` |
| Bounded context creation | `../skills/bounded-context/SKILL.md` |
| Coded values domain | `../skills/coded-values/SKILL.md` |
| Azure AI OpenAI .NET | `../skills/azure-ai-openai-dotnet/SKILL.md` |
| .NET/C# best practices | `../skills/dotnet-best-practices/SKILL.md` |
