using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Pin the postgres superuser password so it stays stable across AppHost
// sessions. Without this, Aspire generates a fresh password on every run and
// the persisted data volume ends up with a role password that no longer
// matches the one injected into the new container, which produces
// "password authentication failed for user \"postgres\"" on every connect.
var pgPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres", password: pgPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin();

// Pin the rabbitmq default user password so it stays stable across AppHost
// sessions. Same rationale as the postgres password: Aspire regenerates
// RABBITMQ_DEFAULT_PASS on every run, but the persisted data volume keeps
// the `guest` user from the previous run, so the env-var-based bootstrap is
// silently skipped and every PLAIN login fails with "invalid credentials".
var rabbitPassword = builder.AddParameter("rabbitmq-password", secret: true);

var rabbit = builder.AddRabbitMQ("rabbitmq", password: rabbitPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithManagementPlugin();

// Settings bounded-context database (replaces the legacy coded-values-db and
// config-db). The Settings bounded context owns both CodedValues and
// FeatureFlag aggregates — see documents/solution/settings-context-merge-spec.md.
var settingsDb = postgres.AddDatabase("settings-db");

var redis = builder.AddRedis("cache");

// WS-E2 (ar-16): MailPit dev mail server — SMTP on 1025, web inbox UI on 8025.
// assignments-api reaches it through the smtp-* parameters below (NOT through
// Aspire's connection-string injection) so the same `Smtp:*` configuration path is
// exercised in dev as in a real SMTP deployment.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit")
    .WithEndpoint(port: 1025, targetPort: 1025, name: "smtp")
    .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "http");

// ── Keycloak dev IdP (ar-20 / ar-21) ──────────────────────────────────────────────
// Ships a Keycloak dev container with the committed school-collab-realm.json import
// (realm `school-collab`, direct-access-grants client school-collab-client, seeded
// dev-teacher user + tenant/teacher protocol mappers). The realm import is
// DEV-ONLY — CREDENTIALS IN THE FILE ARE FOR LOCAL/DEV USE ONLY:
//   * dev-teacher login    : dev-teacher / dev-only-password
//   * school-collab-client : secret dev-only-school-collab-client-secret
// Neither value may ever be reused in a real (production/demo) realm. The tenant_id /
// teacher_id protocol-mapper values pin the FIXED ids seeded by the MigrationService
// DevIdentitySeeder (Dev School = ...0002, Dev Teacher = ...0003), so a dev login
// resolves to real backing rows on first boot. Secrets: `keycloak-admin-password`
// (Keycloak bootstrap admin) gets NO committed default (operator supplies via
// Parameters__keycloak_admin_password / user-secrets). `keycloak-client-secret` has a
// DEV-ONLY value matching the realm file's dev client secret supplied via
// user-secrets/Parameters — see documents/configuration.md §2/§4.
// The container enables Keycloak's built-in readiness health signal
// (KC_HEALTH_ENABLED) on the management interface, and a health check is bound to it
// so the four Auth:Keycloak hosts below `.WaitFor(keycloak)` on real readiness — the
// server does not fully start until the realm import completes.
var keycloakClientId    = builder.AddParameter("keycloak-client-id", "school-collab-client");
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
var keycloakClientSecret  = builder.AddParameter("keycloak-client-secret", secret: true);

// Realm file ships next to the AppHost dll (copy-to-output in the csproj) so the
// bind mount path is stable regardless of the launch working directory.
var keycloakRealmPath = Path.Combine(AppContext.BaseDirectory, "school-collab-realm.json");

var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak:26.2")
    .WithArgs("start-dev", "--import-realm")
    // No keycloak data volume is declared, so the realm file re-imports on every
    // container recreate (the dev-correct behaviour — edit the realm and recreate the
    // container to pick it up; no `docker volume rm` is needed).
    .WithBindMount(keycloakRealmPath, "/opt/keycloak/data/import/school-collab-realm.json")
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
    // Management endpoint (targetPort 9000) carries the readiness health check;
    // no host port is pinned — Aspire assigns an ephemeral host port (proxy-exposed).
    .WithHttpEndpoint(targetPort: 9000, name: "management")
    .WithHttpHealthCheck(path: "/health/ready", endpointName: "management")
    // Main HTTP endpoint: explicit targetPort 8080, host port deliberately left to
    // Aspire's ephemeral assignment (the Authority is an endpoint-reference expression,
    // so no stable host port is needed).
    .WithHttpEndpoint(targetPort: 8080, name: "http");

