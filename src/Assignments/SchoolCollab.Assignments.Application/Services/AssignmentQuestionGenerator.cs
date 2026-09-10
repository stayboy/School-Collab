using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.AI.Abstractions;

namespace SchoolCollab.Assignments.Application.Services;

/// <summary>
/// HTTP implementation of <see cref="IAssignmentQuestionGenerator"/>: POSTs
/// the request to <c>/api/ai/assignments/questions</c> on the
/// <c>settings-ai</c> Aspire resource, parses the validated response, and
/// surfaces any non-success / network / parse failure as a typed
/// <see cref="QuestionGenerationFailed"/>.
/// </summary>
public sealed class AssignmentQuestionGenerator : IAssignmentQuestionGenerator
{
    /// <summary>Path on the AI host. Kept in lock-step with
    /// <c>SchoolCollab.AI.Server.Endpoints.AssignmentQuestionGenerationEndpoints</c>.</summary>
    public const string EndpointPath = "/api/ai/assignments/questions";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<AssignmentQuestionGenerator> _logger;

    public AssignmentQuestionGenerator(
        HttpClient http,
        ILogger<AssignmentQuestionGenerator> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GeneratedQuestionDto>> GenerateAsync(
        QuestionGenerationRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Requesting {Count} assignment questions for topic {TopicName}",
            request.QuestionCount, request.TopicName);

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(EndpointPath, request, JsonOptions, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // EC-2: cancellation must not surface as QuestionGenerationFailed.
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "The AI question service is unreachable.");
            throw new QuestionGenerationFailed(
                "The AI question service is unreachable. Please try again in a moment.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var fallback = $"Question generation failed (HTTP {(int)response.StatusCode}).";
            var body = await TryReadErrorBodyAsync(response, ct);
            throw new QuestionGenerationFailed(body ?? fallback);
        }

        QuestionGenerationResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<QuestionGenerationResponse>(JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "The AI question service returned a malformed success body.");
            throw new QuestionGenerationFailed(
                "The AI returned an unreadable response. Please try again.");
        }

        if (payload is null)
        {
            _logger.LogError("The AI question service returned an empty success body.");
            throw new QuestionGenerationFailed(
                "The AI returned an unreadable response. Please try again.");
        }

        return payload.Questions;
    }

    private async Task<string?> TryReadErrorBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions, ct);
            return envelope?.Error;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Matches the endpoint's <c>{"error": "&lt;msg&gt;"}</c> body.
    /// </summary>
    private sealed record ErrorEnvelope(string? Error);
}
