using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round notification-templates-b2a (Q49) — <see cref="AssignmentPolicyResolver"/> fail-open
/// hardening: transport failures, timeouts, non-JSON 200s and 5xx responses from the Settings
/// and Students APIs all degrade to "nothing configured" (built-in defaults apply). Genuine
/// caller cancellation still propagates instead of being swallowed.
/// <para>Lives in the Api test project because the resolver does — the sibling
/// <see cref="AssignmentPolicyResolverTests"/> covers its wire/policy behaviour.</para>
/// </summary>
[TestClass]
public class AssignmentPolicyResolverHardeningTests
{
    private static AssignmentPolicyResolver NewResolver(HttpMessageHandler handler) =>
        new(new StubFactory(handler), NullLogger<AssignmentPolicyResolver>.Instance);

    [TestMethod]
    public async Task Timeout_DegradesToBuiltInDefaults()
    {
        var resolver = NewResolver(new TimeoutHandler());

        var policy = await resolver.ResolveAsync(null);

        policy.Should().BeEquivalentTo(new EffectiveAssignmentPolicyResolver().Resolve(null, null));
    }

    [TestMethod]
    public async Task NonJson200_DegradesToBuiltInDefaults()
    {
        var resolver = NewResolver(new ScriptedHandler(() =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json")
            }));

        var policy = await resolver.ResolveAsync(null);

        policy.Should().BeEquivalentTo(new EffectiveAssignmentPolicyResolver().Resolve(null, null));
    }

    [TestMethod]
    public async Task ServerError_DegradesToBuiltInDefaults()
    {
        var resolver = NewResolver(new ScriptedHandler(() =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var policy = await resolver.ResolveAsync(null);

        policy.Should().BeEquivalentTo(new EffectiveAssignmentPolicyResolver().Resolve(null, null));
    }

    [TestMethod]
    public async Task GenuineCancellation_Propagates()
    {
        var resolver = NewResolver(new HangingHandler());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => resolver.ResolveAsync(null, cts.Token);
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

    /// <summary>Throws a <see cref="TaskCanceledException"/> while the passed token is still
    /// alive, exercising the resolver's "timeout" catch path (outer token not cancelled).</summary>
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
