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

// ── Assignments bounded context ──

var assignmentsDb = postgres.AddDatabase("assignments-db");

// Unified migration service: runs EF Core migrations for all bounded contexts
// and seeds CodedValues data, then exits. The APIs wait for successful completion
// before starting, ensuring the schema and seed data are ready in all environments.
var migrator = builder.AddProject<Projects.SchoolCollab_MigrationService>("migrator")
    .WithReference(codedValuesDb)
    .WithReference(assignmentsDb)
    .WaitFor(codedValuesDb)
    .WaitFor(assignmentsDb);

var codedValuesApi = builder.AddProject<Projects.SchoolCollab_CodedValues_Api>("coded-values-api")
    .WithReference(codedValuesDb)
    .WithReference(rabbit)
    .WithReference(redis)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(migrator);

var codedValuesAi = builder.AddProject<Projects.SchoolCollab_AI>("coded-values-ai")
    .WithReference(codedValuesApi)
    .WaitFor(codedValuesApi);

var assignmentsApi = builder.AddProject<Projects.SchoolCollab_Assignments_Api>("assignments-api")
    .WithReference(assignmentsDb)
    .WithReference(rabbit)
    .WithReference(redis)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(migrator);

// Unified admin host — serves both CodedValues and Assignments Blazor UIs
builder.AddProject<Projects.SchoolCollab_Admin>("admin")
    .WithReference(codedValuesApi)
    .WithReference(codedValuesAi)
    .WithReference(assignmentsApi)
    .WaitFor(codedValuesApi)
    .WaitFor(codedValuesAi)
    .WaitFor(assignmentsApi);

builder.Build().Run();
