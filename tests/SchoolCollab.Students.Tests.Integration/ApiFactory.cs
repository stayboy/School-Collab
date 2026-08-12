using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Students.Core.Data;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// <see cref="WebApplicationFactory{Program}"/> for the Students API backed by
/// ephemeral Testcontainers Postgres (students-db) + RabbitMQ (outbox). Forces
/// TestAuth, injects the outbox exchange name, and re-points
/// <see cref="StudentsDbContext"/> at the test container. Runs EF Core
/// migrations itself (no MigrationService in tests). Mirrors
/// SchoolCollab.Settings.Tests.Integration.ApiFactory.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    /// <summary>Two well-known tenants used to prove per-tenant caching and the
    /// tenant-scoped overlap check. <see cref="TestAuthHandler"/> prefers the
    /// per-request <c>x-tenant-id</c> header, so requests are stamped with one
    /// of these to simulate distinct tenant contexts.</summary>
    public static readonly Guid TestTenantA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid TestTenantB = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("students_test")
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
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
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
            services.RemoveAll<DbContextOptions<StudentsDbContext>>();
            services.RemoveAll<StudentsDbContext>();

            // Default tenant (overridden per-request via the x-tenant-id header).
            services.Configure<TestAuthHandlerOptions>(options =>
                options.TenantId = TestTenantA);

            // Re-point both the scoped context (handlers) and the factory
            // (outbox publisher) at the test container.
            services.AddDbContext<StudentsDbContext>(opts =>
                opts.UseNpgsql(_postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
            services.AddDbContextFactory<StudentsDbContext>(opts =>
                opts.UseNpgsql(_postgres.GetConnectionString()).UseSnakeCaseNamingConvention());

            // The real IEntityCodeGenerator (Settings module) needs a Settings DB
            // that this Students-only factory does not start. Stub it so teacher /
            // student creation handlers resolve a deterministic code instead of
            // failing to connect to a missing settings-db.
            services.RemoveAll<IEntityCodeGenerator>();
            services.AddSingleton<IEntityCodeGenerator>(new StubEntityCodeGenerator());
        });

        builder.UseEnvironment("Testing");
        // TestAuth (no Keycloak); every request is auto-authenticated.
        builder.UseSetting("FeatureFlags:FEATURE:DisableOIDCAuth", "true");
        builder.UseSetting("ConnectionStrings:students-db", "placeholder-overridden-in-ConfigureServices");
        builder.UseSetting("ConnectionStrings:rabbitmq", _rabbit.GetConnectionString());
        // AddOutbox<StudentsDbContext> validates this; no messages are consumed
        // in tests, so any valid exchange name works.
        builder.UseSetting("Outbox:ExchangeName", "students");
    }
}

/// <summary>
/// Deterministic <see cref="IEntityCodeGenerator"/> for the Students-only test
/// factory. Returns a fixed code so creation handlers don't need a Settings DB.
/// </summary>
internal sealed class StubEntityCodeGenerator : IEntityCodeGenerator
{
    private int _seq;

    public Task<string> GenerateAsync(string ruleCode, CancellationToken cancellationToken = default)
        => Task.FromResult($"{ruleCode}{Interlocked.Increment(ref _seq):D4}");

    public Task<string> GenerateWithNameAsync(string ruleCode, string? nameHint, CancellationToken cancellationToken = default)
        => Task.FromResult($"{ruleCode}{Interlocked.Increment(ref _seq):D4}");
}
