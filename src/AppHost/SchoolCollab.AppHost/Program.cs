var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var rabbit = builder.AddRabbitMQ("rabbitmq")
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
