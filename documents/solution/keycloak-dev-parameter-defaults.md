# Keycloak + SMTP dev parameter defaults (prompt-free `aspire run`)

Round: `documents/rounds/round-keycloak-dev-defaults.md` (Tier-2 light round).
Supersedes the "no committed default" posture recorded for the Keycloak secrets in the
ar-20 / ar-21 rounds — for **Development only**; the production posture is unchanged and is
now guard-asserted.

## Finding

Four AppHost parameters were declared as secret/optional parameters with **no value in any
configuration source**:

| Parameter | `Program.cs` | Sources before this change |
| --- | --- | --- |
| `keycloak-admin-password` | `:64` (`secret: true`) | none |
| `keycloak-client-secret` | `:65` (`secret: true`) | none |
| `smtp-user` | `:158` | none |
| `smtp-password` | `:159` (`secret: true`) | none |

`postgres-password`, `rabbitmq-password`, `openrouter-api-key` and `cache-password` were
already present in the AppHost user-secrets store
(`71bc1e6c-899e-4131-98f2-60199f7d3ba2/secrets.json`), which is why they never prompted.
Because Aspire prompts for a parameter that resolves from nowhere, a plain
`aspire run` / `dotnet run --project src/AppHost/SchoolCollab.AppHost` **stopped for input**
on these four. An out-of-tree launch script (`run-apphost*.cmd` in `%TEMP%\ar21`, exporting
`Parameters__keycloak_admin_password`, `Parameters__keycloak_client_secret` and
`Parameters__smtp_*`) masked the gap for its author, so the failure mode was invisible in
the common case but real for any fresh clone.

### Alternatives considered

| Option | Why not |
| --- | --- |
| Committed defaults in `appsettings.json` | Loaded in **every** environment, so a value would be baked into production/publish manifests. Rejected. |
| `builder.Environment.IsDevelopment()` branching in `Program.cs` | Adds behaviour and a second code path for a config-only concern; the `AddParameter` overload semantics (literal value vs. configuration override) are less obvious than the configuration chain. Rejected. |
| Rely on documented `dotnet user-secrets set` commands | Already the documented path — and it is exactly the friction this change removes. Rejected. |
| **Dev-only defaults in `appsettings.Development.json`** | **Chosen.** No code change; the file is loaded only in Development; and the default configuration chain (`appsettings.json` → `appsettings.{Environment}.json` → user-secrets → environment variables) means user-secrets and the existing `Parameters__…` env vars still **override** the committed literal, so the existing launch scripts and any real-relay credentials keep working unchanged. |

## Implementation

1. `src/AppHost/SchoolCollab.AppHost/appsettings.Development.json` gained a `Parameters`
   object:

   | Parameter | Dev literal |
   | --- | --- |
   | `keycloak-admin-password` | `dev-only-keycloak-admin` |
   | `keycloak-client-secret` | `dev-only-school-collab-client-secret` |
   | `smtp-user` | `dev-user` |
   | `smtp-password` | `dev-only-smtp-password` |

   `dev-only-school-collab-client-secret` is not a free choice: it **must** equal the
   `school-collab-client` client's `secret` in `school-collab-realm.json`, or Keycloak
   rejects `Auth:Keycloak:ClientSecret` and the failure surfaces only at a manual sign-in.
2. `Program.cs` — comment-only corrections in the Keycloak and SMTP blocks, which previously
   asserted "NO committed default". No statement, `AddParameter` signature or `secret:` flag
   changed (the `secret: true` flags are retained so the dashboard still masks the values and
   deployment manifests still treat them as sensitive).
3. New guard
   `tests/SchoolCollab.ArchitectureTests.Unit/AppHostDevParameterDefaultsArchitectureTests.cs`:
   - the committed dev client secret equals the realm file's `school-collab-client` secret;
   - the four parameters have non-empty dev defaults;
   - the four are **dev-only** — present in `appsettings.Development.json` and absent from
     the non-Development `appsettings.json`, which is what keeps the "no committed production
     secret" posture honest.
   Discovery throws instead of passing vacuously (the `AppHostRealmImportArchitectureTests`
   precedent), and the tests parse with `JsonDocument` rather than substring scanning.
4. `documents/configuration.md` — §2 (the four parameter rows + the "For secrets" paragraph
   and the `AddParameter` prompt note), §4 (the AC#4 parameters heading/table, the
   `Auth:Keycloak:ClientSecret` row and the Secrets block) and §12 (production checklist).

## Production posture

Unchanged in substance, tightened in enforcement: no production secret is committed. The
Development file is not loaded outside Development, and the new guard fails the build if any
of the four keys is ever added to the non-Development `appsettings.json`.

## Verification

- `dotnet build SchoolCollab.slnx` — see the round doc's `## Worker Report` / `## Acceptance`.
- `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` — the three new tests pass
  (38 → 41 cases).
