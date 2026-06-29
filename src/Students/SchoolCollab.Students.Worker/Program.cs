using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SchoolCollab.Students.Core;
using SchoolCollab.Students.Worker.Services;
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
builder.Services.Configure<PromotionOptions>(
    builder.Configuration.GetSection(PromotionOptions.SectionName));
builder.Services.AddHostedService<PromotionService>();

var host = builder.Build();
host.Run();