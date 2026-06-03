using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Tests.Integration;

[TestClass]
[DoNotParallelize]
public class CodedValuesApiTests
{
    private static ApiFactory _factory = default!;
    private static HttpClient _client = default!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _factory = new ApiFactory();
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }


    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _client?.Dispose();

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodedValuesDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();

        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE coded_values CASCADE;");
        await cache.RemoveByTagAsync("coded-values");
    }

    [TestMethod]
    public async Task POST_CodedValues_CreatesCategory()
    {
        var response = await _client.PostAsJsonAsync("/coded-values", new
        {
            Code = $"TEST_{Guid.NewGuid():N}",
            Name = "Test Category",
            Description = "Integration test",
            DisplayOrder = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [TestMethod]
    public async Task POST_CodedValues_DuplicateCode_ReturnsConflict()
    {
        var code = $"DUP_{Guid.NewGuid():N}".ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = code, Name = "First", DisplayOrder = 0 });

        var response = await _client.PostAsJsonAsync("/coded-values", new { Code = code, Name = "Second", DisplayOrder = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [TestMethod]
    public async Task GET_CodedValues_ReturnsRootValues()
    {
        var code = $"ROOT_{Guid.NewGuid():N}".ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = code, Name = "Root Value", DisplayOrder = 0 });

        var response = await _client.GetAsync("/coded-values");
        var items = await response.Content.ReadFromJsonAsync<CodedValueDto[]>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        items.Should().NotBeNull();
        items!.Should().Contain(x => x.Code == code);
    }

    [TestMethod]
    public async Task GET_CodedValuesById_ReturnsCorrectItem()
    {
        var code = $"BYID_{Guid.NewGuid():N}".ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = code, Name = "By Id Test", DisplayOrder = 0 });
        var roots = await _client.GetFromJsonAsync<CodedValueDto[]>("/coded-values");
        var created = roots!.Single(x => x.Code == code);

        var response = await _client.GetAsync($"/coded-values/{created.Id}");
        var item = await response.Content.ReadFromJsonAsync<CodedValueDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        item!.Id.Should().Be(created.Id);
        item.Code.Should().Be(code);
    }

    [TestMethod]
    public async Task GET_CodedValuesById_NotFound_Returns404()
    {
        var response = await _client.GetAsync($"/coded-values/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task GET_ByParent_ReturnsChildrenOnly()
    {
        var parentCode = $"PAR_{Guid.NewGuid():N}".ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = parentCode, Name = "Parent", DisplayOrder = 0 });
        var parent = (await _client.GetFromJsonAsync<CodedValueDto[]>("/coded-values"))!
            .Single(x => x.Code == parentCode);

        var childCode = $"CHD_{Guid.NewGuid():N}".ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = childCode, Name = "Child", ParentId = parent.Id, DisplayOrder = 0 });

        var response = await _client.GetAsync($"/coded-values/by-parent?parentId={parent.Id}");
        var children = await response.Content.ReadFromJsonAsync<CodedValueDto[]>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        children!.Should().ContainSingle(x => x.Code == childCode);
    }

    [TestMethod]
    public async Task GET_ByParent_ByParentCode_ReturnsChildren()
    {
        var parentCode = $"PARCODE_{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = parentCode, Name = "Parent", DisplayOrder = 0 });
        var parent = (await _client.GetFromJsonAsync<CodedValueDto[]>("/coded-values"))!
            .Single(x => x.Code == parentCode);

        var childCode = $"CHDCODE_{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = childCode, Name = "Child", ParentId = parent.Id, DisplayOrder = 0 });

        var response = await _client.GetAsync($"/coded-values/by-parent?parentCode={parentCode}");
        var children = await response.Content.ReadFromJsonAsync<CodedValueDto[]>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        children!.Should().Contain(x => x.Code == childCode);
    }

    [TestMethod]
    public async Task PUT_CodedValues_UpdatesItem()
    {
        var code = $"UPD_{Guid.NewGuid():N}".ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = code, Name = "Original", DisplayOrder = 0 });
        var items = await _client.GetFromJsonAsync<CodedValueDto[]>("/coded-values");
        var item = items!.Single(x => x.Code == code);

        var response = await _client.PutAsJsonAsync($"/coded-values/{item.Id}", new { Name = "Updated", Description = "new desc", DisplayOrder = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await _client.GetFromJsonAsync<CodedValueDto>($"/coded-values/{item.Id}");
        updated!.Name.Should().Be("Updated");
    }

    [TestMethod]
    public async Task DisableAndEnable_ToggleIsDisabled()
    {
        var code = $"DIS_{Guid.NewGuid():N}".ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = code, Name = "Disable Test", DisplayOrder = 0 });
        var items = await _client.GetFromJsonAsync<CodedValueDto[]>("/coded-values");
        var item = items!.Single(x => x.Code == code);

        var disableResponse = await _client.PostAsync($"/coded-values/{item.Id}/disable", null);
        disableResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var disabled = await _client.GetFromJsonAsync<CodedValueDto>($"/coded-values/{item.Id}");
        disabled!.IsDisabled.Should().BeTrue();

        var enableResponse = await _client.PostAsync($"/coded-values/{item.Id}/enable", null);
        enableResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var enabled = await _client.GetFromJsonAsync<CodedValueDto>($"/coded-values/{item.Id}");
        enabled!.IsDisabled.Should().BeFalse();
    }

    [TestMethod]
    public async Task Attributes_SetAndRemove()
    {
        var code = $"ATTR_{Guid.NewGuid():N}".ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = code, Name = "Attr Test", DisplayOrder = 0 });
        var items = await _client.GetFromJsonAsync<CodedValueDto[]>("/coded-values");
        var item = items!.Single(x => x.Code == code);

        var setResp = await _client.PutAsJsonAsync($"/coded-values/{item.Id}/attributes/country", new { Value = "US" });
        setResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var withAttr = await _client.GetFromJsonAsync<CodedValueDto>($"/coded-values/{item.Id}");
        withAttr!.Attributes.Should().ContainSingle(a => a.Key == "country" && a.Value == "US");

        var removeResp = await _client.DeleteAsync($"/coded-values/{item.Id}/attributes/country");
        removeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var withoutAttr = await _client.GetFromJsonAsync<CodedValueDto>($"/coded-values/{item.Id}");
        withoutAttr!.Attributes.Should().NotContain(a => a.Key == "country");
    }

    [TestMethod]
    public async Task GET_ByIds_ReturnsMixedDisabledState()
    {
        var code1 = $"ID1_{Guid.NewGuid():N}".ToUpperInvariant();
        var code2 = $"ID2_{Guid.NewGuid():N}".ToUpperInvariant();
        await _client.PostAsJsonAsync("/coded-values", new { Code = code1, Name = "Active Item", DisplayOrder = 0 });
        await _client.PostAsJsonAsync("/coded-values", new { Code = code2, Name = "Disabled Item", DisplayOrder = 0 });

        var items = await _client.GetFromJsonAsync<CodedValueDto[]>("/coded-values");
        var item1 = items!.Single(x => x.Code == code1);
        var item2 = items.Single(x => x.Code == code2);

        await _client.PostAsync($"/coded-values/{item2.Id}/disable", null);

        var response = await _client.GetAsync($"/coded-values/by-ids?ids={item1.Id}&ids={item2.Id}");
        var results = await response.Content.ReadFromJsonAsync<CodedValueDto[]>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        results!.Should().HaveCount(2);
        results.Should().Contain(x => x.Code == code1 && !x.IsDisabled);
        results.Should().Contain(x => x.Code == code2 && x.IsDisabled);
    }
}
