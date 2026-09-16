using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Families.DeepLinks;
using SchoolCollab.Families.Services;

namespace SchoolCollab.Families.Tests.Unit;

/// <summary>
/// WS-E1 (ar-14-deep-links) — the public landing's validate → flag → redirect logic
/// (decision (c)/(g)). Covers: valid token + flag on → correct ward redirect; flag off
/// (dark launch) → no sign-in / expired page; expired / garbage → expired outcome; the
/// <c>OpenedAt</c> stamp invoked exactly once and only on a successful, flag-enabled
/// landing; and a tenant-mismatched token rejected.
/// <para>WS-F3 (ar-15-signoff-relocation, decision (e)) extends the redirect rule: a
/// GUARDIAN token carrying a ward lands directly on that ward's sign-off page for the
/// token's assignment; student-owned tokens keep the ar-14 <c>/ward/{sid}</c> target.</para>
/// </summary>
[TestClass]
public class DeepLinkLandingServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaa1111-aaaa-1111-aaaa-111111111111");
    private static readonly Guid AssignmentId = Guid.Parse("bbbb2222-bbbb-2222-bbbb-222222222222");
    private static readonly Guid ContactId = Guid.Parse("cccc3333-cccc-3333-cccc-333333333333");
    private static readonly Guid WardStudentId = Guid.Parse("dddd4444-dddd-4444-dddd-444444444444");

    /// <summary>The WS-F3 guardian direct-hop target for the fixtures above.</summary>
    private static string SignOffUrl => $"/ward/{WardStudentId}/assignments/{AssignmentId}/sign-off";

    private sealed class RecordingHandler(List<HttpRequestMessage> log) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            log.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    /// <summary>Handler that throws on any request — models a stamp transport failure.</summary>
    private sealed class TransportFaultHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new System.Net.Http.HttpRequestException("simulated stamp transport failure");
    }

    /// <summary>Handler whose response never completes — models a slow-but-reachable stamp
    /// that exceeds the <see cref="FamiliesApiClient.MarkRecipientOpenedAsync"/> ~3s per-call
    /// deadline (P1 / ar-14). It honors the request cancellation token (as a real HTTP
    /// handler does) — so when the linked-CTS deadline fires the await is cancelled with
    /// <see cref="OperationCanceledException"/> — but it never returns a successful response,
    /// so the only way out is the deadline.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return tcs.Task;
        }
    }

    /// <summary>Builds a landing service over a real protector + tenant accessor and a
    /// recording HTTP client (so the stamp call count is observable). Returns the
    /// <see cref="DeepLinkProtector"/> the service owns so tests mint with the SAME
    /// instance (a single <see cref="EphemeralDataProtectionProvider"/> key set).</summary>
    private static (DeepLinkLandingService Service, List<HttpRequestMessage> Requests, Mock<IFeatureFlagService> Flag, DeepLinkProtector Protector)
        Build(MockBehavior flagBehavior = MockBehavior.Strict)
    {
        var log = new List<HttpRequestMessage>();
        var http = new HttpClient(new RecordingHandler(log))
        {
            BaseAddress = new Uri("http://localhost")
        };
        return BuildCore(http, log, flagBehavior);
    }

    /// <summary>Like <see cref="Build"/> but over a caller-provided primary handler, returning
    /// only what the fault/tenant-header tests need (no recording log).</summary>
    private static (DeepLinkLandingService Service, Mock<IFeatureFlagService> Flag, DeepLinkProtector Protector)
        BuildOverHandler(HttpMessageHandler handler, MockBehavior flagBehavior = MockBehavior.Strict)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var (service, _, flag, protector) = BuildCore(http, new List<HttpRequestMessage>(), flagBehavior);
        return (service, flag, protector);
    }

    private static (DeepLinkLandingService Service, List<HttpRequestMessage> Requests, Mock<IFeatureFlagService> Flag, DeepLinkProtector Protector)
        BuildCore(HttpClient http, List<HttpRequestMessage> log, MockBehavior flagBehavior)
    {
        var protector = new DeepLinkProtector(new EphemeralDataProtectionProvider());
        var tenantProvider = new TenantProvider();
        var tenantAccessor = new TenantContextAccessor(tenantProvider);

        var api = new FamiliesApiClient(http, protector, NullLogger<FamiliesApiClient>.Instance);

        var flag = new Mock<IFeatureFlagService>(flagBehavior);
        flag.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.EnableDeepLinks, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = new DeepLinkLandingService(protector, tenantAccessor, flag.Object, api, NullLogger<DeepLinkLandingService>.Instance);
        return (service, log, flag, protector);
    }

    private static string MintToken(DeepLinkProtector protector, Guid? wardStudentId, DateTimeOffset expiresAt,
        int ownerType = 1)
        => protector.Protect(new DeepLinkTokenPayload(TenantId, AssignmentId, ContactId, ownerType, Role: 0, wardStudentId, expiresAt));

    private static bool IsStamp(HttpRequestMessage r) =>
        r.RequestUri?.PathAndQuery.Contains("/recipients/", StringComparison.OrdinalIgnoreCase) == true
        && r.RequestUri!.PathAndQuery.EndsWith("/opened", StringComparison.OrdinalIgnoreCase);

    [TestMethod]
    public async Task ValidToken_FlagOn_RedirectsToWardList_AndStampsExactlyOnce()
    {
        var (service, requests, flag, protector) = Build();
        flag.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.EnableDeepLinks, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var token = MintToken(protector, WardStudentId, DateTimeOffset.UtcNow.AddDays(7));

        var result = await service.HandleAsync(token);

        result.Outcome.Should().Be(DeepLinkLandingOutcome.Success);
        result.RedirectUrl.Should().Be(SignOffUrl);
        result.TenantId.Should().Be(TenantId);
        result.ContactId.Should().Be(ContactId);
        requests.Count(IsStamp).Should().Be(1);
    }

    [TestMethod]
    public async Task StampTransportFailure_DoesNotBreakLandingRedirect()
    {
        // Best-effort contract: a stamp request that throws must NOT escape HandleAsync as
        // an unhandled exception (P1) — the landing must still succeed and redirect.
        var (service, flag, protector) = BuildOverHandler(new TransportFaultHandler());
        flag.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.EnableDeepLinks, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var token = MintToken(protector, WardStudentId, DateTimeOffset.UtcNow.AddDays(7));

        var result = await service.HandleAsync(token);

        result.Outcome.Should().Be(DeepLinkLandingOutcome.Success);
        result.RedirectUrl.Should().Be(SignOffUrl);
        result.TenantId.Should().Be(TenantId);
        result.ContactId.Should().Be(ContactId);
    }

    [TestMethod]
    public async Task StampExceedsPerCallDeadline_DoesNotBreakLandingRedirect()
    {
        // P1 (ar-14): the stamp is bounded to a ~3s per-call deadline. A slow-but-reachable
        // API that never returns must NOT stall the landing — the deadline raises
        // OperationCanceledException with the CALLER's ct NOT cancelled, which the guard
        // classifies as a best-effort stamp failure and continues with the redirect. We
        // assert the outcome, not wall-clock.
        var (service, flag, protector) = BuildOverHandler(new HangingHandler());
        flag.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.EnableDeepLinks, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var token = MintToken(protector, WardStudentId, DateTimeOffset.UtcNow.AddDays(7));

        var result = await service.HandleAsync(token);

        result.Outcome.Should().Be(DeepLinkLandingOutcome.Success);
        result.RedirectUrl.Should().Be(SignOffUrl);
        result.TenantId.Should().Be(TenantId);
        result.ContactId.Should().Be(ContactId);
    }

    [TestMethod]
    public async Task Stamp_CarriesPayloadTenant_Header()
    {
        // P2: the stamp must carry the token's validated payload tenant (x-tenant-id) so a
        // dev selection that differs does not 404 the tenant-scoped lookup.
        var (service, requests, flag, protector) = Build();
        flag.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.EnableDeepLinks, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var token = MintToken(protector, WardStudentId, DateTimeOffset.UtcNow.AddDays(7));

        await service.HandleAsync(token);

        var stamp = requests.Single(IsStamp);
        stamp.Headers.TryGetValues("x-tenant-id", out var values).Should().BeTrue("stamp must carry x-tenant-id");
        values!.Should().ContainSingle().Which.Should().Be(TenantId.ToString());
    }

    [TestMethod]
    public async Task ValidToken_NoWardStudent_RedirectsToWardList()
    {
        var (service, _, flag, protector) = Build();
        flag.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.EnableDeepLinks, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var token = MintToken(protector, wardStudentId: null, DateTimeOffset.UtcNow.AddDays(7));

        var result = await service.HandleAsync(token);

        result.Outcome.Should().Be(DeepLinkLandingOutcome.Success);
        result.RedirectUrl.Should().Be("/ward");
    }

    [TestMethod]
    public async Task StudentOwnedToken_WithWard_LandsOnWardSurface_Unchanged()
    {
        // Decision (e): the guardian direct-to-sign-page hop must NOT change the
        // student-owned token behavior — a student token with a ward still lands on the
        // ward surface.
        var (service, _, flag, protector) = Build();
        flag.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.EnableDeepLinks, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var token = MintToken(protector, WardStudentId, DateTimeOffset.UtcNow.AddDays(7), ownerType: 0);

        var result = await service.HandleAsync(token);

        result.Outcome.Should().Be(DeepLinkLandingOutcome.Success);
        result.RedirectUrl.Should().Be($"/ward/{WardStudentId}");
    }

    [TestMethod]
    public async Task ExpiredToken_ReturnsExpired_AndDoesNotStamp()
    {
        var (service, requests, flag, protector) = Build();
        flag.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var token = MintToken(protector, WardStudentId, DateTimeOffset.UtcNow.AddDays(-1));

        var result = await service.HandleAsync(token);

        result.Outcome.Should().Be(DeepLinkLandingOutcome.Expired);
        result.RedirectUrl.Should().BeNull();
        requests.Count(IsStamp).Should().Be(0);
    }

    [TestMethod]
    public async Task GarbageToken_ReturnsExpired_AndDoesNotStamp()
    {
        var (service, requests, flag, _) = Build();
        flag.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await service.HandleAsync("not-a-valid-token");

        result.Outcome.Should().Be(DeepLinkLandingOutcome.Expired);
        result.RedirectUrl.Should().BeNull();
        requests.Count(IsStamp).Should().Be(0);
    }

    [TestMethod]
    public async Task FlagOff_ReturnsDark_AndDoesNotStamp_NoSignIn()
    {
        var (service, requests, _, protector) = Build();
        var token = MintToken(protector, WardStudentId, DateTimeOffset.UtcNow.AddDays(7));

        var result = await service.HandleAsync(token);

        result.Outcome.Should().Be(DeepLinkLandingOutcome.Dark);
        result.RedirectUrl.Should().BeNull();
        requests.Count(IsStamp).Should().Be(0);
    }

    [TestMethod]
    public async Task TenantMismatch_ReturnsExpired_AndDoesNotStamp()
    {
        var (service, requests, flag, protector) = Build();
        flag.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var token = MintToken(protector, WardStudentId, DateTimeOffset.UtcNow.AddDays(7));
        var otherTenant = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var result = await service.HandleAsync(token, expectedTenantId: otherTenant);

        result.Outcome.Should().Be(DeepLinkLandingOutcome.Expired);
        result.RedirectUrl.Should().BeNull();
        requests.Count(IsStamp).Should().Be(0);
    }
}
