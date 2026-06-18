using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SchoolCollab.Students.Core;
using SchoolCollab.Students.Worker.Services;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

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