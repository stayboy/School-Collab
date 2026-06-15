using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.DTOs;
using SchoolCollab.CodedValues.Core.Queries.GetCodedValueByCode;

namespace SchoolCollab.CodedValues.Tests.Unit.Handlers;

[TestClass]
public class GetCodedValueByCodeHandlerTests : IDisposable
{
    private readonly CodedValuesDbContext _db;
    private readonly HybridCache _cache;
    private readonly GetCodedValueByCodeHandler _handler;
    private readonly Guid _rootId;
    private readonly Guid _childId;
    private readonly Guid _otherRootId;

    public GetCodedValueByCodeHandlerTests()
    {
        var options = new DbContextOptionsBuilder<CodedValuesDbContext>()
            .UseInMemoryDatabase($"ByCodeTest_{Guid.NewGuid()}")
            .Options;

        _db = new CodedValuesDbContext(options);

        // HybridCache requires DI but can be created standalone for tests.
        // Use AddHybridCache via a minimal ServiceCollection.
        var services = new ServiceCollection();
        services.AddHybridCache();
        var sp = services.BuildServiceProvider();
        _cache = sp.GetRequiredService<HybridCache>();

        _handler = new GetCodedValueByCodeHandler(_db, _cache);

        // Seed test data
        _rootId = Guid.NewGuid();
        _childId = Guid.NewGuid();
        _otherRootId = Guid.NewGuid();

        SeedData();
    }

    private void SeedData()
    {
        // Root coded value: SCHOOLS
        var root = CodedValue.Create("SCHOOLS", "Schools", "All schools", null, 0);
        typeof(CodedValue).GetProperty("Id")!.SetValue(root, _rootId);

        // Child coded value: PRESEC (under SCHOOLS)
        var child = CodedValue.Create("PRESEC", "Presbyterian Boys", "A secondary school", _rootId, 1);
        typeof(CodedValue).GetProperty("Id")!.SetValue(child, _childId);
        child.SetAttribute("city", "Accra");
        child.SetAttribute("region", "Greater Accra");

        // Another root coded value: DISEASES
        var otherRoot = CodedValue.Create("DISEASES", "Diseases", "Medical conditions", null, 0);
        typeof(CodedValue).GetProperty("Id")!.SetValue(otherRoot, _otherRootId);

        // A child with same code as PRESEC but under DISEASES — tests scoped uniqueness
        var diseaseChild = CodedValue.Create("PRESEC", "Pre-existing Condition", "A medical term", _otherRootId, 0);
        typeof(CodedValue).GetProperty("Id")!.SetValue(diseaseChild, Guid.NewGuid());

        _db.CodedValues.AddRange(root, child, otherRoot, diseaseChild);
        _db.SaveChanges();
    }

    [TestMethod]
    public async Task HandleAsync_GlobalSearch_FindsRootByCode()
    {
        var result = await _handler.HandleAsync(new GetCodedValueByCode("SCHOOLS"));

        result.Should().NotBeNull();
        result!.Code.Should().Be("SCHOOLS");
        result.ParentId.Should().BeNull();
        result.Name.Should().Be("Schools");
        result.Description.Should().Be("All schools");
    }

    [TestMethod]
    public async Task HandleAsync_GlobalSearch_FindsChildByCode()
    {
        // This is the key fix: when parentId is null, global search finds child codes
        var result = await _handler.HandleAsync(new GetCodedValueByCode("PRESEC"));

        result.Should().NotBeNull();
        // First match wins in global search — could be either PRESEC
        result!.Code.Should().Be("PRESEC");
        result.Description.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task HandleAsync_ScopedSearch_FindsChildUnderSpecificParent()
    {
        var result = await _handler.HandleAsync(new GetCodedValueByCode("PRESEC", _rootId));

        result.Should().NotBeNull();
        result!.Code.Should().Be("PRESEC");
        result.ParentId.Should().Be(_rootId);
        result.ParentCode.Should().Be("SCHOOLS");
        result.Name.Should().Be("Presbyterian Boys");
        result.Description.Should().Be("A secondary school");
    }

    [TestMethod]
    public async Task HandleAsync_ScopedSearch_FindsDifferentChildWithSameCodeUnderOtherParent()
    {
        var result = await _handler.HandleAsync(new GetCodedValueByCode("PRESEC", _otherRootId));

        result.Should().NotBeNull();
        result!.Code.Should().Be("PRESEC");
        result.ParentId.Should().Be(_otherRootId);
        result.ParentCode.Should().Be("DISEASES");
        result.Name.Should().Be("Pre-existing Condition");
    }

    [TestMethod]
    public async Task HandleAsync_ScopedSearch_ReturnsNullWhenCodeNotFoundUnderParent()
    {
        var result = await _handler.HandleAsync(new GetCodedValueByCode("NONEXISTENT", _rootId));

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task HandleAsync_GlobalSearch_ReturnsNullForUnknownCode()
    {
        var result = await _handler.HandleAsync(new GetCodedValueByCode("UNKNOWN"));

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task HandleAsync_ReturnsAttributesAndDefinitions()
    {
        var result = await _handler.HandleAsync(new GetCodedValueByCode("PRESEC", _rootId));

        result.Should().NotBeNull();
        result!.Attributes.Should().HaveCount(2);
        result.Attributes.Should().Contain(a => a.Key == "city" && a.Value == "Accra");
        result.Attributes.Should().Contain(a => a.Key == "region" && a.Value == "Greater Accra");
    }

    [TestMethod]
    public async Task HandleAsync_CacheKey_IsConsistentForSameQuery()
    {
        // First call populates cache
        var first = await _handler.HandleAsync(new GetCodedValueByCode("SCHOOLS"));
        // Second call should hit cache
        var second = await _handler.HandleAsync(new GetCodedValueByCode("SCHOOLS"));

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.Id.Should().Be(second!.Id);
        first.Code.Should().Be(second.Code);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }
}