using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Admin.Services;

namespace SchoolCollab.Admin;

/// <summary>
/// Registration for the Admin host's own (non-module) services.
/// </summary>
/// <remarks>
/// Kept as an extension method rather than inline <c>Add*</c> calls in <c>Program.cs</c>,
/// per the repo's registration rule. Module services (Settings / Assignments / Students)
/// register themselves through their own <c>Add{Layer}</c> helpers — this one exists for
/// services that belong to the Blazor shell itself and therefore have no owning module.
/// </remarks>
public static class AdminPortalServiceExtensions
{
    /// <summary>
    /// Registers the dev-portal tenancy services: the resolver that computes the tenant the
    /// portal opens on (operator preference → platform default) and publishes it so every host
    /// agrees. Scoped, matching the Blazor circuit's lifetime and the transient
    /// <c>TenantsApiClient</c> it composes.
    /// </summary>
    public static IServiceCollection AddAdminPortalTenancy(this IServiceCollection services)
    {
        services.TryAddScoped<AdminDefaultTenantResolver>();
        return services;
    }
}
