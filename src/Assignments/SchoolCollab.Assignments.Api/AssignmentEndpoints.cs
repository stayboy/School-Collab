using SchoolCollab.Assignments.Core;
using Microsoft.AspNetCore.Mvc;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Api;

public static class AssignmentEndpoints
{
    public static WebApplication MapAssignmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/assignments");

        group.MapGet("/", async (
            [FromQuery] AssignmentStatus? status,
            [FromServices] IQueryHandler<ListAssignmentsQuery, AssignmentSummaryDto[]> handler,
            CancellationToken ct) =>
        {
            var query = new ListAssignmentsQuery(status);
            var results = await handler.HandleAsync(query, ct);
            return Results.Ok(results);
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] IQueryHandler<GetAssignmentByIdQuery, AssignmentSummaryDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetAssignmentByIdQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/", async (
            [FromBody] CreateAssignmentRequest req,
            [FromServices] ICommandHandler<CreateAssignmentCommand, Guid> handler,
            CancellationToken ct) =>
        {
            var cmd = new CreateAssignmentCommand(
                req.Title, req.Description, (AssignmentType)req.AssignmentType,
                (GradingFormat)req.GradingFormat, (TargetAudienceType)req.TargetAudienceType,
                req.SubjectCodedValueId, req.GradeCodedValueId,
                req.DueDate, req.MaxScore);
            var id = await handler.HandleAsync(cmd, ct);
            return Results.Created($"/assignments/{id}", new { id });
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateAssignmentRequest req,
            [FromServices] ICommandHandler<UpdateAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                var cmd = new UpdateAssignmentCommand(
                    id, req.Title, req.Description, (AssignmentType)req.AssignmentType,
                    (GradingFormat)req.GradingFormat, (TargetAudienceType)req.TargetAudienceType,
                    req.SubjectCodedValueId, req.GradeCodedValueId,
                    req.DueDate, req.MaxScore);
                await handler.HandleAsync(cmd, ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] ICommandHandler<DeleteAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteAssignmentCommand(id), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        group.MapPost("/{id:guid}/publish", async (
            Guid id,
            [FromServices] ICommandHandler<PublishAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new PublishAssignmentCommand(id), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/{id:guid}/unpublish", async (
            Guid id,
            [FromServices] ICommandHandler<UnpublishAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UnpublishAssignmentCommand(id), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        group.MapPost("/{id:guid}/close", async (
            Guid id,
            [FromServices] ICommandHandler<CloseAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new CloseAssignmentCommand(id), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost("/{id:guid}/review", async (
            Guid id,
            [FromBody] ReviewAssignmentRequest req,
            [FromServices] ICommandHandler<ReviewAssignmentCommand> handler,
            CancellationToken ct) =>
        {
            try
            {
                var cmd = new ReviewAssignmentCommand(id, req.TeacherId, req.Score, req.Comments);
                await handler.HandleAsync(cmd, ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException)
            {
                return Results.NotFound();
            }
        });

        return app;
    }
}