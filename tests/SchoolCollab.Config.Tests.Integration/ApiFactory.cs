using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Config.Core.Data;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace SchoolCollab.Config.Tests.Integration;

/// <summary>
/// <see cref="WebApplicationFactory{Program}"/> for the Config API backed by
/// ephemeral Testcontainers Postgres (config-db) + RabbitMQ (outbox). Mirrors the
/// CodedValues integration <c>ApiFactory</c>: forces TestAuth, injects the
/// outbox exchange name, and re-points the <see cref="ConfigDbContext"/> at the
/// test container. Runs EF Core migrations itself (no MigrationService in tests).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    /// <summary>The well-known tenant the <c>TestAuthHandler</c> associates every
    /// request with (see <c>TestAuthHandlerOptions.TenantId</c>).</summary>
    public static readonly Guid TestTenant = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("config_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _rabbit.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConfigDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ConfigDbContext>>();
            services.RemoveAll<ConfigDbContext>();

            // Re-point both the scoped context (handlers) and the factory (outbox
            // publisher) at the test container. AddConfigCore registered both with
            // the dev-local connection string; the test overrides that here.
            services.AddDbContext<ConfigDbContext>(opts =>
                opts.UseNpgsql(_postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
            services.AddDbContextFactory<ConfigDbContext>(opts =>
                opts.UseNpgsql(_postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
        });

        builder.UseEnvironment("Testing");
        // TestAuth (no Keycloak); every request is auto-authenticated as TestTenant.
        builder.UseSetting("FeatureFlags:FEATURE:DisableOIDCAuth", "true");
        builder.UseSetting("ConnectionStrings:config-db", "placeholder-overridden-in-ConfigureServices");
        builder.UseSetting("ConnectionStrings:rabbitmq", _rabbit.GetConnectionString());
        // Aspire's WithEnvironment("Outbox__ExchangeName", configOutboxExchange) is
        // not present under WebApplicationFactory; inject it so AddOutbox<ConfigDbContext>
        // validation passes.
        builder.UseSetting("Outbox:ExchangeName", "config");
    }
}