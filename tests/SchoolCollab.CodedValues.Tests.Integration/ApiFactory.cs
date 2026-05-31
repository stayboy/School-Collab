using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.CodedValues.Core.Data;
using Testcontainers.PostgreSql;

namespace SchoolCollab.CodedValues.Tests.Integration;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("codedvalues_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // API no longer runs MigrateAsync at startup (that responsibility belongs to
        // the dedicated MigrationService in Aspire). Test factory runs it here so
        // integration tests always start against a current schema.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodedValuesDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<CodedValuesDbContext>>();
            services.RemoveAll<CodedValuesDbContext>();

            services.AddDbContext<CodedValuesDbContext>(opts =>
                opts.UseNpgsql(_postgres.GetConnectionString())
                    .UseSnakeCaseNamingConvention());

            var descriptors = services
                .Where(d => d.ServiceType.FullName?.StartsWith("MassTransit") == true)
                .ToList();
            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<MassTransit.IPublishEndpoint, NoOpPublishEndpoint>();
        });

        builder.UseEnvironment("Testing");
    }

    public new async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
