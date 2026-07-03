using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Config.Core.Data;
using SchoolCollab.Config.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Config.Core;

public static class Extensions
{
    /// <summary>
    /// Wires the Config bounded context: DbContext factory, HybridCache, CQRS
    /// handler scan, the transactional outbox, and a default system actor for
    /// audit (overridable by a host that has an authenticated principal). Mirrors
    /// <c>AddCodedValuesCore</c>.
    /// </summary>
    public static IServiceCollection AddConfigCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTenancy();

        var connectionString = configuration.GetConnectionString("config-db")
            ?? configuration["ConnectionStrings:config-db"]
            ?? "Host=localhost;Port=5432;Database=schoolcollab_config;Username=postgres;Password=postgres";

        services.AddDbContextFactory<ConfigDbContext>(opts =>
            opts.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            };
        });

        // Default system actor; a host with an authenticated principal (the API)
        // replaces this with a ClaimsPrincipal-backed accessor after AddConfigCore.
        services.TryAddSingleton<IActorAccessor>(_ =>
            new SystemActorAccessor("system:unknown", "Unknown Actor"));

        // The auditor writes audit rows in the handler's transaction. It depends on
        // IActorAccessor (registered above) and the scoped ConfigDbContext (injected
        // per call via the handler), so a transient/scoped lifetime is correct.
        services.AddScoped<FeatureFlagAuditor>();

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

        services.AddOutbox<ConfigDbContext>(configuration, outbox =>
        {
            outbox.SetTypeMaxLength(500)
                  .UseJsonbPayload()
                  .UseAttemptsDefaultZero()
                  .UsePartialIndexOnOccurredAt();
        });

        return services;
    }
}