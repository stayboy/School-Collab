using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Messaging;

namespace SchoolCollab.Assignments.Core;

public static class Extensions
{
    public static IServiceCollection AddAssignmentsCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("assignments-db")
            ?? configuration["ConnectionStrings:assignments-db"]
            ?? "Host=localhost;Port=5432;Database=schoolcollab_assignments;Username=postgres;Password=postgres";

        services.AddDbContext<AssignmentsDbContext>(opts =>
            opts.UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IAssignmentRepository, AssignmentRepository>();

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