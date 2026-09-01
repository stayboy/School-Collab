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
    .WithReference(settingsApi)     // enroll/grade validation hop to Settings CodedValues API
    .WithReference(rabbit)
    .WithReference(redis)
    .WithEnvironment("Outbox__ExchangeName", studentsOutboxExchange)
    .WithEnvironment("Students__UseLocalCodedValueProjection", useLocalCodedValueProjection)
    .WithEnvironment("Students__PeriodActivationToleranceDays", periodActivationToleranceDays)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(migrator);
// NOTE: every cross-module HttpClient base address in src/** must have a matching
// .WithReference(<resource>) on the calling project here. CrossModuleWiringTests
// (Core.Tests.Unit/Architecture) enforces this — a missing reference surfaces at
// runtime as "No such host is known (<service>:80)".

var assignmentsApi = builder.AddProject<Projects.SchoolCollab_Assignments_Api>("assignments-api")
    .WithReference(assignmentsDb)
    .WithReference(rabbit)
    .WithReference(redis)
    .WithReference(studentsApi)
    .WithReference(settingsApi)
    .WithEnvironment("Outbox__ExchangeName", assignmentsOutboxExchange)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(migrator);

// Activity-group delete-guard hop (Phase 2, FR-6); 404 = "no references".
// Added after assignmentsApi is declared so the studentsApi block above stays in
// declaration order.
studentsApi = studentsApi.WithReference(assignmentsApi);

// FR-H7 (period-hierarchy-terms-semesters.md): the Settings API's academic-year-
// division switch-rejection queries the Students API for the sub-period count.
// Added here (after studentsApi is fully declared) so Settings resolves students-api
// via service discovery. CrossModuleWiringTests enforces this reference.
settingsApi = settingsApi.WithReference(studentsApi);

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
    .WaitFor(settingsApi)
    .WaitFor(settingsAi)
    .WaitFor(assignmentsApi)
    .WaitFor(studentsApi);

builder.Build().Run();
