using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RichardSzalay.MockHttp;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Families.DeepLinks;
using SchoolCollab.Families.Services;

namespace SchoolCollab.Families.Tests.Unit;

/// <summary>
/// Round notification-templates-b2a (spec §3.6, WS1) — the Families host's public <c>/l/{code}</c>
/// landing. Covers the <see cref="ShortLinkLandingService"/>, which resolves the code through the
/// tenant-less <see cref="ShortLinkResolverClient"/>, and the minimal-API <see cref="ShortLinkRoutes"/>
/// mapper that serves the 302 / expired-page redirect.
/// </summary>
[TestClass]
public class ShortLinkLandingServiceTests
{
    private const string Code = "AbCdEfGh12345678";
    private const string Token = "a-real-deep-link-token";
    private static readonly string ResolutionUrl = $"/short-links/{Code}";

    /// <summary>Builds a <see cref="ShortLinkLandingService"/></summary>
    private static ShortLinkLandingService Build(MockHttpMessageHandler mockHttp)
    {
        var http = mockHttp.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        var client = new ShortLinkResolverClient(http, NullLogger<ShortLinkResolverClient>.Instance);
        return new ShortLinkLandingService(client, NullLogger<ShortLinkLandingService>.Instance);
    }

    [TestMethod]
    public async Task LiveCode_Resolves_ToDeepLinkPath()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost" + ResolutionUrl)
            .Respond(HttpStatusCode.OK, "application/json", $$"""{"deepLinkToken":"{{Token}}"}""");
        var service = Build(mockHttp);

        var redirect = await service.ResolveRedirectAsync(Code);

        redirect.Should().Be(ShortLinkLanding.DeepLinkPath(Token));
    }

    [TestMethod]
    public async Task UnknownCode_LandsOnExpiredPage()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost" + ResolutionUrl)
            .Respond(HttpStatusCode.NotFound);
        var service = Build(mockHttp);

        var redirect = await service.ResolveRedirectAsync(Code);

        redirect.Should().Be(ShortLinkLanding.ExpiredPath);
    }

    [TestMethod]
    public async Task ExpiredCode_LandsOnExpiredPage()
    {
        // The resolution API answers 404 for both "never existed" and "expired", and the landing
        // maps both to the same friendly page.
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost" + ResolutionUrl)
            .Respond(HttpStatusCode.NotFound);
        var service = Build(mockHttp);

        var redirect = await service.ResolveRedirectAsync(Code);

        redirect.Should().Be(ShortLinkLanding.ExpiredPath);
    }

    [TestMethod]
    public async Task MalformedCode_ShortCircuitsToExpiredPage()
    {
        // A code that is not the expected length / character set costs no cross-service round trip.
        var mockHttp = new MockHttpMessageHandler();
        var service = Build(mockHttp);

        var redirect = await service.ResolveRedirectAsync("too-short");

        redirect.Should().Be(ShortLinkLanding.ExpiredPath);
    }

    [TestMethod]
    public void IsWellFormed_AcceptsOnlyUrlSafeLength16()
    {
        ShortLinkLandingService.IsWellFormed(Code).Should().BeTrue();
        ShortLinkLandingService.IsWellFormed(Code + "x").Should().BeFalse("17 characters is not the contract length");
        ShortLinkLandingService.IsWellFormed("AbCdEfGh1234567!").Should().BeFalse("non-URL-safe character is rejected");
        ShortLinkLandingService.IsWellFormed("").Should().BeFalse();
        ShortLinkLandingService.IsWellFormed(null).Should().BeFalse();
    }
}