// Fan the Auth:Keycloak settings onto a consuming host (assignments-api, students-api,
// settings-api, admin). The Authority is the Keycloak HTTP endpoint-reference expression
// (Aspire resolves it at runtime); ClientId/ClientSecret come from the AppHost parameters.
// No named-client service discovery is wired (the hosts read Auth:Keycloak:* directly).
// Each consuming host `.WaitFor(keycloak)`s so it starts only after the container's
// readiness health check is healthy (Keycloak does not fully start until the realm
// import completes — see the container block above).
static void WireKeycloakAuth(
    IResourceBuilder<ProjectResource> host, IResourceBuilder<ContainerResource> keycloak,
    IResourceBuilder<ParameterResource> clientId, IResourceBuilder<ParameterResource> clientSecret)
    => host
        .WithEnvironment("Auth__Keycloak__Authority", $"{keycloak.GetEndpoint("http")}/realms/school-collab")
        .WithEnvironment("Auth__Keycloak__ClientId", clientId)
        .WithEnvironment("Auth__Keycloak__ClientSecret", clientSecret)
        .WaitFor(keycloak);

// Per-bounded-context outbox exchange names. Centralised in the AppHost's
// appsettings.json under Parameters:outbox-exchange-* and fanned out to the
// matching API/Worker via WithEnvironment("Outbox__ExchangeName", param), so
// no per-service appsettings.json needs an Outbox section — the value reaches
// every consumer exclusively through the env var Aspire injects at launch.
// See OutboxExtensions.AddOutbox<TContext> for the consumer side.
var settingsOutboxExchange  = builder.AddParameter("outbox-exchange-settings");
var assignmentsOutboxExchange  = builder.AddParameter("outbox-exchange-assignments");
var studentsOutboxExchange     = builder.AddParameter("outbox-exchange-students");

// Students coded-value projection flag (adr-cross-module-calls.md Phase 1): off by
// default — the projection warms behind the flag (Worker backfill + consumer populate
// local_coded_values), then an operator flips this to "true" to route enroll reads
// through the local read model. Flipping is a one-config change here; both the API
// and the Worker receive the same value so backfill (off when flag on) and reads stay
// consistent. Warm-then-flip rollout: run with false → verify rows → set true.
var useLocalCodedValueProjection = builder.AddParameter("use-local-coded-value-projection");

// Period activation-window tolerance (period-activation-window-auto-activation.md FR-W2):
// default number of days a period may be activated before its StartDate or after its
// EndDate. Fanned out to students-api and students-worker as
// Students__PeriodActivationToleranceDays (read as Students:PeriodActivationToleranceDays).
var periodActivationToleranceDays = builder.AddParameter("period-activation-tolerance-days");

// WS-A1 / FR-210: assignments file-store + upload configuration. Defaults are the
// production caps documented in documents/configuration.md §2/§13 — keep them
// in lockstep with the AttachmentUploadOptions defaults in
// src/Assignments/SchoolCollab.Assignments.Core/Services/AttachmentUploadOptions.cs.
// Fanned out onto assignments-api as Assignments__FileStore__RootPath,
// Assignments__AttachmentUpload__MaxFileSizeBytes,
// Assignments__AttachmentUpload__MaxTotalSizeBytes, and
// Assignments__AttachmentUpload__AllowedExtensions (comma-separated; the config
// binder splits it into the AllowedExtensions array).
var assignmentFileStoreRoot       = builder.AddParameter("assignment-file-store-root");
var assignmentUploadMaxFileBytes  = builder.AddParameter("assignment-upload-max-file-bytes");
var assignmentUploadMaxTotalBytes = builder.AddParameter("assignment-upload-max-total-bytes");
var assignmentUploadAllowedExt    = builder.AddParameter("assignment-upload-allowed-extensions");

// WS-A2 / spec §7 Q2: gates the assignment approval workflow. Default OFF
// (the flag ships dark — tenants opt in via /config-flags). The
// parameter is the IConfiguration cold-start value; the Settings
// Config-service row is the runtime authority (tenant-overridable).
var requireAssignmentApproval = builder.AddParameter("feature-flag-require-assignment-approval");

