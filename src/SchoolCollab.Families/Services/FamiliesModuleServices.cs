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
    /// <summary>Registers <see cref="FamiliesApiClient"/> for the Families host, plus the
    /// tenant-less short-link resolution client (round notification-templates-b2a, spec §3.6) the
    /// public <c>/l/{code}</c> landing reads through.</summary>
    public static IServiceCollection AddFamiliesModule(this IServiceCollection services)
    {
        services.AddCrossModuleHttpClient<FamiliesApiClient>("https+http://assignments-api", propagateTenant: true);
        // propagateTenant: false — the visitor arriving from an SMS has no tenant; the code itself is
        // the credential and the assignments-side read is deliberately cross-tenant (§3.6). The same
        // Assignments API resource the AppHost already references for this host (CrossModuleWiringTests).
        services.AddCrossModuleHttpClient<ShortLinkResolverClient>("https+http://assignments-api", propagateTenant: false);
        return services;
    }
}
