using System.Text.Json.Serialization;
using Serilog;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<AssignmentTypeDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<AssignmentStatusDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<GradingFormatDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<TargetAudienceTypeDto>());
});

var cacheConnectionString = builder.Configuration.GetConnectionString("cache")
    ?? builder.Configuration["Aspire:StackExchange:Redis:ConnectionString"];

if (string.IsNullOrWhiteSpace(cacheConnectionString))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.AddRedisDistributedCache("cache");
}

builder.Services.AddAssignmentsCore(builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();
app.UseSerilogRequestLogging();
app.MapAssignmentEndpoints();

app.Run();

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }