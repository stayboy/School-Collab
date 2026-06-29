# Feature Flag Workflow

This document outlines how feature flags are added, observed, and retired in
the School-Collab solution.

## Overview

Feature flags are **centralised in the Aspire AppHost's `Parameters:` block**
and fanned out to every consumer via `WithEnvironment("FeatureFlags__FEATURE__...", param)`.
There is no separate config service and no HTTP overlay — consumers read
flags from `IConfiguration` directly, via
`SchoolCollab.Core.Features.FeatureFlagService` (registered by
`AddAuthAndTenancy`).

This is the same pattern used for outbox exchanges and AI provider
configuration; see [`documents/configuration.md`](../configuration.md) §2
and §5 for the canonical reference.

## Adding a new flag

1. **Pick a flag key.** Use the dotted form `FEATURE:<AreaName>` so the
   leading section mirrors the existing `FEATURE:DisableOIDCAuth` shape.
   Keep the colon — `FeatureFlagService.CollectFlags` recurses into nested
   sections, so the dotted key remains a single, lookup-able name.

2. **Add the parameter to the AppHost.** Open
   `src/AppHost/SchoolCollab.AppHost/appsettings.json` and add an entry to
   the `Parameters:` block, e.g.

   ```json
   "Parameters": {
     "feature-flag-disable-oidc-auth": "false",
     "feature-flag-my-new-flag": "false"
   }
   ```

   The value should be `"true"` / `"false"` (strings, not bools — that is how
   the configuration provider surfaces them and how
   `FeatureFlagService.IsEnabled(string)` parses them).

3. **Wire the parameter onto every consumer.** In
   `src/AppHost/SchoolCollab.AppHost/Program.cs`, declare the parameter
   next to the existing `feature-flag-disable-oidc-auth` line and add
   `.WithEnvironment("FeatureFlags__FEATURE__MyNewFlag", param)` to every
   project that reads the flag — APIs and the Admin host.

   ```csharp
   var myNewFlag = builder.AddParameter("feature-flag-my-new-flag");

   // ... and on each consumer project resource:
   .WithEnvironment("FeatureFlags__FEATURE__MyNewFlag", myNewFlag)
   ```

4. **Read the flag in code.** Inject `IFeatureFlagService` and call
   `IsEnabled("FEATURE:MyNewFlag")`:

   ```csharp
   public class MyService(IFeatureFlagService featureFlags)
   {
       public void DoWork()
       {
           if (featureFlags.IsEnabled("FEATURE:MyNewFlag"))
           {
               // New logic
           }
       }
   }
   ```

   For conditional authorization, the established pattern is:

   ```csharp
   var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
   var group = app.MapGroup("/api/my-feature");

   if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
   {
       group.RequireAuthorization();
   }
   ```

5. **Update `documents/configuration.md` in the same PR.** §2 (parameters
   table), §5 (introduced flags table), §11 (env-var reference), and §12
   (production checklist) all reference every flag. The rule is enforced
   by the pre-flight review — see
   [`.github/copilot/rules/configuration-documentation.md`](../../.github/copilot/rules/configuration-documentation.md).

6. **Cover both states in tests.** Use
   `IConfigurationBuilder.UseSetting("FeatureFlags:FEATURE:MyNewFlag", "true")`
   (and `"false"`) to set the flag in a test host. See
   `tests/SchoolCollab.Core.Tests.Unit/Auth/AuthTenancyTests.cs` and
   `tests/SchoolCollab.Admin.Tests.Unit/ProgramAuthFeatureFlagTests.cs` for
   examples; see also
   [`.github/copilot/rules/testing.md`](../../.github/copilot/rules/testing.md)
   §"Auth Bypass Verification" for the why.

## Local development

Override the AppHost parameter via user-secrets (preferred) or env-var:

```bash
cd src/AppHost/SchoolCollab.AppHost
dotnet user-secrets set "Parameters:feature-flag-my-new-flag" "true"
```

…or set the corresponding env-var
`Parameters__feature_flag_my_new_flag=true` in the shell that runs the
AppHost / `dotnet run`. Aspire converts `-` to `_` in parameter names
when forming the env-var, and the ASP.NET Core environment-variable
provider converts `_` back to `:` (so the host sees
`FeatureFlags:FEATURE:MyNewFlag` in `IConfiguration`).

## Retiring a flag

Once the gated behaviour ships unconditionally, retire the flag:

1. **Remove the conditional branch.** Leave the always-active code path
   behind.
2. **Remove the read site.** Delete every `IsEnabled(...)` call on the
   flag; delete the `IFeatureFlagService` dependency if that was its only
   consumer in that file.
3. **Remove the parameter.** Delete the `Parameters:feature-flag-<name>`
   entry from `src/AppHost/SchoolCollab.AppHost/appsettings.json`, the
   `AddParameter(...)` line, and every `WithEnvironment(...)` wiring in
   `Program.cs`.
4. **Remove the test cases.** Delete the tests that exercised the retired
   toggle (typically two test methods: one with the flag `true`, one
   with it `false`).
5. **Update `documents/configuration.md`** in the same PR — remove the
   row from §2, §5, and the production-checklist bullet in §12.

## Why no `SchoolCollab.Config` service?

Earlier revisions routed feature flags through a separate `SchoolCollab.Config`
service via `AddRemoteFeatureFlags`, which fetched `GET /api/features` at
startup. That design was retired — see
[`centralized-feature-flags-implementation.superseded.md`](./centralized-feature-flags-implementation.superseded.md)
for the rationale and the migration history.

## See also

- [`configuration.md`](../configuration.md) §2, §5 — canonical reference.
- [`auth-tenancy-pattern.md`](./auth-tenancy-pattern.md) — where
  `AddAuthAndTenancy` registers `IFeatureFlagService` and consults
  `FEATURE:DisableOIDCAuth`.
- [`.github/copilot/rules/configuration-documentation.md`](../../.github/copilot/rules/configuration-documentation.md)
  — every flag must be documented in `configuration.md` in the same PR.
- [`.github/copilot/rules/testing.md`](../../.github/copilot/rules/testing.md)
  — every flag must have both states tested.
