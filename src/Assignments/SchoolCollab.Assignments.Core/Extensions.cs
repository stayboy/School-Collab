using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Services;
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

        // WS-A1 / FR-210: file-store + attachment-upload options. Both sections
        // are AppHost-injected via WithEnvironment in assignments-api (see
        // documents/configuration.md §2/§11/§13). Defaults match the AppHost
        // Parameters: defaults so a developer running without the AppHost still
        // gets sensible behavior.
        services.Configure<AssignmentFileStoreOptions>(
            configuration.GetSection(AssignmentFileStoreOptions.SectionName));
        services.Configure<AttachmentUploadOptions>(
            configuration.GetSection(AttachmentUploadOptions.SectionName));
        services.AddScoped<IFileStore, LocalFileStore>();

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
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();
        services.AddScoped<IAssignmentActivityGroupRepository, AssignmentActivityGroupRepository>();
        services.AddScoped<SchoolCollab.Assignments.Core.Services.IAssignmentNotificationBroadcaster, SchoolCollab.Assignments.Core.Services.AssignmentNotificationBroadcaster>();
        // WS-A3 (spec §3.3): pure scoring engine for AutoGraded /
        // InstantGraded submissions. Handler seam — the engine itself is
        // pure (no DbContext, no clock) and consumed by the submission
        // handlers via constructor injection.
        services.AddScoped<IScoringEngine, ScoringEngine>();

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