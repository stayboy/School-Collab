using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.LinkGuardianToStudent;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.UnlinkGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.UpdateGuardianLink;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardianCountsByStudents;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardiansByStudent;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Api.Endpoints;

public static class StudentGuardianRoutes
{
    /// <summary>
    /// Maps onto the <c>/students</c> group, so routes are
    /// <c>/students/{studentId}/guardians[/...]</c>. Inherits RequireAuthorization
    /// from the group (spec §9 G2).
    /// </summary>
    public static RouteGroupBuilder MapStudentGuardianRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/{studentId:guid}/guardians", async (
            Guid studentId,
            [FromServices] IQueryHandler<ListGuardiansByStudent, StudentGuardianViewDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGuardiansByStudent(studentId), ct)));

        // Bulk guardian-count endpoint: GET /students/guardian-counts?studentIds=…
        // Returns the number of linked (non-deleted) guardians per student in one
        // round-trip so the student landing grid can render "N guardians" without
        // an N+1 per-student fetch. Mirrors the enrollments by-students pattern.
        group.MapGet("/guardian-counts", async (
            [FromQuery] Guid[] studentIds,
            [FromServices] IQueryHandler<ListGuardianCountsByStudents, GuardianCountDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGuardianCountsByStudents(studentIds), ct)));

        group.MapPost("/{studentId:guid}/guardians", async (
            Guid studentId,
            [FromBody] LinkGuardianRequest req,
            [FromServices] ICommandHandler<LinkGuardianToStudent, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(new LinkGuardianToStudent(
                    studentId, req.GuardianId, req.RelationshipCodedValueId, req.Role, req.IsEmergencyContact, req.ActingGuardianId), ct);
                return Results.Created($"/students/{studentId}/guardians/{req.GuardianId}", new { id });
            }
            catch (GuardianNotFoundException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
            catch (StudentNotFoundException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
            catch (GuardianLinkAlreadyExistsException)
            {
                return Results.Conflict(new { message = "A link already exists between this student and guardian." });
            }
        });

        group.MapPut("/{studentId:guid}/guardians/{guardianId:guid}", async (
            Guid studentId,
            Guid guardianId,
            [FromBody] UpdateGuardianLinkRequest req,
            [FromServices] ICommandHandler<UpdateGuardianLink> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateGuardianLink(studentId, guardianId, req.Role, req.RelationshipCodedValueId, req.IsEmergencyContact), ct);
                return Results.NoContent();
            }
            catch (GuardianLinkNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapDelete("/{studentId:guid}/guardians/{guardianId:guid}", async (
            Guid studentId,
            Guid guardianId,
            [FromServices] ICommandHandler<UnlinkGuardian> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UnlinkGuardian(studentId, guardianId), ct);
                return Results.NoContent();
            }
            catch (GuardianLinkNotFoundException)
            {
                return Results.NotFound();
            }
        });

        return group;
    }
}

internal record LinkGuardianRequest(
    Guid GuardianId, Guid? RelationshipCodedValueId, GuardianRole Role, bool IsEmergencyContact, Guid? ActingGuardianId);

internal record UpdateGuardianLinkRequest(GuardianRole Role, Guid? RelationshipCodedValueId, bool IsEmergencyContact);
