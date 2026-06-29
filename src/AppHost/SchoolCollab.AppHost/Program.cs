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

var codedValuesDb = postgres.AddDatabase("coded-values-db");

var redis = builder.AddRedis("cache");

// Per-bounded-context outbox exchange names. Centralised in the AppHost's
// appsettings.json under Parameters:outbox-exchange-* and fanned out to the
// matching API/Worker via WithEnvironment("Outbox__ExchangeName", param), so
// no per-service appsettings.json needs an Outbox section — the value reaches
// every consumer exclusively through the env var Aspire injects at launch.
// See OutboxExtensions.AddOutbox<TContext> for the consumer side.
var codedValuesOutboxExchange = builder.AddParameter("outbox-exchange-coded-values");
var assignmentsOutboxExchange  = builder.AddParameter("outbox-exchange-assignments");
var studentsOutboxExchange     = builder.AddParameter("outbox-exchange-students");

// AI provider configuration that the `coded-values-ai` host reads at startup.
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

// Feature flags. Centralised in the AppHost's appsettings.json under
// Parameters:feature-flag-* and fanned out to the matching API / Admin via
// WithEnvironment("FeatureFlags__FEATURE__...", param). Previously these were
// served by a separate `SchoolCollab.Config` service that proxied a local JSON
// file over HTTP via `AddRemoteFeatureFlags`; the HTTP overlay was removed
// because (a) the proxy was a placeholder, not a real central config service,
// (b) the cost of a synchronous HTTP call at every API/Admin startup wasn't
// justified by a single dev-only flag, and (c) using the same AppHost
// Parameters: pattern as outbox exchanges and AI config gives a single
// mental model for "how a cross-service value gets distributed". New flags
// should follow the same shape: add a `Parameters:feature-flag-<name>` row
// and wire the env-var on each consumer. See documents/configuration.md §2.
var disableOidcAuth = builder.AddParameter("feature-flag-disable-oidc-auth");

// ── Assignments bounded context ──

var assignmentsDb = postgres.AddDatabase("assignments-db");

// ── Students bounded context ──

var studentsDb = postgres.AddDatabase("students-db");

// Unified migration service: runs EF Core migrations for all bounded contexts
// and seeds CodedValues data, then exits. The APIs wait for successful completion
// before starting, ensuring the schema and seed data are ready in all environments.
var migrator = builder.AddProject<Projects.SchoolCollab_MigrationService>("migrator")
    .WithReference(codedValuesDb)
    .WithReference(assignmentsDb)
    .WithReference(studentsDb)
    .WaitFor(codedValuesDb)
    .WaitFor(assignmentsDb)
    .WaitFor(studentsDb);

var codedValuesApi = builder.AddProject<Projects.SchoolCollab_CodedValues_Api>("coded-values-api")
    .WithReference(codedValuesDb)
    .WithReference(rabbit)
    .WithReference(redis)
    .WithEnvironment("Outbox__ExchangeName", codedValuesOutboxExchange)
    .WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", disableOidcAuth)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(migrator);

var codedValuesAi = builder.AddProject<Projects.SchoolCollab_AI>("coded-values-ai")
    .WithReference(codedValuesApi)
    // Env-var names use the double-underscore convention so that ASP.NET
    // Core's EnvironmentVariablesConfigurationProvider maps `__` to `:`
    // when reading them inside `coded-values-ai`. The `:` separator
    // works on Windows but not on Linux/sh, so `__` is the cross-platform
    // safe form here. See documents/configuration.md §11 and the .NET docs
    // (https://learn.microsoft.com/aspnet/core/fundamentals/configuration).
    .WithEnvironment("codedvalue-ai-provider", aiDefaultProvider)
    .WithEnvironment("Ollama__Endpoint", ollamaEndpoint)
    .WithEnvironment("Ollama__DefaultModel", ollamaDefaultModel)
    .WithEnvironment("OpenRouter__Endpoint", openRouterEndpoint)
    .WithEnvironment("OpenRouter__DefaultModel", openRouterDefaultModel)
    .WithEnvironment("OpenRouter__ApiKey", openRouterApiKey)
    .WaitFor(codedValuesApi);

var assignmentsApi = builder.AddProject<Projects.SchoolCollab_Assignments_Api>("assignments-api")
    .WithReference(assignmentsDb)
    .WithReference(rabbit)
    .WithReference(redis)
    .WithEnvironment("Outbox__ExchangeName", assignmentsOutboxExchange)
    .WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", disableOidcAuth)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(migrator);

var studentsApi = builder.AddProject<Projects.SchoolCollab_Students_Api>("students-api")
    .WithReference(studentsDb)
    .WithReference(rabbit)
    .WithReference(redis)
    .WithEnvironment("Outbox__ExchangeName", studentsOutboxExchange)
    .WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", disableOidcAuth)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(migrator);

var studentsWorker = builder.AddProject<Projects.SchoolCollab_Students_Worker>("students-worker")
    .WithReference(studentsDb)
    .WithReference(rabbit)
    .WithEnvironment("Outbox__ExchangeName", studentsOutboxExchange)
    .WaitFor(rabbit)
    .WaitForCompletion(migrator);

// Unified admin host — serves CodedValues, Assignments, and Students Blazor UIs
builder.AddProject<Projects.SchoolCollab_Admin>("admin")
    .WithReference(codedValuesApi)
    .WithReference(codedValuesAi)
    .WithReference(assignmentsApi)
    .WithReference(studentsApi)
    .WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", disableOidcAuth)
    .WaitFor(codedValuesApi)
    .WaitFor(codedValuesAi)
    .WaitFor(assignmentsApi)
    .WaitFor(studentsApi);

builder.Build().Run();
