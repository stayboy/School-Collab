using System.Net;
using System.Text.Json;
using FluentAssertions;
using AdminApi = SchoolCollab.Admin.Shared.Services.CodedValuesApiClient;
using AdminDto = SchoolCollab.Admin.Shared.Services.CodedValueDto;
using AiApi = SchoolCollab.AI.Tools.CodedValues.CodedValuesApiClient;
using AiDto = SchoolCollab.AI.Tools.CodedValues.CodedValueDto;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// The two CodedValues API clients (Admin shell + AI tools) share one error contract: return
/// <c>null</c> on 404 instead of throwing, return the DTO on 200, and still throw on 500.
/// Regression test for: creating a new parent coded value threw
/// <c>CodedValueNotFoundException</c> because <c>GetFromJsonAsync</c> throws on 404.
/// <para>
/// One behaviour per test, with both clients asserted inside it. The pair used to be twelve
/// methods (<c>Admin_*</c> + <c>AI_*</c> per behaviour) — pruning round 2026-10-17
/// (<c>round-codedvalue-test-pruning</c> Q23). The two clients have distinct DTO types, so a
/// <c>[DataRow]</c> factory switch would obscure more than it saves; asserting both in one body
/// keeps the coverage and makes a client that drifts the contract fail the same test.
/// </para>
/// </summary>
[TestClass]
public class CodedValuesApiClientNotFoundTests
{
    private static AdminDto SampleAdminDto(Guid id) => new(
        Id: id,
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

    private static AiDto SampleAiDto(Guid id) => new(
        Id: id,
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

    private static HttpClient Http(MockHttpMessageHandler handler)
        => new(handler) { BaseAddress = new Uri("http://localhost") };

    // ── 404 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetByCodeAsync_ReturnsNullOn404()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "");
        var http = Http(handler);

        var admin = await new AdminApi(http).GetByCodeAsync("NOTEXIST", ct: CancellationToken.None);
        var ai = await new AiApi(http).GetByCodeAsync("NOTEXIST", ct: CancellationToken.None);

        admin.Should().BeNull("404 should return null, not throw (Admin client)");
        ai.Should().BeNull("404 should return null, not throw (AI client)");
        handler.Requests.Should().OnlyContain(r =>
            r.RequestUri!.PathAndQuery == "/api/coded-values/by-code/NOTEXIST",
            "both clients must call the same endpoint");
    }

    [TestMethod]
    public async Task GetByIdAsync_ReturnsNullOn404()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "");
        var http = Http(handler);
        var id = Guid.NewGuid();

        var admin = await new AdminApi(http).GetByIdAsync(id, CancellationToken.None);
        var ai = await new AiApi(http).GetByIdAsync(id, CancellationToken.None);

        admin.Should().BeNull("404 should return null, not throw (Admin client)");
        ai.Should().BeNull("404 should return null, not throw (AI client)");
    }

    // ── 200 ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetByCodeAsync_ReturnsDtoOn200()
    {
        var id = Guid.NewGuid();
        var adminHandler = new MockHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(SampleAdminDto(id)));
        var aiHandler = new MockHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(SampleAiDto(id)));

        var admin = await new AdminApi(Http(adminHandler)).GetByCodeAsync("TEST", ct: CancellationToken.None);
        var ai = await new AiApi(Http(aiHandler)).GetByCodeAsync("TEST", ct: CancellationToken.None);

        admin.Should().NotBeNull();
        admin!.Code.Should().Be("TEST");
        ai.Should().NotBeNull();
        ai!.Code.Should().Be("TEST");
    }

    [TestMethod]
    public async Task GetByIdAsync_ReturnsDtoOn200()
    {
        var id = Guid.NewGuid();
        var adminHandler = new MockHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(SampleAdminDto(id)));
        var aiHandler = new MockHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(SampleAiDto(id)));

        var admin = await new AdminApi(Http(adminHandler)).GetByIdAsync(id, CancellationToken.None);
        var ai = await new AiApi(Http(aiHandler)).GetByIdAsync(id, CancellationToken.None);

        admin.Should().NotBeNull();
        admin!.Id.Should().Be(id);
        ai.Should().NotBeNull();
        ai!.Id.Should().Be(id);
    }

    // ── 500 still throws ───────────────────────────────────────────────────

    [TestMethod]
    public async Task GetByCodeAsync_ThrowsOn500()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "server error");
        var http = Http(handler);

        var adminAct = () => new AdminApi(http).GetByCodeAsync("TEST", ct: CancellationToken.None);
        var aiAct = () => new AiApi(http).GetByCodeAsync("TEST", ct: CancellationToken.None);

        await adminAct.Should().ThrowAsync<HttpRequestException>("500 should still throw (Admin client)");
        await aiAct.Should().ThrowAsync<HttpRequestException>("500 should still throw (AI client)");
    }

    // ── Admin-only surfaces ────────────────────────────────────────────────

    [TestMethod]
    public async Task Admin_GetChildrenByParentCodeAsync_CallsCorrectEndpoint()
    {
        var dto = SampleAdminDto(Guid.NewGuid());
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new[] { dto }));

        var result = await new AdminApi(Http(handler)).GetChildrenByParentCodeAsync("PARENT_CODE", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Should().ContainSingle();
        handler.Requests.Should().ContainSingle(r =>
            r.RequestUri!.PathAndQuery == "/api/coded-values/by-parent?parentCode=PARENT_CODE");
    }

    [TestMethod]
    public async Task Admin_UpsertOverrideAsync_SendsCodeInBody()
    {
        var dto = SampleAdminDto(Guid.NewGuid());
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, JsonSerializer.Serialize(dto));

        var result = await new AdminApi(Http(handler))
            .UpsertOverrideAsync(dto.Id, "New Name", null, "NEWCODE", CancellationToken.None);

        result.Should().NotBeNull();
        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Put);
        req.RequestUri!.PathAndQuery.Should().Be($"/api/coded-values/{dto.Id}/override");
        var body = await req.Content!.ReadAsStringAsync();
        body.Should().Contain("NEWCODE", "the code override is carried in the PUT body");
    }

    // ── Mock HttpMessageHandler ────────────────────────────────────────────

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
