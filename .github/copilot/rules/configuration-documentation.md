# Configuration Documentation

**Every configuration value that is added, removed, renamed, or has its
default changed MUST be documented in `documents/configuration.md` in the
same PR.**

This rule applies to every project in the repository — APIs, workers, the
AppHost, the AI service, the central config service, and the admin host.
It is the sibling of [`testing.md`](./testing.md) and
[`ef-migrations.md`](./ef-migrations.md): like those rules, it is enforced
during the PR pre-flight review (see
[`.github/copilot-instructions.md`](../../copilot-instructions.md) §"Pre-flight
review & PR creation").

## Why this rule exists

`documents/configuration.md` is the **single source of truth** for
user-facing configuration values. If a key is added in code but never
documented, operators cannot discover it without grepping the source —
which is exactly the failure mode this rule prevents.

## What counts as "configuration"

A change counts when **any** of the following happen:

- A new key is read via `IConfiguration` (e.g. `configuration["X:Y"]`,
  `GetSection("X")`, `GetValue<T>("X")`, `GetConnectionString("X")`,
  `Bind(configuration.GetSection("X"))`).
- A new entry is added to a `*Options` class (`OutboxOptions`,
  `PromotionOptions`, future options classes) — even if no current code
  reads it yet.
- A new `AsParameter` or `AddParameter` call is added in
  `src/AppHost/SchoolCollab.AppHost/Program.cs` — these surface as
  `Parameters:*` keys for AppHost secret/parameter injection.
- An existing key is **renamed** or **moved to a different section** —
  document the new path and call out the rename in the same PR.
- A default value is changed in the corresponding `*Options` class
  (e.g. `BatchSize` default flipped from 100 to 200).
- A key is **removed** — remove its row from the table and, if the
  removal is user-visible (env-var, `appsettings` key), add a note in
  the PR description and in the affected service's `appsettings.json`
  history.
- A new connection string is injected by Aspire into a consumer
  (`ConnectionStrings:<name>`) — document it in §8.

## What does NOT count

The following are **not** user configuration and do not need to be
documented:

- Logging categories / levels beyond the conventions in §9.
- Serilog / OpenTelemetry pipeline configuration owned by
  `SchoolCollab.ServiceDefaults`.
- C# constants used inside the code itself (`SomeMagic = 42` declared
  as a `private const` — not exposed via `IConfiguration`).
- Internal in-process state, caches, and `IOptions<>` registrations
  whose values are computed at startup rather than read from
  configuration.

If unsure, ask: *"Would an operator or a CI pipeline ever need to set
this value in `appsettings.Production.json` or via env-var?"* — if yes,
document it.

## How to document the change

1. **Open `documents/configuration.md`** and locate the section that
   covers the affected surface (Outbox / Auth / FeatureFlags / Promotion
   / AI / ConnectionStrings / etc.).
2. **Add the new key** to the table for that section. Include:
   - Property name (matches the `*Options` POCO exactly).
   - Default value (matches the C# default).
   - One-line description of what it controls.
   - If the value is a secret or a production-tuning knob, mark it
     with the **🔐** or **📝** callout used elsewhere in the file.
3. **Add the env-var row** to §11 ("Environment-variable reference") if
   the key is reachable via env-var (it always is for plain config keys;
   `ConnectionStrings:*` and `Parameters:*` are env-var-reachable too).
4. **Update the cross-reference table** in the related section's
   "Per-service overrides" subsection if the key lives in a per-service
   `appsettings.json` file (currently `Outbox:ExchangeName` only).
5. **Update the production checklist** in §12 if the key is
   production-relevant (a secret, a tuning knob, or a deployment-order
   concern).
6. **Add a changelog entry** under the "Last updated" footer if you
   are making a non-trivial rename or removal.

## Worked examples

### Adding a new key (`OutboxOptions.MaxConcurrentPublishers`)

`src/SchoolCollab.Core/Messaging/OutboxOptions.cs`:

```csharp
public int MaxConcurrentPublishers { get; set; } = 4;
```

`documents/configuration.md` §3 — add a row:

```markdown
| `MaxConcurrentPublishers` | `4` | Number of parallel publish channels used by `OutboxDispatcher<TContext>`. |
```

§11 — add a row:

```markdown
| `Outbox:MaxConcurrentPublishers` | `Outbox__MaxConcurrentPublishers` |
```

### Renaming an existing key (`OutboxOptions.PollInterval` → `OutboxOptions.IdlePollInterval`)

1. Update the C# code (rename the property + rename the
   `appsettings.json` value, preserving the old key temporarily as a
   fallback if a staged rollout is needed).
2. `documents/configuration.md` §3 — rename the row and add a
   strikethrough note: ~~`PollInterval`~~ → `IdlePollInterval`.
3. §11 — replace the old env-var row with the new one. Add a "Rename"
   entry to the footer (see template below).

### Removing a key

1. Remove the C# property and its usages.
2. Remove the row from the relevant section table.
3. Remove the row from §11.
4. Mention the removal in the PR description so reviewers can verify
   no `appsettings.json` still references the key.

## Pre-flight checklist

Before opening a PR that adds, removes, or changes a configuration value:

- [ ] `grep -rn 'configuration\["X:Y"\]\|GetSection("X:Y")\|GetValue<.*>("X:Y")' src/` confirms the change is the only place that key is read.
- [ ] `documents/configuration.md` has been updated in the same commit
      (additions, removals, renames, default changes).
- [ ] §11 ("Environment-variable reference") matches the new key path.
- [ ] §12 ("Production checklist") has been re-read; add a row if the
      key is production-relevant.
- [ ] The PR description includes a one-line "Configuration impact"
      summary naming every key that changed.

## Last updated

Add a one-line entry here whenever this rule itself changes; rename or
remove keys as needed.

- _Initial version — companion to `documents/configuration.md`._