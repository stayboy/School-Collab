using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Assignments.Admin.Services;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Assignments.Admin;

public static class ModuleServices
{
    /// <summary>
    /// Wires the Assignments admin module's HTTP client (against
    /// <c>assignments-api</c>). The CodedValues client the Assignments pages
    /// use (e.g. the subject / grade dropdowns on the Create form) is
    /// registered by <c>AddSettingsModule</c> in
    /// <c>SchoolCollab.Settings.Admin.ModuleServices</c> against the unified
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
        // Propagates the dev-selected tenant to the assignments-api via the
        // x-tenant-id header so strict-entity writes resolve the right tenant.
        services.AddScoped<TenantPropagationDelegatingHandler>();
        services.AddHttpClient<AssignmentsApiClient>(client =>
        {
            client.BaseAddress = new Uri("https+http://assignments-api");
        })
        .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        return services;
    }
}