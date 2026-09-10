using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.AI.Abstractions;

namespace SchoolCollab.AI.Services;

/// <summary>
/// Wires the assignment question-generation surface into DI: the prompt
/// provider (registered as its concrete type — NEVER as
/// <see cref="ISystemPromptProvider"/>, to avoid colliding with the
/// CodedValues singleton; decision (a)/(b)) and the service that
/// orchestrates the non-streaming generation.
/// </summary>
public static class AssignmentQuestionGenerationExtensions
{
    /// <summary>
    /// Registers the assignment question-generation prompt provider (concrete type)
    /// and the <see cref="AssignmentQuestionGenerationService"/>. Call once from
    /// the AI host's service-registration pipeline.
    /// </summary>
    public static IServiceCollection AddAssignmentQuestionGeneration(this IServiceCollection services)
    {
        // Register the prompt provider as its concrete type to avoid colliding
        // with the CodedValuesSystemPromptProvider ISystemPromptProvider singleton
        // registered by AddCodedValuesAiTools(). The new endpoint drives the
        // provider directly — see decision (a)/(b).
        services.AddSingleton<AssignmentQuestionGenerationSystemPromptProvider>();
        services.AddSingleton<AssignmentQuestionGenerationService>();
        return services;
    }
}
