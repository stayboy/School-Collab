using Microsoft.AspNetCore.Mvc;
using Serilog;
using SchoolCollab.Students.Core;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.Commands.AssignGradeSubject;
using SchoolCollab.Students.Core.Commands.AssignStudentSubject;
using SchoolCollab.Students.Core.Commands.CompletePeriod;
using SchoolCollab.Students.Core.Commands.CreateGradeLevel;
using SchoolCollab.Students.Core.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Commands.CreateStudent;
using SchoolCollab.Students.Core.Commands.CreateSubject;
using SchoolCollab.Students.Core.Commands.DeleteStudent;
using SchoolCollab.Students.Core.Commands.EnrollStudent;
using SchoolCollab.Students.Core.Commands.RecoverStudent;
using SchoolCollab.Students.Core.Commands.RemoveGradeSubject;
using SchoolCollab.Students.Core.Commands.RemoveStudentSubject;
using SchoolCollab.Students.Core.Commands.TransferStudent;
using SchoolCollab.Students.Core.Commands.UpdateGradeLevel;
using SchoolCollab.Students.Core.Commands.UpdatePeriod;
using SchoolCollab.Students.Core.Commands.UpdateStudent;
using SchoolCollab.Students.Core.Commands.UpdateSubject;
using SchoolCollab.Students.Core.Commands.WithdrawStudent;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.Queries.GetGradeLevelById;
using SchoolCollab.Students.Core.Queries.GetPeriodById;
using SchoolCollab.Students.Core.Queries.GetStudentById;
using SchoolCollab.Students.Core.Queries.GetStudentByStudentNumber;
using SchoolCollab.Students.Core.Queries.GetSubjectByCode;
using SchoolCollab.Students.Core.Queries.GetSubjectById;
using SchoolCollab.Students.Core.Queries.ListDeletedStudents;
using SchoolCollab.Students.Core.Queries.ListEnrollmentsByPeriod;
using SchoolCollab.Students.Core.Queries.ListEnrollmentsByStudent;
using SchoolCollab.Students.Core.Queries.ListGradeLevels;
using SchoolCollab.Students.Core.Queries.ListGradeSubjectAssignmentsByGradeLevel;
using SchoolCollab.Students.Core.Queries.ListGradeSubjectAssignmentsByPeriod;
using SchoolCollab.Students.Core.Queries.ListPeriods;
using SchoolCollab.Students.Core.Queries.ListStudentSubjectAssignmentsByPeriod;
using SchoolCollab.Students.Core.Queries.ListStudentSubjectAssignmentsByStudent;
using SchoolCollab.Students.Core.Queries.ListStudents;
using SchoolCollab.Students.Core.Queries.ListSubjects;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Api;

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

builder.Services.AddStudentsCore(builder.Configuration);
builder.Services.AddOpenApi();

// Auth + tenancy (OIDC via Keycloak)
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
app.MapStudentEndpoints(featureFlags);

app.Run();

app.Run();

// ── Request record types ─────────────────────────────────────────────────

internal record UpdateStudentRequest(string FirstName, string LastName, DateOnly? DateOfBirth, Guid? GenderCodedValueId, string ContactEmail, string? ContactPhone);
internal record UpdateGradeLevelRequest(int Level, string Name, int DisplayOrder);
internal record UpdateSubjectRequest(string Name, int DisplayOrder);
internal record UpdatePeriodRequest(string Name, DateOnly StartDate, DateOnly EndDate, bool AllowSubjectOverrides);
internal record TransferStudentRequest(Guid NewGradeLevelId, DateOnly? TransferDate);
internal record WithdrawStudentRequest(DateOnly? ExitDate);

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }