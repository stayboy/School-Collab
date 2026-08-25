using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Http;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Contracts;

namespace SchoolCollab.Students.Application;

public static class ModuleServices
{
    public static IServiceCollection AddStudentsModule(this IServiceCollection services)
    {
        // Cross-module: admin Blazor app → students-api. Long handler lifetime
        // + retry on disposed-NetworkStream/HttpRequestException so the tenant
        // propagation handler does not appear to "block" calls when the factory
        // rotates its handler pool (adr-cross-module-calls.md reference pattern).
        // propagateTenant:true wires TenantPropagationDelegatingHandler
        // (dev-selected tenant, registered TRANSIENT via TryAdd so it cannot be
        // shared-across-named-clients Singleton that corrupts InnerHandler routing).
        services.AddCrossModuleHttpClient<StudentsApiClient>("https+http://students-api", propagateTenant: true);

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