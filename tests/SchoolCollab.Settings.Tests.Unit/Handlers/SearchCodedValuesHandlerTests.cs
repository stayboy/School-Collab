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

        _handler = new SearchCodedValuesHandler(_db, _cache);
    }

    [TestMethod]
    public async Task HandleAsync_EmptySearchText_ReturnsEmptyArray()
    {
        var result = await _handler.HandleAsync(new SearchCodedValues(""));

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task HandleAsync_WhitespaceSearchText_ReturnsEmptyArray()
    {
        var result = await _handler.HandleAsync(new SearchCodedValues("   "));

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task HandleAsync_NullSearchText_ReturnsEmptyArray()
    {
        var result = await _handler.HandleAsync(new SearchCodedValues(null!));

        result.Should().BeEmpty();
    }

    // Note: EF.Functions.ILike is PostgreSQL-specific and throws with the InMemory provider.
    // The search logic (pattern matching, parentId filtering, includeDisabled filtering)
    // is exercised in integration tests against a real PostgreSQL database.
    // Unit tests verify the early-return logic for empty/whitespace/null input,
    // and the constructor/DI wiring.

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }
}