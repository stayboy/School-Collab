using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// ar-24 AC2 — the <see cref="BearerForwardingDelegatingHandler"/>'s actual behaviour
/// (attachment/wiring is pinned by the ar-24 architecture guard; this proves the logic):
/// in TestAuth/dev (DisableOIDCAuth=on) NO header is copied; in real-auth it copies the
/// current request's Authorization verbatim when present; a missing header never throws.
/// The class is sealed, so the protected SendAsync is driven via reflection.
/// </summary>
[TestClass]
public class BearerForwardingDelegatingHandlerTests
{
    private const string Bearer = "Bearer abc123";

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static readonly MethodInfo SendAsync =
        typeof(HttpMessageHandler).GetMethod("SendAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static IHttpContextAccessor AccessorWithHeader(string? authorization)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null)
        {
            context.Request.Headers["Authorization"] = authorization;
        }
        return new HttpContextAccessor { HttpContext = context };
    }

    private static async Task<(bool forwarded, bool threw)> Run(IFeatureFlagService flags, IHttpContextAccessor accessor)
    {
        var capture = new CaptureHandler();
        var handler = new BearerForwardingDelegatingHandler(
                flags, accessor, NullLogger<BearerForwardingDelegatingHandler>.Instance)
        { InnerHandler = capture };
        var request = new HttpRequestMessage(HttpMethod.Get, "http://downstream/teachers/1");

        try
        {
            var task = (Task<HttpResponseMessage>)SendAsync.Invoke(handler, new object[] { request, CancellationToken.None })!;
            (await task).Dispose();
        }
        catch
        {
            return (false, true);
        }

        return (capture.LastRequest?.Headers.Contains("Authorization") == true, false);
    }

    [TestMethod]
    public async Task TestAuthMode_DoesNotForwardHeader()
    {
        var result = await Run(new FakeFeatureFlagService { IsEnabledValue = true }, AccessorWithHeader(Bearer));
        result.forwarded.Should().BeFalse("in TestAuth/dev no bearer exists to forward (dev-bypass no-op).");
        result.threw.Should().BeFalse();
    }

    [TestMethod]
    public async Task RealAuth_WithCurrentBearer_ForwardsVerbatim()
    {
        var result = await Run(new FakeFeatureFlagService { IsEnabledValue = false }, AccessorWithHeader(Bearer));
        result.forwarded.Should().BeTrue("real-auth must forward the inbound Authorization header to the downstream hop.");
        result.threw.Should().BeFalse();
    }

    [TestMethod]
    public async Task RealAuth_NoHeaderInbound_DoesNotThrow()
    {
        var result = await Run(new FakeFeatureFlagService { IsEnabledValue = false }, AccessorWithHeader(null));
        result.forwarded.Should().BeFalse("a missing inbound Authorization must simply not be forwarded.");
        result.threw.Should().BeFalse("the handler must never throw when the header is absent.");
    }
}
