using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Application.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-B2 (spec §3.4 / decision g) — <see cref="UrlTextExtractor"/>: scheme guard
/// (http/https only), script/style/noscript stripping, 20k-char truncation, and the
/// fail-open posture (non-success status / timeout / network error → Success=false
/// with a friendly error).
/// </summary>
[TestClass]
public class UrlTextExtractorTests
{
    private static IHttpClientFactory Factory(HttpMessageHandler handler) => new StubHttpClientFactory(handler);

    [TestMethod]
    public async Task Rejects_NonHttpScheme()
    {
        var sut = new UrlTextExtractor(Factory(new ScriptedHandler(() => new HttpResponseMessage(HttpStatusCode.OK))),
            NullLogger<UrlTextExtractor>.Instance);

        var result = await sut.ExtractAsync("ftp://example.com/file");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("http and https");
    }

    [TestMethod]
    public async Task Extracts_StripsScriptAndStyle()
    {
        var html =
            "<html><head><style>body{color:red}</style><script>alert('x')</script><noscript>nojs</noscript></head>" +
            "<body><h1>Hello</h1><p>World</p></body></html>";
        var sut = new UrlTextExtractor(
            Factory(new ScriptedHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) })),
            NullLogger<UrlTextExtractor>.Instance);

        var result = await sut.ExtractAsync("https://example.com/page");

        result.Success.Should().BeTrue();
        result.Text.Should().Contain("Hello");
        result.Text.Should().Contain("World");
        result.Text.Should().NotContain("alert");
        result.Text.Should().NotContain("color");
        result.Text.Should().NotContain("nojs");
    }

    [TestMethod]
    public async Task Truncates_AtCap()
    {
        var longText = new string('a', 30_000);
        var sut = new UrlTextExtractor(
            Factory(new ScriptedHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"<p>{longText}</p>") })),
            NullLogger<UrlTextExtractor>.Instance);

        var result = await sut.ExtractAsync("https://example.com/long");

        result.Success.Should().BeTrue();
        result.Text!.Length.Should().Be(20_000);
    }

    [TestMethod]
    public async Task FailsOpen_OnHttpError()
    {
        var sut = new UrlTextExtractor(
            Factory(new ScriptedHandler(() => new HttpResponseMessage(HttpStatusCode.NotFound))),
            NullLogger<UrlTextExtractor>.Instance);

        var result = await sut.ExtractAsync("https://example.com/missing");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("404");
    }

    [TestMethod]
    public async Task FailsOpen_OnTimeout()
    {
        var sut = new UrlTextExtractor(Factory(new TimeoutHandler()), NullLogger<UrlTextExtractor>.Instance);

        var result = await sut.ExtractAsync("https://example.com/slow");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("too long");
    }

    [TestMethod]
    public async Task FailsOpen_OnNetworkError()
    {
        var sut = new UrlTextExtractor(
            Factory(new ScriptedHandler(() => throw new HttpRequestException("unreachable"))),
            NullLogger<UrlTextExtractor>.Instance);

        var result = await sut.ExtractAsync("https://example.com/unreachable");

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class ScriptedHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond());
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new TaskCanceledException("client timeout");
    }
}
