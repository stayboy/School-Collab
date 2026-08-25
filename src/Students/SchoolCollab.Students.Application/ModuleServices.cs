using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Contracts;

namespace SchoolCollab.Students.Application;

public static class ModuleServices
{
    public static IServiceCollection AddStudentsModule(this IServiceCollection services)
    {
        // Propagates the dev-selected tenant to the students-api via the
        // x-tenant-id header so strict-entity writes resolve the right tenant.
        // Registered as a SINGLETON (NOT scoped): it is stateless apart from the
        // singleton IDevTenantSelection + logger, and a scoped DelegatingHandler
        // captured in IHttpClientFactory's cached chain is disposed when the
        // request scope ends -> reused-disposed-handler (ObjectDisposedException /
        // "Cannot access a disposed object … NetworkStream"). This was the
        // EnrollStudentDialog failure.
        // MUST be TRANSIENT (not Singleton, not Scoped): IHttpClientFactory's
        // per-named-client pipeline sets InnerHandler on the handler chain.
        // A Singleton shared across named clients (CodedValuesApiClient,
        // StudentsApiClient, etc.) gets its InnerHandler overwritten by the
        // second client, corrupting the first client's cached pipeline
        // (e.g. coded-values requests hit students-api -> 404 -> blank page).
        services.TryAddTransient<TenantPropagationDelegatingHandler>();
        services.AddHttpClient<StudentsApiClient>(client =>
            client.BaseAddress = new Uri("https+http://students-api"))
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        // Shared contact surface (used by ContactsEditor in Admin.Shared) resolves
        // to the same typed HttpClient-backed client instance.
        services.AddScoped<IContactsClient>(sp => sp.GetRequiredService<StudentsApiClient>());

        // Resolves whether the signed-in user has a real (non-default) tenant.
        // Strict-tenant Admin UI surfaces use it to gate their tools/forms
        // instead of hitting the server's tenant guard.
        services.AddScoped<VisibleTenantService>();

        return services;
    }
}