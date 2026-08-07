using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SchoolCollab.Admin.Shared.Services;
using AdminApi = SchoolCollab.Admin.Shared.Services.CodedValuesApiClient;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Route/contract tests for the tcv/3 provisional coded-value endpoints: the client
/// issues the correct HTTP method, URL, and JSON body for create / list / approve /
/// reject.
/// </summary>
[TestClass]
public class CodedValuesApiClientProvisionalTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body)> Calls { get; } = [];
        private readonly HttpResponseMessage _response;
        public RecordingHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null
                ? null
                : request.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            return Task.FromResult(_response);
        }
    }

    private static AdminApi NewClient(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

    [TestMethod]
    public async Task CreateProvisionalAsync_PostsJsonBodyToProvisionalRoute()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { id }), Encoding.UTF8, "application/json")
        });
        var client = NewClient(handler);

        var result = await client.CreateProvisionalCodedValueAsync(
            new CreateProvisionalCodedValueRequest("CS01", "Computer Science", "Intro", null));

        result.Should().Be(id);
        var call = handler.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.Should().Be("/api/coded-values/provisional");
        var json = JsonDocument.Parse(call.Body!).RootElement;
        json.GetProperty("code").GetString().Should().Be("CS01");
        json.GetProperty("name").GetString().Should().Be("Computer Science");
        json.GetProperty("description").GetString().Should().Be("Intro");
    }

    [TestMethod]
    public async Task ListProvisionalAsync_GetsProvisionalRoute()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        });
        var client = NewClient(handler);

        await client.ListProvisionalCodedValuesAsync();

        var call = handler.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Get);
        call.Url.Should().Be("/api/coded-values/provisional");
    }

    [TestMethod]
    public async Task ApproveProvisionalAsync_PostsApproveAction()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = NewClient(handler);

        await client.ApproveProvisionalCodedValueAsync(id);

        var call = handler.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.Should().Be($"/api/coded-values/provisional/{id}/approve");
    }

    [TestMethod]
    public async Task RejectProvisionalAsync_PostsRejectAction()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = NewClient(handler);

        await client.RejectProvisionalCodedValueAsync(id);

        var call = handler.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.Should().Be($"/api/coded-values/provisional/{id}/reject");
    }
}