// WS-E2 (ar-16): SMTP transport for the MailKit email sender, fanned out onto
// assignments-api as Smtp__Host/Port/User/Password/FromAddress. `smtp-host` blank =
// the log-and-skip NullEmailSender (dev/standalone default; never a failure row).
// `smtp-user` / `smtp-password` have NO committed default: MailPit accepts anonymous
// mail, and a real relay's credentials come from user-secrets
// (Parameters:smtp-password) or the Parameters__smtp_password env var — the same
// posture as Parameters:openrouter-api-key. See documents/configuration.md §2/§11.
var smtpHost        = builder.AddParameter("smtp-host");
var smtpPort        = builder.AddParameter("smtp-port");
var smtpUser        = builder.AddParameter("smtp-user");
var smtpPassword    = builder.AddParameter("smtp-password", secret: true);
var smtpFromAddress = builder.AddParameter("smtp-from-address");

// AI provider configuration that the `settings-ai` host reads at startup.
// Centralised here so an operator (or another developer on first clone) can
// see every knob they may need to set in exactly one place — the AppHost's
// appsettings.json under Parameters:. The values are fanned out as the exact
// env-var names the AI host already binds (`Ollama:Endpoint`, etc.), so the
// AI host itself no longer carries an Ollama/OpenRouter/codedvalue-ai-provider
// section in its own appsettings.json. The OpenRouter API key is a secret
// parameter — not committed here — supplied via the AppHost's user-secrets
// store as `Parameters:openrouter-api-key` or env-var
// `Parameters__openrouter_api_key`. See documents/configuration.md §2.
var aiDefaultProvider       = builder.AddParameter("ai-default-provider");
var ollamaEndpoint          = builder.AddParameter("ollama-endpoint");
var ollamaDefaultModel      = builder.AddParameter("ollama-default-model");
var openRouterEndpoint      = builder.AddParameter("openrouter-endpoint");
var openRouterDefaultModel  = builder.AddParameter("openrouter-default-model");
var openRouterApiKey        = builder.AddParameter("openrouter-api-key", secret: true);

// Feature flags used to be a per-service env-var fanned out from here. Runtime,
// mutable, tenant-overridable flags are now owned by the central Settings
// FeatureFlag aggregate (see documents/solution/settings-context-merge-spec.md).
// The one flag that remains deployment-time — FEATURE:DisableOIDCAuth, a startup
// auth-mode switch — is read by each consumer from its own appsettings.json
// (dev default "true") or the FeatureFlags__FEATURE__DisableOIDCAuth env var in
// production. It is NOT managed as a Config flag because ASP.NET Core auth
// schemes are registered once at startup and cannot be flipped at runtime.

// ── Assignments bounded context ──

var assignmentsDb = postgres.AddDatabase("assignments-db");

// ── Students bounded context ──

var studentsDb = postgres.AddDatabase("students-db");

// Unified migration service: runs EF Core migrations for all bounded contexts
// and seeds Settings data, then exits. The APIs wait for successful completion
// before starting, ensuring the schema and seed data are ready in all environments.
var migrator = builder.AddProject<Projects.SchoolCollab_MigrationService>("migrator")
    .WithReference(settingsDb)
    .WithReference(assignmentsDb)
    .WithReference(studentsDb)
    .WaitFor(settingsDb)
    .WaitFor(assignmentsDb)
    .WaitFor(studentsDb);

// Unified Settings API: exposes /api/coded-values/* (CodedValues aggregate) and
// /api/config/* + /api/features/* (FeatureFlag aggregate) in a single host
// backed by SettingsDbContext and settings-db. Replaces coded-values-api and
// config-api. See spec §8.
var settingsApi = builder.AddProject<Projects.SchoolCollab_Settings_Api>("settings-api")
    .WithReference(settingsDb)
    .WithReference(rabbit)
    .WithReference(redis)
    .WithEnvironment("Outbox__ExchangeName", settingsOutboxExchange)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(migrator);
// ar-20: real bearer/OIDC auth from the Keycloak dev container.
WireKeycloakAuth(settingsApi, keycloak, keycloakClientId, keycloakClientSecret);

// Defensive: migrator references SchoolCollab.Settings.Core which exposes
// AddConfigFeatureFlagClient (URL "https+http://settings-api"). Migrator does
// not currently call that client, but CrossModuleWiringTests requires any
// direct consumer of a cross-module-registering library to have the wiring.
// Added here (after settingsApi is declared) to keep the migrator block readable.
migrator = migrator.WithReference(settingsApi);

