using Microsoft.AspNetCore.Mvc;
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
using SchoolCollab.Core.Features;

namespace SchoolCollab.Students.Api;

public static class StudentEndpoints
{
    public static WebApplication MapStudentEndpoints(this WebApplication app, IFeatureFlagService featureFlags)
    {
        var studentsGroup = app.MapGroup("/students");
        if (!featureFlags.IsEnabled("FEATURE:DisableOIDCAuth"))
        {
            studentsGroup.RequireAuthorization();
        }

        // ── Students ──────────────────────────────────────────────────────────────

        studentsGroup.MapGet("/", async (
            [FromServices] IQueryHandler<ListStudents, StudentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListStudents(), ct)));

        studentsGroup.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] IQueryHandler<GetStudentById, StudentDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetStudentById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        studentsGroup.MapGet("/by-number/{studentNumber}", async (
            string studentNumber,
            [FromServices] IQueryHandler<GetStudentByStudentNumber, StudentDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetStudentByStudentNumber(studentNumber), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        studentsGroup.MapGet("/deleted", async (
            [FromServices] IQueryHandler<ListDeletedStudents, StudentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListDeletedStudents(), ct)));

        studentsGroup.MapPost("/", async (
            [FromBody] CreateStudent command,
            [FromServices] ICommandHandler<CreateStudent, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/students/{id}", new { id });
            }
            catch (DuplicateStudentNumberException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        studentsGroup.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateStudentRequest req,
            [FromServices] ICommandHandler<UpdateStudent> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateStudent(id, req.FirstName, req.LastName,
                    req.DateOfBirth, req.GenderCodedValueId, req.ContactEmail, req.ContactPhone), ct);
                return Results.NoContent();
            }
            catch (StudentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        studentsGroup.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] ICommandHandler<DeleteStudent> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteStudent(id), ct);
                return Results.NoContent();
            }
            catch (StudentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        studentsGroup.MapPost("/{id:guid}/recover", async (
            Guid id,
            [FromServices] ICommandHandler<RecoverStudent> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RecoverStudent(id), ct);
                return Results.NoContent();
            }
            catch (StudentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        // ── Grade Levels ──────────────────────────────────────────────────────────

        studentsGroup.MapGet("/grade-levels", async (
            [FromServices] IQueryHandler<ListGradeLevels, GradeLevelDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGradeLevels(), ct)));

        studentsGroup.MapGet("/grade-levels/{id:guid}", async (
            Guid id,
            [FromServices] IQueryHandler<GetGradeLevelById, GradeLevelDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetGradeLevelById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        studentsGroup.MapPost("/grade-levels", async (
            [FromBody] CreateGradeLevel command,
            [FromServices] ICommandHandler<CreateGradeLevel, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/grade-levels/{id}", new { id });
        });

        studentsGroup.MapPut("/grade-levels/{id:guid}", async (
            Guid id,
            [FromBody] UpdateGradeLevelRequest req,
            [FromServices] ICommandHandler<UpdateGradeLevel> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateGradeLevel(id, req.Level, req.Name, req.DisplayOrder), ct);
                return Results.NoContent();
            }
            catch (GradeLevelNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        // ── Subjects ──────────────────────────────────────────────────────────────

        studentsGroup.MapGet("/subjects", async (
            [FromServices] IQueryHandler<ListSubjects, SubjectDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListSubjects(), ct)));

        studentsGroup.MapGet("/subjects/{id:guid}", async (
            Guid id,
            [FromServices] IQueryHandler<GetSubjectById, SubjectDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetSubjectById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        studentsGroup.MapGet("/subjects/by-code/{code}", async (
            string code,
            [FromServices] IQueryHandler<GetSubjectByCode, SubjectDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetSubjectByCode(code), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        studentsGroup.MapPost("/subjects", async (
            [FromBody] CreateSubject command,
            [FromServices] ICommandHandler<CreateSubject, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/subjects/{id}", new { id });
            }
            catch (DuplicateSubjectCodeException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        studentsGroup.MapPut("/subjects/{id:guid}", async (
            Guid id,
            [FromBody] UpdateSubjectRequest req,
            [FromServices] ICommandHandler<UpdateSubject> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateSubject(id, req.Name, req.DisplayOrder), ct);
                return Results.NoContent();
            }
            catch (SubjectNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        // ── Periods ───────────────────────────────────────────────────────────────

        studentsGroup.MapGet("/periods", async (
            [FromServices] IQueryHandler<ListPeriods, PeriodDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListPeriods(), ct)));

        studentsGroup.MapGet("/periods/{id:guid}", async (
            Guid id,
            [FromServices] IQueryHandler<GetPeriodById, PeriodDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetPeriodById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        studentsGroup.MapPost("/periods", async (
            [FromBody] CreatePeriod command,
            [FromServices] ICommandHandler<CreatePeriod, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/periods/{id}", new { id });
        });

        studentsGroup.MapPut("/periods/{id:guid}", async (
            Guid id,
            [FromBody] UpdatePeriodRequest req,
            [FromServices] ICommandHandler<UpdatePeriod> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdatePeriod(id, req.Name, req.StartDate,
                    req.EndDate, req.AllowSubjectOverrides), ct);
                return Results.NoContent();
            }
            catch (PeriodNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        studentsGroup.MapPost("/periods/{id:guid}/activate", async (
            Guid id,
            [FromServices] ICommandHandler<ActivatePeriod> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ActivatePeriod(id), ct);
                return Results.NoContent();
            }
            catch (PeriodNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        studentsGroup.MapPost("/periods/{id:guid}/complete", async (
            Guid id,
            [FromServices] ICommandHandler<CompletePeriod> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new CompletePeriod(id), ct);
                return Results.NoContent();
            }
            catch (PeriodNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        // ── Enrollments ───────────────────────────────────────────────────────────

        studentsGroup.MapGet("/enrollments/by-student/{studentId:guid}", async (
            Guid studentId,
            [FromServices] IQueryHandler<ListEnrollmentsByStudent, StudentEnrollmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListEnrollmentsByStudent(studentId), ct)));

        studentsGroup.MapGet("/enrollments/by-period/{periodId:guid}", async (
            Guid periodId,
            [FromServices] IQueryHandler<ListEnrollmentsByPeriod, StudentEnrollmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListEnrollmentsByPeriod(periodId), ct)));

        studentsGroup.MapPost("/enrollments", async (
            [FromBody] EnrollStudent command,
            [FromServices] ICommandHandler<EnrollStudent, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/enrollments/{id}", new { id });
            }
            catch (StudentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        studentsGroup.MapPost("/enrollments/{id:guid}/transfer", async (
            Guid id,
            [FromBody] TransferStudentRequest req,
            [FromServices] ICommandHandler<TransferStudent> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new TransferStudent(id, req.NewGradeLevelId, req.TransferDate), ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        studentsGroup.MapPost("/enrollments/{id:guid}/withdraw", async (
            Guid id,
            [FromBody] WithdrawStudentRequest req,
            [FromServices] ICommandHandler<WithdrawStudent> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new WithdrawStudent(id, req.ExitDate), ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        // ── Grade Subject Assignments ─────────────────────────────────────────────

        studentsGroup.MapGet("/grade-subjects/by-period/{periodId:guid}", async (
            Guid periodId,
            [FromServices] IQueryHandler<ListGradeSubjectAssignmentsByPeriod, GradeSubjectAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGradeSubjectAssignmentsByPeriod(periodId), ct)));

        studentsGroup.MapGet("/grade-subjects/by-grade/{gradeLevelId:guid}/period/{periodId:guid}", async (
            Guid gradeLevelId,
            Guid periodId,
            [FromServices] IQueryHandler<ListGradeSubjectAssignmentsByGradeLevel, GradeSubjectAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGradeSubjectAssignmentsByGradeLevel(gradeLevelId, periodId), ct)));

        studentsGroup.MapPost("/grade-subjects", async (
            [FromBody] AssignGradeSubject command,
            [FromServices] ICommandHandler<AssignGradeSubject, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/grade-subjects/{id}", new { id });
        });

        studentsGroup.MapDelete("/grade-subjects/{id:guid}", async (
            Guid id,
            [FromServices] ICommandHandler<RemoveGradeSubject> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveGradeSubject(id), ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
        });

        // ── Student Subject Assignments ───────────────────────────────────────────

        studentsGroup.MapGet("/student-subjects/by-student/{studentId:guid}/period/{periodId:guid}", async (
            Guid studentId,
            Guid periodId,
            [FromServices] IQueryHandler<ListStudentSubjectAssignmentsByStudent, StudentSubjectAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListStudentSubjectAssignmentsByStudent(studentId, periodId), ct)));

        studentsGroup.MapGet("/student-subjects/by-period/{periodId:guid}", async (
            Guid periodId,
            [FromServices] IQueryHandler<ListStudentSubjectAssignmentsByPeriod, StudentSubjectAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListStudentSubjectAssignmentsByPeriod(periodId), ct)));

        studentsGroup.MapPost("/student-subjects", async (
            [FromBody] AssignStudentSubject command,
            [FromServices] ICommandHandler<AssignStudentSubject, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/student-subjects/{id}", new { id });
        });

        studentsGroup.MapDelete("/student-subjects/{id:guid}", async (
            Guid id,
            [FromServices] ICommandHandler<RemoveStudentSubject> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveStudentSubject(id), ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
        });

        return app;
    }
}

internal record UpdateStudentRequest(string FirstName, string LastName, DateOnly? DateOfBirth, Guid? GenderCodedValueId, string ContactEmail, string? ContactPhone);
internal record UpdateGradeLevelRequest(int Level, string Name, int DisplayOrder);
internal record UpdateSubjectRequest(string Name, int DisplayOrder);
internal record UpdatePeriodRequest(string Name, DateOnly StartDate, DateOnly EndDate, bool AllowSubjectOverrides);
internal record TransferStudentRequest(Guid NewGradeLevelId, DateOnly? TransferDate);
internal record WithdrawStudentRequest(DateOnly? ExitDate);