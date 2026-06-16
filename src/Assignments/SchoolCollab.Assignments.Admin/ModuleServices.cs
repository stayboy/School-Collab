using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Assignments.Admin.Services;

namespace SchoolCollab.Assignments.Admin;

public static class ModuleServices
{
    public static IServiceCollection AddAssignmentsModule(this IServiceCollection services)
    {
        services.AddHttpClient<AssignmentsApiClient>(client =>
        {
            client.BaseAddress = new Uri("https+http://assignments-api");
        });

        return services;
    }
}