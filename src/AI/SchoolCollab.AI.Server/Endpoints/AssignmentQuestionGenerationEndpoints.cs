using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.AI.Services;

namespace SchoolCollab.AI.Endpoints;

/// <summary>
/// Minimal-API endpoint group for the assignment question-generation surface
/// (<c>POST /api/ai/assignments/questions</c>). Anonymous by design (dev
/// posture); the generate handler NOW reads the caller's tenant organization
/// prompt via the propagated tenant header (WS-B2), fail-open to the embedded
/// default when no tenant header arrives or the Settings API is unreachable — so
/// an external anonymous caller never resolves another tenant's prompt (the
/// header is required to do so).
/// </summary>
public static class AssignmentQuestionGenerationEndpoints
{
    /// <summary>
    /// Maps the assignment question-generation endpoint on <paramref name="app"/>.
    /// </summary>
    public static WebApplication MapAssignmentQuestionGenerationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/ai/assignments/questions", HandleGenerateAsync);
        return app;
    }

    private static async Task<IResult> HandleGenerateAsync(
        QuestionGenerationRequest? request,
        AssignmentQuestionGenerationService service,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Results.Json(
                new { error = "A question generation request is required." },
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var response = await service.GenerateAsync(request, ct);
            return Results.Ok(response);
        }
        catch (AssignmentQuestionGenerationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
        }
    }
}
