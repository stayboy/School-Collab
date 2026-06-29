using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.Data.Repositories;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.CodedValues.Core.Services;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.CodedValues.Core;

public static class Extensions
{
    public static IServiceCollection AddCodedValuesCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Ensure the tenant provider is available for the DbContext and handlers
        // even when this module is used without authentication (e.g. worker/tests).
        services.AddTenancy();
        var connectionString = configuration.GetConnectionString("coded-values-db")
            ?? configuration["ConnectionStrings:coded-values-db"]
            ?? "Host=localhost;Port=5432;Database=schoolcollab_coded_values;Username=postgres;Password=postgres";

        services.AddDbContextFactory<CodedValuesDbContext>(opts =>
            opts.UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());

        services.AddScoped<ICodedValueRepository, CodedValueRepository>();
        services.AddScoped<ICodedValueResolver, CodedValueResolver>();

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

        services.AddOutbox<CodedValuesDbContext>(configuration, outbox =>
        {
            // CodedValues-specific outbox table shape:
            // - 500-char Type (longer than the default 200 for
            //   fully-qualified event names)
            // - jsonb payload column (PostgreSQL native JSON)
            // - default 0 on Attempts (lets the database apply the
            //   default on insert)
            // - partial index on OccurredAt WHERE DispatchedAt IS NULL
            //   (keeps the dispatcher's SELECT cheap as old dispatched
            //   rows accumulate)
            outbox.SetTypeMaxLength(500)
                  .UseJsonbPayload()
                  .UseAttemptsDefaultZero()
                  .UsePartialIndexOnOccurredAt();
        });

        return services;
    }
}
