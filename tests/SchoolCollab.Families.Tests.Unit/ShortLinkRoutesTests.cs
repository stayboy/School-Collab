using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RichardSzalay.MockHttp;
using SchoolCollab.Families.DeepLinks;
using SchoolCollab.Families.Services;

namespace SchoolCollab.Families.Tests.Unit;

/// <summary>
/// Round notification-templates-b2a (spec §3.6, WS1) — the Families minimal-API route that serves the
/// public <c>/l/{code}</c> short link. The route is deliberately outside the Razor Components
/// authorization gate and requires no tenant or credentials: the code in the URL is the credential.
/// </summary>
[TestClass]
public class ShortLinkRoutesTests
{
    private const string Code = "AbCdEfGh12345678";
    private const string Token = "a-real-deep-link-token";
    private static readonly string Url = $"/l/{Code}";
    private static readonly string ResolutionUrl = $"/short-links/{Code}";

    private static async Task<WebApplication> StartHostAsync(HttpMessageHandler handler)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        builder.Services.AddSingleton(http);
        builder.Services.AddSingleton<ShortLinkLandingService>(sp => new ShortLinkLandingService(
            new ShortLinkResolverClient(sp.GetRequiredService<HttpClient>(),
                NullLogger<ShortLinkResolverClient>.Instance),
            NullLogger<ShortLinkLandingService>.Instance));

        var app = builder.Build();
        app.MapGroup("/l").MapShortLinkRoutes();
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    [TestMethod]
    public async Task LiveCode_302s_ToDeepLink()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost" + ResolutionUrl)
            .Respond(HttpStatusCode.OK, "application/json", $$"""{"deepLinkToken":"{{Token}}"}""");

        await using var app = await StartHostAsync(mockHttp);
        var client = CreateClient(app);

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Be(ShortLinkLanding.DeepLinkPath(Token));
    }

    [TestMethod]
    public async Task UnknownCode_302s_ToExpiredPage()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost" + ResolutionUrl)
            .Respond(HttpStatusCode.NotFound);

        await using var app = await StartHostAsync(mockHttp);
        var client = CreateClient(app);

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Be(ShortLinkLanding.ExpiredPath);
    }

    [TestMethod]
    public async Task RouteWorks_WithoutAuthentication_OrTenant()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost" + ResolutionUrl)
            .Respond(HttpStatusCode.OK, "application/json", $$"""{"deepLinkToken":"{{Token}}"}""");

        await using var app = await StartHostAsync(mockHttp);
        var client = CreateClient(app);
        // No auth header, no tenant header, no cookie — the route is anonymous by design.

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }
}
