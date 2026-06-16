using System.Net;
using System.Text.Json;
using FluentAssertions;
using AdminApi = SchoolCollab.CodedValues.Admin.Services.CodedValuesApiClient;
using AdminDto = SchoolCollab.CodedValues.Admin.Services.CodedValueDto;
using AiApi = SchoolCollab.AI.Services.CodedValuesApiClient;
using AiDto = SchoolCollab.AI.Services.CodedValueDto;

namespace SchoolCollab.CodedValues.Tests.Unit;

/// <summary>
/// Tests that GetByCodeAsync and GetByIdAsync return null on 404 responses
/// instead of throwing HttpRequestException.
/// Regression test for: creating a new parent coded value threw
/// CodedValueNotFoundException because GetFromJsonAsync throws on 404.
/// </summary>
[TestClass]
public class CodedValuesApiClientNotFoundTests
{
    private static AdminDto SampleAdminDto() => new(
        Id: Guid.NewGuid(),
        Code: "TEST",
        Name: "Test Value",
        Description: null,
        ParentId: null,
        ParentCode: null,
        IsDisabled: false,
        DisplayOrder: 0,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        Attributes: [],
        AttributeDefinitions: [],
        ChildrenCount: 0);

    private static AiDto SampleAiDto() => new(
        Id: Guid.NewGuid(),
        Code: "TEST",
        Name: "Test Value",
        Description: null,
        ParentId: null,
        ParentCode: null,
        IsDisabled: false,
        DisplayOrder: 0,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        Attributes: [],
        AttributeDefinitions: [],
        ChildrenCount: 0);

    // --- Admin CodedValuesApiClient tests ---

    [TestMethod]
    public async Task Admin_GetByCodeAsync_ReturnsNullOn404()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "");
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AdminApi(http);

        var result = await client.GetByCodeAsync("NOTEXIST", ct: CancellationToken.None);

        result.Should().BeNull("404 should return null, not throw");
        handler.Requests.Should().ContainSingle(r => r.RequestUri!.PathAndQuery == "/coded-values/by-code/NOTEXIST");
    }

    [TestMethod]
    public async Task Admin_GetByCodeAsync_ReturnsDtoOn200()
    {
        var dto = SampleAdminDto();
        var json = JsonSerializer.Serialize(dto);
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AdminApi(http);

        var result = await client.GetByCodeAsync("TEST", ct: CancellationToken.None);

        result.Should().NotBeNull();
        result!.Code.Should().Be("TEST");
    }

    [TestMethod]
    public async Task Admin_GetByIdAsync_ReturnsNullOn404()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "");
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AdminApi(http);

        var result = await client.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull("404 should return null, not throw");
    }

    [TestMethod]
    public async Task Admin_GetByIdAsync_ReturnsDtoOn200()
    {
        var dto = SampleAdminDto();
        var json = JsonSerializer.Serialize(dto);
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AdminApi(http);

        var result = await client.GetByIdAsync(dto.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(dto.Id);
    }

    [TestMethod]
    public async Task Admin_GetByCodeAsync_ThrowsOn500()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "server error");
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AdminApi(http);

        var act = () => client.GetByCodeAsync("TEST", ct: CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>("500 should still throw");
    }

    // --- AI CodedValuesApiClient tests ---

    [TestMethod]
    public async Task AI_GetByCodeAsync_ReturnsNullOn404()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "");
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AiApi(http);

        var result = await client.GetByCodeAsync("NOTEXIST", ct: CancellationToken.None);

        result.Should().BeNull("404 should return null, not throw");
    }

    [TestMethod]
    public async Task AI_GetByCodeAsync_ReturnsDtoOn200()
    {
        var dto = SampleAiDto();
        var json = JsonSerializer.Serialize(dto);
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AiApi(http);

        var result = await client.GetByCodeAsync("TEST", ct: CancellationToken.None);

        result.Should().NotBeNull();
        result!.Code.Should().Be("TEST");
    }

    [TestMethod]
    public async Task AI_GetByIdAsync_ReturnsNullOn404()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "");
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AiApi(http);

        var result = await client.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull("404 should return null, not throw");
    }

    [TestMethod]
    public async Task AI_GetByIdAsync_ReturnsDtoOn200()
    {
        var dto = SampleAiDto();
        var json = JsonSerializer.Serialize(dto);
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AiApi(http);

        var result = await client.GetByIdAsync(dto.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(dto.Id);
    }

    [TestMethod]
    public async Task AI_GetByCodeAsync_ThrowsOn500()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "server error");
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AiApi(http);

        var act = () => client.GetByCodeAsync("TEST", ct: CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>("500 should still throw");
    }

    // --- Mock HttpMessageHandler ---

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;
        public List<HttpRequestMessage> Requests { get; } = [];

        public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}