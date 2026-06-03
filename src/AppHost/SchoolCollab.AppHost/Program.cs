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

var rabbit = builder.AddRabbitMQ("rabbitmq")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithManagementPlugin();

var codedValuesDb = postgres.AddDatabase("coded-values-db");

// Dedicated migration service: runs EF Core migrations + CSV seeding then exits.
// The API waits for successful completion before starting, ensuring the schema
// and seed data are ready in all environments without human intervention.
var codedValuesMigrator = builder.AddProject<Projects.SchoolCollab_CodedValues_MigrationService>("coded-values-migrator")
    .WithReference(codedValuesDb)
    .WaitFor(codedValuesDb);

var redis = builder.AddRedis("cache");

var codedValuesApi = builder.AddProject<Projects.SchoolCollab_CodedValues_Api>("coded-values-api")
    .WithReference(codedValuesDb)
    .WithReference(rabbit)
    .WithReference(redis)
    .WaitFor(rabbit)
    .WaitFor(redis)
    .WaitForCompletion(codedValuesMigrator);

builder.AddProject<Projects.SchoolCollab_CodedValues_Admin>("coded-values-admin")
    .WithReference(codedValuesApi)
    .WaitFor(codedValuesApi);

builder.Build().Run();
