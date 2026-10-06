using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Assignments.Worker.Messaging;
using SchoolCollab.Assignments.Worker.Services;
using SchoolCollab.Core.Http;
using SchoolCollab.Core.Messaging;

var builder = Host.CreateApplicationBuilder(args);

// Re-anchor appsettings.json to the assembly directory rather than the process's
// current working directory (mandatory for Microsoft.NET.Sdk Exe hosts — the same
// reason Students.Worker does this). The csproj has explicit <Content> copy elements
// so the file is on disk next to the assembly in every deployment shape.
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

builder.Services.AddAssignmentsCore(builder.Configuration);

// E3 (ar-19): the relocated HTTP-backed effective-policy resolver + the Students-API
// address resolver, so the sweeps render queue-time messages the same way the Api drain
// does. Both named clients are registered with the documented AddCrossModuleHttpClient
// helper, whose base address is the literal Aspire service name — that literal is what
// Aspire service discovery resolves (AppHost wires the worker → settings-api +
// students-api, per CrossModuleWiringTests).
builder.Services.AddCrossModuleHttpClient("settings-api", "https+http://settings-api", propagateTenant: false);
builder.Services.AddCrossModuleHttpClient("students-api", "https+http://students-api", propagateTenant: false);
builder.Services.AddScoped<INotificationPolicyResolver, NotificationPolicyResolver>();
builder.Services.AddScoped<IContactAddressResolver, StudentsContactAddressResolver>();

// Subscribe to the assignments exchange; the published handler kicks one reminder sweep
// pass immediately after publish (idempotent by construction).
builder.Services.AddScoped<IIntegrationEventHandler<AssignmentPublishedIntegrationEvent>,
    AssignmentPublishedReminderHandler>();
builder.Services.AddRabbitMqSubscriber(
    builder.Configuration,
    typeof(AssignmentPublishedIntegrationEvent));

// E3 sweeps. ReminderSweepService is a singleton so the message handler can kick a pass;
// the hosted registration feeds that same instance into the BackgroundService loop.
builder.Services.AddSingleton<ReminderSweepService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReminderSweepService>());
builder.Services.AddHostedService<OverdueSweepService>();

var host = builder.Build();
host.Run();
