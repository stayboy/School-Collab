using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// Shared Testcontainers Postgres lifetime for the E3 (ar-19) integration suite: the
/// container starts once at assembly init and is dropped at assembly cleanup, and each
/// discriminating test creates its own <i>database</i> on it (so the dedupe migration
/// can be applied from a pre-dedupe schema).
/// </summary>
[TestClass]
public static class AssignmentsPostgres
{
    private static PostgreSqlContainer? _container;

    /// <summary>The shared container's connection string (points at the <c>postgres</c>
    /// database, from which per-test databases are created).</summary>
    public static string ConnectionString => _container!.GetConnectionString();

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("postgres")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _container.StartAsync();
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
