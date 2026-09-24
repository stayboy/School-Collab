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
// (Keycloak bootstrap admin) and `keycloak-client-secret` both carry committed DEV-ONLY
// defaults in appsettings.Development.json (`dev-only-keycloak-admin` and
// `dev-only-school-collab-client-secret` — the latter matching the realm file's dev client
// secret), so a plain `aspire run` starts Keycloak with no prompt. A non-dev value is
// supplied via user-secrets (`Parameters:keycloak-*`) or the Parameters__keycloak_* env
// vars, which override the committed dev literal; production never loads the Development
// file — see documents/configuration.md §2/§4.
// The container enables Keycloak's built-in readiness health signal
// (KC_HEALTH_ENABLED) on the management interface, and a health check is bound to it
// so the four Auth:Keycloak hosts below `.WaitFor(keycloak)` on real readiness — the
// server does not fully start until the realm import completes.
var keycloakClientId    = builder.AddParameter("keycloak-client-id", "school-collab-client");
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
var keycloakClientSecret  = builder.AddParameter("keycloak-client-secret", secret: true);

// Service-account secret for the Keycloak Admin REST client (client `school-collab-auth-admin` in
// the realm import). Same posture as keycloak-client-secret: DEV-ONLY literal in
// appsettings.Development.json, no committed production value (configuration.md §2/§4/§12);
// the auth service (pass 2) receives it as Auth__Keycloak__ServiceAccountClientSecret below.
var keycloakAuthAdminSecret = builder.AddParameter("keycloak-auth-admin-secret", secret: true);

// Realm file ships next to the AppHost dll (copy-to-output in the csproj) so the
// bind mount path is stable regardless of the launch working directory.
var keycloakRealmPath = Path.Combine(AppContext.BaseDirectory, "school-collab-realm.json");

// 26.4+ — official passkeys support (spec §6/§11.5). Newest docker-published 26.4.x as of
// 2026-09-22 is 26.4.7 (26.4.8-16 exist as git tags but have no quay image).
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak:26.4.7")
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

// Round B (keycloak-ui-auth, spec §10 / D4/D5): selects WHICH login UI the browser-facing
// hosts present — OFF (default) = Keycloak's hosted page via the standard OIDC code flow,
// ON = the prefab auth portal's login form. A pure UI toggle: validation and authorization
// are identical in both states. Like FEATURE:DisableOIDCAuth it is a deployment-time startup
// switch (auth schemes are registered once), so it is NOT a Settings/Config-service flag;
// it is fanned out below as FeatureFlags__FEATURE__DisableKeycloakLoginUi to all four
// consumers (admin, families, auth, auth-portal).
var disableKeycloakLoginUi = builder.AddParameter("feature-flag-disable-keycloak-login-ui");

// Round B pass B5b (spec §14, plan-review P1-3): the per-app callback allowlist — the redirect
// targets a one-time handshake code may be minted for. ONE parameter holds the browser-facing
// apps' callbacks (the four Blazor hosts' /signin-handshake route, in both launch-profile
// schemes: admin 5300/7300, families 5400/7400 — the exact spelling B1's challenge handler and
// B4's BuildCallbackUri mint, whose query half is ignored by the matcher) and is fanned to BOTH
// consumers: `Auth:AppCallbackPrefixes` on the auth service (which enforces it at code issuance)
// and `AuthPortal:AppCallbackPrefixes` on the portal (its own defense-in-depth copy, used to
// validate `return_uri` before rendering or redirecting). The portal's own bootstrap redemption
// URI is appended to the AUTH copy only — it is derived from the portal's Aspire endpoint below
// (no hardcoded port) and is never a `return_uri` the portal itself should accept.
// A non-empty value is mandatory: the auth service validates it at startup (ValidateOnStart) and
// the matcher is fail-closed, so a blank allowlist is a startup failure, never a fail-open.
var appCallbackPrefixes = builder.AddParameter(
    "app-callback-prefixes",
    "http://localhost:5300/signin-handshake;https://localhost:7300/signin-handshake"
    + ";http://localhost:5400/signin-handshake;https://localhost:7400/signin-handshake");

