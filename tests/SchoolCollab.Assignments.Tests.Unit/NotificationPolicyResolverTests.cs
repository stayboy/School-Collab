using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round notification-templates-b2a (Q49) — <see cref="NotificationPolicyResolver"/> fail-open
/// hardening: transport failures, timeouts, non-JSON 200s and 5xx responses from the Settings
/// and Students APIs all degrade to the built-in default. Genuine caller cancellation still
/// propagates instead of being swallowed.
/// </summary>
[TestClass]
public class NotificationPolicyResolverTests
{
    private static NotificationPolicyResolver NewResolver(HttpMessageHandler handler) =>
        new(new StubFactory(handler), NullLogger<NotificationPolicyResolver>.Instance);

    [TestMethod]
    public async Task Timeout_DegradesToBuiltInDefault()
    {
        var resolver = NewResolver(new TimeoutHandler());

        var policy = await resolver.ResolveEffectiveAsync(Guid.NewGuid(), null);

        policy.Should().BeEquivalentTo(new EffectiveNotificationPolicyResolver().Resolve(null, null));
    }

    [TestMethod]
    public async Task NonJson200_DegradesToBuiltInDefault()
    {
        var resolver = NewResolver(new ScriptedHandler(() =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json")
            }));

        var policy = await resolver.ResolveEffectiveAsync(Guid.NewGuid(), null);

        policy.Should().BeEquivalentTo(new EffectiveNotificationPolicyResolver().Resolve(null, null));
    }

    [TestMethod]
    public async Task ServerError_DegradesToBuiltInDefault()
    {
        var resolver = NewResolver(new ScriptedHandler(() =>
            new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var policy = await resolver.ResolveEffectiveAsync(Guid.NewGuid(), null);

        policy.Should().BeEquivalentTo(new EffectiveNotificationPolicyResolver().Resolve(null, null));
    }

    [TestMethod]
    public async Task GenuineCancellation_Propagates()
    {
        var resolver = NewResolver(new HangingHandler());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => resolver.ResolveEffectiveAsync(Guid.NewGuid(), null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("http://localhost") };
    }

    private sealed class ScriptedHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond());
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new TaskCanceledException("simulated settings/students timeout");
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        }
    }
}
