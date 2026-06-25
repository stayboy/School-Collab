using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.CodedValues.Admin.Services;

namespace SchoolCollab.CodedValues.Admin;

public static class ModuleServices
{
    public static IServiceCollection AddCodedValuesModule(this IServiceCollection services)
    {
        services.AddHttpClient<CodedValuesApiClient>(client =>
            client.BaseAddress = new Uri("https+http://coded-values-api"));

        services.AddHttpClient<AiChatClient>(client =>
            client.BaseAddress = new Uri("https+http://coded-values-ai"));

        // Scoped bridge for mirroring the inline CodedValuesChat conversation
        // into the drawer chat. See CodedValuesChatHub for the rationale.
        services.AddScoped<CodedValuesChatHub>();

        return services;
    }
}