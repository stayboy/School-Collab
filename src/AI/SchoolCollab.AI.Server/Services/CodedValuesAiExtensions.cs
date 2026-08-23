using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.AI.Tools.CodedValues;

namespace SchoolCollab.AI.Services;

/// <summary>
/// Wires the CodedValues AI tool bag into the generic <see cref="AIChatEngine"/>:
/// the <see cref="ICodedValuesApiClient"/> HttpClient (pointed at the Settings
/// REST API), the <see cref="CodedValuesToolProvider"/> (the 9 coded-value
/// tools + per-turn <c>SelectToolsForPrompt</c> narrowing + SSE formatting),
/// and the <see cref="CodedValuesSystemPromptProvider"/>. Call once from the
/// AI server's startup, e.g.
/// <c>builder.Services.AddCodedValuesAiTools(c =&gt; c.BaseAddress = new Uri("https+http://settings-api"));</c>.
/// Adding a second bounded context is a parallel <c>AddXxxAiTools()</c> call —
/// the engine picks up every registered <see cref="IToolProvider"/>.
/// </summary>
public static class CodedValuesAiExtensions
{
    public static IServiceCollection AddCodedValuesAiTools(
        this IServiceCollection services,
        Action<HttpClient> configureCodedValuesApi,
        Action<IHttpClientBuilder>? configureCodedValuesClientBuilder = null)
    {
        var httpClientBuilder = services.AddHttpClient<ICodedValuesApiClient, CodedValuesApiClient>(configureCodedValuesApi);
        configureCodedValuesClientBuilder?.Invoke(httpClientBuilder);
        services.AddSingleton<IToolProvider, CodedValuesToolProvider>();
        services.AddSingleton<ISystemPromptProvider, CodedValuesSystemPromptProvider>();
        return services;
    }
}