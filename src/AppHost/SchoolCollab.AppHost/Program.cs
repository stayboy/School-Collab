var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var rabbit = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var codedValuesDb = postgres.AddDatabase("coded-values-db");

var codedValuesApi = builder.AddProject<Projects.SchoolCollab_CodedValues_Api>("coded-values-api")
    .WithReference(codedValuesDb)
    .WithReference(rabbit)
    .WaitFor(codedValuesDb)
    .WaitFor(rabbit);

builder.AddProject<Projects.SchoolCollab_CodedValues_Admin>("coded-values-admin")
    .WithReference(codedValuesApi)
    .WaitFor(codedValuesApi);

builder.Build().Run();
