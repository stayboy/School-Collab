using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Assignments.Admin.Services;
using SchoolCollab.CodedValues.Admin.Services;

namespace SchoolCollab.Assignments.Admin;

public static class ModuleServices
{
    public static IServiceCollection AddAssignmentsModule(this IServiceCollection services)
    {
        services.AddHttpClient<AssignmentsApiClient>(client =>
        {
            client.BaseAddress = new Uri("https+http://assignments-api");
        });

        services.AddHttpClient<CodedValuesApiClient>(client =>
            client.BaseAddress = new Uri("https+http://coded-values-api"));

        return services;
    }
}