using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.CreateSubjectEnrollmentException;
using SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.CreateSubjectEnrollmentExceptions;
using SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.RemoveSubjectEnrollmentException;
using SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Queries.ListSubjectEnrollmentExceptions;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Api.Endpoints;

/// <summary>
/// Subject enrollment exceptions (subject-period-exception-model.md v3 §6): an exception
/// makes an otherwise-assigned subject unavailable for a period part / date span. Routes
/// are grouped on the <c>/students</c> group (see <see cref="StudentEndpoints"/>) and
/// therefore inherit its OIDC-gated <c>RequireAuthorization</c> — never map a fresh
/// <c>/students</c> group in <c>Program.cs</c>.
///
/// <para>No route takes a period id — an exception never references a period instance
/// (§0 decision 7). Both owner forms (grade level or activity group) are accepted
/// everywhere, mirroring the owner toggle on the management page.</para>
/// </summary>
public static class EnrollmentExceptionRoutes
{
    public static RouteGroupBuilder MapEnrollmentExceptionRoutes(this RouteGroupBuilder group)
    {
        // List exceptions for one owner. The narrow form (topicId) answers "what does
        // this subject already except" for the pickers.
        group.MapGet("/enrollment-exceptions", async (
            Guid? gradeLevelId,
            Guid? activityGroupId,
            Guid? topicId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListSubjectEnrollmentExceptions, SchoolCollab.Students.Core.DTOs.SubjectEnrollmentExceptionDto[]> handler,
            CancellationToken ct) =>
        {
            if ((gradeLevelId is null) == (activityGroupId is null))
            {
                return Results.BadRequest(new
                {
                    message = "Listing subject enrollment exceptions requires exactly one owner filter: gradeLevelId or activityGroupId."
                });
            }

            return Results.Ok(await handler.HandleAsync(
                new ListSubjectEnrollmentExceptions(gradeLevelId, activityGroupId, topicId), ct));
        });

        // "Is this subject excepted on this date?" — the pickers' check. Served by the
        // list query narrowed to the topic, with the containment test evaluated on
        // onDate; both owner forms are accepted (AC-18).
        group.MapGet("/enrollment-exceptions/check", async (
            Guid? gradeLevelId,
            Guid? activityGroupId,
            Guid topicId,
            DateOnly onDate,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListSubjectEnrollmentExceptions, SchoolCollab.Students.Core.DTOs.SubjectEnrollmentExceptionDto[]> handler,
            CancellationToken ct) =>
        {
            if ((gradeLevelId is null) == (activityGroupId is null))
            {
                return Results.BadRequest(new
                {
                    message = "Checking a subject enrollment exception requires exactly one owner filter: gradeLevelId or activityGroupId."
                });
            }

            var exceptions = await handler.HandleAsync(
                new ListSubjectEnrollmentExceptions(gradeLevelId, activityGroupId, topicId), ct);

            var excepted = exceptions.Any(e =>
                (e.StartDate is null || e.StartDate <= onDate)
                && (e.EndDate is null || e.EndDate >= onDate));

            return Results.Ok(new { excepted });
        });

        group.MapPost("/enrollment-exceptions", async (
            [FromBody] CreateSubjectEnrollmentException command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateSubjectEnrollmentException, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/students/enrollment-exceptions/{id}", new { id });
            }
            catch (GradeLevelNotFoundException) { return Results.NotFound(); }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
            catch (TopicNotFoundException) { return Results.NotFound(); }
            catch (TopicAssignmentPeriodException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
            catch (DuplicateSubjectEnrollmentException ex) { return Results.Conflict(new { ex.Message }); }
        });

        // Bulk create — one request, one ROW PER ITEM (§11.3, decision 18). A separate route rather
        // than a set-shaped body on the route above, deliberately: that shape would force every
        // existing caller and test of the single-item contract to be rewritten to a one-item list,
        // and it matches the repo's own bulk idiom (`POST /coded-values/bulk` →
        // `BulkCreateCodedValues`). The page posts here for EVERY add, one item or several, so
        // there is still exactly one write path in the UI.
        group.MapPost("/enrollment-exceptions/bulk", async (
            [FromBody] CreateSubjectEnrollmentExceptions command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateSubjectEnrollmentExceptions, Guid[]> handler,
            CancellationToken ct) =>
        {
            try
            {
                var ids = await handler.HandleAsync(command, ct);
                // 200 with the created ids, not 201: a batch has no single location to point at,
                // which is the same call the house bulk route makes.
                return Results.Ok(new { ids });
            }
            catch (GradeLevelNotFoundException) { return Results.NotFound(); }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
            catch (TopicNotFoundException) { return Results.NotFound(); }
            catch (TopicAssignmentPeriodException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
            catch (DuplicateSubjectEnrollmentException ex) { return Results.Conflict(new { ex.Message }); }
        });

        // Idempotent removal: an unknown or already-removed id is a 204.
        group.MapDelete("/enrollment-exceptions/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RemoveSubjectEnrollmentException> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(new RemoveSubjectEnrollmentException(id), ct);
            return Results.NoContent();
        });

        return group;
    }
}
