using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Data;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Minimal <see cref="IDbContextFactory{SettingsDbContext}"/> for unit tests
/// that seed via a long-lived <see cref="SettingsDbContext"/> but hand
/// handlers a factory (handlers now create short-lived contexts per call —
/// see GetCodedValuesByParentHandler for the ObjectDisposedException
/// rationale). Each CreateDbContext builds a FRESH context over the SAME
/// DbContextOptions, so InMemory databases (named per test class) share one
/// backing store between the seeding context and every handler-created
/// context — mirroring production lifetime semantics without DI.
/// </summary>
public sealed class TestSettingsDbContextFactory(
    DbContextOptions<SettingsDbContext> options,
    ITenantProvider tenantProvider) : IDbContextFactory<SettingsDbContext>
{
    public SettingsDbContext CreateDbContext() => new(options, tenantProvider);

    public Task<SettingsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());
}
