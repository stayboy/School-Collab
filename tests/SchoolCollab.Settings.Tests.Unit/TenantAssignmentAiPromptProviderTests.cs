using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SchoolCollab.AI.Services;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// WS-B2 (spec §3.4 line 91) — fail-open behavior of the AI host's
/// <see cref="TenantAssignmentAiPromptProvider"/>: 204/404 ⇒ null, a valid 200 ⇒
/// the mapped DTO, and a network failure ⇒ null (never throws, so a Settings
/// outage cannot block question generation).
/// </summary>
[TestClass]
public class TenantAssignmentAiPromptProviderTests
{
    [TestMethod]
    public async Task FetchAsync_ReturnsNullOn204()
    {
        var provider = NewProvider(new ScriptedHandler("", HttpStatusCode.NoContent));

        (await provider.FetchAsync()).Should().BeNull();
    }

    [TestMethod]
    public async Task FetchAsync_ReturnsNullOn404()
    {
        var provider = NewProvider(new ScriptedHandler("", HttpStatusCode.NotFound));

        (await provider.FetchAsync()).Should().BeNull();
    }

    [TestMethod]
    public async Task FetchAsync_MapsDtoOn200()
    {
        var body = JsonSerializer.Serialize(new { systemPrompt = "Org prompt.", isLocked = true });
        var provider = NewProvider(new ScriptedHandler(body, HttpStatusCode.OK));

        var result = await provider.FetchAsync();

        result.Should().NotBeNull();
        result!.SystemPrompt.Should().Be("Org prompt.");
        result.IsLocked.Should().BeTrue();
    }

    [TestMethod]
    public async Task FetchAsync_FailsOpenOnNetworkError()
    {
        var provider = NewProvider(new FaultingHandler());

        var result = await provider.FetchAsync();

        result.Should().BeNull("fail-open — a network failure must not block question generation (WS-B2)");
    }

    private static TenantAssignmentAiPromptProvider NewProvider(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://settings") },
            new TestLogger<TenantAssignmentAiPromptProvider>());

    private sealed class ScriptedHandler(string? body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? "", Encoding.UTF8, "application/json")
            });
    }

    private sealed class FaultingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("boom");
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
