using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Data;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.SearchCodedValues;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class SearchCodedValuesHandlerTests : IDisposable
{
    private readonly SettingsDbContext _db;
    private readonly HybridCache _cache;
    private readonly SearchCodedValuesHandler _handler;

    public SearchCodedValuesHandlerTests()
    {
        var tenantProvider = new DesignTimeTenantProvider();
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"SearchTest_{Guid.NewGuid()}")
            .Options;

        _db = new SettingsDbContext(options, tenantProvider);

        var services = new ServiceCollection();
        services.AddHybridCache();
        var sp = services.BuildServiceProvider();
        _cache = sp.GetRequiredService<HybridCache>();

        // The handler resolves tenants itself (ITenantProvider) and creates
        // short-lived contexts via IDbContextFactory; share this test's options
        // so InMemory data seeded on _db is visible to handler-created contexts.
        _handler = new SearchCodedValuesHandler(
            new TestSettingsDbContextFactory(options, tenantProvider),
            _cache,
            tenantProvider);
    }

    [TestMethod]
    public async Task HandleAsync_BlankSearchText_ReturnsEmptyArray()
    {
        // One representative of the three former cases ("", "   ", null): IsNullOrWhiteSpace covers all
        // three branches, and the HTTP boundary owns the contract test for a blank query
        // (CodedValueQueryTests.Search_WithBlankText_Returns200EmptyArrayWithoutCallingTheHandler)
        // — pruning round 2026-10-17, Q24.
        var result = await _handler.HandleAsync(new SearchCodedValues("   "));

        result.Should().BeEmpty();
    }

    // Note: EF.Functions.ILike is PostgreSQL-specific and throws with the InMemory provider.
    // The search logic (pattern matching, parentId filtering, includeDisabled filtering)
    // is exercised in integration tests against a real PostgreSQL database.
    // This unit test verifies the early-return guard only; the HTTP-visible half of that guard
    // lives in tests/SchoolCollab.Settings.Api.Tests.Unit/CodedValueQueryTests.cs.

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }
}