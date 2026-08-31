# Review — Phase 6.1 Pilot-tenant override implementation

> Reviewer report for `documents/specs/plan-phase6-1.md`.
> Persisted here from the inline reviewer report during the acceptance pass.

## Scope inspected
- Plan: `documents/specs/plan-phase6-1.md`
- New seeder: `src/SchoolCollab.MigrationService/Seeding/PilotActivityGroupFlagOverrideSeeder.cs`
- Migration service entry point: `src/SchoolCollab.MigrationService/Program.cs`
- Tenant seeder: `src/SchoolCollab.MigrationService/Seeding/TenantSeeder.cs`
- Documentation: `documents/configuration.md` §5
- Unit tests: `tests/SchoolCollab.Settings.Tests.Unit/Tenancy/PilotActivityGroupFlagOverrideSeederTests.cs`
- Test project reference: `tests/SchoolCollab.Settings.Tests.Unit/SchoolCollab.Settings.Tests.Unit.csproj`

## Correct (with evidence)
- **Pilot override seeded ON** for `Hydeson School`. `PilotActivityGroupFlagOverrideSeeder.PilotTenantName = "Hydeson School"` (Seeder.cs:38). The override is created with `isEnabled: true`, `value: null`, `effectiveFrom: null`, `effectiveTo: null`, and a clear reason mentioning the pilot rollout (Seeder.cs:91–100).
- **Idempotent by existence**: the seeder queries `TenantFlagOverrides` cross-tenant (`IgnoreQueryFilters(["Tenant"])`) and short-circuits if an override already exists, producing no new audit row (Seeder.cs:74–84).
- **Runs after tenant + flag seeds**: in `Program.cs` the flag is seeded at line 98 (`SeedEnableActivityGroupsAsync`), the tenant registry is captured at line 109 (`var tenantIdsByName = await tenantSeeder.SeedAsync()`), and the pilot seeder is invoked at line 115 immediately afterward.
- **Audit traceability**: one `FlagAuditEntry` is written inside the explicit-tenant scope with `ChangeKind.OverrideCreated`, `TenantId == pilotTenantId`, `NewIsEnabled == true`, `PreviousIsEnabled == null`, actor `system:migrator` / `"Migration Service"`, and the same reason as the override (Seeder.cs:102–109).
- **Global default stays OFF**: `SeedEnableActivityGroupsAsync` still creates the flag with `isEnabled: false` (Program.cs:336–342). No global flip, no `appsettings.PilotTenant.json` created (none found in repo).
- **Documentation updated**: `documents/configuration.md` §5 now notes in the runtime-flags table that the global default remains OFF and a tenant override turns the flag ON for `Hydeson School` only, plus a new "Pilot-tenant override (Phase 6.1)" subsection describing mechanism, pilot tenant, traceability, effect, and how to disable it (configuration.md:280–320).
- **Tests implemented**: four focused unit tests cover the happy path, idempotency, missing pilot tenant, and missing flag (SeederTests.cs).
- **Cross-tenant write uses the sanctioned path**: `tenantContextAccessor.RunWithExplicitTenantAsync(pilotTenantId, …)` so the save-guard accepts the strict-tenant insert, matching `UpsertTenantFlagOverrideHandler`.

## Commands run / not run
Originally not run by the reviewer (no shell). The acceptance pass (this orchestrator) subsequently executed all commands below and they passed — see the Acceptance section in `plan-phase6-1.md`.

```bash
cd C:/Users/skwar/source/repos/School-Collab
dotnet build SchoolCollab.sln -c Debug -v q
dotnet test tests/SchoolCollab.Settings.Tests.Unit/SchoolCollab.Settings.Tests.Unit.csproj
dotnet test tests/SchoolCollab.Students.Tests.Unit/SchoolCollab.Students.Tests.Unit.csproj
dotnet test tests/SchoolCollab.Admin.Tests.Unit/SchoolCollab.Admin.Tests.Unit.csproj
```

(Note: omitted `--nologo` as instructed because it hangs on this machine.)

## Per-criterion verdict
| Criterion | Verdict | Notes |
| --- | --- | --- |
| 1. Pilot override seeded ON for `Hydeson School` with correct shape/reason | **Verified** | Seeder.cs:38, 91–100 |
| 2. Idempotent — no duplicate override or audit on re-run | **Verified** | Seeder.cs:74–84 |
| 3. Runs after flag seed and tenant seed | **Verified** | Program.cs:98, 109, 115 |
| 4. Audit traceability with system actor | **Verified** | Seeder.cs:102–109 |
| 5. Global default unchanged / no global flip | **Verified** | Program.cs:336–342; no pilot appsettings created |
| 6. `documents/configuration.md` §5 documents override | **Verified** | configuration.md:280–320 |
| 7. Tests pass and build succeeds | **Partial** | Test code present and correct; build/test execution pending at review time (executed and passed during acceptance) |
| 8. No scope creep | **Verified** | No new migration, no entity change, no new global flag, no pilot appsettings |

## Findings
No code issues found. All implementation details match the plan, existing codebase patterns, and the centralized Settings feature-flag model.

## Residual risks
- Runtime verification (build + tests) had not been executed at review time; resolved during acceptance (build + all three unit test projects green).
- EF Core model validation requires the new `SettingsDbContext` test setup to build successfully; the InMemory + `AddTenancy` pattern is already proven by `MigrationServiceTenancyTests`.

## Final recommendation
**OK with notes — close the round after the parent/supervisor runs the build and test commands above and confirms they pass.** Acceptance pass has since run them: build OK, Settings Unit 446/446, Students 316/316, Admin 477/477 — all green.