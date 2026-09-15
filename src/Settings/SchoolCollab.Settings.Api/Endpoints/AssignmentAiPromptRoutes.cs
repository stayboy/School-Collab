using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Commands.UpsertTenantAssignmentAiPrompt;
using SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Queries.GetTenantAssignmentAiPrompt;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class AssignmentAiPromptRoutes
{
    /// <summary>
    /// Maps the per-tenant organization AI prompt for assignment question
    /// generation at <c>/api/settings/assignment-ai-prompt</c> (WS-B2 /
    /// spec §3.4 line 91).
    /// </summary>
    public static RouteGroupBuilder MapAssignmentAiPromptRoutes(this RouteGroupBuilder group)
    {
        // ── Get the tenant's organization prompt (204 when unset — caller falls back to the embedded default) ──
        group.MapGet("/assignment-ai-prompt", async (
            [FromServices] IQueryHandler<GetTenantAssignmentAiPrompt, TenantAssignmentAiPromptDto?> handler,
            CancellationToken ct) =>
        {
            var prompt = await handler.HandleAsync(GetTenantAssignmentAiPrompt.Instance, ct);
            return prompt is null ? Results.NoContent() : Results.Ok(prompt);
        });

        // ── Upsert (create or replace) the tenant's organization prompt + lock posture ──
        // The 4000-character cap throws a typed ArgumentException — mapped to
        // 400 with the message so the admin page can surface it.
        group.MapPut("/assignment-ai-prompt", async (
            [FromBody] UpsertTenantAssignmentAiPromptRequest req,
            [FromServices] ICommandHandler<UpsertTenantAssignmentAiPrompt, TenantAssignmentAiPromptDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(
                    new UpsertTenantAssignmentAiPrompt(req.SystemPrompt, req.IsLocked), ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        return group;
    }

    public sealed record UpsertTenantAssignmentAiPromptRequest(string? SystemPrompt, bool IsLocked);
}
