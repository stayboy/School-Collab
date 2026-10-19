using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
        group.MapGet("/assignment-ai-prompt", async Task<Results<Ok<TenantAssignmentAiPromptDto>, NoContent>> (
            [FromServices] IQueryHandler<GetTenantAssignmentAiPrompt, TenantAssignmentAiPromptDto?> handler,
            CancellationToken ct) =>
        {
            var prompt = await handler.HandleAsync(GetTenantAssignmentAiPrompt.Instance, ct);
            return prompt is null ? TypedResults.NoContent() : TypedResults.Ok(prompt);
        });

        // ── Upsert (create or replace) the tenant's organization prompt + lock posture ──
        // The 4000-character cap throws a typed ArgumentException — mapped to
        // 400 with the message so the admin page can surface it.
        group.MapPut("/assignment-ai-prompt",
            async Task<Results<Ok<TenantAssignmentAiPromptDto>, BadRequest<ErrorResponse>>> (
            [FromBody] UpsertTenantAssignmentAiPromptRequest req,
            [FromServices] ICommandHandler<UpsertTenantAssignmentAiPrompt, TenantAssignmentAiPromptDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(
                    new UpsertTenantAssignmentAiPrompt(req.SystemPrompt, req.IsLocked), ct);
                return TypedResults.Ok(result);
            }
            catch (ArgumentException ex)
            {
                // {"error":…}, not {"message":…} — the key this route has always emitted. TypedResults.BadRequest<T>
                // is wire-identical to the Json(…, statusCode: 400) it replaces (400, application/json, same body and
                // the app's JsonOptions) and, unlike JsonHttpResult<T>, it documents the 400 schema (round review F).
                return TypedResults.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return group;
    }

    public sealed record UpsertTenantAssignmentAiPromptRequest(string? SystemPrompt, bool IsLocked);
}
