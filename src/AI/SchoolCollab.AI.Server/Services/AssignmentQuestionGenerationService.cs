using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolCollab.AI.Abstractions;

namespace SchoolCollab.AI.Services;

/// <summary>
/// Orchestrates a single non-streaming assignment-question generation
/// request: input validation → chat-client call → JSON parsing + schema
/// validation. Never reuses the streaming SSE engine — the response is a
/// single JSON document (spec §1.5), so the direct <see cref="IChatClient"/>
/// call is the right shape (decision (a)).
/// </summary>
public sealed class AssignmentQuestionGenerationService
{
    /// <summary>
    /// Maximum <see cref="QuestionGenerationRequest.QuestionCount"/>. A guardrail
    /// (not spec-fixed); the wizard never asks for more than ~30.
    /// </summary>
    public const int MaxQuestionCount = 30;

    /// <summary>
    /// Maximum length of <see cref="QuestionGenerationRequest.PromptOverride"/>
    /// in characters — matches the <c>AiPromptOverride</c> column cap in
    /// <c>SchoolCollab.Assignments.Core</c>.
    /// </summary>
    public const int MaxPromptOverrideLength = 4000;

    private readonly AssignmentQuestionGenerationSystemPromptProvider _promptProvider;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AssignmentQuestionGenerationService> _logger;

    public AssignmentQuestionGenerationService(
        AssignmentQuestionGenerationSystemPromptProvider promptProvider,
        IChatClientFactory chatClientFactory,
        IConfiguration config,
        ILogger<AssignmentQuestionGenerationService> logger)
    {
        _promptProvider = promptProvider;
        _chatClientFactory = chatClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Generates and validates a question set for the supplied request.
    /// </summary>
    public async Task<QuestionGenerationResponse> GenerateAsync(
        QuestionGenerationRequest request,
        CancellationToken ct)
    {
        ValidateRequest(request);

        var (_, model) = ChatModelResolver.Resolve(
            _config["codedvalue-ai-provider"],
            _config["Ollama:DefaultModel"],
            _config["OpenRouter:DefaultModel"]);

        _logger.LogInformation(
            "Generating {Count} assignment questions for topic {TopicName} via {Model}",
            request.QuestionCount, request.TopicName, model);

        var chatOptions = new ChatOptions { ModelId = model };
        var messages = _promptProvider.BuildMessages(request);

        string modelText;
        try
        {
            var response = await _chatClientFactory.GetClient().GetResponseAsync(messages, chatOptions, ct);
            modelText = response.Text ?? string.Empty;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // EC-2: cancellation must not surface as a 502 — let it propagate.
            throw;
        }
        catch (ClientResultException ex)
        {
            _logger.LogError(ex, "AI provider returned an error during question generation (Status: {Status}).", ex.Status);
            throw new AssignmentQuestionGenerationException(ProviderErrorFormatter.Format(ex), 502);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP failure during question generation.");
            throw new AssignmentQuestionGenerationException(ProviderErrorFormatter.Format(ex), 502);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during question generation.");
            throw new AssignmentQuestionGenerationException(ProviderErrorFormatter.Format(ex), 502);
        }

        return AssignmentQuestionResponseParser.Parse(modelText);
    }

    private static void ValidateRequest(QuestionGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TopicName))
        {
            throw new AssignmentQuestionGenerationException(
                "A topic name is required to generate questions.", 400);
        }

        if (request.QuestionCount < 1 || request.QuestionCount > MaxQuestionCount)
        {
            throw new AssignmentQuestionGenerationException(
                $"Question count must be between 1 and {MaxQuestionCount}.", 400);
        }

        if (request.PromptOverride is not null && request.PromptOverride.Length > MaxPromptOverrideLength)
        {
            throw new AssignmentQuestionGenerationException(
                $"Prompt override must be {MaxPromptOverrideLength} characters or fewer.", 400);
        }
    }
}
