using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Http;
using SchoolCollab.Students.Api.Services;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Api.Tests.Unit;

/// <summary>
/// Issue #444 (layer L2b) — the wire half of the student-view stats panel: <see cref="StudentAssignmentStatsReaderHttpClient"/>.
/// <list type="number">
///   <item>the request path carries the <c>/assignments</c> segment. The named client's base address is the
///       host root, and layer L2a's route lives on the <c>/assignments</c> group, so a path without it would
///       404 — and the panel would render a convincing "unavailable" against a healthy endpoint;</item>
///   <item>the request carries the tenant and the forwarded bearer token. Asserted over the <b>real</b>
///       handler chain registered here exactly as <c>Students.Api/Program.cs</c> registers it — a hand-built
///       <c>HttpClient</c> would carry no handlers and the assertion would be vacuous;</item>
///   <item>all six counts survive a real <c>ReadFromJsonAsync</c> round trip. <c>Students.Core</c> mirrors the
///       Assignments contract by name, and a misspelled member deserializes to <c>0</c> silently — this is the
///       only automated failure that catches it.</item>
/// </list>
/// </summary>
[TestClass]
public class StudentAssignmentStatsReaderHttpClientTests
{
    private static readonly Guid StudentId = Guid.Parse("a0000000-0000-0000-0000-0000000000ff");
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>The named client's configured base address, verbatim from <c>Students.Api/Program.cs</c>.</summary>
    private const string BaseAddress = "https+http://assignments-api";

    private const string BearerToken = "Bearer test-bearer-token";

    /// <summary>Six <b>distinct</b> values, so a member bound to the wrong JSON name is visible rather than
    /// coincidentally equal.</summary>
    private const string StatsJson = """
        {
          "assigned": 24,
          "sentOut": 21,
          "submitted": 18,
          "marked": 15,
          "reviewRequired": 12,
          "reviewed": 10
        }
        """;

    private sealed record CapturedCall(Uri Uri, string TenantHeader, string Authorization);

    /// <summary>The innermost handler: records what survived the delegating chain and answers a fixed body.</summary>
    private sealed class CapturingHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<CapturedCall> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(new CapturedCall(
                request.RequestUri!,
                Header(request, "x-tenant-id"),
                Header(request, "Authorization")));

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }

        private static string Header(HttpRequestMessage request, string name)
            => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;
    }

    /// <summary>Real-auth posture (<c>DisableOIDCAuth</c> off) is the only mode in which a bearer is forwarded
    /// at all — in the dev bypass the handler is a strict no-op.</summary>
    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => false;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private static IHttpContextAccessor InboundRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["x-tenant-id"] = TenantId.ToString();
        context.Request.Headers.Authorization = BearerToken;

        return new HttpContextAccessor { HttpContext = context };
    }

    /// <summary>
    /// The named client registered the way <c>Students.Api/Program.cs</c> registers it — same name, same base
    /// address, same two forwarding handlers — with the capturing handler as the primary one.
    /// </summary>
    private static (StudentAssignmentStatsReaderHttpClient Sut, ServiceProvider Provider) BuildClient(
        IHttpContextAccessor accessor, HttpMessageHandler primary)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(accessor);
        services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        services.TryAddTransient<TenantForwardingDelegatingHandler>();
        services.TryAddTransient<BearerForwardingDelegatingHandler>();

        services.AddCrossModuleHttpClient("assignments-api", BaseAddress, propagateTenant: false)
            .AddHttpMessageHandler<TenantForwardingDelegatingHandler>()
            .AddHttpMessageHandler<BearerForwardingDelegatingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => primary);

        var provider = services.BuildServiceProvider();
        var sut = new StudentAssignmentStatsReaderHttpClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            NullLogger<StudentAssignmentStatsReaderHttpClient>.Instance);

        return (sut, provider);
    }

    [TestMethod]
    public async Task GetAsync_RequestsTheAssignmentsGroupPathOnTheNamedClient()
    {
        var handler = new CapturingHandler(StatsJson);
        var (sut, provider) = BuildClient(InboundRequest(), handler);
        using (provider)
        {
            await sut.GetAsync(StudentId);
        }

        var call = handler.Calls.Should().ContainSingle().Subject;
        call.Uri.Host.Should().Be("assignments-api", "the hop rides the existing named client (Q151)");
        call.Uri.AbsolutePath.Should().Be(
            $"/assignments/{StudentId}/assignment-stats",
            "the /assignments segment is REQUIRED: the named client's base address is the host root, and "
            + "layer L2a's route lives on the /assignments group. Without it the hop 404s and the panel "
            + "reports 'unavailable' against an endpoint that is working");
    }

    [TestMethod]
    public async Task GetAsync_ForwardsTheTenantAndTheBearerThroughTheRealHandlerChain()
    {
        var handler = new CapturingHandler(StatsJson);
        var (sut, provider) = BuildClient(InboundRequest(), handler);
        using (provider)
        {
            await sut.GetAsync(StudentId);
        }

        var call = handler.Calls.Should().ContainSingle().Subject;
        call.TenantHeader.Should().Be(TenantId.ToString(),
            "assignments are strict-tenant entities; a tenant-less hop can resolve the wrong tenant's counts");
        call.Authorization.Should().Be(BearerToken,
            "the receiving API is bearer-gated (ar-24), so the caller's token has to travel with the request");
    }

    [TestMethod]
    public async Task GetAsync_BindsAllSixCountsThroughTheJsonRoundTrip()
    {
        var handler = new CapturingHandler(StatsJson);
        var (sut, provider) = BuildClient(InboundRequest(), handler);
        using (provider)
        {
            var stats = await sut.GetAsync(StudentId);

            // One assertion per member, named: a mirror property that no longer matches the Assignments
            // contract's JSON name would deserialize to 0 here rather than failing to compile.
            stats.Assigned.Should().Be(24);
            stats.SentOut.Should().Be(21, "a misspelled 'SentOut' binds to 0, never to an error");
            stats.Submitted.Should().Be(18);
            stats.Marked.Should().Be(15);
            stats.ReviewRequired.Should().Be(12, "the mirror's names must match the Assignments contract exactly");
            stats.Reviewed.Should().Be(10);
        }
    }

    [TestMethod]
    public async Task GetAsync_ThrowsWhenTheAssignmentsApiAnswersNonSuccess()
    {
        // The port's contract is to THROW (the caller decides what a failed hop means): the Students endpoint
        // turns this into a logged 502, which is what keeps the breakage visible to telemetry.
        var handler = new CapturingHandler("{}", HttpStatusCode.InternalServerError);
        var (sut, provider) = BuildClient(InboundRequest(), handler);
        using (provider)
        {
            var act = () => sut.GetAsync(StudentId);

            await act.Should().ThrowAsync<HttpRequestException>();
        }
    }
}