var settingsAi = builder.AddProject<Projects.SchoolCollab_AI_Server>("settings-ai")
    .WithReference(settingsApi)
    // Env-var names use the double-underscore convention so that ASP.NET
    // Core's EnvironmentVariablesConfigurationProvider maps `__` to `:`
    // when reading them inside `settings-ai`. The `:` separator
    // works on Windows but not on Linux/sh, so `__` is the cross-platform
    // safe form here. See documents/configuration.md §11 and the .NET docs
    // (https://learn.microsoft.com/aspnet/core/fundamentals/configuration).
    .WithEnvironment("codedvalue-ai-provider", aiDefaultProvider)
    .WithEnvironment("Ollama__Endpoint", ollamaEndpoint)
    .WithEnvironment("Ollama__DefaultModel", ollamaDefaultModel)
    .WithEnvironment("OpenRouter__Endpoint", openRouterEndpoint)
    .WithEnvironment("OpenRouter__DefaultModel", openRouterDefaultModel)
    .WithEnvironment("OpenRouter__ApiKey", openRouterApiKey)
    .WaitFor(settingsApi);

var studentsApi = builder.AddProject<Projects.SchoolCollab_Students_Api>("students-api")
    .WithReference(studentsDb)
    // AddSettingsCore (IEntityCodeGenerator -> EntityCodeRule rows) resolves
    // ConnectionStrings:settings-db. Without this reference Settings.Core/Extensions.cs
    // silently falls back to Host=localhost, Port=5432 and the Settings outbox
    // dispatcher registered by AddSettingsCore fails on every retry.
    .WithReference(settingsDb)
    .WithReference(settingsApi)     // enroll/grade validation hop to Settings CodedValues API
    .WithReference(rabbit)
    .WithReference(redis)
    .WithEnvironment("Outbox__ExchangeName", studentsOutboxExchange)
    .WithEnvironment("Students__UseLocalCodedValueProjection", useLocalCodedValueProjection)
    .WithEnvironment("Students__PeriodActivationToleranceDays", periodActivationToleranceDays)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(migrator);
// ar-20: real auth wiring for students-api (its endpoint-group bearer posture is a
// cheap follow-up per decision 3; the shared AddAuthAndTenancy registration applies here).
WireKeycloakAuth(studentsApi, keycloak, keycloakClientId, keycloakClientSecret);
// NOTE: every cross-module HttpClient base address in src/** must have a matching
// .WithReference(<resource>) on the calling project here. CrossModuleWiringTests
// (Core.Tests.Unit/Architecture) enforces this — a missing reference surfaces at
// runtime as "No such host is known (<service>:80)".

var assignmentsApi = builder.AddProject<Projects.SchoolCollab_Assignments_Api>("assignments-api")
    .WithReference(assignmentsDb)
    // Same AddSettingsCore requirement as students-api above (IEntityCodeGenerator for
    // auto-generated assignment codes) — see the AppHostSettingsDbWiringArchitectureTests guard.
    .WithReference(settingsDb)
    .WithReference(rabbit)
    .WithReference(redis)
    .WithReference(studentsApi)
    .WithReference(settingsApi)
    .WithEnvironment("Outbox__ExchangeName", assignmentsOutboxExchange)
    .WithEnvironment("Assignments__FileStore__RootPath", assignmentFileStoreRoot)
    .WithEnvironment("Assignments__AttachmentUpload__MaxFileSizeBytes", assignmentUploadMaxFileBytes)
    .WithEnvironment("Assignments__AttachmentUpload__MaxTotalSizeBytes", assignmentUploadMaxTotalBytes)
    .WithEnvironment("Assignments__AttachmentUpload__AllowedExtensions", assignmentUploadAllowedExt)
    .WithEnvironment("FeatureFlags__FEATURE__RequireAssignmentApproval", requireAssignmentApproval)
    .WithEnvironment("Smtp__Host", smtpHost)
    .WithEnvironment("Smtp__Port", smtpPort)
    .WithEnvironment("Smtp__User", smtpUser)
    .WithEnvironment("Smtp__Password", smtpPassword)
    .WithEnvironment("Smtp__FromAddress", smtpFromAddress)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(migrator);
// ar-20: wire the assignment endpoint groups to the Bearer scheme in the OIDC branch.
WireKeycloakAuth(assignmentsApi, keycloak, keycloakClientId, keycloakClientSecret);

// Activity-group delete-guard hop (Phase 2, FR-6); 404 = "no references".
// Added after assignmentsApi is declared so the studentsApi block above stays in
// declaration order.
studentsApi = studentsApi.WithReference(assignmentsApi);

// FR-H7 (period-hierarchy-terms-semesters.md): the Settings API's academic-year-
// division switch-rejection queries the Students API for the sub-period count.
// Added here (after studentsApi is fully declared) so Settings resolves students-api
// via service discovery. CrossModuleWiringTests enforces this reference.
settingsApi = settingsApi.WithReference(studentsApi);

