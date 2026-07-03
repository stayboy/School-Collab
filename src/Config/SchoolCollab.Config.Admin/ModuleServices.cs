using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Config.Admin;

public static class ModuleServices
{
    /// <summary>
    /// Registers the Config Flags admin module's HTTP client for the central
    /// Config service. Mirrors <c>AddCodedValuesModule</c>: the unified
    /// <c>SchoolCollab.Admin</c> host calls this next to the other module
    /// registrations so the Config Flags pages get a service-discovered
    /// <see cref="ConfigFlagsApiClient"/>.
    /// </summary>
    public static IServiceCollection AddConfigModule(this IServiceCollection services)
    {
        services.AddHttpClient<ConfigFlagsApiClient>(client =>
            client.BaseAddress = new Uri("https+http://config-api"));

        return services;
    }
}