using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Students.Core;
using SchoolCollab.Students.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Re-anchor appsettings.json to the assembly directory rather than the
// process's current working directory. Without this, `dotnet run` from any
// directory other than the project resolves the content root to that
// unrelated directory; Microsoft.NET.Sdk does NOT auto-copy appsettings.json
// to a bin/ output that lives next to the assembly, and the missing
// ExchangeName surfaces as a hard-to-diagnose
// "ExchangeName must be set in the 'Outbox' configuration section."
// exception at startup.
// AppContext.BaseDirectory is the directory where the running assembly lives
// (bin/Debug/net10.0/ in dev, the published output in production). The csproj
// also has an explicit <Content> element so the file is on disk there in
// every deployment shape (dotnet run, dotnet exec, self-contained publish,
// Aspire-launched child process, container image).
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
    optional: false,
    reloadOnChange: false);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, $"appsettings.{builder.Environment.EnvironmentName}.json"),
    optional: true,
    reloadOnChange: false);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");

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

builder.Services.AddStudentsCore(builder.Configuration);

// Backfill HTTP client for the one permitted startup reference-data hop
// (adr-cross-module-calls.md Phase 1 step 4).
builder.Services.AddHttpClient("settings-api", client =>
{
    client.BaseAddress = new Uri("http://settings-api");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Coded-value projection consumer: subscribe to the Settings module's
// exchange for the six coded-value events (adr-cross-module-calls.md Phase 1).
builder.Services.AddRabbitMqSubscriber(
    builder.Configuration,
    typeof(CodedValueCreated),
    typeof(CodedValueUpdated),
    typeof(CodedValueDisabled),
    typeof(CodedValueEnabled),
    typeof(CodedValueDeleted),
    typeof(CodedValueOverrideUpserted),
    typeof(CodedValueOverrideRemoved));

// One-time projection backfill (no-op while the flag is off).
builder.Services.AddHostedService<CodedValueBackfillService>();

// Scheduled DateRange rollover sweep (spec activity-group-enrollment.md FR-54).
// Only acts on DateRange groups (which exist only under FEATURE:EnableActivityGroups,
// off by default), so it is a no-op while the feature is dark.
builder.Services.AddHostedService<ActivityGroupRolloverService>();

var host = builder.Build();
host.Run();