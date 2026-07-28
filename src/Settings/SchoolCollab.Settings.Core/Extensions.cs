using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Tenancy;

namespace SchoolCollab.Settings.Core;

public static class Extensions
{
    /// <summary>
    /// Wires the unified Settings bounded context. Combines the CodedValues
    /// aggregate (CodedValue hierarchies, tenant overrides, attribute
    /// definitions, repository, resolver) with the FeatureFlag aggregate
    /// (global flags, tenant overrides, audit trail, system actor). Both
    /// aggregates share the SettingsDbContext, the transactional outbox, and
    /// the ITenantProvider for query filters. See
    /// documents/solution/settings-context-merge-spec.md §6.
    /// </summary>
    public static IServiceCollection AddSettingsCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Ensure the tenant provider is available for the DbContext and handlers
        // even when this module is used without authentication (e.g. worker/tests).
        services.AddTenancy();

        var connectionString = configuration.GetConnectionString("settings-db")
            ?? configuration["ConnectionStrings:settings-db"]
            ?? "Host=localhost;Port=5432;Database=schoolcollab_settings;Username=postgres;Password=postgres";

        services.AddDbContextFactory<SettingsDbContext>(opts =>
            opts.UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());

        // CodedValues aggregate services
        services.AddScoped<ICodedValueRepository, CodedValueRepository>();
        services.AddScoped<ICodedValueResolver, CodedValueResolver>();

        // EntityCodeRule aggregate services (auto-generated entity codes — spec §3.1)
        services.AddScoped<IEntityCodeRuleRepository, EntityCodeRuleRepository>();
        services.AddScoped<ITenantEntityCodeRuleOverrideRepository, TenantEntityCodeRuleOverrideRepository>();
        services.AddScoped<IEntityCodeGenerator, EntityCodeGenerator>();

        // Cross-context tenant directory (FR-16): lets workers enumerate tenants
        // without a direct SettingsDbContext dependency at the DbContext level.
        services.TryAddSingleton<ITenantDirectory, TenantDirectory>();

        // FeatureFlag aggregate services — default system actor; a host with
        // an authenticated principal (the API) replaces this with a
        // ClaimsPrincipal-backed accessor after AddSettingsCore.
        services.TryAddSingleton<IActorAccessor>(_ =>
            new SystemActorAccessor("system:unknown", "Unknown Actor"));

        // The auditor writes audit rows in the handler's transaction. It depends
        // on IActorAccessor (registered above) and the scoped SettingsDbContext
        // (injected per call via the handler), so a transient/scoped lifetime
        // is correct.
        services.AddScoped<FeatureFlagAuditor>();

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

        services.AddOutbox<SettingsDbContext>(configuration, outbox =>
        {
            // Settings-specific outbox table shape (shared by both aggregates):
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

    /// <summary>
    /// Lightweight registration for cross-context workers (e.g. the Students
    /// <c>PromotionService</c>) that need to enumerate tenants via
    /// <see cref="ITenantDirectory"/> but do NOT need the full Settings aggregate
    /// (CodedValues, FeatureFlags, handlers, outbox). Registers only a
    /// <see cref="SettingsDbContext"/> factory (for the tenant read) and
    /// <see cref="ITenantDirectory"/>. Also ensures <see cref="ITenantProvider"/>
    /// / <see cref="ITenantContextAccessor"/> are present (via
    /// <see cref="TenancyServiceExtensions.AddTenancy"/>). See
    /// <c>global-tenant-filter.md</c> §8.4 / FR-16.
    /// </summary>
    public static IServiceCollection AddTenantDirectory(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTenancy();

        var connectionString = configuration.GetConnectionString("settings-db")
            ?? configuration["ConnectionStrings:settings-db"]
            ?? "Host=localhost;Port=5432;Database=schoolcollab_settings;Username=postgres;Password=postgres";

        services.AddDbContextFactory<SettingsDbContext>(opts =>
            opts.UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());

        services.TryAddSingleton<ITenantDirectory, TenantDirectory>();
        return services;
    }
}
