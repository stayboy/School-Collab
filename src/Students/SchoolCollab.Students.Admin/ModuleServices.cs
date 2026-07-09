using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Students.Admin.Services;

namespace SchoolCollab.Students.Admin;

public static class ModuleServices
{
    public static IServiceCollection AddStudentsModule(this IServiceCollection services)
    {
        // Propagates the dev-selected tenant to the students-api via the
        // x-tenant-id header so strict-entity writes resolve the right tenant.
        services.AddScoped<TenantPropagationDelegatingHandler>();
        services.AddHttpClient<StudentsApiClient>(client =>
            client.BaseAddress = new Uri("https+http://students-api"))
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        // Resolves whether the signed-in user has a real (non-default) tenant.
        // Strict-tenant Admin UI surfaces use it to gate their tools/forms
        // instead of hitting the server's tenant guard.
        services.AddScoped<VisibleTenantService>();

        return services;
    }
}