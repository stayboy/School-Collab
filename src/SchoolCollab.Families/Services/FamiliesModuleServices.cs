using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Http;

namespace SchoolCollab.Families.Services;

/// <summary>
/// F1 (slice 2b) — wires the Families surface's HTTP client against the
/// Assignments API via Aspire service discovery (<c>https+http://assignments-api</c>),
/// with the same cross-module resilience reference pattern used by the Admin host
/// (retry handler + tenant propagation + long handler lifetime).
/// </summary>
public static class ModuleServices
{
    /// <summary>Registers <see cref="FamiliesApiClient"/> for the Families host.</summary>
    public static IServiceCollection AddFamiliesModule(this IServiceCollection services)
    {
        services.AddCrossModuleHttpClient<FamiliesApiClient>("https+http://assignments-api", propagateTenant: true);
        return services;
    }
}
