using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Students.Admin.Services;

namespace SchoolCollab.Students.Admin;

public static class ModuleServices
{
    public static IServiceCollection AddStudentsModule(this IServiceCollection services)
    {
        services.AddHttpClient<StudentsApiClient>(client =>
            client.BaseAddress = new Uri("https+http://students-api"));

        return services;
    }
}