using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core;

public static class Extensions
{
    public static IServiceCollection AddAssignmentsCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Ensure the tenant provider is available for the DbContext and handlers
        // even when this module is used without authentication (e.g. worker/tests).
        services.AddTenancy();
        var connectionString = configuration.GetConnectionString("assignments-db")
            ?? configuration["ConnectionStrings:assignments-db"]
            ?? "Host=localhost;Port=5432;Database=schoolcollab_assignments;Username=postgres;Password=postgres";

        // AddDbContextFactory registers both the factory (needed by the shared
        // OutboxIntegrationEventPublisher<TContext> + OutboxDispatcher<TContext>)
        // and the scoped DbContext for command handlers.
        services.AddDbContextFactory<AssignmentsDbContext>(opts =>
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
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithTransientLifetime());
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithTransientLifetime());
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        services.AddOutbox<AssignmentsDbContext>(configuration, outbox =>
        {
            // Assignments keeps its existing partial index on
            // `dispatched_at WHERE dispatched_at IS NULL` (previously
            // on `processed_at`). The dispatcher reads with
            // `FOR UPDATE SKIP LOCKED` and the partial index keeps
            // the SELECT cheap as dispatched rows accumulate.
            outbox.UsePartialIndexOnOccurredAt();
        });

        return services;
    }
}