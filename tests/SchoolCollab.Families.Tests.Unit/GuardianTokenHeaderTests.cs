using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Families.Services;

namespace SchoolCollab.Families.Tests.Unit;

/// <summary>
/// WS-F3 (ar-15-signoff-relocation, decision (a)/to-verify 1) — the Families client's
/// guardian call family re-mints a short-TTL <c>x-deeplink-token</c> header per call,
/// protected with the shared <see cref="DeepLinkProtector"/> + <see cref="DeepLinkConstants.Purpose"/>.
/// Binding coverage: the 15-minute TTL constant; the payload fields carried
/// (tenant/contact/assignment/ward); the header attach on every guardian call; and
/// purpose parity (a token minted by the re-mint path unprotects on the API side built
/// under the same purpose). Exercises the new behavioural code in <see cref="FamiliesApiClient"/>.
/// </summary>
[TestClass]
public class GuardianTokenHeaderTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid ContactId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
    private static readonly Guid AssignmentId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");
    private static readonly Guid WardStudentId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");

    [TestMethod]
    public void GuardianTokenReMintTtl_Is15Minutes()
    {
        // Owner decision 2026-09-16 — a short re-mint TTL that cannot outlive the stored
        // DeepLinkExpiresAt (the API cross-checks the authoritative expiry server-side).
        FamiliesApiClient.GuardianTokenReMintTtl.Should().Be(TimeSpan.FromMinutes(15));
    }

    [TestMethod]
    public async Task ReMint_AttachesHeader_PayloadCarriesCallerAndArguments()
    {
        var mockHttp = new MockHttpMessageHandler();
        var captured = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        mockHttp.When(HttpMethod.Get, $"http://localhost/guardian/assignments/{AssignmentId}/students/{WardStudentId}/sign-off")
            .Respond(HttpStatusCode.OK, "application/json", "{ }")   // placeholder body (never used here)
            .With(req =>
            {
                captured.TrySetResult(req.Headers.TryGetValues("x-deeplink-token", out var values)
                    ? string.Join(",", values)
                    : string.Empty);
                return true;
            });

        var protector = new DeepLinkProtector(new EphemeralDataProtectionProvider());
        var client = NewClient(mockHttp, protector);

        await client.GetGuardianSignOffContextAsync(
            AssignmentId, WardStudentId, new GuardianCallerContext(TenantId, ContactId), CancellationToken.None);

        var rawToken = await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        rawToken.Should().NotBeNullOrWhiteSpace("the guardian call must attach the x-deeplink-token header");

        var payload = protector.TryUnprotect(rawToken);
        payload.Should().NotBeNull("the re-minted token must unprotect under the shared keyring + purpose");
        payload!.TenantId.Should().Be(TenantId);
        payload.ContactId.Should().Be(ContactId);
        payload.AssignmentId.Should().Be(AssignmentId);
        payload.WardStudentId.Should().Be(WardStudentId);
        payload.OwnerType.Should().Be((int)ContactOwnerTypeDto.Guardian);
        // The re-mint is short-lived (15 min) — a fresh token must still be within the
        // window (it cannot be expired at mint time).
        payload.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        (payload.ExpiresAt - DateTimeOffset.UtcNow).Should().BeLessThan(FamiliesApiClient.GuardianTokenReMintTtl.Add(TimeSpan.FromMinutes(1)));
    }

    [TestMethod]
    public void ReMintedToken_PurposeParity_UnprotectsOnApiSide()
    {
        // Purpose parity with the API's validating protector: both sides build a
        // DeepLinkProtector over the SAME keyring + DeepLinkConstants.Purpose, so the
        // token the Families client re-mints must unprotect on a (modelled) API-side
        // protector — the exact contract the Assignments GuardianTokenEndpointFilter trusts.
        var sharedProvider = new EphemeralDataProtectionProvider();
        var mintSide = new DeepLinkProtector(sharedProvider);
        var apiSide = new DeepLinkProtector(sharedProvider); // modelled API-side validating protector
        var payload = new DeepLinkTokenPayload(
            TenantId, AssignmentId, ContactId, OwnerType: 1, Role: 0, WardStudentId,
            DateTimeOffset.UtcNow + FamiliesApiClient.GuardianTokenReMintTtl);
        var token = mintSide.Protect(payload);

        var apiView = apiSide.TryUnprotect(token);
        apiView.Should().BeEquivalentTo(payload);

        // The shared constants are the load-bearing parity (a drifted purpose would not
        // unprotect) — pinned so a refactor cannot silently change the seam.
        DeepLinkConstants.Purpose.Should().Be("ar-deeplink");
    }

    private static FamiliesApiClient NewClient(MockHttpMessageHandler mockHttp, DeepLinkProtector protector)
    {
        var http = mockHttp.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        return new FamiliesApiClient(http, protector, Mock.Of<ILogger<FamiliesApiClient>>());
    }
}