// WS-E2 (ar-16): SMTP transport for the MailKit email sender, fanned out onto
// assignments-api as Smtp__Host/Port/User/Password/FromAddress. `smtp-host` blank =
// the log-and-skip NullEmailSender (dev/standalone default; never a failure row).
// `smtp-user` / `smtp-password` carry committed DEV-ONLY defaults in
// appsettings.Development.json (`dev-user` / `dev-only-smtp-password`) so a plain
// `aspire run` does not prompt; MailPit accepts anonymous mail, and a real relay's
// credentials must come from user-secrets (Parameters:smtp-password) or the
// Parameters__smtp_password env var, which override the committed dev literal — the same
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
// The worker's Program.cs calls AddAssignmentsCore, which unconditionally registers the
// shared OutboxDispatcher — so it needs Outbox__ExchangeName as well, or OutboxOptions
// validation fails the host at startup with "ExchangeName must be set in the 'Outbox'
// configuration section" (the worker's own appsettings.json has no Outbox section by
// design — the AppHost is the single fan-out point).
// Mirrors the students-worker shape: waits for rabbit + the migrator and subscribes to
// the assignments exchange. No new Parameters: entries — the existing
// outbox-exchange-assignments is reused for both environment variables.
// It deliberately does NOT receive the smtp-* parameters: the worker only queues
// NotificationLog rows; the drain that sends them — the only component that resolves
// IEmailSender — is the API-side NotificationDispatchSweepService in assignments-api,
// which already carries those settings.
var assignmentsWorker = builder.AddProject<Projects.SchoolCollab_Assignments_Worker>("assignments-worker")
    .WithReference(assignmentsDb)
    .WithReference(rabbit)
    .WithReference(settingsApi)
    .WithReference(studentsApi)
    .WithEnvironment("Outbox__ExchangeName", assignmentsOutboxExchange)
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

// ── Auth service (round A pass 2) ──────────────────────────────────────────────────
// The new SchoolCollab.Auth C# service: Direct-Grant credential exchange, the one-time-code
// handshake, portal-session token custody and the Keycloak Admin REST client (passes 3a-3d).
// Wired with the same WireKeycloakAuth shape as the three APIs — its AuthServiceOptions
// validation (ValidateOnStart) then sees Auth:Keycloak:Authority/ClientId/ClientSecret — plus
// the service-account secret the Admin REST client will use (pass 3b). Round B adds the
// login-UI flag fan-out, the mediated-read references and the portal bootstrap redirect below.
var auth = builder.AddProject<Projects.SchoolCollab_Auth>("auth")
    // D17: the mediated picker reads (tenants / teachers) are HTTP calls the auth service makes
    // on the session's behalf, so both data APIs must be reachable through service discovery.
    .WithReference(settingsApi)
    .WithReference(studentsApi)
    .WithEnvironment("Auth__Keycloak__ServiceAccountClientSecret", keycloakAuthAdminSecret)
    // D5: the auth service reads the login-UI flag at startup (its own AddAuthAndTenancy must
    // know the flag), but it deliberately does NOT receive Auth:Portal:LoginUrl — the
    // browser-facing login URL stays off this host, so the flag-ON challenge here fails closed
    // with 401 instead of ever redirecting a browser (plan-review P2-5).
    .WithEnvironment("FeatureFlags__FEATURE__DisableKeycloakLoginUi", disableKeycloakLoginUi);
WireKeycloakAuth(auth, keycloak, keycloakClientId, keycloakClientSecret);

// ── Auth portal (round B, spec §12 / D1) ──────────────────────────────────────────
// The prefab login/logout/challenge UI + the user/role admin UI as a Python app (uv +
// FastAPI + Prefab UI), hosted solely by this AppHost and a pure HTTP consumer of the auth
// service — it holds no credential and makes no direct Keycloak or data-API call (D7/AC11).
// Registered like `portals` below, NOT with WireKeycloakAuth: that helper takes a typed
// IResourceBuilder<ProjectResource> and this resource is a Python app.
var authPortal = builder.AddUvicornApp("auth-portal", "..\\..\\SchoolCollab.AuthPortal", "app:app")
    .WithUv()
    // D13 option (ii) / plan-review P1-1: pin the portal's host port so that
    // `authPortal.GetEndpoint("http")` deterministically resolves to `http://localhost:5700`.
    // The realm import carries the post-logout landing URI as a committed LITERAL
    // (`postLogoutRedirectUris: "http://localhost:5700/"`) because Keycloak matches
    // `post_logout_redirect_uri` exactly, and the auth service receives that same URI as
    // `Auth__PostLogoutRedirectUri` from the endpoint expression below — the two can only agree
    // by construction if the port is pinned. An AddUvicornApp resource has no
    // launchSettings.json (unlike the fixed Blazor dev ports 5300/7300/5400/7400, which is why
    // those can be literals in the realm), so the pin lives here.
    // This UPDATEs the toolkit-created `http` endpoint in place — Aspire's WithEndpoint overload
    // updates an existing endpoint of the same name (null means "don't change") — and must never
    // add a second annotation: GetEndpoint("http") is resolved against this same name below
    // (AuthPortal__PublicBaseUrl, Auth__Portal__BootstrapRedirectUrl, Auth__AppCallbackPrefixes,
    // Auth__Portal__LoginUrl ×2). `targetPort` is passed as well (the mailpit precedent at the
    // top of this file): AddUvicornApp launches uvicorn with `--port {endpoint TargetPort}`, so a
    // host-only pin would leave the process bound to a random target port.
    .WithHttpEndpoint(port: 5700, targetPort: 5700, name: "http")
    // The portal's only upstream: WithReference injects the services__auth__http__0 discovery
    // env var the typed client resolves (B2's AuthApiClient).
    .WithReference(auth)
    .WithEnvironment("FeatureFlags__FEATURE__DisableKeycloakLoginUi", disableKeycloakLoginUi)
    .WaitFor(auth);
