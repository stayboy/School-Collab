using Microsoft.AspNetCore.Mvc;
using Serilog;
using SchoolCollab.CodedValues.Core;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Commands.CreateCodedValue;
using SchoolCollab.CodedValues.Core.Commands.DisableCodedValue;
using SchoolCollab.CodedValues.Core.Commands.EnableCodedValue;
using SchoolCollab.CodedValues.Core.Commands.DeleteCodedValue;
using SchoolCollab.CodedValues.Core.Commands.RecoverCodedValue;
using SchoolCollab.CodedValues.Core.Commands.RemoveCodedValueAttribute;
using SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttribute;
using SchoolCollab.CodedValues.Core.Commands.RemoveCodedValueAttributeDefinition;
using SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttributeDefinition;
using SchoolCollab.CodedValues.Core.Commands.UpdateCodedValue;
using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.Domain.Exceptions;
using SchoolCollab.CodedValues.Core.DTOs;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValueById;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValueByCode;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByIds;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValuesByParent;
using SchoolCollab.CodedValues.Core.Queries.SearchCodedValues;
using SchoolCollab.CodedValues.Core.Queries.ListRootCodedValues;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.CodedValues.Api;
using SchoolCollab.CodedValues.Api.Infrastructure.Auth;
using SchoolCollab.CodedValues.Api.Infrastructure.Data;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddRemoteFeatureFlags("https+http://config");

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

builder.Services.AddCodedValuesCore(builder.Configuration);
builder.Services.AddOpenApi();

// Tenancy & Auth Infrastructure
builder.Services.AddAuthAndTenancy(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.UseSerilogRequestLogging();

var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
app.MapCodedValueEndpoints(featureFlags);

app.Run();

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }