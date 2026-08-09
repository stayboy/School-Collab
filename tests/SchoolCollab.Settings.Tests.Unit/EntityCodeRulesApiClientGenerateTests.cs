using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Route/contract tests for the topic-create dialog's "regenerate template
/// code" endpoint: <c>EntityCodeRulesApiClient.GenerateAsync</c> issues the
/// correct HTTP method, URL, and JSON body to
/// <c>POST /api/entity-code-rules/generate</c> and parses the returned code.
/// </summary>
[TestClass]
public class EntityCodeRulesApiClientGenerateTests
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

    private static EntityCodeRulesApiClient NewClient(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

    [TestMethod]
    public async Task GenerateAsync_PostsRuleCodeAndNameHint_AndReturnsCode()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { code = "CS01" }), Encoding.UTF8, "application/json")
        });
        var client = NewClient(handler);

        var code = await client.GenerateAsync("TOPIC_CODE", "computer science");

        code.Should().Be("CS01");
        var call = handler.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.Should().Be("/api/entity-code-rules/generate");
        var json = JsonDocument.Parse(call.Body!).RootElement;
        json.GetProperty("ruleCode").GetString().Should().Be("TOPIC_CODE");
        json.GetProperty("nameHint").GetString().Should().Be("computer science");
    }

    [TestMethod]
    public async Task GenerateAsync_WithNullNameHint_OmitsNameHint()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { code = "MATH01" }), Encoding.UTF8, "application/json")
        });
        var client = NewClient(handler);

        var code = await client.GenerateAsync("TOPIC_CODE", null);

        code.Should().Be("MATH01");
        var json = JsonDocument.Parse(handler.Calls.Single().Body!).RootElement;
        json.GetProperty("ruleCode").GetString().Should().Be("TOPIC_CODE");
        json.GetProperty("nameHint").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
