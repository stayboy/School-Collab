using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Messaging;

namespace SchoolCollab.Students.Core;

public static class Extensions
{
    public static IServiceCollection AddStudentsCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("students-db")
            ?? configuration["ConnectionStrings:students-db"]
            ?? "Host=localhost;Port=5432;Database=schoolcollab_students;Username=postgres;Password=postgres";

        services.AddDbContext<StudentsDbContext>(opts =>
            opts.UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<IGradeLevelRepository, GradeLevelRepository>();
        services.AddScoped<ISubjectRepository, SubjectRepository>();
        services.AddScoped<IPeriodRepository, PeriodRepository>();
        services.AddScoped<IStudentEnrollmentRepository, StudentEnrollmentRepository>();
        services.AddScoped<IGradeSubjectAssignmentRepository, GradeSubjectAssignmentRepository>();
        services.AddScoped<IStudentSubjectAssignmentRepository, StudentSubjectAssignmentRepository>();

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            };
        });

        var assembly = typeof(Extensions).Assembly;
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(CQRS.ICommandHandler<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithTransientLifetime());
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(CQRS.ICommandHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithTransientLifetime());
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(CQRS.IQueryHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}