// The portal's own browser-facing base URL comes from its Aspire endpoint; referencing the
// resource from inside its own chain is why this is a second statement (owner-adjudicated
// pattern: WithEnvironment from the endpoint, mirroring the Keycloak endpoint fan-out).
authPortal = authPortal.WithEnvironment("AuthPortal__PublicBaseUrl", authPortal.GetEndpoint("http"));

// B5b: the portal's copy of the app-callback allowlist. The portal validates `return_uri`
// against it before rendering the form or redirecting (defense-in-depth + UX, B8); the
// load-bearing enforcement stays in the auth service below.
authPortal = authPortal.WithEnvironment("AuthPortal__AppCallbackPrefixes", appCallbackPrefixes);

// D16: on the passkey path the AUTH SERVICE is the OIDC relying party, so it is what 302s the
// browser back to the portal after the WebAuthn ceremony — hence the bootstrap redirect target
// reaches the auth service only (plan-review P2-5). The portal redeems the single-use bootstrap
// code there and holds no token (AC11/AC12).
auth = auth.WithEnvironment("Auth__Portal__BootstrapRedirectUrl", $"{authPortal.GetEndpoint("http")}/bootstrap");

// D13 option (ii): the URI the auth service puts in the `post_logout_redirect_uri` query
// parameter of the end_session URL it hands back to the portal. It is the portal's own landing
// URI, so it is fanned from the portal's endpoint expression — resolved to
// `http://localhost:5700/` by the port pin above — and NOT as a hardcoded literal, while the
// realm's committed `postLogoutRedirectUris` literal must equal it exactly (trailing slash
// included; AppHostRealmImportArchitectureTests cross-checks the literal against the pinned
// port). The auth service is the only consumer: it is what builds the URL, and it validates the
// key at startup.
auth = auth.WithEnvironment("Auth__PostLogoutRedirectUri", $"{authPortal.GetEndpoint("http")}/");

// B5b: the enforced allowlist. The portal's bootstrap redemption URI is appended from the
// portal's endpoint expression (never a hardcoded port) so the D16 bootstrap code — which is
// URI-bound like every other one-time code — satisfies the same allowlist the apps' callbacks do.
// `auth` receives it WITHOUT `Auth:Portal:LoginUrl`, so its flag-ON challenge still fails closed.
auth = auth.WithEnvironment("Auth__AppCallbackPrefixes", $"{appCallbackPrefixes};{authPortal.GetEndpoint("http")}/bootstrap");

// Unified admin host — serves the unified Settings (CodedValues + Config
// Flags), Assignments, and Students Blazor UIs.
builder.AddProject<Projects.SchoolCollab_Admin>("admin")
    .WithReference(settingsApi)
    .WithReference(settingsAi)
    .WithReference(assignmentsApi)
    .WithReference(studentsApi)
    .WithReference(redis)
    // P1-3: the D6 handshake transport. The host's typed redeem client uses the literal
    // `https+http://auth` base address, which CrossModuleWiringTests only accepts when this
    // matching reference exists — without it the handshake dies with "No such host is known".
    .WithReference(auth)
    .WithEnvironment("FeatureFlags__FEATURE__RequireAssignmentApproval", requireAssignmentApproval)
    // D4/D5: the Blazor host's login-UI flag + the portal login URL the flag-ON challenge
    // redirects to. The URL reaches the two browser-facing hosts only (plan-review P2-5).
    .WithEnvironment("FeatureFlags__FEATURE__DisableKeycloakLoginUi", disableKeycloakLoginUi)
    .WithEnvironment("Auth__Portal__LoginUrl", $"{authPortal.GetEndpoint("http")}/login")
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
    // P1-3: same handshake transport as admin above — the literal base address at the call site
    // in Families/Program.cs is matched by this reference (CrossModuleWiringTests).
    .WithReference(auth)
    // D4/D5: same flag + portal login URL fan-out as admin.
    .WithEnvironment("FeatureFlags__FEATURE__DisableKeycloakLoginUi", disableKeycloakLoginUi)
    .WithEnvironment("Auth__Portal__LoginUrl", $"{authPortal.GetEndpoint("http")}/login")
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
