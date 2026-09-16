using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using SchoolCollab.Core.DeepLinks;

namespace SchoolCollab.Core.Tests.Unit.DeepLinks;

/// <summary>
/// WS-E1 (ar-14-deep-links) — token crypto coverage: the purpose-scoped
/// <see cref="DeepLinkProtector"/> must round-trip the exact <see cref="DeepLinkTokenPayload"/>
/// (incl. the tenant id), reject tampered ciphertext without throwing, and be isolated
/// from a different purpose or a different application name. Expiry and tenant-mismatch
/// rejection live in the Families landing logic (<see cref="DeepLinkLandingServiceTests"/>);
/// here we assert the crypto preserves the fields (expiry + tenant inclusive) that logic
/// later checks.
/// </summary>
[TestClass]
public class DeepLinkProtectorTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssignmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid WardStudentId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static DeepLinkProtector CreateProtector(string applicationName = DeepLinkConstants.ApplicationName)
        => new(DataProtectionProvider.Create(applicationName));

    private static DeepLinkTokenPayload Payload(DateTimeOffset expiresAt)
        => new(
            TenantId,
            AssignmentId,
            ContactId,
            OwnerType: 1, // Guardian
            Role: 0,      // Primary
            WardStudentId,
            expiresAt);

    [TestMethod]
    public void RoundTrip_ReturnsIdenticalPayload_WithTenantAndExpiry()
    {
        var protector = CreateProtector();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        var payload = Payload(expiresAt);

        var token = protector.Protect(payload);
        var result = protector.TryUnprotect(token);

        result.Should().BeEquivalentTo(payload);
        result!.TenantId.Should().Be(TenantId);
        result.ExpiresAt.Should().Be(expiresAt);
    }

    [TestMethod]
    public void ExpiredPayload_RoundTripsFaithfully_TheLandingRejectsIt()
    {
        // The protector faithfully preserves an already-expired ExpiresAt; the landing
        // uses  Now < ExpiresAt  to reject it (covered in DeepLinkLandingServiceTests).
        var protector = CreateProtector();
        var pastExpiry = DateTimeOffset.UtcNow.AddDays(-1);

        var token = protector.Protect(Payload(pastExpiry));
        var result = protector.TryUnprotect(token);

        result.Should().NotBeNull();
        result!.ExpiresAt.Should().Be(pastExpiry);
    }

    [TestMethod]
    public void TamperedToken_ReturnsNull_WithoutThrowing()
    {
        var protector = CreateProtector();
        var token = protector.Protect(Payload(DateTimeOffset.UtcNow.AddDays(7)));

        var flipped = token.Length > 8
            ? token[..8] + (token[8] == 'A' ? 'B' : 'A') + token[9..]
            : token + "XX";
        var tampered = flipped + (flipped.Length == token.Length ? "!" : "");

        var result = protector.TryUnprotect(tampered);

        result.Should().BeNull();
    }

    [TestMethod]
    public void WrongPurpose_CannotUnprotect()
    {
        var protector = CreateProtector();
        var payload = Payload(DateTimeOffset.UtcNow.AddDays(7));

        // Protect the same payload under a different purpose on the same keyring.
        var otherPurposeProtector = new EphemeralDataProtectionProvider().CreateProtector("some-other-purpose");
        var foreignToken = otherPurposeProtector.Protect(JsonSerializer.Serialize(payload));

        protector.TryUnprotect(foreignToken).Should().BeNull();
    }

    [TestMethod]
    public void DifferentApplicationName_CannotUnprotect()
    {
        // A host forgetting SetApplicationName would drift its application name and
        // therefore be unable to unprotect mint-side ciphertext — the wiring-level
        // symptom DeepLinkWiringTests guards against at the constant/round-trip level.
        var mintSide = CreateProtector("school-collab-deeplinks");
        var driftedSide = CreateProtector("school-collab-deeplinks-typo");

        var token = mintSide.Protect(Payload(DateTimeOffset.UtcNow.AddDays(7)));

        driftedSide.TryUnprotect(token).Should().BeNull();
    }
}