// E3 (ar-19): Assignments.Worker — reminder / completion-to-guardian / overdue sweeps
// that queue NotificationLog rows (the Assignments.Api drain sends them), plus the
// AssignmentPublishedIntegrationEvent subscription that kicks the reminder sweep.
// Mirrors the students-worker shape: waits for rabbit + the migrator and subscribes to
// the assignments exchange. No new Parameters: entries — the existing
// outbox-exchange-assignments is reused.
var assignmentsWorker = builder.AddProject<Projects.SchoolCollab_Assignments_Worker>("assignments-worker")
    .WithReference(assignmentsDb)
    .WithReference(rabbit)
    .WithReference(settingsApi)
    .WithReference(studentsApi)
    .WithEnvironment("RabbitMq__Subscriber__ExchangeName", assignmentsOutboxExchange)
    .WaitFor(rabbit)
    .WaitForCompletion(migrator);

var studentsWorker = builder.AddProject<Projects.SchoolCollab_Students_Worker>("students-worker")
    .WithReference(studentsDb)
    .WithReference(rabbit)
    .WithReference(settingsApi) // coded-value backfill hop (adr-cross-module-calls.md Phase 1)
    .WithEnvironment("Outbox__ExchangeName", studentsOutboxExchange)
    // Coded-value projection consumer reads from the Settings exchange
    // (adr-cross-module-calls.md Phase 1).
    .WithEnvironment("RabbitMq__Subscriber__ExchangeName", settingsOutboxExchange)
    .WithEnvironment("Students__UseLocalCodedValueProjection", useLocalCodedValueProjection)
    .WithEnvironment("Students__PeriodActivationToleranceDays", periodActivationToleranceDays)
    .WaitFor(rabbit)
    .WaitForCompletion(migrator);

// Unified admin host — serves the unified Settings (CodedValues + Config
// Flags), Assignments, and Students Blazor UIs.
builder.AddProject<Projects.SchoolCollab_Admin>("admin")
    .WithReference(settingsApi)
    .WithReference(settingsAi)
    .WithReference(assignmentsApi)
    .WithReference(studentsApi)
    .WithReference(redis)
    .WithEnvironment("FeatureFlags__FEATURE__RequireAssignmentApproval", requireAssignmentApproval)
    .WaitFor(settingsApi)
    .WaitFor(settingsAi)
    .WaitFor(assignmentsApi)
    .WaitFor(studentsApi)
    // ar-20: the Admin Blazor shell's existing OIDC code-flow (unchanged) now has real
    // Keycloak wiring via the dev container; its UI/auth code is untouched this round.
    // ar-21: admin is gated on keycloak readiness like the three APIs above (it also
    // receives Auth:Keycloak:*).
    .WithEnvironment("Auth__Keycloak__Authority", $"{keycloak.GetEndpoint("http")}/realms/school-collab")
    .WithEnvironment("Auth__Keycloak__ClientId", keycloakClientId)
    .WithEnvironment("Auth__Keycloak__ClientSecret", keycloakClientSecret)
    .WaitFor(keycloak);

// F1 (slice 2b) — the Families ward/guardian surface app (owner decision:
// Option B, a separate host rather than routes on Admin). Depends on the
// Assignments API for its ward-facing endpoints. Its auth-mode switch
// (FEATURE:DisableOIDCAuth) is read from its own appsettings.json (dev default
// "true" = TestAuth) — no AppHost flag param needed, mirroring the Admin note.
builder.AddProject<Projects.SchoolCollab_Families>("families")
    .WithReference(assignmentsApi)
    // WS-E1 (ar-14-deep-links): the Families host resolves the runtime
    // FEATURE:EnableDeepLinks flag via AddConfigFeatureFlagClient (Settings aggregate),
    // so it needs the settings-api reference for service discovery.
    .WithReference(settingsApi)
    .WithReference(redis)
    .WaitFor(assignmentsApi);

// ar-23 / prefab plan Phase-0 spike (documents/specs/teachers-ward-portal-prefab-plan.md):
// the ward portal as a Python app (uv + FastAPI + Prefab UI), hosted solely by this
// AppHost (plan Q5) and a pure HTTP consumer of assignments-api — zero backend change
// (plan Q3). Spike only: the owner re-decides the MVP go/no-go before further work.
builder.AddUvicornApp("portals", "..\\..\\SchoolCollab.Portals", "app:app")
    .WithUv()
    .WithReference(assignmentsApi)
    .WaitFor(assignmentsApi);

builder.Build().Run();
