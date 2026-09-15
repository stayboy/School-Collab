using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Core.Http;

namespace SchoolCollab.Assignments.Application;

public static class ModuleServices
{
    /// <summary>
    /// Wires the Assignments admin module's HTTP client (against
    /// <c>assignments-api</c>) and the AI question-generation HTTP client
    /// (against <c>settings-ai</c>). The CodedValues client the Assignments
    /// pages use (e.g. the subject / grade dropdowns on the Create form) is
    /// registered by <c>AddSettingsModule</c> in
    /// <c>SchoolCollab.Settings.Application.ModuleServices</c> against the unified
    /// <c>settings-api</c> — do NOT re-register it here. A pre-merge
    /// duplicate registration pointed the typed client at the now-defunct
    /// <c>https+http://coded-values-api</c> Aspire resource, which was the
    /// actual cause of the Coded Values API "not working" symptom after the
    /// Settings context merge (#59): because
    /// <see cref="M:Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient``1(System.IServiceCollection,System.Action{System.Net.Http.HttpClient})"/>
    /// is last-write-wins, the duplicate silently replaced the
    /// <c>settings-api</c> base address with the unresolvable
    /// <c>coded-values-api</c> host.
    /// </summary>
    public static IServiceCollection AddAssignmentsModule(this IServiceCollection services)
    {
        // Cross-module: admin Blazor app → assignments-api, with the same
        // resilience reference pattern used for the other module clients.
        // propagateTenant:true wires TenantPropagationDelegatingHandler
        // (dev-selected tenant) plus the retry handler + long handler lifetime.
        services.AddCrossModuleHttpClient<AssignmentsApiClient>("https+http://assignments-api", propagateTenant: true);

        // AI question-generation seam (spec §3.4 / round ar-2 decision (a)/(d);
        // WS-B2 / ar-10). The endpoint is anonymous on the AI host (matches
        // /api/ai/chat posture) but the AI host now resolves the caller's tenant
        // organization prompt (WS-B2) via the propagated tenant header, so the
        // client propagates the tenant (the ar-2 "flip when WS-B2 org-level
        // prompts land" note). The typed client is exposed through
        // IAssignmentQuestionGenerator so the wizard depends on the abstraction.
        services.AddCrossModuleHttpClient<AssignmentQuestionGenerator>("https+http://settings-ai", propagateTenant: true);
        services.AddTransient<IAssignmentQuestionGenerator>(sp =>
            sp.GetRequiredService<AssignmentQuestionGenerator>());

        // URL text extraction for AI question generation (WS-B2 / spec §3.4
        // decision g). The named client carries ONLY the 5s timeout — NO tenant
        // propagation handler (external hosts must not learn internal tenancy).
        services.AddHttpClient("url-fetcher", client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddTransient<IUrlTextExtractor, UrlTextExtractor>();

        // C3 certificate download (decision (f)): wraps the ApiClient PDF fetch + the
        // fileDownload.js save. Owns the lazily-loaded JS module ref, disposed by the
        // consuming component.
        services.AddTransient<CertificateDownloadService>();

        return services;
    }
}